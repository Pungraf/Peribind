using System;
using System.Text;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace Peribind.Unity.Networking
{
    public class UgsBootstrap : MonoBehaviour
    {
        public const string ProfilePrefKey = "UGS_PROFILE_OVERRIDE";

        [SerializeField] private bool dontDestroyOnLoad = true;

        private bool _initialized;
        private Task _initTask;

        private async void Awake()
        {
            if (dontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }

            await EnsureInitializedAsync();
        }

        public async Task EnsureInitializedAsync()
        {
            if (_initialized)
            {
                return;
            }

            if (_initTask != null)
            {
                await _initTask;
                return;
            }

            _initTask = InitializeInternalAsync();
            await _initTask;
        }

        private async Task InitializeInternalAsync()
        {
            try
            {
                var options = new InitializationOptions();
                var profile = GetProfileOverride();
                if (!string.IsNullOrWhiteSpace(profile))
                {
                    options.SetProfile(profile);
                }

                await UnityServices.InitializeAsync(options);
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                _initialized = true;
                Debug.Log($"[UGS] Initialized and signed in. Profile='{profile}' PlayerId='{AuthenticationService.Instance.PlayerId}'");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UGS] Initialization failed: {ex.Message}");
            }
        }

        private static string GetProfileOverride()
        {
            var stored = PlayerPrefs.GetString(ProfilePrefKey, string.Empty);
            var normalized = NormalizeProfileName(stored);
            if (!string.Equals(stored, normalized, StringComparison.Ordinal))
            {
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    PlayerPrefs.DeleteKey(ProfilePrefKey);
                }
                else
                {
                    PlayerPrefs.SetString(ProfilePrefKey, normalized);
                }

                PlayerPrefs.Save();
            }

            return normalized;
        }

        public static string BuildProfileFromIdentity(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity))
            {
                return string.Empty;
            }

            // Keep a short deterministic profile per credential identity.
            return NormalizeProfileName($"p_{identity}");
        }

        public static string NormalizeProfileName(string rawProfile)
        {
            if (string.IsNullOrWhiteSpace(rawProfile))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(rawProfile.Length);
            foreach (var ch in rawProfile)
            {
                if ((ch >= 'a' && ch <= 'z') ||
                    (ch >= 'A' && ch <= 'Z') ||
                    (ch >= '0' && ch <= '9') ||
                    ch == '-' ||
                    ch == '_')
                {
                    builder.Append(ch);
                    if (builder.Length >= 30)
                    {
                        break;
                    }
                }
            }

            return builder.ToString();
        }
    }
}
