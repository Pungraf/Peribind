using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace Peribind.Unity.Networking
{
    public class MatchRegistryClient : MonoBehaviour
    {
        [SerializeField] private string baseUrl = "http://209.38.222.103:8080";
        [SerializeField] private bool allowEnvironmentOverride = true;
        [SerializeField] private string environmentBaseUrlKey = "PERIBIND_MATCH_REGISTRY_URL";
        [SerializeField] private bool allowInternalTokenEnvironmentOverride = true;
        [SerializeField] private string internalTokenEnvironmentKey = "PERIBIND_INTERNAL_API_TOKEN";
        [SerializeField] private string internalApiToken = string.Empty;
        public string LastErrorMessage { get; private set; } = string.Empty;

        private void Awake()
        {
            if (!allowEnvironmentOverride)
            {
                return;
            }

            var fromEnv = Environment.GetEnvironmentVariable(environmentBaseUrlKey);
            if (string.IsNullOrWhiteSpace(fromEnv))
            {
                if (allowInternalTokenEnvironmentOverride)
                {
                    var tokenFromEnv = Environment.GetEnvironmentVariable(internalTokenEnvironmentKey);
                    if (!string.IsNullOrWhiteSpace(tokenFromEnv))
                    {
                        internalApiToken = tokenFromEnv.Trim();
                    }
                }

                return;
            }

            baseUrl = fromEnv.TrimEnd('/');
            Debug.Log($"[MatchRegistry] Base URL from env: {baseUrl}");

            if (allowInternalTokenEnvironmentOverride)
            {
                var tokenFromEnv = Environment.GetEnvironmentVariable(internalTokenEnvironmentKey);
                if (!string.IsNullOrWhiteSpace(tokenFromEnv))
                {
                    internalApiToken = tokenFromEnv.Trim();
                }
            }
        }

        public async Task<MatchInfo> CreateMatchAsync(string lobbyId, List<string> players, string map, string mode, string region)
        {
            LastErrorMessage = string.Empty;
            var payload = new CreateRequest
            {
                lobbyId = lobbyId,
                players = players ?? new List<string>(),
                map = map ?? string.Empty,
                mode = mode ?? string.Empty,
                region = region ?? string.Empty
            };

            var json = JsonUtility.ToJson(payload);
            var url = $"{baseUrl}/match/create";
            using var request = BuildPostRequest(url, json);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[MatchRegistry] Create failed: {request.responseCode} {request.error}");
                LastErrorMessage = ExtractErrorMessage(request, "create_failed");
                return null;
            }

            var body = request.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(body))
            {
                Debug.LogWarning("[MatchRegistry] Create returned empty body.");
                return null;
            }

            return JsonUtility.FromJson<MatchInfo>(body);
        }

        public async Task<LobbyInfo> CreateLobbyAsync(string playerId, string lobbyName, int maxPlayers, string map, string mode, string region)
        {
            LastErrorMessage = string.Empty;
            var payload = new LobbyCreateRequest
            {
                playerId = playerId ?? string.Empty,
                name = lobbyName ?? string.Empty,
                maxPlayers = maxPlayers,
                map = map ?? string.Empty,
                mode = mode ?? string.Empty,
                region = region ?? string.Empty
            };

            return await PostForLobbyAsync($"{baseUrl}/lobby/create", JsonUtility.ToJson(payload), "[MatchRegistry] Lobby create failed");
        }

        public async Task<LobbyInfo> JoinLobbyByIdAsync(string playerId, string lobbyId)
        {
            LastErrorMessage = string.Empty;
            var payload = new LobbyJoinRequest
            {
                playerId = playerId ?? string.Empty,
                lobbyId = lobbyId ?? string.Empty
            };

            return await PostForLobbyAsync($"{baseUrl}/lobby/join", JsonUtility.ToJson(payload), "[MatchRegistry] Lobby join failed");
        }

        public async Task<LobbyInfo> JoinLobbyByCodeAsync(string playerId, string code)
        {
            LastErrorMessage = string.Empty;
            var payload = new LobbyJoinCodeRequest
            {
                playerId = playerId ?? string.Empty,
                code = code ?? string.Empty
            };

            return await PostForLobbyAsync($"{baseUrl}/lobby/join-by-code", JsonUtility.ToJson(payload), "[MatchRegistry] Lobby join-by-code failed");
        }

        public async Task<LobbyInfo> GetLobbyByIdAsync(string lobbyId)
        {
            LastErrorMessage = string.Empty;
            var url = $"{baseUrl}/lobby/{UnityWebRequest.EscapeURL(lobbyId ?? string.Empty)}";
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LastErrorMessage = ExtractErrorMessage(request, "get_lobby_failed");
                return null;
            }

            var json = request.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonUtility.FromJson<LobbyInfo>(json);
        }

        public async Task<List<LobbyInfo>> QueryLobbiesAsync(string map, string mode, string region)
        {
            LastErrorMessage = string.Empty;
            var queryParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(map))
            {
                queryParts.Add($"map={UnityWebRequest.EscapeURL(map)}");
            }

            var url = $"{baseUrl}/lobby/list";
            if (queryParts.Count > 0)
            {
                url += "?" + string.Join("&", queryParts);
            }

            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[MatchRegistry] Lobby list failed: {request.responseCode} {request.error}");
                LastErrorMessage = ExtractErrorMessage(request, "list_lobbies_failed");
                return new List<LobbyInfo>();
            }

            var json = request.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<LobbyInfo>();
            }

            var response = JsonUtility.FromJson<LobbyListResponse>(json);
            return response?.results ?? new List<LobbyInfo>();
        }

        public async Task<LobbyLeaveResult> LeaveLobbyAsync(string lobbyId, string playerId)
        {
            LastErrorMessage = string.Empty;
            var payload = new LobbyLeaveRequest
            {
                lobbyId = lobbyId ?? string.Empty,
                playerId = playerId ?? string.Empty
            };

            var json = JsonUtility.ToJson(payload);
            var url = $"{baseUrl}/lobby/leave";
            using var request = BuildPostRequest(url, json);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[MatchRegistry] Lobby leave failed: {request.responseCode} {request.error}");
                LastErrorMessage = ExtractErrorMessage(request, "leave_lobby_failed");
                return new LobbyLeaveResult { ok = false, closed = true, lobby = null };
            }

            var body = request.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(body))
            {
                return new LobbyLeaveResult { ok = true, closed = true, lobby = null };
            }

            var response = JsonUtility.FromJson<LobbyLeaveResult>(body);
            if (response == null)
            {
                return new LobbyLeaveResult { ok = true, closed = true, lobby = null };
            }

            return response;
        }

        public async Task<LobbyInfo> SetPlayerReadyAsync(string lobbyId, string playerId, bool isReady)
        {
            LastErrorMessage = string.Empty;
            var payload = new LobbyReadyRequest
            {
                lobbyId = lobbyId ?? string.Empty,
                playerId = playerId ?? string.Empty,
                isReady = isReady
            };

            return await PostForLobbyAsync(
                $"{baseUrl}/lobby/player-ready",
                JsonUtility.ToJson(payload),
                "[MatchRegistry] Lobby ready update failed");
        }

        public async Task<LobbyInfo> SetServerInfoAsync(string lobbyId, string playerId, string serverIp, int serverPort, string matchId)
        {
            LastErrorMessage = string.Empty;
            var payload = new LobbyServerInfoRequest
            {
                lobbyId = lobbyId ?? string.Empty,
                playerId = playerId ?? string.Empty,
                serverIp = serverIp ?? string.Empty,
                serverPort = serverPort,
                matchId = matchId ?? string.Empty
            };

            return await PostForLobbyAsync(
                $"{baseUrl}/lobby/server-info",
                JsonUtility.ToJson(payload),
                "[MatchRegistry] Lobby server-info update failed");
        }

        public async Task<bool> RegisterMatchAsync(string matchId, string serverIp, int serverPort, List<string> players)
        {
            LastErrorMessage = string.Empty;
            var payload = new RegisterRequest
            {
                matchId = matchId,
                serverIp = serverIp,
                serverPort = serverPort,
                players = players
            };

            var json = JsonUtility.ToJson(payload);
            var url = $"{baseUrl}/match/register";
            return await SendJsonAsync(url, json);
        }

        public async Task<MatchInfo> GetMatchAsync(string matchId)
        {
            LastErrorMessage = string.Empty;
            var url = $"{baseUrl}/match/{matchId}";
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[MatchRegistry] Get failed: {request.error}");
                LastErrorMessage = ExtractErrorMessage(request, "get_match_failed");
                return null;
            }

            var json = request.downloadHandler.text;
            return JsonUtility.FromJson<MatchInfo>(json);
        }

        public async Task<bool> EndMatchAsync(string matchId)
        {
            LastErrorMessage = string.Empty;
            var payload = new EndRequest { matchId = matchId };
            var json = JsonUtility.ToJson(payload);
            var url = $"{baseUrl}/match/end";
            return await SendJsonAsync(url, json);
        }

        public async Task<bool> SubmitMatchResultAsync(
            string matchId,
            string winnerPlayerId,
            bool wasSurrendered,
            string surrenderingPlayerId,
            List<MatchResultPlayerEntry> players)
        {
            LastErrorMessage = string.Empty;
            var payload = new MatchResultRequest
            {
                matchId = matchId ?? string.Empty,
                winnerPlayerId = winnerPlayerId ?? string.Empty,
                wasSurrendered = wasSurrendered,
                surrenderingPlayerId = surrenderingPlayerId ?? string.Empty,
                players = players ?? new List<MatchResultPlayerEntry>()
            };

            var json = JsonUtility.ToJson(payload);
            var url = $"{baseUrl}/match/result";
            return await SendJsonAsync(url, json);
        }

        public async Task<bool> SetMatchPresenceAsync(string matchId, string playerId, bool connected)
        {
            LastErrorMessage = string.Empty;
            var payload = new MatchPresenceRequest
            {
                matchId = matchId ?? string.Empty,
                playerId = playerId ?? string.Empty,
                connected = connected
            };

            var json = JsonUtility.ToJson(payload);
            var url = $"{baseUrl}/match/presence";
            return await SendJsonAsync(url, json);
        }

        public async Task<PlayerProfile> UpsertPlayerAsync(string ugsPlayerId, string username, string displayName = "")
        {
            LastErrorMessage = string.Empty;
            var payload = new PlayerUpsertRequest
            {
                ugsPlayerId = ugsPlayerId ?? string.Empty,
                username = username ?? string.Empty,
                displayName = displayName ?? string.Empty
            };

            var json = JsonUtility.ToJson(payload);
            var url = $"{baseUrl}/player/upsert";
            using var request = BuildPostRequest(url, json);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[MatchRegistry] Player upsert failed: {request.responseCode} {request.error}");
                LastErrorMessage = ExtractErrorMessage(request, "player_upsert_failed");
                return null;
            }

            var body = request.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            return JsonUtility.FromJson<PlayerProfile>(body);
        }

        public async Task<ReleaseInfo> GetLatestReleaseAsync(string channel, string platform)
        {
            LastErrorMessage = string.Empty;
            var safeChannel = string.IsNullOrWhiteSpace(channel) ? "stable" : channel.Trim();
            var safePlatform = string.IsNullOrWhiteSpace(platform) ? "win64" : platform.Trim();
            var url =
                $"{baseUrl}/release/latest?channel={UnityWebRequest.EscapeURL(safeChannel)}&platform={UnityWebRequest.EscapeURL(safePlatform)}";
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (request.responseCode != 404)
                {
                    Debug.LogWarning($"[MatchRegistry] Release lookup failed: {request.responseCode} {request.error}");
                    LastErrorMessage = ExtractErrorMessage(request, "release_lookup_failed");
                }
                return null;
            }

            var json = request.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonUtility.FromJson<ReleaseInfo>(json);
        }

        public async Task<PlayerProfileResponse> GetPlayerProfileAsync(string playerId)
        {
            LastErrorMessage = string.Empty;
            var url = $"{baseUrl}/player/{UnityWebRequest.EscapeURL(playerId ?? string.Empty)}/profile";
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LastErrorMessage = ExtractErrorMessage(request, "player_profile_failed");
                return null;
            }

            var json = request.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonUtility.FromJson<PlayerProfileResponse>(json);
        }

        public async Task<PlayerMatchHistoryResponse> GetPlayerMatchHistoryAsync(string playerId, int limit = 20)
        {
            LastErrorMessage = string.Empty;
            var safeLimit = Mathf.Clamp(limit, 1, 100);
            var url =
                $"{baseUrl}/player/{UnityWebRequest.EscapeURL(playerId ?? string.Empty)}/matches?limit={safeLimit}";
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LastErrorMessage = ExtractErrorMessage(request, "player_match_history_failed");
                return null;
            }

            var json = request.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonUtility.FromJson<PlayerMatchHistoryResponse>(json);
        }

        public async Task<LeaderboardResponse> GetLeaderboardAsync(int limit = 50)
        {
            LastErrorMessage = string.Empty;
            var safeLimit = Mathf.Clamp(limit, 1, 200);
            var url = $"{baseUrl}/leaderboard?limit={safeLimit}";
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                LastErrorMessage = ExtractErrorMessage(request, "leaderboard_failed");
                return null;
            }

            var json = request.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonUtility.FromJson<LeaderboardResponse>(json);
        }

        public static bool IsVersionSupported(string currentVersion, string minSupportedVersion)
        {
            if (string.IsNullOrWhiteSpace(minSupportedVersion))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(currentVersion))
            {
                return false;
            }

            return CompareVersions(currentVersion, minSupportedVersion) >= 0;
        }

        private static int CompareVersions(string left, string right)
        {
            var leftTokens = TokenizeVersion(left);
            var rightTokens = TokenizeVersion(right);
            var maxCount = Mathf.Max(leftTokens.Count, rightTokens.Count);

            for (var i = 0; i < maxCount; i++)
            {
                var leftToken = i < leftTokens.Count ? leftTokens[i] : "0";
                var rightToken = i < rightTokens.Count ? rightTokens[i] : "0";

                var leftIsNumber = int.TryParse(leftToken, out var leftNumber);
                var rightIsNumber = int.TryParse(rightToken, out var rightNumber);

                if (leftIsNumber && rightIsNumber)
                {
                    if (leftNumber < rightNumber) return -1;
                    if (leftNumber > rightNumber) return 1;
                    continue;
                }

                var compare = string.Compare(leftToken, rightToken, StringComparison.OrdinalIgnoreCase);
                if (compare != 0)
                {
                    return compare;
                }
            }

            return 0;
        }

        private static List<string> TokenizeVersion(string version)
        {
            return new List<string>(version.Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private async Task<bool> SendJsonAsync(string url, string json)
        {
            using var request = BuildPostRequest(url, json);

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[MatchRegistry] Request failed: {request.error}");
                LastErrorMessage = ExtractErrorMessage(request, "request_failed");
                return false;
            }

            LastErrorMessage = string.Empty;
            return true;
        }

        private async Task<LobbyInfo> PostForLobbyAsync(string url, string json, string errorPrefix)
        {
            using var request = BuildPostRequest(url, json);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"{errorPrefix}: {request.responseCode} {request.error}");
                LastErrorMessage = ExtractErrorMessage(request, "lobby_request_failed");
                return null;
            }

            var body = request.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(body))
            {
                LastErrorMessage = "empty_response";
                return null;
            }

            LastErrorMessage = string.Empty;
            return JsonUtility.FromJson<LobbyInfo>(body);
        }

        private static string ExtractErrorMessage(UnityWebRequest request, string fallback)
        {
            var body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    var error = JsonUtility.FromJson<ErrorResponse>(body);
                    if (error != null && !string.IsNullOrWhiteSpace(error.error))
                    {
                        return error.error;
                    }
                }
                catch
                {
                    // best effort only
                }
            }

            if (!string.IsNullOrWhiteSpace(request.error))
            {
                return request.error;
            }

            return fallback;
        }

        private UnityWebRequest BuildPostRequest(string url, string json)
        {
            var request = new UnityWebRequest(url, "POST");
            var body = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrWhiteSpace(internalApiToken))
            {
                request.SetRequestHeader("x-internal-token", internalApiToken);
            }
            return request;
        }

        [Serializable]
        private class CreateRequest
        {
            public string lobbyId;
            public List<string> players;
            public string map;
            public string mode;
            public string region;
        }

        [Serializable]
        private class RegisterRequest
        {
            public string matchId;
            public string serverIp;
            public int serverPort;
            public List<string> players;
        }

        [Serializable]
        private class PlayerUpsertRequest
        {
            public string ugsPlayerId;
            public string username;
            public string displayName;
        }

        [Serializable]
        private class LobbyCreateRequest
        {
            public string playerId;
            public string name;
            public int maxPlayers;
            public string map;
            public string mode;
            public string region;
        }

        [Serializable]
        private class LobbyJoinRequest
        {
            public string lobbyId;
            public string playerId;
        }

        [Serializable]
        private class LobbyJoinCodeRequest
        {
            public string code;
            public string playerId;
        }

        [Serializable]
        private class LobbyLeaveRequest
        {
            public string lobbyId;
            public string playerId;
        }

        [Serializable]
        private class LobbyReadyRequest
        {
            public string lobbyId;
            public string playerId;
            public bool isReady;
        }

        [Serializable]
        private class LobbyServerInfoRequest
        {
            public string lobbyId;
            public string playerId;
            public string serverIp;
            public int serverPort;
            public string matchId;
        }

        [Serializable]
        private class MatchPresenceRequest
        {
            public string matchId;
            public string playerId;
            public bool connected;
        }

        [Serializable]
        private class MatchResultRequest
        {
            public string matchId;
            public string winnerPlayerId;
            public bool wasSurrendered;
            public string surrenderingPlayerId;
            public List<MatchResultPlayerEntry> players;
        }

        [Serializable]
        private class LobbyListResponse
        {
            public List<LobbyInfo> results;
        }

        [Serializable]
        private class ErrorResponse
        {
            public string error;
        }

        [Serializable]
        public class MatchInfo
        {
            public string matchId;
            public string serverIp;
            public int serverPort;
            public List<string> players;
            public List<string> connectedPlayers;
            public long expiresAt;
        }

        [Serializable]
        public class LobbyPlayerInfo
        {
            public string id;
            public bool ready;
        }

        [Serializable]
        public class LobbyInfo
        {
            public string id;
            public string lobbyCode;
            public string name;
            public int maxPlayers;
            public string hostId;
            public string map;
            public string mode;
            public string region;
            public string serverIp;
            public int serverPort;
            public string matchId;
            public List<LobbyPlayerInfo> players;
            public string createdAt;
            public string updatedAt;
        }

        [Serializable]
        public class LobbyLeaveResult
        {
            public bool ok;
            public bool closed;
            public LobbyInfo lobby;
        }

        [Serializable]
        public class ReleaseInfo
        {
            public string channel;
            public string platform;
            public string version;
            public string minSupportedVersion;
            public string downloadUrl;
            public string sha256;
            public string notesUrl;
            public long sizeBytes;
            public string publishedAt;
        }

        [Serializable]
        public class MatchResultPlayerEntry
        {
            public string playerId;
            public int playerSlot;
            public int score;
        }

        [Serializable]
        public class PlayerProfile
        {
            public string ugsPlayerId;
            public string username;
            public string displayName;
            public string createdAt;
            public string lastSeenAt;
        }

        [Serializable]
        public class PlayerStats
        {
            public string playerId;
            public int gamesPlayed;
            public int wins;
            public int losses;
            public int draws;
            public int surrenders;
            public int rankPoints;
            public int scoreTotal;
            public string lastResult;
            public string lastMatchId;
            public string lastMatchAt;
            public string updatedAt;
        }

        [Serializable]
        public class PlayerProfileResponse
        {
            public PlayerProfile player;
            public PlayerStats stats;
        }

        [Serializable]
        public class MatchHistoryOpponent
        {
            public string playerId;
            public int playerSlot;
            public int score;
            public string result;
            public string displayName;
            public string username;
        }

        [Serializable]
        public class PlayerMatchHistoryEntry
        {
            public string matchId;
            public string completedAt;
            public bool wasSurrendered;
            public string winnerPlayerId;
            public string surrenderingPlayerId;
            public string playerId;
            public int playerSlot;
            public int score;
            public string result;
            public List<MatchHistoryOpponent> opponents;
        }

        [Serializable]
        public class PlayerMatchHistoryResponse
        {
            public string playerId;
            public List<PlayerMatchHistoryEntry> results;
        }

        [Serializable]
        public class LeaderboardEntry
        {
            public int rank;
            public string playerId;
            public string username;
            public string displayName;
            public PlayerStats stats;
        }

        [Serializable]
        public class LeaderboardResponse
        {
            public List<LeaderboardEntry> leaderboard;
        }

        [Serializable]
        private class EndRequest
        {
            public string matchId;
        }
    }
}
