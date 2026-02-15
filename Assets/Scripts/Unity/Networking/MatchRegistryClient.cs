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

        private void Awake()
        {
            if (!allowEnvironmentOverride)
            {
                return;
            }

            var fromEnv = Environment.GetEnvironmentVariable(environmentBaseUrlKey);
            if (string.IsNullOrWhiteSpace(fromEnv))
            {
                return;
            }

            baseUrl = fromEnv.TrimEnd('/');
            Debug.Log($"[MatchRegistry] Base URL from env: {baseUrl}");
        }

        public async Task<MatchInfo> CreateMatchAsync(string lobbyId, List<string> players, string map, string mode, string region)
        {
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

        public async Task<bool> RegisterMatchAsync(string matchId, string serverIp, int serverPort, List<string> players)
        {
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
            var url = $"{baseUrl}/match/{matchId}";
            using var request = UnityWebRequest.Get(url);
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[MatchRegistry] Get failed: {request.error}");
                return null;
            }

            var json = request.downloadHandler.text;
            return JsonUtility.FromJson<MatchInfo>(json);
        }

        public async Task<bool> EndMatchAsync(string matchId)
        {
            var payload = new EndRequest { matchId = matchId };
            var json = JsonUtility.ToJson(payload);
            var url = $"{baseUrl}/match/end";
            return await SendJsonAsync(url, json);
        }

        public async Task<ReleaseInfo> GetLatestReleaseAsync(string channel, string platform)
        {
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

        private static async Task<bool> SendJsonAsync(string url, string json)
        {
            using var request = BuildPostRequest(url, json);

            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[MatchRegistry] Request failed: {request.error}");
                return false;
            }

            return true;
        }

        private static UnityWebRequest BuildPostRequest(string url, string json)
        {
            var request = new UnityWebRequest(url, "POST");
            var body = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
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
        public class MatchInfo
        {
            public string matchId;
            public string serverIp;
            public int serverPort;
            public List<string> players;
            public long expiresAt;
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
        private class EndRequest
        {
            public string matchId;
        }
    }
}
