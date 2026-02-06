using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using Unity.Networking.Transport.Relay;

namespace Peribind.Unity.Networking
{
    public class MultiplayerSessionController : MonoBehaviour
    {
        [SerializeField] private ServicesBootstrap services;
        [SerializeField] private int maxPlayers = 2;
        [SerializeField] private string sessionType = "Session";
        [SerializeField] private bool autoCreateOnQuickJoin = true;
        [SerializeField] private float quickJoinTimeoutSeconds = 5f;
        [SerializeField] private float leaveTimeoutSeconds = 6f;
        [SerializeField] private bool verboseLogging = true;
        [SerializeField] private int relayReconnectBuffer = 1;
        [SerializeField] private int disconnectTimeoutMs = 5000;
        [SerializeField] private int heartbeatTimeoutMs = 500;

        private ISession _session;
        private bool _eventsBound;
        private bool _isPrimaryInstance;
        private bool _isLeaving;
        private bool _hasShutdown;
        private string _activeRelayJoinCode;
        private bool _isStartingClient;
        private bool _clientConnectPending;
        private float _nextClientAttemptTime;
        private bool _allowSessionDrivenClientStart;
        private bool _networkCallbacksBound;
        private NetworkManager _boundNetworkManager;
        private System.Threading.Tasks.Task _leaveTask;
        public const string GameStartedKey = "game_started";
        public const string RelayJoinCodeKey = "relay_join_code";

        private void Awake()
        {
            // Keep a single session controller across scene loads.
            var existing = FindObjectsOfType<MultiplayerSessionController>();
            if (existing.Length > 1)
            {
                _isPrimaryInstance = false;
                Destroy(gameObject);
                return;
            }

            _isPrimaryInstance = true;
            DontDestroyOnLoad(gameObject);

            if (services == null)
            {
                services = FindObjectOfType<ServicesBootstrap>();
            }
        }

        private void OnApplicationQuit()
        {
            if (!_isPrimaryInstance)
            {
                return;
            }

            _ = ShutdownAsync(true);
        }

        private void OnDestroy()
        {
            if (!_isPrimaryInstance)
            {
                return;
            }

            _ = ShutdownAsync(true);
        }

        public string CurrentSessionId => _session != null ? _session.Id : string.Empty;
        public string CurrentSessionCode => _session != null ? _session.Code : string.Empty;
        public bool HasSession => _session != null;
        public ISession CurrentSession => _session;
        public bool IsGameStarted => GetBoolSessionProperty(GameStartedKey);
        public bool IsLeaving => _isLeaving;
        public string CurrentPlayerId => _session?.CurrentPlayer?.Id ?? string.Empty;

        public async void CreateSession()
        {
            if (!await EnsureServicesReady())
            {
                return;
            }

            if (_leaveTask != null && !_leaveTask.IsCompleted)
            {
                Debug.Log("[Multiplayer] CreateSession waiting for leave to complete.");
                await _leaveTask;
            }

            await LeaveRegisteredSessionsAsync();

            EnsureNetworkManager();
            ResetLocalSessionState();
            LogNetworkState("CreateSession (after reset)");

            try
            {
                var options = new SessionOptions
                {
                    MaxPlayers = maxPlayers,
                    Type = sessionType,
                    IsLocked = false,
                    IsPrivate = false
                };

                _session = await MultiplayerService.Instance.CreateSessionAsync(options);
                BindSessionEvents();
                await EnsureGameStartedPropertyAsync(false);
                Debug.Log($"Session created. Id={_session.Id} Code={_session.Code}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public async void JoinSessionByCode(string sessionCode)
        {
            if (string.IsNullOrWhiteSpace(sessionCode))
            {
                Debug.LogWarning("JoinSessionByCode called with empty sessionCode.");
                return;
            }
            Debug.Log($"[Multiplayer] JoinSessionByCode called. Code='{sessionCode}'");

            if (!await EnsureServicesReady())
            {
                return;
            }

            if (_leaveTask != null && !_leaveTask.IsCompleted)
            {
                Debug.Log("[Multiplayer] JoinSessionByCode waiting for leave to complete.");
                await _leaveTask;
            }

            await LeaveRegisteredSessionsAsync();

            EnsureNetworkManager();
            ResetLocalSessionState();
            LogNetworkState("JoinSessionByCode (after reset)");
            _allowSessionDrivenClientStart = true;

            try
            {
                _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(sessionCode);
                BindSessionEvents();
                Debug.Log($"Joined session. Id={_session.Id} Code={_session.Code}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public async void QuickJoinOrCreate()
        {
            if (!await EnsureServicesReady())
            {
                return;
            }

            if (_leaveTask != null && !_leaveTask.IsCompleted)
            {
                Debug.Log("[Multiplayer] QuickJoinOrCreate waiting for leave to complete.");
                await _leaveTask;
            }

            await LeaveRegisteredSessionsAsync();

            EnsureNetworkManager();
            ResetLocalSessionState();
            LogNetworkState("QuickJoinOrCreate (after reset)");
            _allowSessionDrivenClientStart = true;

            try
            {
                var quickJoinOptions = new QuickJoinOptions
                {
                    Timeout = TimeSpan.FromSeconds(Mathf.Max(1f, quickJoinTimeoutSeconds)),
                    CreateSession = autoCreateOnQuickJoin
                };

                var sessionOptions = new SessionOptions
                {
                    MaxPlayers = maxPlayers,
                    Type = sessionType,
                    IsLocked = false,
                    IsPrivate = false
                };

                _session = await MultiplayerService.Instance.MatchmakeSessionAsync(quickJoinOptions, sessionOptions);
                BindSessionEvents();
                Debug.Log($"Quick-join session ready. Id={_session.Id}");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        public async void LeaveSession()
        {
            await LeaveSessionAsync();
        }

        public void LeaveAndShutdown()
        {
            _ = LeaveAndShutdownAsync(true);
        }

        public void LeaveAndShutdown(bool destroyNetworkManager)
        {
            _ = LeaveAndShutdownAsync(destroyNetworkManager);
        }

        public async System.Threading.Tasks.Task LeaveAndShutdownAsync(bool destroyNetworkManager)
        {
            await GracefulLeaveAsync(destroyNetworkManager);
        }

        private async System.Threading.Tasks.Task ShutdownAsync(bool destroyNetworkManager)
        {
            if (_hasShutdown)
            {
                return;
            }

            _hasShutdown = true;
            await GracefulLeaveAsync(destroyNetworkManager);
        }

        public System.Threading.Tasks.Task LeaveSessionAsync()
        {
            if (_leaveTask != null)
            {
                return _leaveTask;
            }

            _leaveTask = LeaveSessionInternalAsync();
            return _leaveTask;
        }

        private async System.Threading.Tasks.Task LeaveSessionInternalAsync()
        {
            if (_session == null)
            {
                await LeaveRegisteredSessionsAsync();
                _leaveTask = null;
                return;
            }

            _isLeaving = true;
            var session = _session;

            try
            {
                await LeaveRegisteredSessionsAsync();
                Debug.Log($"[Multiplayer] LeaveSessionAsync starting. SessionId={session.Id} PlayerId={session.CurrentPlayer?.Id}");
                var leaveTask = session.LeaveAsync();
                var timeoutSeconds = Mathf.Max(0f, leaveTimeoutSeconds);
                if (timeoutSeconds > 0f)
                {
                    var completed = await System.Threading.Tasks.Task.WhenAny(
                        leaveTask,
                        System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));

                    if (completed != leaveTask)
                    {
                        Debug.LogWarning($"[Multiplayer] LeaveSessionAsync timed out after {timeoutSeconds:0.0}s. Clearing local state.");
                    }
                    else
                    {
                        await leaveTask;
                        Debug.Log("[Multiplayer] LeaveSessionAsync completed.");
                    }
                }
                else
                {
                    await leaveTask;
                    Debug.Log("[Multiplayer] LeaveSessionAsync completed.");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"LeaveSession failed: {ex.Message}");
            }
            finally
            {
                _isLeaving = false;
                _allowSessionDrivenClientStart = false;
                UnbindSessionEvents();
                _session = null;
                _leaveTask = null;
            }
        }

        public async System.Threading.Tasks.Task RemovePlayerAsync(string playerId)
        {
            if (_session == null || string.IsNullOrWhiteSpace(playerId))
            {
                return;
            }

            try
            {
                if (_session.IsHost)
                {
                    await _session.AsHost().RemovePlayerAsync(playerId);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"RemovePlayerAsync failed: {ex.Message}");
            }
        }

        public async void StartGameAsHost()
        {
            if (_session == null || !_session.IsHost)
            {
                Debug.LogWarning("StartGameAsHost called but no host session is active.");
                return;
            }

            try
            {
                if (!await EnsureServicesReady())
                {
                    return;
                }

                EnsureNetworkManager();
                var joinCode = await StartHostWithRelayAsync();
                if (string.IsNullOrWhiteSpace(joinCode))
                {
                    Debug.LogWarning("[Multiplayer] StartGameAsHost failed to start relay host.");
                    return;
                }

                await EnsureGameStartedPropertyAsync(true, joinCode);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static void ShutdownNetwork(bool destroyNetworkManager)
        {
            var manager = NetworkManager.Singleton;
            if (manager != null)
            {
                Debug.Log($"[Multiplayer] ShutdownNetwork destroy={destroyNetworkManager} IsServer={manager.IsServer} IsClient={manager.IsClient} IsHost={manager.IsHost}");
                Debug.Log($"[Multiplayer] ShutdownNetwork stack:\n{Environment.StackTrace}");
                manager.Shutdown();
                if (destroyNetworkManager)
                {
                    Destroy(manager.gameObject);
                }
            }
        }

        private void EnsureNetworkManager()
        {
            if (NetworkManager.Singleton != null)
            {
                var existingTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
                ConfigureTransport(existingTransport);
                BindNetworkCallbacks(NetworkManager.Singleton);
                return;
            }

            var managerObject = new GameObject("NetworkManager");
            managerObject.AddComponent<NetworkManagerBootstrap>();

            var transport = managerObject.AddComponent<UnityTransport>();
            var manager = managerObject.AddComponent<NetworkManager>();

            if (manager.NetworkConfig == null)
            {
                manager.NetworkConfig = new NetworkConfig();
            }

            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.EnableSceneManagement = true;
            ConfigureTransport(transport);
            BindNetworkCallbacks(manager);
        }

        private void ConfigureTransport(UnityTransport transport)
        {
            if (transport == null)
            {
                return;
            }

            if (disconnectTimeoutMs > 0)
            {
                transport.DisconnectTimeoutMS = disconnectTimeoutMs;
            }

            if (heartbeatTimeoutMs > 0)
            {
                transport.HeartbeatTimeoutMS = heartbeatTimeoutMs;
            }
        }

        private void ResetLocalSessionState()
        {
            if (verboseLogging)
            {
                Debug.Log($"[Multiplayer] ResetLocalSessionState session={( _session != null ? _session.Id : "null")} eventsBound={_eventsBound}");
            }
            UnbindSessionEvents();
            _session = null;
            _activeRelayJoinCode = string.Empty;
            _isStartingClient = false;
            _clientConnectPending = false;
            _nextClientAttemptTime = 0f;
            _allowSessionDrivenClientStart = false;
            ShutdownNetwork(false);
        }

        private void BindSessionEvents()
        {
            if (_session == null || _eventsBound)
            {
                return;
            }

            _session.PlayerJoined += OnPlayerJoined;
            _session.PlayerLeaving += OnPlayerLeaving;
            _session.PlayerHasLeft += OnPlayerHasLeft;
            _session.SessionHostChanged += OnSessionHostChanged;
            _session.StateChanged += OnStateChanged;
            _session.Changed += OnSessionChanged;
            _session.RemovedFromSession += OnRemovedFromSession;
            _session.SessionPropertiesChanged += OnSessionPropertiesChanged;
            _session.PlayerPropertiesChanged += OnPlayerPropertiesChanged;
            _session.Deleted += OnSessionDeleted;

            _eventsBound = true;
        }

        private void UnbindSessionEvents()
        {
            if (_session == null || !_eventsBound)
            {
                return;
            }

            _session.PlayerJoined -= OnPlayerJoined;
            _session.PlayerLeaving -= OnPlayerLeaving;
            _session.PlayerHasLeft -= OnPlayerHasLeft;
            _session.SessionHostChanged -= OnSessionHostChanged;
            _session.StateChanged -= OnStateChanged;
            _session.Changed -= OnSessionChanged;
            _session.RemovedFromSession -= OnRemovedFromSession;
            _session.SessionPropertiesChanged -= OnSessionPropertiesChanged;
            _session.PlayerPropertiesChanged -= OnPlayerPropertiesChanged;
            _session.Deleted -= OnSessionDeleted;

            _eventsBound = false;
        }

        private void OnPlayerJoined(string playerId)
        {
            Debug.Log($"Session event: PlayerJoined {playerId}");
        }

        private void OnPlayerLeaving(string playerId)
        {
            Debug.Log($"Session event: PlayerLeaving {playerId}");
        }

        private void OnPlayerHasLeft(string playerId)
        {
            Debug.Log($"Session event: PlayerHasLeft {playerId}");
        }

        private void OnSessionHostChanged(string playerId)
        {
            Debug.Log($"Session event: HostChanged {playerId}");
        }

        private void OnStateChanged(SessionState state)
        {
            Debug.Log($"Session event: StateChanged {state}");
            LogNetworkState("OnStateChanged");
        }

        private void OnSessionChanged()
        {
            Debug.Log("Session event: Changed");
            if (_session != null)
            {
                Debug.Log($"[Multiplayer] SessionChanged: Players={_session.Players?.Count ?? 0} CurrentPlayer={_session.CurrentPlayer?.Id}");
            }

            if (_allowSessionDrivenClientStart && _session != null && !_session.IsHost && IsGameStarted)
            {
                _ = TryStartClientFromSessionAsync();
            }
        }

        private void OnRemovedFromSession()
        {
            Debug.Log("Session event: RemovedFromSession");
            _allowSessionDrivenClientStart = false;
        }

        private void OnSessionPropertiesChanged()
        {
            Debug.Log("Session event: SessionPropertiesChanged");
            if (_session != null)
            {
                Debug.Log($"[Multiplayer] SessionProperties: game_started={IsGameStarted}");
            }

            if (!IsGameStarted)
            {
                return;
            }

            if (_session != null && _session.IsHost)
            {
                return;
            }

            if (_allowSessionDrivenClientStart)
            {
                _ = TryStartClientFromSessionAsync();
            }
        }

        private void OnPlayerPropertiesChanged()
        {
            Debug.Log("Session event: PlayerPropertiesChanged");
        }

        private void OnSessionDeleted()
        {
            Debug.Log("Session event: Deleted");
        }

        private async System.Threading.Tasks.Task<bool> EnsureServicesReady()
        {
            if (services != null)
            {
                await services.InitializeAsync();
                if (!services.IsInitialized || !services.IsSignedIn)
                {
                    Debug.LogWarning($"ServicesBootstrap not ready. Initialized={services.IsInitialized} SignedIn={services.IsSignedIn}");
                    return false;
                }
            }
            else
            {
                Debug.LogWarning("ServicesBootstrap not set. Add it to the scene for proper initialization.");
                return false;
            }

            return true;
        }

        private async System.Threading.Tasks.Task LeaveRegisteredSessionsAsync()
        {
            try
            {
                var service = MultiplayerService.Instance;
                var sessions = service?.Sessions;
                if (sessions == null || sessions.Count == 0)
                {
                    return;
                }

                Debug.Log($"[Multiplayer] Leaving {sessions.Count} registered session(s).");
                var toLeave = new List<ISession>(sessions.Values);
                foreach (var session in toLeave)
                {
                    if (session == null)
                    {
                        continue;
                    }

                    try
                    {
                        await session.LeaveAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Multiplayer] Leave registered session failed: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Multiplayer] LeaveRegisteredSessionsAsync failed: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task GracefulLeaveAsync(bool destroyNetworkManager)
        {
            _allowSessionDrivenClientStart = false;
            _clientConnectPending = false;
            _nextClientAttemptTime = 0f;
            await LeaveSessionAsync();
            ShutdownNetwork(destroyNetworkManager);
            // Give lobby/relay time to process disconnect before rejoin attempts.
            await System.Threading.Tasks.Task.Delay(750);
        }

        private bool GetBoolSessionProperty(string key)
        {
            if (_session == null || _session.Properties == null)
            {
                return false;
            }

            if (_session.Properties.TryGetValue(key, out var property) && property != null)
            {
                var value = property.Value;
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private string GetStringSessionProperty(string key)
        {
            if (_session == null || _session.Properties == null)
            {
                return string.Empty;
            }

            if (_session.Properties.TryGetValue(key, out var property) && property != null)
            {
                return property.Value ?? string.Empty;
            }

            return string.Empty;
        }

        private async System.Threading.Tasks.Task EnsureGameStartedPropertyAsync(bool started, string relayJoinCode = "")
        {
            if (_session == null || !_session.IsHost)
            {
                return;
            }

            var host = _session.AsHost();
            if (!string.IsNullOrWhiteSpace(relayJoinCode))
            {
                host.SetProperty(RelayJoinCodeKey, new SessionProperty(relayJoinCode, VisibilityPropertyOptions.Public));
            }
            host.SetProperty(GameStartedKey, new SessionProperty(started ? "1" : "0", VisibilityPropertyOptions.Public));
            await host.SavePropertiesAsync();
        }

        private async System.Threading.Tasks.Task TryStartClientFromSessionAsync()
        {
            if (_session == null)
            {
                return;
            }

            if (!_allowSessionDrivenClientStart)
            {
                return;
            }

            if (Time.unscaledTime < _nextClientAttemptTime)
            {
                return;
            }

            if (_isStartingClient || _clientConnectPending)
            {
                return;
            }

            var manager = NetworkManager.Singleton;
            if (manager != null && (manager.IsHost || manager.IsServer))
            {
                return;
            }

            if (manager != null && (manager.IsClient || manager.IsListening))
            {
                Debug.Log($"[Multiplayer] Client start skipped: connection already in progress/active. IsClient={manager.IsClient} IsListening={manager.IsListening}");
                return;
            }

            _isStartingClient = true;
            try
            {
                if (!await EnsureServicesReady())
                {
                    return;
                }

                EnsureNetworkManager();

                var joinCode = GetStringSessionProperty(RelayJoinCodeKey);
                if (string.IsNullOrWhiteSpace(joinCode))
                {
                    try
                    {
                        await _session.RefreshAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Multiplayer] Session refresh failed while waiting for relay join code: {ex.Message}");
                    }
                }

                joinCode = GetStringSessionProperty(RelayJoinCodeKey);
                if (string.IsNullOrWhiteSpace(joinCode))
                {
                    Debug.LogWarning("[Multiplayer] relay_join_code missing. Client cannot start.");
                    _nextClientAttemptTime = Time.unscaledTime + 1f;
                    return;
                }

                Debug.Log($"[Multiplayer] Starting client with relay join code {joinCode}.");
                var started = await StartClientWithRelayAsync(joinCode);
                if (started)
                {
                    _clientConnectPending = true;
                }
                else
                {
                    _nextClientAttemptTime = Time.unscaledTime + 1.5f;
                }
            }
            finally
            {
                _isStartingClient = false;
            }
        }

        private async System.Threading.Tasks.Task<string> StartHostWithRelayAsync()
        {
            EnsureNetworkManager();
            var manager = NetworkManager.Singleton;
            if (manager == null)
            {
                Debug.LogWarning("[Multiplayer] NetworkManager missing.");
                return string.Empty;
            }

            if (manager.IsListening || manager.IsHost || manager.IsServer)
            {
                var existingCode = GetStringSessionProperty(RelayJoinCodeKey);
                if (!string.IsNullOrWhiteSpace(existingCode))
                {
                    Debug.LogWarning("[Multiplayer] NetworkManager already listening. Using existing relay join code.");
                    _activeRelayJoinCode = existingCode;
                    return existingCode;
                }

                Debug.LogWarning("[Multiplayer] NetworkManager already listening without relay join code. Shutting down and restarting relay host.");
                manager.Shutdown();
                for (var i = 0; i < 20 && manager.IsListening; i++)
                {
                    await System.Threading.Tasks.Task.Delay(50);
                }
            }

            var transport = manager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogWarning("[Multiplayer] UnityTransport missing on NetworkManager.");
                return string.Empty;
            }

            try
            {
                var reconnectBuffer = Mathf.Max(0, relayReconnectBuffer);
                var maxConnections = Mathf.Max(1, (maxPlayers - 1) + reconnectBuffer);
                Debug.Log($"[Multiplayer] Creating Relay allocation (maxConnections={maxConnections})...");
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
                Debug.Log($"[Multiplayer] Relay allocation created. Region={allocation?.Region} Endpoints={allocation?.ServerEndpoints?.Count ?? 0}");
                var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                Debug.Log($"[Multiplayer] Relay join code acquired: {joinCode}");
                var relayData = allocation.ToRelayServerData(RelayProtocol.Default);
                transport.SetRelayServerData(relayData);

                var started = manager.StartHost();
                Debug.Log($"[Multiplayer] NetworkManager.StartHost returned {started}.");
                if (!started)
                {
                    Debug.LogWarning("[Multiplayer] NetworkManager.StartHost failed.");
                    return string.Empty;
                }

                _activeRelayJoinCode = joinCode;
                return joinCode;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Multiplayer] Relay host start failed: {ex.Message}");
                Debug.LogException(ex);
                return string.Empty;
            }
        }

        private async System.Threading.Tasks.Task<bool> StartClientWithRelayAsync(string joinCode)
        {
            if (string.IsNullOrWhiteSpace(joinCode))
            {
                return false;
            }

            EnsureNetworkManager();
            var manager = NetworkManager.Singleton;
            if (manager == null)
            {
                Debug.LogWarning("[Multiplayer] NetworkManager missing.");
                return false;
            }

            if (manager.IsListening || manager.IsClient || manager.IsHost || manager.IsServer)
            {
                Debug.Log($"[Multiplayer] StartClientWithRelayAsync skipped because NetworkManager is busy. IsClient={manager.IsClient} IsServer={manager.IsServer} IsHost={manager.IsHost} IsListening={manager.IsListening}");
                return false;
            }

            var transport = manager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogWarning("[Multiplayer] UnityTransport missing on NetworkManager.");
                return false;
            }

            try
            {
                Debug.Log($"[Multiplayer] Joining Relay allocation (code={joinCode})...");
                JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
                Debug.Log($"[Multiplayer] Relay join allocation received. Region={allocation?.Region} Endpoints={allocation?.ServerEndpoints?.Count ?? 0}");
                var relayData = allocation.ToRelayServerData(RelayProtocol.Default);
                transport.SetRelayServerData(relayData);
                var started = manager.StartClient();
                Debug.Log($"[Multiplayer] NetworkManager.StartClient returned {started}.");
                if (started)
                {
                    _activeRelayJoinCode = joinCode;
                }
                return started;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Multiplayer] Relay client start failed: {ex.Message}");
                Debug.LogException(ex);
                return false;
            }
        }

        private void LogNetworkState(string context)
        {
            if (!verboseLogging)
            {
                return;
            }

            var manager = NetworkManager.Singleton;
            if (manager == null)
            {
                Debug.Log($"[Multiplayer] {context}: NetworkManager missing.");
                return;
            }

            var transport = manager.NetworkConfig != null ? manager.NetworkConfig.NetworkTransport : null;
            Debug.Log($"[Multiplayer] {context}: NM present IsServer={manager.IsServer} IsClient={manager.IsClient} IsHost={manager.IsHost} " +
                      $"SceneMgmt={(manager.NetworkConfig != null && manager.NetworkConfig.EnableSceneManagement)} " +
                      $"Transport={(transport != null ? transport.GetType().Name : "null")}");
        }

        private void BindNetworkCallbacks(NetworkManager manager)
        {
            if (manager == null)
            {
                return;
            }

            if (_boundNetworkManager == manager && _networkCallbacksBound)
            {
                return;
            }

            if (_boundNetworkManager != null && _networkCallbacksBound)
            {
                _boundNetworkManager.OnClientConnectedCallback -= OnClientConnected;
                _boundNetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
                _boundNetworkManager.OnServerStarted -= OnServerStarted;
                _boundNetworkManager.OnTransportFailure -= OnTransportFailure;
            }

            _boundNetworkManager = manager;
            _boundNetworkManager.OnClientConnectedCallback += OnClientConnected;
            _boundNetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
            _boundNetworkManager.OnServerStarted += OnServerStarted;
            _boundNetworkManager.OnTransportFailure += OnTransportFailure;
            _networkCallbacksBound = true;
        }

        private void OnClientConnected(ulong clientId)
        {
            _clientConnectPending = false;
            _nextClientAttemptTime = 0f;
            Debug.Log($"[Multiplayer] Network client connected. ClientId={clientId} IsHost={NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost}");
        }

        private void OnClientDisconnected(ulong clientId)
        {
            _clientConnectPending = false;
            _nextClientAttemptTime = Time.unscaledTime + 1f;
            var reason = NetworkManager.Singleton != null ? NetworkManager.Singleton.DisconnectReason : string.Empty;
            Debug.LogWarning($"[Multiplayer] Network client disconnected. ClientId={clientId} Reason='{reason}'");
        }

        private void OnServerStarted()
        {
            Debug.Log("[Multiplayer] Network server started.");
        }

        private void OnTransportFailure()
        {
            _clientConnectPending = false;
            _nextClientAttemptTime = Time.unscaledTime + 1f;
            Debug.LogWarning("[Multiplayer] Network transport failure.");
        }
    }
}
