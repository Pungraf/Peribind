using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Peribind.Unity.Networking;
using Unity.Services.Lobbies.Models;
using Unity.Services.Authentication;

namespace Peribind.Unity.UI
{
    public class LobbyUgsMenu : MonoBehaviour
    {
        [SerializeField] private LobbyServiceController lobbyService;
        [Header("Create")]
        [SerializeField] private TMP_InputField lobbyNameInput;
        [SerializeField] private TMP_InputField mapInput;
        [SerializeField] private TMP_InputField modeInput;
        [SerializeField] private TMP_InputField regionInput;
        [SerializeField] private Button createButton;

        [Header("Join")]
        [SerializeField] private TMP_InputField joinCodeInput;
        [SerializeField] private Button joinCodeButton;
        [SerializeField] private Button refreshButton;

        [Header("List")]
        [SerializeField] private Transform listContent;
        [SerializeField] private Button lobbyRowButtonPrefab;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button exitButton;
        [SerializeField] private Button readyButton;
        [SerializeField] private TMP_Text readyButtonText;

        [Header("Server")]
        [SerializeField] private DirectConnectionController directConnection;
        [SerializeField] private string serverIp = "209.38.222.103";
        [SerializeField] private int serverPort = 7777;
        [SerializeField] private MatchRegistryClient matchRegistry;
        [SerializeField] private Button reconnectButton;
        [SerializeField] private string matchIdPrefsKey = "last_match_id";
        [SerializeField] private PlayerIdentityProvider identityProvider;

        private bool _isReady;
        private bool _connecting;
        private bool _isListRefreshInFlight;
        private int _lastObservedLobbyPlayerCount = -1;
        private float _nextAllowedListRefreshTime;
        private const float ListRefreshCooldownSeconds = 2f;

        private void Awake()
        {
            if (lobbyService == null)
            {
                lobbyService = FindObjectOfType<LobbyServiceController>();
            }

            if (createButton != null) createButton.onClick.AddListener(OnCreateClicked);
            if (joinCodeButton != null) joinCodeButton.onClick.AddListener(OnJoinCodeClicked);
            if (refreshButton != null) refreshButton.onClick.AddListener(OnRefreshClicked);
            if (exitButton != null) exitButton.onClick.AddListener(OnExitClicked);
            if (readyButton != null) readyButton.onClick.AddListener(OnReadyClicked);
            if (reconnectButton != null) reconnectButton.onClick.AddListener(OnReconnectClicked);

            if (directConnection == null)
            {
                directConnection = FindObjectOfType<DirectConnectionController>();
            }

            if (matchRegistry == null)
            {
                matchRegistry = FindObjectOfType<MatchRegistryClient>();
            }

            if (identityProvider == null)
            {
                identityProvider = FindObjectOfType<PlayerIdentityProvider>(true);
            }

            if (lobbyService != null)
            {
                lobbyService.LobbiesQueried += UpdateLobbyList;
                lobbyService.LobbyUpdated += OnLobbyUpdated;
                lobbyService.LobbyError += OnLobbyError;
            }
        }

        private void OnEnable()
        {
            _ = RefreshLobbyListAsync(force: true);
        }

        private void OnDestroy()
        {
            if (createButton != null) createButton.onClick.RemoveListener(OnCreateClicked);
            if (joinCodeButton != null) joinCodeButton.onClick.RemoveListener(OnJoinCodeClicked);
            if (refreshButton != null) refreshButton.onClick.RemoveListener(OnRefreshClicked);
            if (exitButton != null) exitButton.onClick.RemoveListener(OnExitClicked);
            if (readyButton != null) readyButton.onClick.RemoveListener(OnReadyClicked);
            if (reconnectButton != null) reconnectButton.onClick.RemoveListener(OnReconnectClicked);

            if (lobbyService != null)
            {
                lobbyService.LobbiesQueried -= UpdateLobbyList;
                lobbyService.LobbyUpdated -= OnLobbyUpdated;
                lobbyService.LobbyError -= OnLobbyError;
            }
        }

        private async void OnCreateClicked()
        {
            if (lobbyService == null) return;

            var name = lobbyNameInput != null && !string.IsNullOrWhiteSpace(lobbyNameInput.text)
                ? lobbyNameInput.text
                : "Match";
            await lobbyService.CreateLobbyAsync(name, 2, GetText(mapInput), GetText(modeInput), GetText(regionInput));
            await RefreshLobbyListAsync(force: true);
        }

        private async void OnJoinCodeClicked()
        {
            if (lobbyService == null) return;
            var code = GetText(joinCodeInput);
            if (string.IsNullOrWhiteSpace(code)) return;
            await lobbyService.JoinLobbyByCodeAsync(code);
        }

        private async void OnRefreshClicked()
        {
            await RefreshLobbyListAsync(force: true);
        }

        private async void OnExitClicked()
        {
            if (lobbyService == null) return;
            await lobbyService.LeaveLobbyAsync();
            await RefreshLobbyListAsync(force: true);
        }

        private async void OnReadyClicked()
        {
            if (lobbyService == null || lobbyService.CurrentLobby == null) return;
            _isReady = !_isReady;
            UpdateReadyButton();
            await lobbyService.SetPlayerReadyAsync(_isReady);
        }

        private async void OnReconnectClicked()
        {
            if (matchRegistry == null || directConnection == null) return;
            var key = GetScopedMatchIdPrefsKey();
            var matchId = PlayerPrefs.GetString(key, string.Empty);
            if (string.IsNullOrWhiteSpace(matchId))
            {
                if (statusText != null)
                {
                    statusText.text = "No match to reconnect for this account.";
                }

                return;
            }

            var info = await matchRegistry.GetMatchAsync(matchId);
            if (info == null) return;

            var playerId = AuthenticationService.Instance.PlayerId;
            if (info.players == null || !info.players.Contains(playerId))
            {
                if (statusText != null)
                {
                    statusText.text = "Not allowed to rejoin this match.";
                }

                return;
            }

            directConnection.StartClient(info.serverIp, info.serverPort);
        }

        private void UpdateLobbyList(List<Lobby> lobbies)
        {
            if (listContent == null || lobbyRowButtonPrefab == null) return;

            ClearList();

            if (lobbies == null || lobbies.Count == 0)
            {
                if (statusText != null)
                {
                    statusText.text = "No lobbies found.";
                }

                return;
            }

            var localPlayerId = AuthenticationService.Instance.PlayerId;
            foreach (var lobby in lobbies)
            {
                var isMember = lobby.Players != null && lobby.Players.Exists(p => p.Id == localPlayerId);
                var row = Instantiate(lobbyRowButtonPrefab, listContent);
                var label = row.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    var map = lobby.Data != null && lobby.Data.TryGetValue("map", out var mapObj) ? mapObj.Value : "";
                    var mode = lobby.Data != null && lobby.Data.TryGetValue("mode", out var modeObj) ? modeObj.Value : "";
                    var region = lobby.Data != null && lobby.Data.TryGetValue("region", out var regionObj) ? regionObj.Value : "";
                    var youText = isMember ? " | You" : string.Empty;
                    label.text = $"{lobby.Name} | {lobby.Players.Count}/{lobby.MaxPlayers} | {map}/{mode}/{region}{youText} | Code: {lobby.LobbyCode}";
                }

                row.onClick.AddListener(() => OnLobbyRowClicked(lobby, isMember));
            }
        }

        private void OnLobbyUpdated(Lobby lobby)
        {
            if (lobby == null)
            {
                if (statusText != null)
                {
                    statusText.text = "Left lobby.";
                }

                _lastObservedLobbyPlayerCount = -1;
                return;
            }

            if (statusText != null)
            {
                statusText.text = $"In lobby: {lobby.Name} | {lobby.Players.Count}/{lobby.MaxPlayers} | Code: {lobby.LobbyCode}";
            }

            UpdateReadyState(lobby);
            TryStartServerIfReady(lobby);
            TryConnectToServer(lobby);

            if (!_connecting)
            {
                var playerCount = lobby.Players != null ? lobby.Players.Count : 0;
                if (playerCount != _lastObservedLobbyPlayerCount)
                {
                    _lastObservedLobbyPlayerCount = playerCount;
                    _ = RefreshLobbyListAsync();
                }
            }
        }

        private void OnLobbyError(string message)
        {
            if (statusText == null) return;
            statusText.text = $"Lobby error: {message}";
        }

        private static string GetText(TMP_InputField input)
        {
            return input != null ? input.text : string.Empty;
        }

        private async void OnLobbyRowClicked(Lobby lobby, bool isMember)
        {
            if (lobbyService == null || lobby == null)
            {
                return;
            }

            if (isMember)
            {
                await lobbyService.GetLobbyByIdAsync(lobby.Id);
                await RefreshLobbyListAsync(force: true);
                return;
            }

            await lobbyService.JoinLobbyByIdAsync(lobby.Id);
            await RefreshLobbyListAsync(force: true);
        }

        private async Task RefreshLobbyListAsync(bool force = false)
        {
            if (lobbyService == null)
            {
                return;
            }

            if (_isListRefreshInFlight)
            {
                return;
            }

            if (!force && Time.unscaledTime < _nextAllowedListRefreshTime)
            {
                return;
            }

            _isListRefreshInFlight = true;
            _nextAllowedListRefreshTime = Time.unscaledTime + ListRefreshCooldownSeconds;
            try
            {
                await lobbyService.QueryLobbiesAsync(GetText(mapInput), GetText(modeInput), GetText(regionInput));
            }
            finally
            {
                _isListRefreshInFlight = false;
            }
        }

        private void ClearList()
        {
            if (listContent == null) return;
            for (var i = listContent.childCount - 1; i >= 0; i--)
            {
                Destroy(listContent.GetChild(i).gameObject);
            }
        }

        private void UpdateReadyState(Lobby lobby)
        {
            if (lobby == null) return;
            var playerId = AuthenticationService.Instance.PlayerId;
            var player = lobby.Players.Find(p => p.Id == playerId);
            if (player != null && player.Data != null && player.Data.TryGetValue("ready", out var readyObj))
            {
                _isReady = readyObj.Value == "1";
            }

            UpdateReadyButton();
        }

        private void UpdateReadyButton()
        {
            if (readyButtonText != null)
            {
                readyButtonText.text = _isReady ? "Ready (OK)" : "Ready";
            }
        }

        private void TryStartServerIfReady(Lobby lobby)
        {
            if (lobbyService == null || lobby == null) return;
            if (lobby.Players == null || lobby.Players.Count < 2) return;

            var isHost = lobby.HostId == AuthenticationService.Instance.PlayerId;
            if (!isHost) return;

            if (lobby.Data != null && lobby.Data.ContainsKey("server_ip"))
            {
                Debug.Log("[LobbyUgs] Server info already set in lobby data.");
                return;
            }

            var allReady = true;
            foreach (var player in lobby.Players)
            {
                if (player.Data == null || !player.Data.TryGetValue("ready", out var readyObj) || readyObj.Value != "1")
                {
                    allReady = false;
                    break;
                }
            }

            if (allReady)
            {
                var matchId = Guid.NewGuid().ToString("N");
                Debug.Log($"[LobbyUgs] All ready. Setting server info. matchId={matchId} ip={serverIp} port={serverPort}");
                _ = lobbyService.SetServerInfoAsync(serverIp, serverPort, matchId);
                _ = RegisterMatchAsync(matchId, lobby);
            }
        }

        private void TryConnectToServer(Lobby lobby)
        {
            if (_connecting || directConnection == null || lobby == null) return;
            if (lobby.Data == null) return;

            if (!lobby.Data.TryGetValue("server_ip", out var ipObj)) return;
            if (!lobby.Data.TryGetValue("server_port", out var portObj)) return;
            if (!lobby.Data.TryGetValue("match_id", out var matchObj)) return;

            var ip = ipObj.Value;
            var matchId = matchObj.Value;
            if (!int.TryParse(portObj.Value, out var port))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(ip)) return;

            Debug.Log($"[LobbyUgs] Connecting to server {ip}:{port} matchId={matchId}");
            if (!string.IsNullOrWhiteSpace(matchId))
            {
                PlayerPrefs.SetString(GetScopedMatchIdPrefsKey(), matchId);
                PlayerPrefs.Save();
            }

            _connecting = true;
            if (lobbyService != null)
            {
                lobbyService.PauseLobbyRefresh();
            }
            directConnection.StartClient(ip, port);
        }

        private async Task RegisterMatchAsync(string matchId, Lobby lobby)
        {
            if (matchRegistry == null || lobby == null) return;
            if (string.IsNullOrWhiteSpace(matchId)) return;

            var players = new List<string>();
            if (lobby.Players != null)
            {
                foreach (var player in lobby.Players)
                {
                    players.Add(player.Id);
                }
            }

            await matchRegistry.RegisterMatchAsync(matchId, serverIp, serverPort, players);
        }

        private string GetScopedMatchIdPrefsKey()
        {
            if (identityProvider == null)
            {
                identityProvider = FindObjectOfType<PlayerIdentityProvider>(true);
            }

            if (identityProvider == null || string.IsNullOrWhiteSpace(identityProvider.PlayerId))
            {
                return matchIdPrefsKey;
            }

            var playerId = identityProvider.PlayerId;
            var suffix = playerId.Length <= 16 ? playerId : playerId.Substring(0, 16);
            return $"{matchIdPrefsKey}_{suffix}";
        }
    }
}
