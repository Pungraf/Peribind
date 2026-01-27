using System;
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

        private ISession _session;
        private bool _eventsBound;

        private void Awake()
        {
            // Keep a single session controller across scene loads.
            var existing = FindObjectsOfType<MultiplayerSessionController>();
            if (existing.Length > 1)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
        }

        public string CurrentSessionId => _session != null ? _session.Id : string.Empty;
        public string CurrentSessionCode => _session != null ? _session.Code : string.Empty;
        public bool HasSession => _session != null;

        public async void CreateSession()
        {
            if (!await EnsureServicesReady())
            {
                return;
            }

            try
            {
                var options = new SessionOptions
                {
                    MaxPlayers = maxPlayers,
                    Type = sessionType
                }.WithRelayNetwork();

                _session = await MultiplayerService.Instance.CreateSessionAsync(options);
                BindSessionEvents();
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

            if (!await EnsureServicesReady())
            {
                return;
            }

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
                    Type = sessionType
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
            if (_session == null)
            {
                return;
            }

            try
            {
                await _session.LeaveAsync();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                UnbindSessionEvents();
                _session = null;
            }
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
        }

        private void OnSessionChanged()
        {
            Debug.Log("Session event: Changed");
        }

        private void OnRemovedFromSession()
        {
            Debug.Log("Session event: RemovedFromSession");
        }

        private void OnSessionPropertiesChanged()
        {
            Debug.Log("Session event: SessionPropertiesChanged");
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
            }
            else
            {
                Debug.LogWarning("ServicesBootstrap not set. Add it to the scene for proper initialization.");
            }

            return true;
        }
    }
}
