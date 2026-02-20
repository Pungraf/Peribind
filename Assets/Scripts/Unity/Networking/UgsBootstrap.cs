using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using Unity.Services.Core;
using UnityEngine;

namespace Peribind.Unity.Networking
{
    public class UgsBootstrap : MonoBehaviour
    {
        private const int PlayerAccountFlowTimeoutMs = 10 * 60 * 1000;

        public readonly struct AuthOperationResult
        {
            public readonly bool Success;
            public readonly string Message;
            public readonly int ErrorCode;

            public AuthOperationResult(bool success, string message, int errorCode = 0)
            {
                Success = success;
                Message = message;
                ErrorCode = errorCode;
            }
        }

        public const string ProfilePrefKey = "UGS_PROFILE_OVERRIDE";

        [SerializeField] private bool dontDestroyOnLoad = true;

        private bool _initialized;
        private Task _initTask;
        private CancellationTokenSource _playerAccountFlowCts = new CancellationTokenSource();

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

        public async Task<AuthOperationResult> SignInWithPlayerAccountAsync(bool isSignUpFlow)
        {
            await EnsureInitializedAsync();
            if (!_initialized)
            {
                return new AuthOperationResult(false, "UGS initialization failed.");
            }

            try
            {
                SignOutAndClearAuthentication();

                var playerAccountResult = await StartPlayerAccountFlowAsync(isSignUpFlow);
                if (!playerAccountResult.Success)
                {
                    return playerAccountResult;
                }

                var token = PlayerAccountService.Instance.AccessToken;
                if (string.IsNullOrWhiteSpace(token))
                {
                    return new AuthOperationResult(false, "Player Accounts token is missing. Please retry.");
                }

                await AuthenticationService.Instance.SignInWithUnityAsync(token);
                Debug.Log($"[UGS] Player Accounts sign-in succeeded. PlayerId='{AuthenticationService.Instance.PlayerId}'");
                return new AuthOperationResult(true, isSignUpFlow ? "Registration successful." : "Login successful.");
            }
            catch (AuthenticationException ex)
            {
                Debug.LogWarning($"[UGS] Player Accounts sign-in failed: {ex.Message}");
                SignOutAndClearAuthentication();
                return new AuthOperationResult(false, "Authentication failed. Please retry.", ex.ErrorCode);
            }
            catch (RequestFailedException ex)
            {
                Debug.LogWarning($"[UGS] Player Accounts sign-in request failed: {ex.Message}");
                SignOutAndClearAuthentication();
                if (ex.ErrorCode == CommonErrorCodes.TooManyRequests)
                {
                    return new AuthOperationResult(false, "Too many attempts. Please wait and retry.", ex.ErrorCode);
                }

                return new AuthOperationResult(false, "Authentication failed. Please retry.", ex.ErrorCode);
            }
        }

        public void CancelPlayerAccountFlow()
        {
            try
            {
                _playerAccountFlowCts.Cancel();
                _playerAccountFlowCts.Dispose();
            }
            catch
            {
                // Best effort only.
            }
            finally
            {
                _playerAccountFlowCts = new CancellationTokenSource();
            }

            SignOutAndClearAuthentication();
            SignOutPlayerAccount();
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

        public static void SignOutAll()
        {
            SignOutAndClearAuthentication();
            SignOutPlayerAccount();
        }

        private static void SignOutAndClearAuthentication()
        {
            try
            {
                if (AuthenticationService.Instance != null)
                {
                    AuthenticationService.Instance.SignOut(true);
                    AuthenticationService.Instance.ClearSessionToken();
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }

        private static void SignOutPlayerAccount()
        {
            try
            {
                if (PlayerAccountService.Instance != null && PlayerAccountService.Instance.IsSignedIn)
                {
                    PlayerAccountService.Instance.SignOut();
                }
            }
            catch
            {
                // Best effort cleanup only.
            }
        }

        private async Task<AuthOperationResult> StartPlayerAccountFlowAsync(bool isSignUpFlow)
        {
            var service = PlayerAccountService.Instance;
            if (service == null)
            {
                return new AuthOperationResult(false, "Player Accounts service is unavailable.");
            }

            if (service.IsSignedIn && !string.IsNullOrWhiteSpace(service.AccessToken))
            {
                return new AuthOperationResult(true, string.Empty);
            }

            var signInCompletion = new TaskCompletionSource<AuthOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnSignedIn()
            {
                signInCompletion.TrySetResult(new AuthOperationResult(true, string.Empty));
            }

            void OnSignInFailed(RequestFailedException ex)
            {
                signInCompletion.TrySetResult(new AuthOperationResult(false, MapPlayerAccountError(ex), ex.ErrorCode));
            }

            service.SignedIn += OnSignedIn;
            service.SignInFailed += OnSignInFailed;
            try
            {
                var startTask = service.StartSignInAsync(isSignUpFlow);
                var timeoutTask = Task.Delay(PlayerAccountFlowTimeoutMs);
                var cancelTask = Task.Delay(Timeout.Infinite, _playerAccountFlowCts.Token);
                var firstCompleted = await Task.WhenAny(signInCompletion.Task, startTask, timeoutTask, cancelTask);

                if (firstCompleted == signInCompletion.Task)
                {
                    return await signInCompletion.Task;
                }

                if (firstCompleted == cancelTask)
                {
                    return new AuthOperationResult(false, "Authentication cancelled.");
                }

                if (firstCompleted == timeoutTask)
                {
                    CancelPlayerAccountFlow();
                    return new AuthOperationResult(false, "Sign-in timed out. Please retry.");
                }

                // startTask completed first; now wait for sign-in outcome event.
                if (startTask.IsCanceled)
                {
                    return new AuthOperationResult(false, "Authentication cancelled.");
                }

                if (startTask.IsFaulted)
                {
                    var requestFailedEx = startTask.Exception?.GetBaseException() as RequestFailedException;
                    if (requestFailedEx != null)
                    {
                        return new AuthOperationResult(false, MapPlayerAccountError(requestFailedEx), requestFailedEx.ErrorCode);
                    }

                    return new AuthOperationResult(false, "Authentication failed. Please retry.");
                }

                var secondCompleted = await Task.WhenAny(signInCompletion.Task, timeoutTask, cancelTask);
                if (secondCompleted == signInCompletion.Task)
                {
                    return await signInCompletion.Task;
                }

                if (secondCompleted == cancelTask)
                {
                    return new AuthOperationResult(false, "Authentication cancelled.");
                }

                CancelPlayerAccountFlow();
                return new AuthOperationResult(false, "Sign-in timed out. Please retry.");
            }
            catch (PlayerAccountsException ex)
            {
                Debug.LogWarning($"[UGS] Player Accounts flow failed: {ex.Message}");
                return new AuthOperationResult(false, MapPlayerAccountError(ex), ex.ErrorCode);
            }
            catch (RequestFailedException ex)
            {
                Debug.LogWarning($"[UGS] Player Accounts flow request failed: {ex.Message}");
                return new AuthOperationResult(false, MapPlayerAccountError(ex), ex.ErrorCode);
            }
            finally
            {
                service.SignedIn -= OnSignedIn;
                service.SignInFailed -= OnSignInFailed;
            }
        }

        private static string MapPlayerAccountError(RequestFailedException ex)
        {
            if (ex == null)
            {
                return "Authentication failed. Please retry.";
            }

            switch (ex.ErrorCode)
            {
                case PlayerAccountsErrorCodes.MissingClientId:
                    return "Player Accounts is not configured (missing Client ID in UnityPlayerAccountSettings).";
                case PlayerAccountsErrorCodes.InvalidState:
                    return "Authentication is already in progress. Complete or cancel the browser flow and retry.";
                case PlayerAccountsErrorCodes.UnauthorizedClient:
                case PlayerAccountsErrorCodes.InvalidClient:
                    return "Player Accounts configuration is invalid for this project.";
                case PlayerAccountsErrorCodes.InvalidGrant:
                    return "Sign-in failed or was cancelled. Please retry.";
                default:
                    if (ex.ErrorCode == CommonErrorCodes.TooManyRequests)
                    {
                        return "Too many attempts. Please wait and retry.";
                    }

                    return string.IsNullOrWhiteSpace(ex.Message)
                        ? "Authentication failed. Please retry."
                        : ex.Message;
            }
        }

    }
}
