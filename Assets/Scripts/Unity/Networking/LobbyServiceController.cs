using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace Peribind.Unity.Networking
{
    public class LobbyServiceController : MonoBehaviour
    {
        [SerializeField] private UgsBootstrap ugsBootstrap;
        [SerializeField] private MatchRegistryClient matchRegistry;
        [SerializeField] private float lobbyRefreshIntervalSeconds = 2.5f;
        [SerializeField] private int queryRetryCount = 1;
        [SerializeField] private float queryRetryDelaySeconds = 0.75f;

        public MatchRegistryClient.LobbyInfo CurrentLobby { get; private set; }
        public event Action<MatchRegistryClient.LobbyInfo> LobbyUpdated;
        public event Action<List<MatchRegistryClient.LobbyInfo>> LobbiesQueried;
        public event Action<string> LobbyError;

        private Coroutine _refreshRoutine;

        private async void Awake()
        {
            if (ugsBootstrap == null)
            {
                ugsBootstrap = FindObjectOfType<UgsBootstrap>();
            }

            if (matchRegistry == null)
            {
                matchRegistry = FindObjectOfType<MatchRegistryClient>();
            }

            if (ugsBootstrap != null)
            {
                await ugsBootstrap.EnsureInitializedAsync();
            }
        }

        public async Task<MatchRegistryClient.LobbyInfo> CreateLobbyAsync(string lobbyName, int maxPlayers, string map, string mode, string region)
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return null;
            }

            try
            {
                var lobby = await matchRegistry.CreateLobbyAsync(GetLocalPlayerId(), lobbyName, maxPlayers, map, mode, region);
                if (lobby == null)
                {
                    LobbyError?.Invoke(ResolveLobbyError("Failed to create lobby."));
                    return null;
                }

                CurrentLobby = lobby;
                StartRefresh();
                LobbyUpdated?.Invoke(CurrentLobby);
                return CurrentLobby;
            }
            catch (Exception ex)
            {
                LobbyError?.Invoke(ex.Message);
                return null;
            }
        }

        public async Task<MatchRegistryClient.LobbyInfo> JoinLobbyByCodeAsync(string code)
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return null;
            }

            try
            {
                var lobby = await matchRegistry.JoinLobbyByCodeAsync(GetLocalPlayerId(), code);
                if (lobby == null)
                {
                    LobbyError?.Invoke(ResolveLobbyError("Failed to join lobby by code."));
                    return null;
                }

                CurrentLobby = lobby;
                StartRefresh();
                LobbyUpdated?.Invoke(CurrentLobby);
                return CurrentLobby;
            }
            catch (Exception ex)
            {
                LobbyError?.Invoke(ex.Message);
                return null;
            }
        }

        public async Task<MatchRegistryClient.LobbyInfo> JoinLobbyByIdAsync(string lobbyId)
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return null;
            }

            try
            {
                var lobby = await matchRegistry.JoinLobbyByIdAsync(GetLocalPlayerId(), lobbyId);
                if (lobby == null)
                {
                    LobbyError?.Invoke(ResolveLobbyError("Failed to join lobby."));
                    return null;
                }

                CurrentLobby = lobby;
                StartRefresh();
                LobbyUpdated?.Invoke(CurrentLobby);
                return CurrentLobby;
            }
            catch (Exception ex)
            {
                LobbyError?.Invoke(ex.Message);
                return null;
            }
        }

        public async Task<MatchRegistryClient.LobbyInfo> GetLobbyByIdAsync(string lobbyId)
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return null;
            }

            try
            {
                var lobby = await matchRegistry.GetLobbyByIdAsync(lobbyId);
                if (lobby == null)
                {
                    LobbyError?.Invoke(ResolveLobbyError("Lobby not found."));
                    return null;
                }

                CurrentLobby = lobby;
                StartRefresh();
                LobbyUpdated?.Invoke(CurrentLobby);
                return CurrentLobby;
            }
            catch (Exception ex)
            {
                LobbyError?.Invoke(ex.Message);
                return null;
            }
        }

        public async Task<List<MatchRegistryClient.LobbyInfo>> QueryLobbiesAsync(string map, string mode, string region)
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return new List<MatchRegistryClient.LobbyInfo>();
            }

            var attempts = Mathf.Max(1, queryRetryCount + 1);
            for (var attempt = 1; attempt <= attempts; attempt++)
            {
                try
                {
                    var lobbies = await matchRegistry.QueryLobbiesAsync(map, mode, region);
                    var results = lobbies ?? new List<MatchRegistryClient.LobbyInfo>();
                    LobbiesQueried?.Invoke(results);
                    return results;
                }
                catch (Exception ex)
                {
                    if (attempt < attempts)
                    {
                        var wait = Mathf.Max(0.15f, queryRetryDelaySeconds) * attempt;
                        await Task.Delay(TimeSpan.FromSeconds(wait));
                        continue;
                    }

                    LobbyError?.Invoke(ex.Message);
                    return new List<MatchRegistryClient.LobbyInfo>();
                }
            }

            return new List<MatchRegistryClient.LobbyInfo>();
        }

        public async Task LeaveLobbyAsync()
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return;
            }

            if (CurrentLobby == null || string.IsNullOrWhiteSpace(CurrentLobby.id))
            {
                return;
            }

            try
            {
                StopRefresh();
                await matchRegistry.LeaveLobbyAsync(CurrentLobby.id, GetLocalPlayerId());
            }
            catch (Exception ex)
            {
                LobbyError?.Invoke(ex.Message);
            }
            finally
            {
                CurrentLobby = null;
                LobbyUpdated?.Invoke(null);
            }
        }

        public async Task<MatchRegistryClient.LobbyInfo> SetPlayerReadyAsync(bool isReady)
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return null;
            }

            if (CurrentLobby == null || string.IsNullOrWhiteSpace(CurrentLobby.id))
            {
                return null;
            }

            try
            {
                var updated = await matchRegistry.SetPlayerReadyAsync(CurrentLobby.id, GetLocalPlayerId(), isReady);
                if (updated != null)
                {
                    CurrentLobby = updated;
                    LobbyUpdated?.Invoke(CurrentLobby);
                }
                else
                {
                    LobbyError?.Invoke(ResolveLobbyError("Failed to update ready state."));
                }

                return updated;
            }
            catch (Exception ex)
            {
                LobbyError?.Invoke(ex.Message);
                return null;
            }
        }

        public async Task<MatchRegistryClient.LobbyInfo> SetServerInfoAsync(string serverIp, int serverPort, string matchId)
        {
            if (!await EnsureLobbyReadyAsync())
            {
                return null;
            }

            if (CurrentLobby == null || string.IsNullOrWhiteSpace(CurrentLobby.id))
            {
                return null;
            }

            try
            {
                var updated = await matchRegistry.SetServerInfoAsync(CurrentLobby.id, GetLocalPlayerId(), serverIp, serverPort, matchId);
                if (updated != null)
                {
                    CurrentLobby = updated;
                    LobbyUpdated?.Invoke(CurrentLobby);
                }
                else
                {
                    LobbyError?.Invoke(ResolveLobbyError("Failed to publish server info."));
                }

                return updated;
            }
            catch (Exception ex)
            {
                LobbyError?.Invoke(ex.Message);
                return null;
            }
        }

        public void PauseLobbyRefresh()
        {
            StopRefresh();
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

        private IEnumerator RefreshRoutine()
        {
            while (CurrentLobby != null && !string.IsNullOrWhiteSpace(CurrentLobby.id))
            {
                yield return new WaitForSecondsRealtime(lobbyRefreshIntervalSeconds);
                if (CurrentLobby == null || string.IsNullOrWhiteSpace(CurrentLobby.id))
                {
                    continue;
                }

                _ = RefreshLobbyAsync(CurrentLobby.id);
            }
        }

        private async Task RefreshLobbyAsync(string lobbyId)
        {
            try
            {
                var refreshed = await matchRegistry.GetLobbyByIdAsync(lobbyId);
                if (refreshed == null)
                {
                    CurrentLobby = null;
                    StopRefresh();
                    LobbyUpdated?.Invoke(null);
                    return;
                }

                CurrentLobby = refreshed;
                LobbyUpdated?.Invoke(CurrentLobby);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Lobby] Refresh failed: {ex.Message}");
            }
        }

        private async Task<bool> EnsureLobbyReadyAsync()
        {
            if (ugsBootstrap == null)
            {
                ugsBootstrap = FindObjectOfType<UgsBootstrap>();
            }

            if (matchRegistry == null)
            {
                matchRegistry = FindObjectOfType<MatchRegistryClient>();
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

            if (matchRegistry == null)
            {
                LobbyError?.Invoke("Match registry client is missing.");
                return false;
            }

            return true;
        }

        private static string GetLocalPlayerId()
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

        private string ResolveLobbyError(string fallback)
        {
            var error = matchRegistry != null ? matchRegistry.LastErrorMessage : string.Empty;
            if (string.IsNullOrWhiteSpace(error))
            {
                return fallback;
            }

            switch (error)
            {
                case "already_in_active_match_connected":
                    return "This account is already connected to an active match.";
                case "already_in_active_match_disconnected":
                    return "This account already has an active match. Use Reconnect.";
                case "already_in_active_match":
                    return "This account is already in an active match.";
                case "player_busy_in_other_match":
                    return "This account is already in another active match.";
                case "not_in_match":
                    return "This account is not assigned to that match.";
                case "lobby_full":
                    return "Lobby is full.";
                case "not found":
                    return "Lobby not found.";
                case "host_only":
                    return "Only host can start a match.";
                case "player_not_in_lobby":
                    return "You are not in this lobby.";
                case "invalid request":
                case "invalid playerId":
                    return "Invalid lobby request.";
                default:
                    return error;
            }
        }
    }
}
