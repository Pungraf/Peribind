using UnityEngine;

namespace Peribind.Unity.Networking
{
    public class PlayerIdentityProvider : MonoBehaviour
    {
        [SerializeField] private string playerPrefsKey = "Peribind.PlayerId";
        [SerializeField] private string cachedPlayerId;

        public string PlayerId => GetOrCreatePlayerId();
        public bool HasPlayerId => !string.IsNullOrWhiteSpace(cachedPlayerId) || PlayerPrefs.HasKey(playerPrefsKey);

        private void Awake()
        {
            var existing = FindObjectsOfType<PlayerIdentityProvider>();
            if (existing.Length > 1)
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
        }

        public void SetPlayerId(string playerId, bool persist = true)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                Debug.LogWarning("[PlayerIdentityProvider] Ignored empty player id.");
                return;
            }

            cachedPlayerId = playerId.Trim();
            if (persist)
            {
                PlayerPrefs.SetString(playerPrefsKey, cachedPlayerId);
                PlayerPrefs.Save();
            }
        }

        public void SetFromCredentials(string login, string password, bool persist = true)
        {
            var normalizedLogin = string.IsNullOrWhiteSpace(login) ? string.Empty : login.Trim();
            var normalizedPassword = string.IsNullOrWhiteSpace(password) ? string.Empty : password;
            var combined = $"{normalizedLogin}:{normalizedPassword}";
            SetPlayerId(HashToHex(combined), persist);
        }

        private string GetOrCreatePlayerId()
        {
            if (!string.IsNullOrWhiteSpace(cachedPlayerId))
            {
                return cachedPlayerId;
            }

            if (PlayerPrefs.HasKey(playerPrefsKey))
            {
                cachedPlayerId = PlayerPrefs.GetString(playerPrefsKey);
                if (!string.IsNullOrWhiteSpace(cachedPlayerId))
                {
                    return cachedPlayerId;
                }
            }

            cachedPlayerId = System.Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(playerPrefsKey, cachedPlayerId);
            PlayerPrefs.Save();
            return cachedPlayerId;
        }

        private static string HashToHex(string input)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(input ?? string.Empty);
            var hash = sha256.ComputeHash(bytes);
            var builder = new System.Text.StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                builder.Append(b.ToString("x2"));
            }
            return builder.ToString();
        }
    }
}
