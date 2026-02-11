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
        private const string PasswordRequirementsMessage = "Password must be 8-30 chars and include uppercase, lowercase, number, and symbol.";

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

        public async Task<AuthOperationResult> SignInWithUsernamePasswordAsync(string username, string password)
        {
            username = username?.Trim() ?? string.Empty;
            password = password ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return new AuthOperationResult(false, "Enter login and password.");
            }

            await EnsureInitializedAsync();
            if (!_initialized)
            {
                return new AuthOperationResult(false, "UGS initialization failed.");
            }

            try
            {
                SignOutAndClear();

                await AuthenticationService.Instance.SignInWithUsernamePasswordAsync(username, password);
                Debug.Log($"[UGS] Username/password sign-in succeeded. PlayerId='{AuthenticationService.Instance.PlayerId}'");
                return new AuthOperationResult(true, "Login successful.");
            }
            catch (AuthenticationException signInEx)
            {
                Debug.LogWarning($"[UGS] Username/password sign-in failed: {signInEx.Message}");
                SignOutAndClear();
                return new AuthOperationResult(false, "Invalid login or password.", signInEx.ErrorCode);
            }
            catch (RequestFailedException signInEx)
            {
                Debug.LogWarning($"[UGS] Username/password sign-in request failed: {signInEx.Message}");
                SignOutAndClear();
                if (signInEx.ErrorCode == CommonErrorCodes.TooManyRequests)
                {
                    return new AuthOperationResult(false, "Too many attempts. Please wait and retry.", signInEx.ErrorCode);
                }

                return new AuthOperationResult(false, "Invalid login or password.", signInEx.ErrorCode);
            }
        }

        public async Task<AuthOperationResult> RegisterWithUsernamePasswordAsync(string username, string password)
        {
            username = username?.Trim() ?? string.Empty;
            password = password ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return new AuthOperationResult(false, "Enter login and password.");
            }

            var passwordValidation = ValidatePassword(password);
            if (!passwordValidation.Success)
            {
                return passwordValidation;
            }

            await EnsureInitializedAsync();
            if (!_initialized)
            {
                return new AuthOperationResult(false, "UGS initialization failed.");
            }

            try
            {
                // Ensure registration always starts from a fresh anonymous identity.
                SignOutAndClear();
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                await AuthenticationService.Instance.AddUsernamePasswordAsync(username, password);
                Debug.Log($"[UGS] Username/password registration succeeded. PlayerId='{AuthenticationService.Instance.PlayerId}'");
                return new AuthOperationResult(true, "Registration successful.");
            }
            catch (AuthenticationException ex)
            {
                Debug.LogWarning($"[UGS] Username/password registration failed: {ex.Message}");
                SignOutAndClear();
                if (LooksLikeUsernameTaken(ex.Message))
                {
                    return new AuthOperationResult(false, "Login already exists.", ex.ErrorCode);
                }

                if (LooksLikeInvalidPassword(ex.Message))
                {
                    return new AuthOperationResult(false, PasswordRequirementsMessage, ex.ErrorCode);
                }

                return new AuthOperationResult(false, ExtractDetailOrFallback(ex.Message, "Registration failed. Check credentials and retry."), ex.ErrorCode);
            }
            catch (RequestFailedException ex)
            {
                Debug.LogWarning($"[UGS] Username/password registration request failed: {ex.Message}");
                SignOutAndClear();
                if (ex.ErrorCode == CommonErrorCodes.Conflict || LooksLikeUsernameTaken(ex.Message))
                {
                    return new AuthOperationResult(false, "Login already exists.", ex.ErrorCode);
                }

                if (ex.ErrorCode == CommonErrorCodes.TooManyRequests)
                {
                    return new AuthOperationResult(false, "Too many attempts. Please wait and retry.", ex.ErrorCode);
                }

                if (LooksLikeInvalidPassword(ex.Message))
                {
                    return new AuthOperationResult(false, PasswordRequirementsMessage, ex.ErrorCode);
                }

                return new AuthOperationResult(false, ExtractDetailOrFallback(ex.Message, "Registration failed. Check credentials and retry."), ex.ErrorCode);
            }
        }

        public async Task<bool> SignInOrCreateWithUsernamePasswordAsync(string username, string password, bool createIfMissing = true)
        {
            var login = await SignInWithUsernamePasswordAsync(username, password);
            if (login.Success || !createIfMissing)
            {
                return login.Success;
            }

            var register = await RegisterWithUsernamePasswordAsync(username, password);
            return register.Success;
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

        private static void SignOutAndClear()
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

        private static bool LooksLikeUsernameTaken(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return message.IndexOf("already", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("exists", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("taken", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("in use", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeInvalidPassword(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            return message.IndexOf("INVALID_PASSWORD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("Password does not match requirements", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ExtractDetailOrFallback(string message, string fallback)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return fallback;
            }

            const string token = "\"detail\":\"";
            var start = message.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return fallback;
            }

            start += token.Length;
            var end = message.IndexOf("\"", start, StringComparison.Ordinal);
            if (end <= start)
            {
                return fallback;
            }

            var detail = message.Substring(start, end - start)
                .Replace("\\\"", "\"")
                .Replace("\\n", " ")
                .Trim();
            return string.IsNullOrWhiteSpace(detail) ? fallback : detail;
        }

        private static AuthOperationResult ValidatePassword(string password)
        {
            if (password.Length < 8 || password.Length > 30)
            {
                return new AuthOperationResult(false, PasswordRequirementsMessage);
            }

            var hasUpper = false;
            var hasLower = false;
            var hasDigit = false;
            var hasSymbol = false;

            foreach (var ch in password)
            {
                if (char.IsUpper(ch))
                {
                    hasUpper = true;
                    continue;
                }

                if (char.IsLower(ch))
                {
                    hasLower = true;
                    continue;
                }

                if (char.IsDigit(ch))
                {
                    hasDigit = true;
                    continue;
                }

                hasSymbol = true;
            }

            return hasUpper && hasLower && hasDigit && hasSymbol
                ? new AuthOperationResult(true, string.Empty)
                : new AuthOperationResult(false, PasswordRequirementsMessage);
        }
    }
}
