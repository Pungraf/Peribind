using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Peribind.Unity.Networking;
using Unity.Netcode;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.SceneManagement;
using UIDocument = UnityEngine.UIElements.UIDocument;
using UiButton = UnityEngine.UIElements.Button;
using UiLabel = UnityEngine.UIElements.Label;
using UiTextField = UnityEngine.UIElements.TextField;
using UiScrollView = UnityEngine.UIElements.ScrollView;
using UiVisualElement = UnityEngine.UIElements.VisualElement;
using VisualTreeAsset = UnityEngine.UIElements.VisualTreeAsset;
using StyleSheet = UnityEngine.UIElements.StyleSheet;

namespace Peribind.Unity.UI
{
    public class LobbyUgsMenu : MonoBehaviour
    {
        [SerializeField] private LobbyServiceController lobbyService;

        [Header("Navigation")]
        [SerializeField] private string starterSceneName = "StarterScene";

        [Header("Server")]
        [SerializeField] private DirectConnectionController directConnection;
        [SerializeField] private MatchRegistryClient matchRegistry;
        [SerializeField] private string matchIdPrefsKey = "last_match_id";

        [Header("UI Toolkit")]
        [SerializeField] private bool enableUiToolkit = true;
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private bool autoAssignUiDocument = true;
        [SerializeField] private bool autoAssignVisualTreeFromResources = true;
        [SerializeField] private bool autoAssignStylesFromResources = true;

        private const string LobbyUxmlResourcePath = "UI/Toolkit/Lobby/LobbyMenu";
        private const string CommonStyleResourcePath = "UI/Toolkit/Common/PeribindTheme";
        private const string LobbyStyleResourcePath = "UI/Toolkit/Lobby/LobbyMenu";
        private const string LobbyNameInputName = "lobby-name-input";
        private const string MapInputName = "map-input";
        private const string JoinCodeInputName = "join-code-input";
        private const string CreateButtonName = "create-button";
        private const string JoinCodeButtonName = "join-code-button";
        private const string RefreshButtonName = "refresh-button";
        private const string ReturnButtonName = "return-button";
        private const string LeaveButtonName = "leave-button";
        private const string ReadyButtonName = "ready-button";
        private const string ReconnectButtonName = "reconnect-button";
        private const string StatusLabelName = "status-label";
        private const string LobbyListScrollName = "lobby-list-scroll";

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
        private bool _uiToolkitCallbacksRegistered;

        private UiVisualElement _uiRoot;
        private UiTextField _uiLobbyNameInput;
        private UiTextField _uiMapInput;
        private UiTextField _uiJoinCodeInput;
        private UiButton _uiCreateButton;
        private UiButton _uiJoinCodeButton;
        private UiButton _uiRefreshButton;
        private UiButton _uiReturnButton;
        private UiButton _uiLeaveButton;
        private UiButton _uiReadyButton;
        private UiButton _uiReconnectButton;
        private UiLabel _uiStatusLabel;
        private UiScrollView _uiLobbyListScroll;

        private void Awake()
        {
            TryBindUiToolkit();

            if (lobbyService == null)
            {
                lobbyService = FindObjectOfType<LobbyServiceController>();
            }

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
            if (enableUiToolkit && _uiRoot == null)
            {
                TryBindUiToolkit();
            }

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

            if (!manager.IsClient && !manager.IsListening && !manager.ShutdownInProgress)
            {
                _connecting = false;
            }
        }

        private void OnDestroy()
        {
            if (lobbyService != null)
            {
                lobbyService.LobbiesQueried -= UpdateLobbyList;
                lobbyService.LobbyUpdated -= OnLobbyUpdated;
                lobbyService.LobbyError -= OnLobbyError;
            }

            UnregisterUiToolkitCallbacks();
        }

        private async void OnCreateClicked()
        {
            if (lobbyService == null)
            {
                return;
            }

            var lobbyNameInputValue = GetLobbyNameInputValue();
            var name = !string.IsNullOrWhiteSpace(lobbyNameInputValue)
                ? lobbyNameInputValue
                : "Match";

            await lobbyService.CreateLobbyAsync(name, 2, GetMapInputValue(), string.Empty, string.Empty);
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
            if (lobbyService == null)
            {
                return;
            }

            var code = GetJoinCodeInputValue();
            if (string.IsNullOrWhiteSpace(code))
            {
                return;
            }

            await lobbyService.JoinLobbyByCodeAsync(code);
        }

        private async void OnRefreshClicked()
        {
            await RefreshLobbyListAsync(force: true);
        }

        private async void OnExitClicked()
        {
            if (lobbyService == null)
            {
                return;
            }

            await lobbyService.LeaveLobbyAsync();
            await RefreshLobbyListAsync();
        }

        private async void OnReadyClicked()
        {
            if (lobbyService == null || lobbyService.CurrentLobby == null || _isReadyUpdateInFlight)
            {
                return;
            }

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
            if (matchRegistry == null || directConnection == null)
            {
                return;
            }

            var key = GetScopedMatchIdPrefsKey();
            var matchId = PlayerPrefs.GetString(key, string.Empty);
            if (string.IsNullOrWhiteSpace(matchId))
            {
                SetStatusText("No match to reconnect for this account.");
                return;
            }

            var info = await matchRegistry.GetMatchAsync(matchId);
            if (info == null)
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
                SetStatusText("Match expired or unavailable.");
                return;
            }

            var playerId = AuthenticationService.Instance.PlayerId;
            if (info.players == null || !info.players.Contains(playerId))
            {
                SetStatusText("No reconnectable match for this account.");
                return;
            }

            if (info.connectedPlayers != null && info.connectedPlayers.Contains(playerId))
            {
                SetStatusText("This account is already connected to that match.");
                return;
            }

            directConnection.StartClient(info.serverIp, info.serverPort);
        }

        private void UpdateLobbyList(List<MatchRegistryClient.LobbyInfo> lobbies)
        {
            ClearList();

            if (lobbies == null || lobbies.Count == 0)
            {
                SetStatusText("No lobbies found.");
                return;
            }

            var localPlayerId = AuthenticationService.Instance.PlayerId;
            foreach (var lobby in lobbies)
            {
                var isMember = lobby.players != null && lobby.players.Exists(p => p.id == localPlayerId);
                var playerCount = lobby.players != null ? lobby.players.Count : 0;
                var rowText = $"{lobby.name} | {playerCount}/{lobby.maxPlayers} | Code: {lobby.lobbyCode}";

                if (_uiLobbyListScroll == null)
                {
                    continue;
                }

                var rowButton = new UiButton
                {
                    text = rowText
                };
                rowButton.AddToClassList("lobby-row-button");
                rowButton.clicked += () => OnLobbyRowClicked(lobby, isMember);
                _uiLobbyListScroll.Add(rowButton);
            }
        }

        private void OnLobbyUpdated(MatchRegistryClient.LobbyInfo lobby)
        {
            if (lobby == null)
            {
                SetStatusText("Left lobby.");
                _lastObservedLobbyPlayerCount = -1;
                _pendingAllocation = null;
                _pendingAllocationLobbyId = string.Empty;
                return;
            }

            var lobbyPlayers = lobby.players != null ? lobby.players.Count : 0;
            SetStatusText($"In lobby: {lobby.name} | {lobbyPlayers}/{lobby.maxPlayers} | Code: {lobby.lobbyCode}");

            if (!string.IsNullOrWhiteSpace(_pendingAllocationLobbyId) &&
                !string.Equals(_pendingAllocationLobbyId, lobby.id, StringComparison.Ordinal))
            {
                _pendingAllocation = null;
                _pendingAllocationLobbyId = string.Empty;
            }

            UpdateReadyState(lobby);
            TryStartServerIfReady(lobby);
            TryConnectToServer(lobby);

            var previousCount = _lastObservedLobbyPlayerCount;
            var playerCount = lobby.players != null ? lobby.players.Count : 0;
            _lastObservedLobbyPlayerCount = playerCount;

            if (!_connecting && previousCount >= 0 && previousCount != playerCount)
            {
                _ = RefreshLobbyListAsync();
            }
        }

        private void OnLobbyError(string message)
        {
            SetStatusText($"Lobby error: {message}");
        }

        private void TryBindUiToolkit()
        {
            if (!enableUiToolkit)
            {
                return;
            }

            if (uiDocument == null && autoAssignUiDocument)
            {
                uiDocument = FindObjectOfType<UIDocument>(true);
            }

            if (uiDocument == null)
            {
                return;
            }

            if (autoAssignVisualTreeFromResources && uiDocument.visualTreeAsset == null)
            {
                var tree = Resources.Load<VisualTreeAsset>(LobbyUxmlResourcePath);
                if (tree == null)
                {
                    Debug.LogWarning($"[LobbyUgsUITK] Missing UXML at Resources/{LobbyUxmlResourcePath}.uxml");
                    return;
                }

                uiDocument.visualTreeAsset = tree;
            }

            _uiRoot = uiDocument.rootVisualElement;
            if (_uiRoot == null)
            {
                return;
            }

            if (autoAssignStylesFromResources)
            {
                TryAddStyle(_uiRoot, CommonStyleResourcePath);
                TryAddStyle(_uiRoot, LobbyStyleResourcePath);
            }

            _uiLobbyNameInput = UnityEngine.UIElements.UQueryExtensions.Q<UiTextField>(_uiRoot, LobbyNameInputName);
            _uiMapInput = UnityEngine.UIElements.UQueryExtensions.Q<UiTextField>(_uiRoot, MapInputName);
            _uiJoinCodeInput = UnityEngine.UIElements.UQueryExtensions.Q<UiTextField>(_uiRoot, JoinCodeInputName);
            _uiCreateButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, CreateButtonName);
            _uiJoinCodeButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, JoinCodeButtonName);
            _uiRefreshButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, RefreshButtonName);
            _uiReturnButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, ReturnButtonName);
            _uiLeaveButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, LeaveButtonName);
            _uiReadyButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, ReadyButtonName);
            _uiReconnectButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, ReconnectButtonName);
            _uiStatusLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, StatusLabelName);
            _uiLobbyListScroll = UnityEngine.UIElements.UQueryExtensions.Q<UiScrollView>(_uiRoot, LobbyListScrollName);

            RegisterUiToolkitCallbacks();
            UpdateReadyButton();
        }

        private void RegisterUiToolkitCallbacks()
        {
            if (_uiToolkitCallbacksRegistered)
            {
                return;
            }

            if (_uiCreateButton != null)
            {
                _uiCreateButton.clicked += OnCreateClicked;
            }

            if (_uiJoinCodeButton != null)
            {
                _uiJoinCodeButton.clicked += OnJoinCodeClicked;
            }

            if (_uiRefreshButton != null)
            {
                _uiRefreshButton.clicked += OnRefreshClicked;
            }

            if (_uiReturnButton != null)
            {
                _uiReturnButton.clicked += OnReturnClicked;
            }

            if (_uiLeaveButton != null)
            {
                _uiLeaveButton.clicked += OnExitClicked;
            }

            if (_uiReadyButton != null)
            {
                _uiReadyButton.clicked += OnReadyClicked;
            }

            if (_uiReconnectButton != null)
            {
                _uiReconnectButton.clicked += OnReconnectClicked;
            }

            _uiToolkitCallbacksRegistered = true;
        }

        private void UnregisterUiToolkitCallbacks()
        {
            if (!_uiToolkitCallbacksRegistered)
            {
                return;
            }

            if (_uiCreateButton != null)
            {
                _uiCreateButton.clicked -= OnCreateClicked;
            }

            if (_uiJoinCodeButton != null)
            {
                _uiJoinCodeButton.clicked -= OnJoinCodeClicked;
            }

            if (_uiRefreshButton != null)
            {
                _uiRefreshButton.clicked -= OnRefreshClicked;
            }

            if (_uiReturnButton != null)
            {
                _uiReturnButton.clicked -= OnReturnClicked;
            }

            if (_uiLeaveButton != null)
            {
                _uiLeaveButton.clicked -= OnExitClicked;
            }

            if (_uiReadyButton != null)
            {
                _uiReadyButton.clicked -= OnReadyClicked;
            }

            if (_uiReconnectButton != null)
            {
                _uiReconnectButton.clicked -= OnReconnectClicked;
            }

            _uiToolkitCallbacksRegistered = false;
        }

        private string GetLobbyNameInputValue()
        {
            return _uiLobbyNameInput != null ? _uiLobbyNameInput.value ?? string.Empty : string.Empty;
        }

        private string GetMapInputValue()
        {
            return _uiMapInput != null ? _uiMapInput.value ?? string.Empty : string.Empty;
        }

        private string GetJoinCodeInputValue()
        {
            return _uiJoinCodeInput != null ? _uiJoinCodeInput.value ?? string.Empty : string.Empty;
        }

        private void SetStatusText(string message)
        {
            if (_uiStatusLabel != null)
            {
                _uiStatusLabel.text = message ?? string.Empty;
            }
        }

        private static void TryAddStyle(UiVisualElement root, string resourcePath)
        {
            if (root == null || string.IsNullOrWhiteSpace(resourcePath))
            {
                return;
            }

            var styleSheet = Resources.Load<StyleSheet>(resourcePath);
            if (styleSheet == null)
            {
                return;
            }

            if (!root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }
        }

        private async void OnLobbyRowClicked(MatchRegistryClient.LobbyInfo lobby, bool isMember)
        {
            if (lobbyService == null || lobby == null)
            {
                return;
            }

            // Always execute join flow so backend can enforce active-match/account-busy rules.
            await lobbyService.JoinLobbyByIdAsync(lobby.id);
            await RefreshLobbyListAsync();
        }

        private async Task RefreshLobbyListAsync(bool force = false)
        {
            if (lobbyService == null || _isListRefreshInFlight || _connecting)
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
                await lobbyService.QueryLobbiesAsync(GetMapInputValue(), string.Empty, string.Empty);
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
            _uiLobbyListScroll?.Clear();
        }

        private void UpdateReadyState(MatchRegistryClient.LobbyInfo lobby)
        {
            if (lobby == null || lobby.players == null)
            {
                return;
            }

            var playerId = AuthenticationService.Instance.PlayerId;
            var player = lobby.players.Find(p => p.id == playerId);
            if (player != null)
            {
                _isReady = player.ready;
            }

            UpdateReadyButton();
        }

        private void UpdateReadyButton()
        {
            if (_uiReadyButton != null)
            {
                _uiReadyButton.text = _isReady ? "Ready (OK)" : "Ready";
            }
        }

        private void TryStartServerIfReady(MatchRegistryClient.LobbyInfo lobby)
        {
            if (lobbyService == null || lobby == null || lobby.players == null || lobby.players.Count < 2)
            {
                return;
            }

            var isHost = lobby.hostId == AuthenticationService.Instance.PlayerId;
            if (!isHost)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(lobby.serverIp) && lobby.serverPort > 0)
            {
                _pendingAllocation = null;
                _pendingAllocationLobbyId = string.Empty;
                return;
            }

            var allReady = true;
            foreach (var player in lobby.players)
            {
                if (!player.ready)
                {
                    allReady = false;
                    break;
                }
            }

            if (!allReady)
            {
                return;
            }

            if (_pendingAllocation != null && string.Equals(_pendingAllocationLobbyId, lobby.id, StringComparison.Ordinal))
            {
                _ = PublishPendingServerInfoAsync(lobby);
                return;
            }

            if (_isServerAllocationInFlight && string.Equals(_allocatingLobbyId, lobby.id, StringComparison.Ordinal))
            {
                return;
            }

            _ = AllocateAndPublishServerInfoAsync(lobby);
        }

        private void TryConnectToServer(MatchRegistryClient.LobbyInfo lobby)
        {
            if (_connecting || directConnection == null || lobby == null)
            {
                return;
            }

            var ip = lobby.serverIp;
            var matchId = lobby.matchId;
            var port = lobby.serverPort;
            if (string.IsNullOrWhiteSpace(ip) || port <= 0)
            {
                return;
            }

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
                SetStatusText("Failed to start client connection.");
            }
        }

        private async Task AllocateAndPublishServerInfoAsync(MatchRegistryClient.LobbyInfo lobby)
        {
            if (lobbyService == null || matchRegistry == null || lobby == null)
            {
                return;
            }

            _isServerAllocationInFlight = true;
            _allocatingLobbyId = lobby.id;
            try
            {
                var players = new List<string>();
                if (lobby.players != null)
                {
                    foreach (var player in lobby.players)
                    {
                        players.Add(player.id);
                    }
                }

                var allocation = await matchRegistry.CreateMatchAsync(lobby.id, players, lobby.map, lobby.mode, lobby.region);
                if (allocation == null || string.IsNullOrWhiteSpace(allocation.serverIp) || allocation.serverPort <= 0 || string.IsNullOrWhiteSpace(allocation.matchId))
                {
                    SetStatusText("Server allocation failed.");
                    return;
                }

                _pendingAllocation = allocation;
                _pendingAllocationLobbyId = lobby.id;
                await PublishPendingServerInfoAsync(lobby);
            }
            finally
            {
                _isServerAllocationInFlight = false;
                _allocatingLobbyId = string.Empty;
            }
        }

        private async Task PublishPendingServerInfoAsync(MatchRegistryClient.LobbyInfo lobby)
        {
            if (lobbyService == null || lobby == null || _pendingAllocation == null)
            {
                return;
            }

            if (!string.Equals(_pendingAllocationLobbyId, lobby.id, StringComparison.Ordinal))
            {
                return;
            }

            var allocation = _pendingAllocation;
            Debug.Log($"[LobbyUgs] Publishing server info matchId={allocation.matchId} ip={allocation.serverIp} port={allocation.serverPort}");
            var updated = await lobbyService.SetServerInfoAsync(allocation.serverIp, allocation.serverPort, allocation.matchId);
            if (updated == null)
            {
                SetStatusText("Server ready, retrying lobby update...");
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

