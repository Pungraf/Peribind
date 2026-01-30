using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine;

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

        private ISession _session;
        private bool _eventsBound;
        private bool _isPrimaryInstance;
        private bool _isLeaving;
        private bool _hasShutdown;
        private System.Threading.Tasks.Task _leaveTask;
        public const string GameStartedKey = "game_started";

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
                }.WithRelayNetwork();

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

            EnsureNetworkManager();
            ResetLocalSessionState();
            LogNetworkState("JoinSessionByCode (after reset)");

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

            EnsureNetworkManager();
            ResetLocalSessionState();
            LogNetworkState("QuickJoinOrCreate (after reset)");

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
                }.WithRelayNetwork();

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
            await LeaveSessionAsync();
            ShutdownNetwork(destroyNetworkManager);
        }

        private async System.Threading.Tasks.Task ShutdownAsync(bool destroyNetworkManager)
        {
            if (_hasShutdown)
            {
                return;
            }

            _hasShutdown = true;
            await LeaveSessionAsync();
            ShutdownNetwork(destroyNetworkManager);
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
                _leaveTask = null;
                return;
            }

            _isLeaving = true;
            var session = _session;

            try
            {
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
                await EnsureGameStartedPropertyAsync(true);
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

        private static void EnsureNetworkManager()
        {
            if (NetworkManager.Singleton != null)
            {
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
        }

        private void ResetLocalSessionState()
        {
            if (verboseLogging)
            {
                Debug.Log($"[Multiplayer] ResetLocalSessionState session={( _session != null ? _session.Id : "null")} eventsBound={_eventsBound}");
            }
            UnbindSessionEvents();
            _session = null;
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
        }

        private void OnRemovedFromSession()
        {
            Debug.Log("Session event: RemovedFromSession");
        }

        private void OnSessionPropertiesChanged()
        {
            Debug.Log("Session event: SessionPropertiesChanged");
            if (_session != null)
            {
                Debug.Log($"[Multiplayer] SessionProperties: game_started={IsGameStarted}");
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
                if (!services.IsInitialized)
                {
                    Debug.LogWarning("ServicesBootstrap failed to initialize.");
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

        private async System.Threading.Tasks.Task EnsureGameStartedPropertyAsync(bool started)
        {
            if (_session == null || !_session.IsHost)
            {
                return;
            }

            var host = _session.AsHost();
            host.SetProperty(GameStartedKey, new SessionProperty(started ? "1" : "0", VisibilityPropertyOptions.Public));
            await host.SavePropertiesAsync();
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
    }
}
