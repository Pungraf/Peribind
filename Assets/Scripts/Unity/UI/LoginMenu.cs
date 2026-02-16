using Peribind.Unity.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using System.Text.RegularExpressions;

namespace Peribind.Unity.UI
{
    public class LoginMenu : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject loginPanel;
        [SerializeField] private GameObject registerPanel;

        [Header("Login")]
        [SerializeField] private TMP_InputField loginInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private TMP_Text loginInfoText;
        [SerializeField] private Button loginButton;
        [SerializeField] private Button registerButton;
        [SerializeField] private Button quitButton;

        [Header("Register")]
        [SerializeField] private TMP_InputField registerLoginInput;
        [SerializeField] private TMP_InputField registerPasswordInput;
        [SerializeField] private TMP_InputField registerConfirmPasswordInput;
        [SerializeField] private TMP_Text registerInfoText;
        [SerializeField] private Button registerSubmitButton;
        [SerializeField] private Button returnButton;

        [Header("Flow")]
        [SerializeField] private UgsBootstrap ugsBootstrap;
        [SerializeField] private MatchRegistryClient matchRegistryClient;
        [SerializeField] private string nextSceneName = "StarterScene";
        [SerializeField] private bool proceedToNextSceneAfterRegister = true;
        [SerializeField] private bool useUnityPlayerAccounts = true;

        [Header("Client Version Gate")]
        [SerializeField] private bool enforceMinClientVersion = true;
        [SerializeField] private string releaseChannel = "stable";
        [SerializeField] private string releasePlatform = "win64";

        private bool _isSubmitting;
        private static readonly Regex EmailRegex =
            new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private void Awake()
        {
            if (loginButton != null)
            {
                loginButton.onClick.AddListener(OnLoginClicked);
            }

            if (registerButton != null)
            {
                registerButton.onClick.AddListener(OnRegisterPanelClicked);
            }

            if (quitButton != null)
            {
                quitButton.onClick.AddListener(OnQuitClicked);
            }

            if (registerSubmitButton != null)
            {
                registerSubmitButton.onClick.AddListener(OnRegisterSubmitClicked);
            }

            if (returnButton != null)
            {
                returnButton.onClick.AddListener(OnReturnToLoginClicked);
            }

            SetPasswordMode(passwordInput);
            SetEmailMode(loginInput);
            SetPasswordMode(registerPasswordInput);
            SetPasswordMode(registerConfirmPasswordInput);
            SetEmailMode(registerLoginInput);

            SetPanelState(showLogin: true);
            SetLoginInfo(string.Empty);
            SetRegisterInfo(string.Empty);
            FocusInput(loginInput);
        }

        private void Update()
        {
            if (!IsLoginPanelActive())
            {
                return;
            }

            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selected == null)
            {
                return;
            }

            if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame)
            {
                if (selected == loginInput?.gameObject)
                {
                    FocusInput(passwordInput);
                }
                else if (selected == passwordInput?.gameObject)
                {
                    FocusInput(loginInput);
                }
            }

            if (Keyboard.current != null &&
                (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            {
                if (selected == loginInput?.gameObject || selected == passwordInput?.gameObject)
                {
                    OnLoginClicked();
                }
            }
        }

        private void OnDestroy()
        {
            if (loginButton != null)
            {
                loginButton.onClick.RemoveListener(OnLoginClicked);
            }

            if (quitButton != null)
            {
                quitButton.onClick.RemoveListener(OnQuitClicked);
            }

            if (registerButton != null)
            {
                registerButton.onClick.RemoveListener(OnRegisterPanelClicked);
            }

            if (registerSubmitButton != null)
            {
                registerSubmitButton.onClick.RemoveListener(OnRegisterSubmitClicked);
            }

            if (returnButton != null)
            {
                returnButton.onClick.RemoveListener(OnReturnToLoginClicked);
            }
        }

        private async void OnLoginClicked()
        {
            if (_isSubmitting)
            {
                if (!useUnityPlayerAccounts)
                {
                    return;
                }

                CancelPendingPlayerAccountFlow("Previous browser authentication cancelled. Restarting sign-in...");
            }

            var login = loginInput != null ? loginInput.text : string.Empty;
            if (!useUnityPlayerAccounts)
            {
                var password = passwordInput != null ? passwordInput.text : string.Empty;
                if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
                {
                    SetLoginInfo("Enter email and password.");
                    return;
                }

                if (!LooksLikeEmail(login))
                {
                    SetLoginInfo("Enter a valid email address.");
                    return;
                }
            }

            var versionGate = await EnsureClientVersionAllowedAsync(SetLoginInfo);
            if (!versionGate)
            {
                return;
            }

            if (ugsBootstrap == null)
            {
                ugsBootstrap = FindObjectOfType<UgsBootstrap>(true);
            }

            if (ugsBootstrap == null)
            {
                SetLoginInfo("Authentication service is unavailable.");
                return;
            }

            _isSubmitting = true;
            if (!useUnityPlayerAccounts)
            {
                SetButtonsInteractable(false);
            }
            SetLoginInfo(useUnityPlayerAccounts ? "Opening browser for sign-in..." : "Signing in...");
            try
            {
                UgsBootstrap.AuthOperationResult result;
                if (useUnityPlayerAccounts)
                {
                    result = await ugsBootstrap.SignInWithPlayerAccountAsync(isSignUpFlow: false);
                }
                else
                {
                    var password = passwordInput != null ? passwordInput.text : string.Empty;
                    result = await ugsBootstrap.SignInWithUsernamePasswordAsync(login, password);
                }

                if (!result.Success)
                {
                    SetLoginInfo(string.IsNullOrWhiteSpace(result.Message) ? "Invalid email or password." : result.Message);
                    return;
                }

                await SyncPlayerProfileAsync(ResolveProfileUsername(login));

                SetLoginInfo("Login successful.");
                ClearSelection();
                if (!string.IsNullOrWhiteSpace(nextSceneName))
                {
                    SceneManager.LoadScene(nextSceneName);
                }
            }
            finally
            {
                _isSubmitting = false;
                SetButtonsInteractable(true);
            }
        }

        private void OnQuitClicked()
        {
            ClearSelection();
            UnityEngine.Application.Quit();
        }

        public void OpenPlayerAccountPortal()
        {
            var url = "https://player-account.unity.com";
            try
            {
                if (PlayerAccountService.Instance != null && !string.IsNullOrWhiteSpace(PlayerAccountService.Instance.AccountPortalUrl))
                {
                    url = PlayerAccountService.Instance.AccountPortalUrl;
                }
            }
            catch
            {
                // Fallback to default URL.
            }

            UnityEngine.Application.OpenURL(url);
            SetLoginInfo("Open browser account portal for password reset and account management.");
        }

        private void OnRegisterPanelClicked()
        {
            if (_isSubmitting)
            {
                if (!useUnityPlayerAccounts)
                {
                    return;
                }

                CancelPendingPlayerAccountFlow("Browser authentication cancelled.");
            }

            SetPanelState(showLogin: false);
            SetRegisterInfo(useUnityPlayerAccounts
                ? "Account creation continues in browser."
                : string.Empty);
            ClearSelection();
        }

        private async void OnRegisterSubmitClicked()
        {
            if (_isSubmitting)
            {
                if (!useUnityPlayerAccounts)
                {
                    return;
                }

                CancelPendingPlayerAccountFlow("Previous browser authentication cancelled. Restarting registration...");
            }

            var login = registerLoginInput != null ? registerLoginInput.text : string.Empty;
            if (!useUnityPlayerAccounts)
            {
                var password = registerPasswordInput != null ? registerPasswordInput.text : string.Empty;
                var confirm = registerConfirmPasswordInput != null ? registerConfirmPasswordInput.text : string.Empty;

                if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirm))
                {
                    SetRegisterInfo("Fill all fields.");
                    return;
                }

                if (!LooksLikeEmail(login))
                {
                    SetRegisterInfo("Enter a valid email address.");
                    return;
                }

                if (!string.Equals(password, confirm, System.StringComparison.Ordinal))
                {
                    SetRegisterInfo("Password and confirmation do not match.");
                    return;
                }
            }

            var versionGate = await EnsureClientVersionAllowedAsync(SetRegisterInfo);
            if (!versionGate)
            {
                return;
            }

            if (ugsBootstrap == null)
            {
                ugsBootstrap = FindObjectOfType<UgsBootstrap>(true);
            }

            if (ugsBootstrap == null)
            {
                SetRegisterInfo("Authentication service is unavailable.");
                return;
            }

            _isSubmitting = true;
            if (!useUnityPlayerAccounts)
            {
                SetButtonsInteractable(false);
            }
            SetRegisterInfo(useUnityPlayerAccounts ? "Opening browser for registration..." : "Creating account...");
            try
            {
                UgsBootstrap.AuthOperationResult result;
                if (useUnityPlayerAccounts)
                {
                    result = await ugsBootstrap.SignInWithPlayerAccountAsync(isSignUpFlow: true);
                }
                else
                {
                    var password = registerPasswordInput != null ? registerPasswordInput.text : string.Empty;
                    result = await ugsBootstrap.RegisterWithUsernamePasswordAsync(login, password);
                }

                if (!result.Success)
                {
                    SetRegisterInfo(string.IsNullOrWhiteSpace(result.Message) ? "Registration failed." : result.Message);
                    return;
                }

                await SyncPlayerProfileAsync(ResolveProfileUsername(login));

                SetRegisterInfo("Registration successful.");
                if (proceedToNextSceneAfterRegister && !string.IsNullOrWhiteSpace(nextSceneName))
                {
                    ClearSelection();
                    SceneManager.LoadScene(nextSceneName);
                }
            }
            finally
            {
                _isSubmitting = false;
                SetButtonsInteractable(true);
            }
        }

        private void OnReturnToLoginClicked()
        {
            if (_isSubmitting)
            {
                if (!useUnityPlayerAccounts)
                {
                    return;
                }

                CancelPendingPlayerAccountFlow("Browser authentication cancelled.");
            }

            if (registerLoginInput != null) registerLoginInput.text = string.Empty;
            if (registerPasswordInput != null) registerPasswordInput.text = string.Empty;
            if (registerConfirmPasswordInput != null) registerConfirmPasswordInput.text = string.Empty;
            SetRegisterInfo(string.Empty);
            SetPanelState(showLogin: true);
            FocusInput(loginInput);
        }

        private void CancelPendingPlayerAccountFlow(string infoMessage)
        {
            if (ugsBootstrap == null)
            {
                ugsBootstrap = FindObjectOfType<UgsBootstrap>(true);
            }

            ugsBootstrap?.CancelPlayerAccountFlow();
            _isSubmitting = false;
            SetButtonsInteractable(true);
            SetLoginInfo(infoMessage);
            SetRegisterInfo(infoMessage);
        }

        private void ClearSelection()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                eventSystem.SetSelectedGameObject(null);
            }
        }

        private void SetButtonsInteractable(bool interactable)
        {
            if (loginButton != null)
            {
                loginButton.interactable = interactable;
            }

            if (registerButton != null)
            {
                registerButton.interactable = interactable;
            }

            if (quitButton != null)
            {
                quitButton.interactable = interactable;
            }

            if (registerSubmitButton != null)
            {
                registerSubmitButton.interactable = interactable;
            }

            if (returnButton != null)
            {
                returnButton.interactable = interactable;
            }
        }

        private void SetPanelState(bool showLogin)
        {
            if (loginPanel != null)
            {
                loginPanel.SetActive(showLogin);
            }

            if (registerPanel != null)
            {
                registerPanel.SetActive(!showLogin);
            }
        }

        private bool IsLoginPanelActive()
        {
            return loginPanel == null || loginPanel.activeInHierarchy;
        }

        private void FocusInput(TMP_InputField input)
        {
            if (input == null)
            {
                return;
            }

            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                eventSystem.SetSelectedGameObject(input.gameObject);
            }

            input.ActivateInputField();
            input.MoveTextEnd(false);
        }

        private void SetLoginInfo(string message)
        {
            if (loginInfoText != null)
            {
                loginInfoText.text = message ?? string.Empty;
            }
        }

        private void SetRegisterInfo(string message)
        {
            if (registerInfoText != null)
            {
                registerInfoText.text = message ?? string.Empty;
            }
        }

        private static void SetPasswordMode(TMP_InputField input)
        {
            if (input == null)
            {
                return;
            }

            input.contentType = TMP_InputField.ContentType.Password;
            input.ForceLabelUpdate();
        }

        private static void SetEmailMode(TMP_InputField input)
        {
            if (input == null)
            {
                return;
            }

            input.contentType = TMP_InputField.ContentType.EmailAddress;
            input.ForceLabelUpdate();
        }

        private static bool LooksLikeEmail(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            return EmailRegex.IsMatch(value.Trim());
        }

        private async System.Threading.Tasks.Task SyncPlayerProfileAsync(string username)
        {
            if (matchRegistryClient == null)
            {
                matchRegistryClient = FindObjectOfType<MatchRegistryClient>(true);
            }

            if (matchRegistryClient == null || string.IsNullOrWhiteSpace(username))
            {
                return;
            }

            string playerId;
            try
            {
                if (AuthenticationService.Instance == null || !AuthenticationService.Instance.IsSignedIn)
                {
                    return;
                }

                playerId = AuthenticationService.Instance.PlayerId;
            }
            catch
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(playerId))
            {
                return;
            }

            // Keep current custom display name on login/register sync.
            await matchRegistryClient.UpsertPlayerAsync(playerId, username);
        }

        private static string ResolveProfileUsername(string fallbackUsername)
        {
            var fallback = string.IsNullOrWhiteSpace(fallbackUsername) ? string.Empty : fallbackUsername.Trim();

            try
            {
                var claims = PlayerAccountService.Instance?.IdTokenClaims;
                if (claims != null && !string.IsNullOrWhiteSpace(claims.Email))
                {
                    return claims.Email.Trim();
                }
            }
            catch
            {
                // Best effort only.
            }

            try
            {
                if (AuthenticationService.Instance != null && !string.IsNullOrWhiteSpace(AuthenticationService.Instance.PlayerId))
                {
                    return string.IsNullOrWhiteSpace(fallback) ? AuthenticationService.Instance.PlayerId : fallback;
                }
            }
            catch
            {
                // Best effort only.
            }

            return fallback;
        }

        private async System.Threading.Tasks.Task<bool> EnsureClientVersionAllowedAsync(System.Action<string> setInfo)
        {
            if (!enforceMinClientVersion)
            {
                return true;
            }

            if (matchRegistryClient == null)
            {
                matchRegistryClient = FindObjectOfType<MatchRegistryClient>(true);
            }

            if (matchRegistryClient == null)
            {
                return true;
            }

            var release = await matchRegistryClient.GetLatestReleaseAsync(releaseChannel, releasePlatform);
            if (release == null)
            {
                return true;
            }

            var currentVersion = UnityEngine.Application.version;
            if (MatchRegistryClient.IsVersionSupported(currentVersion, release.minSupportedVersion))
            {
                return true;
            }

            var message =
                $"Update required. Current {currentVersion}, minimum {release.minSupportedVersion}.";
            if (!string.IsNullOrWhiteSpace(release.downloadUrl))
            {
                message = $"{message} Download latest client.";
            }
            setInfo?.Invoke(message);
            return false;
        }
    }
}
