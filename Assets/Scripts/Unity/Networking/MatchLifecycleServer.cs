using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace Peribind.Unity.Networking
{
    public class MatchLifecycleServer : MonoBehaviour
    {
        [SerializeField] private MatchRegistryClient matchRegistry;
        [SerializeField] private float emptyShutdownDelaySeconds = 300f;
        [SerializeField] private string matchIdEnvironmentKey = "PERIBIND_MATCH_ID";
        [SerializeField] private string matchIdArgumentName = "-matchId";

        private NetworkManager _networkManager;
        private Coroutine _emptyShutdownRoutine;
        private bool _subscribed;
        private bool _shutdownStarted;
        private string _matchId = string.Empty;

        private void OnEnable()
        {
            TryInitialize();
        }

        private void Start()
        {
            TryInitialize();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void TryInitialize()
        {
            if (_subscribed || !IsDedicatedServerRuntime())
            {
                return;
            }

            _networkManager = NetworkManager.Singleton;
            if (_networkManager == null)
            {
                _networkManager = FindObjectOfType<NetworkManager>();
            }

            if (_networkManager == null)
            {
                return;
            }

            if (matchRegistry == null)
            {
                matchRegistry = FindObjectOfType<MatchRegistryClient>(true);
            }

            if (matchRegistry == null)
            {
                matchRegistry = _networkManager.gameObject.AddComponent<MatchRegistryClient>();
            }

            _matchId = ResolveMatchId();
            if (string.IsNullOrWhiteSpace(_matchId))
            {
                Debug.LogWarning("[MatchLifecycle] No match id found (env/args). match/end notify will be skipped.");
            }
            else
            {
                Debug.Log($"[MatchLifecycle] Running for matchId={_matchId}");
            }

            _networkManager.OnClientConnectedCallback += OnClientConnected;
            _networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            _subscribed = true;

            EvaluateEmptyState();
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _networkManager == null)
            {
                return;
            }

            _networkManager.OnClientConnectedCallback -= OnClientConnected;
            _networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            _subscribed = false;
        }

        private void OnClientConnected(ulong _)
        {
            EvaluateEmptyState();
        }

        private void OnClientDisconnected(ulong _)
        {
            EvaluateEmptyState();
        }

        private void EvaluateEmptyState()
        {
            if (_shutdownStarted)
            {
                return;
            }

            var playerCount = GetConnectedPlayerCount();
            if (playerCount > 0)
            {
                if (_emptyShutdownRoutine != null)
                {
                    StopCoroutine(_emptyShutdownRoutine);
                    _emptyShutdownRoutine = null;
                    Debug.Log("[MatchLifecycle] Player reconnected. Empty shutdown timer cancelled.");
                }

                return;
            }

            if (_emptyShutdownRoutine == null)
            {
                _emptyShutdownRoutine = StartCoroutine(ShutdownAfterEmptyDelay());
                Debug.Log($"[MatchLifecycle] No connected players. Shutdown in {emptyShutdownDelaySeconds:0}s unless players return.");
            }
        }

        private IEnumerator ShutdownAfterEmptyDelay()
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, emptyShutdownDelaySeconds));
            _emptyShutdownRoutine = null;

            if (_shutdownStarted || GetConnectedPlayerCount() > 0)
            {
                yield break;
            }

            _shutdownStarted = true;
            yield return ShutdownServerFlow();
        }

        private IEnumerator ShutdownServerFlow()
        {
            if (matchRegistry != null && !string.IsNullOrWhiteSpace(_matchId))
            {
                var endTask = matchRegistry.EndMatchAsync(_matchId);
                while (!endTask.IsCompleted)
                {
                    yield return null;
                }

                if (endTask.IsFaulted)
                {
                    Debug.LogWarning($"[MatchLifecycle] match/end failed: {endTask.Exception?.GetBaseException().Message}");
                }
            }

            if (_networkManager != null && (_networkManager.IsListening || _networkManager.IsServer || _networkManager.IsClient))
            {
                _networkManager.Shutdown();
                while (_networkManager.ShutdownInProgress)
                {
                    yield return null;
                }
            }

            Debug.Log("[MatchLifecycle] Empty match timeout reached. Exiting process.");
            global::UnityEngine.Application.Quit(0);
        }

        private int GetConnectedPlayerCount()
        {
            if (_networkManager == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var clientId in _networkManager.ConnectedClientsIds)
            {
                if (clientId == NetworkManager.ServerClientId)
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        private string ResolveMatchId()
        {
            var fromEnv = Environment.GetEnvironmentVariable(matchIdEnvironmentKey);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                return fromEnv.Trim();
            }

            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], matchIdArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = args[i + 1];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static bool IsDedicatedServerRuntime()
        {
#if UNITY_SERVER
            return true;
#else
            return global::UnityEngine.Application.isBatchMode;
#endif
        }
    }
}
