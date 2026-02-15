using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Peribind.Unity.Networking;
using Unity.Services.Lobbies.Models;
using Unity.Services.Authentication;
using Unity.Netcode;

namespace Peribind.Unity.UI
{
    public class LobbyUgsMenu : MonoBehaviour
    {
        [SerializeField] private LobbyServiceController lobbyService;
        [Header("Create")]
        [SerializeField] private TMP_InputField lobbyNameInput;
        [SerializeField] private TMP_InputField mapInput;
        [SerializeField] private Button createButton;
        [SerializeField] private Button returnButton;
        [SerializeField] private string starterSceneName = "StarterScene";

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
        [SerializeField] private MatchRegistryClient matchRegistry;
        [SerializeField] private Button reconnectButton;
        [SerializeField] private string matchIdPrefsKey = "last_match_id";

        private bool _isReady;
        private bool _connecting;
        private bool _isListRefreshInFlight;
        private bool _isServerAllocationInFlight;
        private bool _isReadyUpdateInFlight;
        private string _allocatingLobbyId = string.Empty;
        private MatchRegistryClient.MatchInfo _pendingAllocation;
        private string _pendingAllocationLobbyId = string.Empty;
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
            if (returnButton != null) returnButton.onClick.AddListener(OnReturnClicked);
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

            if (lobbyService != null)
            {
                lobbyService.LobbiesQueried += UpdateLobbyList;
                lobbyService.LobbyUpdated += OnLobbyUpdated;
                lobbyService.LobbyError += OnLobbyError;
            }
        }

        private void OnEnable()
        {
            ResetConnectionStateForLobby();
            _ = RefreshLobbyListAsync(force: true);
        }

        private void Update()
        {
            if (!_connecting)
            {
                return;
            }

            var manager = NetworkManager.Singleton;
            if (manager == null)
            {
                _connecting = false;
                return;
            }

            // If client start was initiated but Netcode is no longer in a client/listening state,
            // clear the local lock so another connect attempt is possible.
            if (!manager.IsClient && !manager.IsListening && !manager.ShutdownInProgress)
            {
                _connecting = false;
            }
        }

        private void OnDestroy()
        {
            if (createButton != null) createButton.onClick.RemoveListener(OnCreateClicked);
            if (returnButton != null) returnButton.onClick.RemoveListener(OnReturnClicked);
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
            await lobbyService.CreateLobbyAsync(name, 2, GetText(mapInput), string.Empty, string.Empty);
            await RefreshLobbyListAsync();
        }

        private async void OnReturnClicked()
        {
            if (lobbyService != null && lobbyService.CurrentLobby != null)
            {
                await lobbyService.LeaveLobbyAsync();
            }

            if (!string.IsNullOrWhiteSpace(starterSceneName))
            {
                SceneManager.LoadScene(starterSceneName);
            }
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
            await RefreshLobbyListAsync();
        }

        private async void OnReadyClicked()
        {
            if (lobbyService == null || lobbyService.CurrentLobby == null) return;
            if (_isReadyUpdateInFlight) return;

            _isReadyUpdateInFlight = true;
            _isReady = !_isReady;
            UpdateReadyButton();
            try
            {
                await lobbyService.SetPlayerReadyAsync(_isReady);
            }
            finally
            {
                _isReadyUpdateInFlight = false;
            }
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
            if (info == null)
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
                if (statusText != null)
                {
                    statusText.text = "Match expired or unavailable.";
                }

                return;
            }

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
                    var playerCount = lobby.Players != null ? lobby.Players.Count : 0;
                    label.text = $"{lobby.Name} | {playerCount}/{lobby.MaxPlayers} | Code: {lobby.LobbyCode}";
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
                _pendingAllocation = null;
                _pendingAllocationLobbyId = string.Empty;
                return;
            }

            if (statusText != null)
            {
                statusText.text = $"In lobby: {lobby.Name} | {lobby.Players.Count}/{lobby.MaxPlayers} | Code: {lobby.LobbyCode}";
            }

            if (!string.IsNullOrWhiteSpace(_pendingAllocationLobbyId) &&
                !string.Equals(_pendingAllocationLobbyId, lobby.Id, StringComparison.Ordinal))
            {
                _pendingAllocation = null;
                _pendingAllocationLobbyId = string.Empty;
            }

            UpdateReadyState(lobby);
            TryStartServerIfReady(lobby);
            TryConnectToServer(lobby);

            var previousCount = _lastObservedLobbyPlayerCount;
            var playerCount = lobby.Players != null ? lobby.Players.Count : 0;
            _lastObservedLobbyPlayerCount = playerCount;

            if (!_connecting && previousCount >= 0 && previousCount != playerCount)
            {
                _ = RefreshLobbyListAsync();
            }
        }

        private void OnLobbyError(string message)
        {
            if (statusText == null) return;
            if (!string.IsNullOrWhiteSpace(message) &&
                (message.IndexOf("too many", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 message.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                statusText.text = "Lobby busy, retrying...";
                return;
            }

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
                await RefreshLobbyListAsync();
                return;
            }

            await lobbyService.JoinLobbyByIdAsync(lobby.Id);
            await RefreshLobbyListAsync();
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

            if (_connecting)
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
                await lobbyService.QueryLobbiesAsync(GetText(mapInput), string.Empty, string.Empty);
            }
            finally
            {
                _isListRefreshInFlight = false;
            }
        }

        private void ResetConnectionStateForLobby()
        {
            _connecting = false;
            _isServerAllocationInFlight = false;
            _isReadyUpdateInFlight = false;
            _allocatingLobbyId = string.Empty;

            var manager = NetworkManager.Singleton;
            if (manager == null)
            {
                return;
            }

            if (manager.IsClient || manager.IsServer || manager.IsHost || manager.IsListening)
            {
                Debug.Log("[LobbyUgs] Active NetworkManager detected in lobby. Shutting down stale session state.");
                manager.Shutdown();
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
                _pendingAllocation = null;
                _pendingAllocationLobbyId = string.Empty;
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
                if (_pendingAllocation != null && string.Equals(_pendingAllocationLobbyId, lobby.Id, StringComparison.Ordinal))
                {
                    _ = PublishPendingServerInfoAsync(lobby);
                    return;
                }

                if (_isServerAllocationInFlight && string.Equals(_allocatingLobbyId, lobby.Id, StringComparison.Ordinal))
                {
                    return;
                }

                _ = AllocateAndPublishServerInfoAsync(lobby);
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
            var started = directConnection.StartClient(ip, port);
            if (!started)
            {
                _connecting = false;
                if (statusText != null)
                {
                    statusText.text = "Failed to start client connection.";
                }
            }
        }

        private async Task AllocateAndPublishServerInfoAsync(Lobby lobby)
        {
            if (lobbyService == null || matchRegistry == null || lobby == null)
            {
                return;
            }

            _isServerAllocationInFlight = true;
            _allocatingLobbyId = lobby.Id;
            try
            {
                var players = new List<string>();
                if (lobby.Players != null)
                {
                    foreach (var player in lobby.Players)
                    {
                        players.Add(player.Id);
                    }
                }

                var map = lobby.Data != null && lobby.Data.TryGetValue("map", out var mapObj) ? mapObj.Value : string.Empty;
                var mode = lobby.Data != null && lobby.Data.TryGetValue("mode", out var modeObj) ? modeObj.Value : string.Empty;
                var region = lobby.Data != null && lobby.Data.TryGetValue("region", out var regionObj) ? regionObj.Value : string.Empty;
                var allocation = await matchRegistry.CreateMatchAsync(lobby.Id, players, map, mode, region);
                if (allocation == null || string.IsNullOrWhiteSpace(allocation.serverIp) || allocation.serverPort <= 0 || string.IsNullOrWhiteSpace(allocation.matchId))
                {
                    if (statusText != null)
                    {
                        statusText.text = "Server allocation failed.";
                    }
                    return;
                }

                _pendingAllocation = allocation;
                _pendingAllocationLobbyId = lobby.Id;
                await PublishPendingServerInfoAsync(lobby);
            }
            finally
            {
                _isServerAllocationInFlight = false;
                _allocatingLobbyId = string.Empty;
            }
        }

        private async Task PublishPendingServerInfoAsync(Lobby lobby)
        {
            if (lobbyService == null || lobby == null || _pendingAllocation == null)
            {
                return;
            }

            if (!string.Equals(_pendingAllocationLobbyId, lobby.Id, StringComparison.Ordinal))
            {
                return;
            }

            var allocation = _pendingAllocation;
            Debug.Log($"[LobbyUgs] Publishing server info matchId={allocation.matchId} ip={allocation.serverIp} port={allocation.serverPort}");
            var updated = await lobbyService.SetServerInfoAsync(allocation.serverIp, allocation.serverPort, allocation.matchId);
            if (updated == null)
            {
                if (statusText != null)
                {
                    statusText.text = "Server ready, retrying lobby update...";
                }
                return;
            }

            _pendingAllocation = null;
            _pendingAllocationLobbyId = string.Empty;
        }

        private string GetScopedMatchIdPrefsKey()
        {
            var playerId = TryGetAuthenticatedPlayerId();
            if (string.IsNullOrWhiteSpace(playerId))
            {
                return matchIdPrefsKey;
            }

            var suffix = playerId.Length <= 16 ? playerId : playerId.Substring(0, 16);
            return $"{matchIdPrefsKey}_{suffix}";
        }

        private static string TryGetAuthenticatedPlayerId()
        {
            try
            {
                if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
                {
                    return AuthenticationService.Instance.PlayerId ?? string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }

            return string.Empty;
        }
    }
}
