using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace Peribind.Unity.Networking
{
    public class UgsBootstrap : MonoBehaviour
    {
        public const string EditorProfilePrefKey = "UGS_PROFILE_OVERRIDE";

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
                var profile = GetEditorProfileOverride();
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
                Debug.Log("[UGS] Initialized and signed in.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UGS] Initialization failed: {ex.Message}");
            }
        }

        private static string GetEditorProfileOverride()
        {
            if (!UnityEngine.Application.isEditor)
            {
                return string.Empty;
            }

            return PlayerPrefs.GetString(EditorProfilePrefKey, string.Empty);
        }
    }
}
