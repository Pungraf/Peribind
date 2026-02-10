using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;

namespace Peribind.Unity.Networking
{
    public class LobbyServiceController : MonoBehaviour
    {
        [SerializeField] private UgsBootstrap ugsBootstrap;
        [SerializeField] private float heartbeatIntervalSeconds = 15f;
        [SerializeField] private float lobbyRefreshIntervalSeconds = 8f;
        [SerializeField] private int writeRateLimitRetryCount = 4;
        [SerializeField] private float writeRateLimitRetryDelaySeconds = 1.0f;
        [SerializeField] private int queryRetryCount = 1;
        [SerializeField] private float queryRetryDelaySeconds = 1.0f;

        public Lobby CurrentLobby { get; private set; }
        public event Action<Lobby> LobbyUpdated;
        public event Action<List<Lobby>> LobbiesQueried;
        public event Action<string> LobbyError;

        private Coroutine _heartbeatRoutine;
        private Coroutine _refreshRoutine;

        private async void Awake()
        {
            if (ugsBootstrap == null)
            {
                ugsBootstrap = FindObjectOfType<UgsBootstrap>();
            }

            if (ugsBootstrap != null)
            {
                await ugsBootstrap.EnsureInitializedAsync();
            }
        }

        public async Task<Lobby> CreateLobbyAsync(string lobbyName, int maxPlayers, string map, string mode, string region)
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return null;
            }

            try
            {
                var lobbyData = new Dictionary<string, DataObject>
                {
                    ["map"] = new DataObject(DataObject.VisibilityOptions.Public, map ?? string.Empty, DataObject.IndexOptions.S1),
                    ["mode"] = new DataObject(DataObject.VisibilityOptions.Public, mode ?? string.Empty, DataObject.IndexOptions.S2),
                    ["region"] = new DataObject(DataObject.VisibilityOptions.Public, region ?? string.Empty, DataObject.IndexOptions.S3),
                };

                var player = BuildPlayer();
                CurrentLobby = await LobbyService.Instance.CreateLobbyAsync(
                    lobbyName,
                    maxPlayers,
                    new CreateLobbyOptions
                    {
                        IsPrivate = false,
                        Data = lobbyData,
                        Player = player
                    });

                StartHeartbeat();
                StartRefresh();
                LobbyUpdated?.Invoke(CurrentLobby);
                return CurrentLobby;
            }
            catch (Exception ex)
            {
                LobbyError?.Invoke(ex.Message);
                Debug.LogWarning($"[Lobby] Create failed: {ex.Message}");
                return null;
            }
        }

        public async Task<Lobby> JoinLobbyByCodeAsync(string code)
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return null;
            }

            try
            {
                var player = BuildPlayer();
                CurrentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(code, new JoinLobbyByCodeOptions
                {
                    Player = player
                });

                StopHeartbeat();
                StartRefresh();
                LobbyUpdated?.Invoke(CurrentLobby);
                return CurrentLobby;
            }
            catch (Exception ex)
            {
                LobbyError?.Invoke(ex.Message);
                Debug.LogWarning($"[Lobby] Join by code failed: {ex.Message}");
                return null;
            }
        }

        public async Task<Lobby> JoinLobbyByIdAsync(string lobbyId)
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return null;
            }

            try
            {
                var player = BuildPlayer();
                CurrentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(lobbyId, new JoinLobbyByIdOptions
                {
                    Player = player
                });

                StopHeartbeat();
                StartRefresh();
                LobbyUpdated?.Invoke(CurrentLobby);
                return CurrentLobby;
            }
            catch (Exception ex)
            {
                LobbyError?.Invoke(ex.Message);
                Debug.LogWarning($"[Lobby] Join by id failed: {ex.Message}");
                return null;
            }
        }

        public async Task<Lobby> GetLobbyByIdAsync(string lobbyId)
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return null;
            }

            try
            {
                CurrentLobby = await LobbyService.Instance.GetLobbyAsync(lobbyId);
                StartRefresh();
                LobbyUpdated?.Invoke(CurrentLobby);
                return CurrentLobby;
            }
            catch (Exception ex)
            {
                LobbyError?.Invoke(ex.Message);
                Debug.LogWarning($"[Lobby] Get lobby failed: {ex.Message}");
                return null;
            }
        }

        public async Task<Lobby> QuickJoinAsync(string map, string mode, string region)
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return null;
            }

            try
            {
                var filters = new List<QueryFilter>
                {
                    new QueryFilter(QueryFilter.FieldOptions.AvailableSlots, "0", QueryFilter.OpOptions.GT),
                };

                if (!string.IsNullOrWhiteSpace(map))
                {
                    filters.Add(new QueryFilter(QueryFilter.FieldOptions.S1, map, QueryFilter.OpOptions.EQ));
                }

                if (!string.IsNullOrWhiteSpace(mode))
                {
                    filters.Add(new QueryFilter(QueryFilter.FieldOptions.S2, mode, QueryFilter.OpOptions.EQ));
                }

                if (!string.IsNullOrWhiteSpace(region))
                {
                    filters.Add(new QueryFilter(QueryFilter.FieldOptions.S3, region, QueryFilter.OpOptions.EQ));
                }

                var player = BuildPlayer();
                CurrentLobby = await LobbyService.Instance.QuickJoinLobbyAsync(new QuickJoinLobbyOptions
                {
                    Filter = filters,
                    Player = player
                });

                StopHeartbeat();
                StartRefresh();
                LobbyUpdated?.Invoke(CurrentLobby);
                return CurrentLobby;
            }
            catch (LobbyServiceException ex)
            {
                LobbyError?.Invoke(ex.Message);
                Debug.LogWarning($"[Lobby] QuickJoin failed: {ex.Message}");
                return null;
            }
        }

        public async Task<List<Lobby>> QueryLobbiesAsync(string map, string mode, string region)
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return new List<Lobby>();
            }

            var attempts = Mathf.Max(1, queryRetryCount + 1);
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    var filters = new List<QueryFilter>();

                    if (!string.IsNullOrWhiteSpace(map))
                    {
                        filters.Add(new QueryFilter(QueryFilter.FieldOptions.S1, map, QueryFilter.OpOptions.EQ));
                    }

                    if (!string.IsNullOrWhiteSpace(mode))
                    {
                        filters.Add(new QueryFilter(QueryFilter.FieldOptions.S2, mode, QueryFilter.OpOptions.EQ));
                    }

                    if (!string.IsNullOrWhiteSpace(region))
                    {
                        filters.Add(new QueryFilter(QueryFilter.FieldOptions.S3, region, QueryFilter.OpOptions.EQ));
                    }

                    var response = await LobbyService.Instance.QueryLobbiesAsync(new QueryLobbiesOptions
                    {
                        Filters = filters,
                        Count = 25
                    });

                    var results = response.Results ?? new List<Lobby>();
                    LobbiesQueried?.Invoke(results);
                    return results;
                }
                catch (LobbyServiceException ex) when (ex.Reason == LobbyExceptionReason.RateLimited)
                {
                    if (attempt >= attempts)
                    {
                        Debug.LogWarning("[Lobby] Query rate-limited.");
                        return new List<Lobby>();
                    }

                    var wait = Mathf.Max(0.2f, queryRetryDelaySeconds) * attempt;
                    Debug.LogWarning($"[Lobby] Query rate-limited, retrying in {wait:0.0}s (attempt {attempt}/{attempts}).");
                    await Task.Delay(TimeSpan.FromSeconds(wait));
                }
                catch (LobbyServiceException ex)
                {
                    var transient = ex.Message != null &&
                                    ex.Message.IndexOf("Internal Server Error", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (transient && attempt < attempts)
                    {
                        var wait = Mathf.Max(0.2f, queryRetryDelaySeconds) * attempt;
                        Debug.LogWarning($"[Lobby] Query transient backend error ({ex.Reason}), retrying in {wait:0.0}s (attempt {attempt}/{attempts}).");
                        await Task.Delay(TimeSpan.FromSeconds(wait));
                        continue;
                    }

                    LobbyError?.Invoke(ex.Message);
                    Debug.LogWarning($"[Lobby] Query failed ({ex.Reason}): {ex.Message}");
                    return new List<Lobby>();
                }
                catch (Exception ex)
                {
                    LobbyError?.Invoke(ex.Message);
                    Debug.LogWarning($"[Lobby] Query failed: {ex.Message}");
                    return new List<Lobby>();
                }
            }

            return new List<Lobby>();
        }

        public async Task LeaveLobbyAsync()
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return;
            }

            if (CurrentLobby == null)
            {
                return;
            }

            try
            {
                StopHeartbeat();
                StopRefresh();
                await LobbyService.Instance.RemovePlayerAsync(CurrentLobby.Id, AuthenticationService.Instance.PlayerId);
                CurrentLobby = null;
                LobbyUpdated?.Invoke(null);
            }
            catch (Exception ex)
            {
                LobbyError?.Invoke(ex.Message);
                Debug.LogWarning($"[Lobby] Leave failed: {ex.Message}");
            }
        }

        public async Task<Lobby> SetPlayerReadyAsync(bool isReady)
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return null;
            }

            if (CurrentLobby == null)
            {
                return null;
            }

            var data = new Dictionary<string, PlayerDataObject>
            {
                ["ready"] = new PlayerDataObject(PlayerDataObject.VisibilityOptions.Member, isReady ? "1" : "0")
            };

            var result = await ExecuteLobbyWriteWithRetryAsync(
                () => LobbyService.Instance.UpdatePlayerAsync(
                    CurrentLobby.Id,
                    AuthenticationService.Instance.PlayerId,
                    new UpdatePlayerOptions
                    {
                        Data = data
                    }),
                "Set ready");

            if (result != null)
            {
                CurrentLobby = result;
                LobbyUpdated?.Invoke(CurrentLobby);
            }

            return result;
        }

        public async Task<Lobby> SetServerInfoAsync(string serverIp, int serverPort, string matchId)
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return null;
            }

            if (CurrentLobby == null)
            {
                return null;
            }

            var data = new Dictionary<string, DataObject>
            {
                ["server_ip"] = new DataObject(DataObject.VisibilityOptions.Public, serverIp ?? string.Empty),
                ["server_port"] = new DataObject(DataObject.VisibilityOptions.Public, serverPort.ToString()),
                ["match_id"] = new DataObject(DataObject.VisibilityOptions.Public, matchId ?? string.Empty)
            };

            var result = await ExecuteLobbyWriteWithRetryAsync(
                () => LobbyService.Instance.UpdateLobbyAsync(CurrentLobby.Id, new UpdateLobbyOptions
                {
                    Data = data
                }),
                "Set server info");

            if (result != null)
            {
                CurrentLobby = result;
                LobbyUpdated?.Invoke(CurrentLobby);
            }

            return result;
        }

        private Player BuildPlayer()
        {
            return new Player(AuthenticationService.Instance.PlayerId);
        }

        private void StartHeartbeat()
        {
            StopHeartbeat();
            _heartbeatRoutine = StartCoroutine(HeartbeatRoutine());
        }

        private void StopHeartbeat()
        {
            if (_heartbeatRoutine != null)
            {
                StopCoroutine(_heartbeatRoutine);
                _heartbeatRoutine = null;
            }
        }

        private void StartRefresh()
        {
            StopRefresh();
            _refreshRoutine = StartCoroutine(RefreshRoutine());
        }

        private void StopRefresh()
        {
            if (_refreshRoutine != null)
            {
                StopCoroutine(_refreshRoutine);
                _refreshRoutine = null;
            }
        }

        public void PauseLobbyRefresh()
        {
            StopRefresh();
        }

        private IEnumerator HeartbeatRoutine()
        {
            while (CurrentLobby != null)
            {
                yield return new WaitForSecondsRealtime(heartbeatIntervalSeconds);
                if (CurrentLobby == null)
                {
                    continue;
                }

                _ = SendHeartbeatAsync(CurrentLobby.Id);
            }
        }

        private IEnumerator RefreshRoutine()
        {
            while (CurrentLobby != null)
            {
                yield return new WaitForSecondsRealtime(lobbyRefreshIntervalSeconds);
                if (CurrentLobby == null) continue;
                _ = RefreshLobbyAsync(CurrentLobby.Id);
            }
        }

        private async Task RefreshLobbyAsync(string lobbyId)
        {
            try
            {
                var refreshed = await LobbyService.Instance.GetLobbyAsync(lobbyId);
                if (refreshed != null)
                {
                    CurrentLobby = refreshed;
                    LobbyUpdated?.Invoke(CurrentLobby);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Lobby] Refresh failed: {ex.Message}");
            }
        }

        private async Task SendHeartbeatAsync(string lobbyId)
        {
            try
            {
                await LobbyService.Instance.SendHeartbeatPingAsync(lobbyId);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Lobby] Heartbeat failed: {ex.Message}");
            }
        }

        private async Task<bool> EnsureLobbyReadyAsync()
        {
            if (ugsBootstrap == null)
            {
                ugsBootstrap = FindObjectOfType<UgsBootstrap>();
            }

            if (ugsBootstrap != null)
            {
                await ugsBootstrap.EnsureInitializedAsync();
            }

            var initialized = UnityServices.State == ServicesInitializationState.Initialized;
            var signedIn = false;
            if (initialized)
            {
                try
                {
                    signedIn = AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn;
                }
                catch
                {
                    signedIn = false;
                }
            }

            if (!initialized || !signedIn)
            {
                LobbyError?.Invoke("UGS is not ready yet. Please wait a second and retry.");
                return false;
            }

            return true;
        }

        private async Task<Lobby> ExecuteLobbyWriteWithRetryAsync(Func<Task<Lobby>> action, string operationName)
        {
            var attempts = Mathf.Max(1, writeRateLimitRetryCount + 1);
            var delaySeconds = Mathf.Max(0.1f, writeRateLimitRetryDelaySeconds);

            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (LobbyServiceException ex) when (ex.Reason == LobbyExceptionReason.RateLimited)
                {
                    if (attempt >= attempts)
                    {
                        LobbyError?.Invoke("Lobby is busy. Please retry.");
                        Debug.LogWarning($"[Lobby] {operationName} rate-limited after {attempt} attempts.");
                        return null;
                    }

                    var waitSeconds = delaySeconds * attempt;
                    Debug.LogWarning($"[Lobby] {operationName} rate-limited, retrying in {waitSeconds:0.0}s (attempt {attempt}/{attempts}).");
                    await Task.Delay(TimeSpan.FromSeconds(waitSeconds));
                }
                catch (Exception ex)
                {
                    LobbyError?.Invoke(ex.Message);
                    Debug.LogWarning($"[Lobby] {operationName} failed: {ex.Message}");
                    return null;
                }
            }

            return null;
        }
    }
}
