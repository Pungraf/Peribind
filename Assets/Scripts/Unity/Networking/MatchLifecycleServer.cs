using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Peribind.Unity.Networking
{
    public class MatchLifecycleServer : MonoBehaviour
    {
        [SerializeField] private MatchRegistryClient matchRegistry;
        [SerializeField] private float emptyShutdownDelaySeconds = 300f;
        [SerializeField] private bool shutdownOnGameOver = true;
        [SerializeField] private float gameOverShutdownDelaySeconds = 8f;
        [SerializeField] private string matchIdEnvironmentKey = "PERIBIND_MATCH_ID";
        [SerializeField] private string matchIdArgumentName = "-matchId";

        private NetworkManager _networkManager;
        private NetworkGameController _gameController;
        private Coroutine _emptyShutdownRoutine;
        private Coroutine _gameOverShutdownRoutine;
        private bool _subscribed;
        private bool _shutdownStarted;
        private bool _resultSubmitted;
        private string _matchId = string.Empty;
        private readonly Dictionary<ulong, string> _authByClientId = new Dictionary<ulong, string>();

        private void OnEnable()
        {
            TryInitialize();
        }

        private void Start()
        {
            TryInitialize();
        }

        private void Update()
        {
            if (!_subscribed || _shutdownStarted || !shutdownOnGameOver)
            {
                return;
            }

            if (_gameController == null)
            {
                _gameController = FindObjectOfType<NetworkGameController>();
                if (_gameController == null)
                {
                    return;
                }
            }

            var session = _gameController.Session;
            if (session == null || !session.IsGameOver)
            {
                return;
            }

            if (_gameOverShutdownRoutine == null)
            {
                if (_emptyShutdownRoutine != null)
                {
                    StopCoroutine(_emptyShutdownRoutine);
                    _emptyShutdownRoutine = null;
                }

                _gameOverShutdownRoutine = StartCoroutine(ShutdownAfterGameOverDelay());
                Debug.Log($"[MatchLifecycle] Game over detected. Shutdown in {gameOverShutdownDelaySeconds:0}s.");
            }
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

            SeedConnectedPresenceFromExistingClients();
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

        private void OnClientConnected(ulong clientId)
        {
            EvaluateEmptyState();
            if (_shutdownStarted || clientId == NetworkManager.ServerClientId)
            {
                return;
            }

            if (TryResolveAuthId(clientId, out var playerId))
            {
                _authByClientId[clientId] = playerId;
                _ = ReportPresenceAsync(playerId, true);
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            EvaluateEmptyState();
            if (clientId == NetworkManager.ServerClientId)
            {
                return;
            }

            string playerId = string.Empty;
            if (!TryResolveAuthId(clientId, out playerId))
            {
                _authByClientId.TryGetValue(clientId, out playerId);
            }

            _authByClientId.Remove(clientId);
            if (!string.IsNullOrWhiteSpace(playerId))
            {
                _ = ReportPresenceAsync(playerId, false);
            }
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

        private IEnumerator ShutdownAfterGameOverDelay()
        {
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, gameOverShutdownDelaySeconds));
            _gameOverShutdownRoutine = null;

            if (_shutdownStarted)
            {
                yield break;
            }

            _shutdownStarted = true;
            yield return ShutdownServerFlow();
        }

        private IEnumerator ShutdownServerFlow()
        {
            var resultTask = SubmitMatchResultIfNeededAsync();
            while (!resultTask.IsCompleted)
            {
                yield return null;
            }

            var markDisconnectedTask = ReportKnownPresenceDisconnectedAsync();
            while (!markDisconnectedTask.IsCompleted)
            {
                yield return null;
            }

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

        private async System.Threading.Tasks.Task SubmitMatchResultIfNeededAsync()
        {
            if (_resultSubmitted || string.IsNullOrWhiteSpace(_matchId) || matchRegistry == null)
            {
                return;
            }

            if (_gameController == null)
            {
                _gameController = FindObjectOfType<NetworkGameController>();
            }

            var session = _gameController != null ? _gameController.Session : null;
            if (session == null || !session.IsGameOver)
            {
                return;
            }

            var authToPlayer = NetworkGameController.GetAuthPlayerAssignmentsSnapshot();
            if (authToPlayer == null || authToPlayer.Count == 0)
            {
                Debug.LogWarning("[MatchLifecycle] Skipping result submit: no auth/player assignments.");
                return;
            }

            var players = new List<MatchRegistryClient.MatchResultPlayerEntry>();
            foreach (var pair in authToPlayer)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                var score = 0;
                var slot = pair.Value;
                if (session.TotalScores != null && slot >= 0 && slot < session.TotalScores.Length)
                {
                    score = session.TotalScores[slot];
                }

                players.Add(new MatchRegistryClient.MatchResultPlayerEntry
                {
                    playerId = pair.Key,
                    playerSlot = slot,
                    score = score
                });
            }

            if (players.Count == 0)
            {
                Debug.LogWarning("[MatchLifecycle] Skipping result submit: no valid players.");
                return;
            }

            var winnerPlayerId = ResolveAuthIdForSlot(authToPlayer, session.WinningPlayerId);
            var surrenderingPlayerId = ResolveAuthIdForSlot(authToPlayer, session.SurrenderingPlayerId);
            var ok = await matchRegistry.SubmitMatchResultAsync(
                _matchId,
                winnerPlayerId,
                session.WasSurrendered,
                surrenderingPlayerId,
                players);

            if (!ok)
            {
                Debug.LogWarning($"[MatchLifecycle] match/result failed: {matchRegistry.LastErrorMessage}");
                return;
            }

            _resultSubmitted = true;
            Debug.Log($"[MatchLifecycle] match/result submitted matchId={_matchId} players={players.Count}");
        }

        private static string ResolveAuthIdForSlot(IReadOnlyDictionary<string, int> authToPlayer, int slot)
        {
            if (authToPlayer == null || slot < 0)
            {
                return string.Empty;
            }

            foreach (var pair in authToPlayer)
            {
                if (pair.Value == slot)
                {
                    return pair.Key ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private void SeedConnectedPresenceFromExistingClients()
        {
            if (_networkManager == null)
            {
                return;
            }

            foreach (var clientId in _networkManager.ConnectedClientsIds)
            {
                if (clientId == NetworkManager.ServerClientId)
                {
                    continue;
                }

                if (!TryResolveAuthId(clientId, out var playerId))
                {
                    continue;
                }

                _authByClientId[clientId] = playerId;
                _ = ReportPresenceAsync(playerId, true);
            }
        }

        private bool TryResolveAuthId(ulong clientId, out string playerId)
        {
            if (NetworkGameController.TryGetConnectedAuthId(clientId, out playerId))
            {
                return true;
            }

            playerId = string.Empty;
            return false;
        }

        private async System.Threading.Tasks.Task ReportKnownPresenceDisconnectedAsync()
        {
            if (string.IsNullOrWhiteSpace(_matchId) || matchRegistry == null)
            {
                return;
            }

            var uniquePlayers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in _authByClientId.Values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    uniquePlayers.Add(value);
                }
            }

            if (_networkManager != null)
            {
                foreach (var clientId in _networkManager.ConnectedClientsIds)
                {
                    if (clientId == NetworkManager.ServerClientId)
                    {
                        continue;
                    }

                    if (TryResolveAuthId(clientId, out var playerId) && !string.IsNullOrWhiteSpace(playerId))
                    {
                        uniquePlayers.Add(playerId);
                    }
                }
            }

            foreach (var playerId in uniquePlayers)
            {
                await ReportPresenceAsync(playerId, false);
            }
        }

        private async System.Threading.Tasks.Task ReportPresenceAsync(string playerId, bool connected)
        {
            if (string.IsNullOrWhiteSpace(_matchId) || matchRegistry == null || string.IsNullOrWhiteSpace(playerId))
            {
                return;
            }

            var ok = await matchRegistry.SetMatchPresenceAsync(_matchId, playerId, connected);
            if (!ok)
            {
                Debug.LogWarning(
                    $"[MatchLifecycle] Presence update failed matchId={_matchId} player={playerId} connected={connected} error={matchRegistry.LastErrorMessage}");
            }
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
