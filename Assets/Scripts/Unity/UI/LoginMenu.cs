using Peribind.Unity.Networking;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Unity.Services.Authentication;
using Unity.Services.Authentication.PlayerAccounts;
using UIDocument = UnityEngine.UIElements.UIDocument;
using UiButton = UnityEngine.UIElements.Button;
using UiLabel = UnityEngine.UIElements.Label;
using UiVisualElement = UnityEngine.UIElements.VisualElement;
using UiDisplayStyle = UnityEngine.UIElements.DisplayStyle;
using VisualTreeAsset = UnityEngine.UIElements.VisualTreeAsset;
using StyleSheet = UnityEngine.UIElements.StyleSheet;

namespace Peribind.Unity.UI
{
    public class LoginMenu : MonoBehaviour
    {
        [Header("Flow")]
        [SerializeField] private UgsBootstrap ugsBootstrap;
        [SerializeField] private MatchRegistryClient matchRegistryClient;
        [SerializeField] private string nextSceneName = "StarterScene";
        [SerializeField] private bool proceedToNextSceneAfterRegister = true;

        [Header("Client Version Gate")]
        [SerializeField] private bool enforceMinClientVersion = true;
        [SerializeField] private string releaseChannel = "stable";
        [SerializeField] private string releasePlatform = "win64";

        [Header("UI Toolkit")]
        [SerializeField] private bool enableUiToolkit = true;
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private bool autoAssignUiDocument = true;
        [SerializeField] private bool autoAssignVisualTreeFromResources = true;
        [SerializeField] private bool autoAssignStylesFromResources = true;

        private const string LoginUxmlResourcePath = "UI/Toolkit/Login/LoginMenu";
        private const string CommonStyleResourcePath = "UI/Toolkit/Common/PeribindTheme";
        private const string LoginStyleResourcePath = "UI/Toolkit/Login/LoginMenu";
        private const string LoginPanelName = "login-panel";
        private const string RegisterPanelName = "register-panel";
        private const string LoginInfoName = "login-info-label";
        private const string RegisterInfoName = "register-info-label";
        private const string RegisterModeInfoName = "register-mode-info-label";
        private const string DefaultLoginInfoMessage = "Sign in with your Unity Player Account in browser.";
        private const string LoginButtonName = "login-submit-button";
        private const string RegisterPanelButtonName = "open-register-button";
        private const string QuitButtonName = "quit-button";
        private const string RegisterSubmitButtonName = "register-submit-button";
        private const string ReturnButtonName = "return-login-button";

        private bool _isSubmitting;
        private bool _isLoginPanelVisible = true;
        private bool _uiToolkitCallbacksRegistered;

        private UiVisualElement _uiRoot;
        private UiVisualElement _uiLoginPanel;
        private UiVisualElement _uiRegisterPanel;
        private UiLabel _uiLoginInfoLabel;
        private UiLabel _uiRegisterInfoLabel;
        private UiLabel _uiRegisterModeInfoLabel;
        private UiButton _uiLoginButton;
        private UiButton _uiRegisterPanelButton;
        private UiButton _uiQuitButton;
        private UiButton _uiRegisterSubmitButton;
        private UiButton _uiReturnButton;

        private void Awake()
        {
            TryBindUiToolkit();
            SetPanelState(showLogin: true);
            SetLoginInfo(string.Empty);
            SetRegisterInfo(string.Empty);
            ApplyAuthModePresentation();
        }

        private void Update()
        {
            if (enableUiToolkit && _uiRoot == null)
            {
                TryBindUiToolkit();
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (!keyboard.enterKey.wasPressedThisFrame && !keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                return;
            }

            if (_isLoginPanelVisible)
            {
                OnLoginClicked();
            }
            else
            {
                OnRegisterSubmitClicked();
            }
        }

        private void OnDestroy()
        {
            UnregisterUiToolkitCallbacks();
        }

        private async void OnLoginClicked()
        {
            if (_isSubmitting)
            {
                CancelPendingPlayerAccountFlow("Previous browser authentication cancelled. Restarting sign-in...");
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
            SetButtonsInteractable(false);
            SetLoginInfo("Opening browser for sign-in...");
            try
            {
                var result = await ugsBootstrap.SignInWithPlayerAccountAsync(isSignUpFlow: false);
                if (!result.Success)
                {
                    SetLoginInfo(string.IsNullOrWhiteSpace(result.Message) ? "Sign-in failed." : result.Message);
                    return;
                }

                await SyncPlayerProfileAsync();

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

        private async void OnRegisterSubmitClicked()
        {
            if (_isSubmitting)
            {
                CancelPendingPlayerAccountFlow("Previous browser authentication cancelled. Restarting registration...");
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
            SetButtonsInteractable(false);
            SetRegisterInfo("Opening browser for registration...");
            try
            {
                var result = await ugsBootstrap.SignInWithPlayerAccountAsync(isSignUpFlow: true);
                if (!result.Success)
                {
                    SetRegisterInfo(string.IsNullOrWhiteSpace(result.Message) ? "Registration failed." : result.Message);
                    return;
                }

                await SyncPlayerProfileAsync();

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

        private void OnRegisterPanelClicked()
        {
            if (_isSubmitting)
            {
                CancelPendingPlayerAccountFlow("Browser authentication cancelled.");
            }

            SetPanelState(showLogin: false);
            SetRegisterInfo("Account creation continues in browser.");
            ClearSelection();
        }

        private void OnReturnToLoginClicked()
        {
            if (_isSubmitting)
            {
                CancelPendingPlayerAccountFlow("Browser authentication cancelled.");
            }

            SetRegisterInfo(string.Empty);
            SetPanelState(showLogin: true);
        }

        private void OnQuitClicked()
        {
            ClearSelection();
            UnityEngine.Application.Quit();
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

        private static void ClearSelection()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                eventSystem.SetSelectedGameObject(null);
            }
        }

        private void SetButtonsInteractable(bool interactable)
        {
            _uiLoginButton?.SetEnabled(interactable);
            _uiRegisterPanelButton?.SetEnabled(interactable);
            _uiQuitButton?.SetEnabled(interactable);
            _uiRegisterSubmitButton?.SetEnabled(interactable);
            _uiReturnButton?.SetEnabled(interactable);
        }

        private void SetPanelState(bool showLogin)
        {
            _isLoginPanelVisible = showLogin;

            if (_uiLoginPanel != null)
            {
                _uiLoginPanel.style.display = showLogin ? UiDisplayStyle.Flex : UiDisplayStyle.None;
            }

            if (_uiRegisterPanel != null)
            {
                _uiRegisterPanel.style.display = showLogin ? UiDisplayStyle.None : UiDisplayStyle.Flex;
            }
        }

        private void SetLoginInfo(string message)
        {
            if (_uiLoginInfoLabel != null)
            {
                var resolved = string.IsNullOrWhiteSpace(message) ? DefaultLoginInfoMessage : message;
                _uiLoginInfoLabel.text = resolved;
                _uiLoginInfoLabel.style.display = UiDisplayStyle.Flex;
            }
        }

        private void SetRegisterInfo(string message)
        {
            if (_uiRegisterInfoLabel != null)
            {
                var resolved = message ?? string.Empty;
                _uiRegisterInfoLabel.text = resolved;
                _uiRegisterInfoLabel.style.display = string.IsNullOrWhiteSpace(resolved) ? UiDisplayStyle.None : UiDisplayStyle.Flex;
            }
        }

        private void ApplyAuthModePresentation()
        {
            SetLoginInfo(DefaultLoginInfoMessage);

            if (_uiRegisterModeInfoLabel != null)
            {
                _uiRegisterModeInfoLabel.text = "Account creation continues in browser.";
            }
        }

        private void TryBindUiToolkit()
        {
            if (!enableUiToolkit)
            {
                return;
            }

            if (uiDocument == null && autoAssignUiDocument)
            {
                uiDocument = FindObjectOfType<UIDocument>(true);
            }

            if (uiDocument == null)
            {
                return;
            }

            if (autoAssignVisualTreeFromResources && uiDocument.visualTreeAsset == null)
            {
                var tree = Resources.Load<VisualTreeAsset>(LoginUxmlResourcePath);
                if (tree == null)
                {
                    Debug.LogWarning($"[LoginMenuUITK] Missing UXML at Resources/{LoginUxmlResourcePath}.uxml");
                    return;
                }

                uiDocument.visualTreeAsset = tree;
            }

            _uiRoot = uiDocument.rootVisualElement;
            if (_uiRoot == null)
            {
                return;
            }

            if (autoAssignStylesFromResources)
            {
                TryAddStyle(_uiRoot, CommonStyleResourcePath);
                TryAddStyle(_uiRoot, LoginStyleResourcePath);
            }

            _uiLoginPanel = UnityEngine.UIElements.UQueryExtensions.Q<UiVisualElement>(_uiRoot, LoginPanelName);
            _uiRegisterPanel = UnityEngine.UIElements.UQueryExtensions.Q<UiVisualElement>(_uiRoot, RegisterPanelName);
            _uiLoginInfoLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, LoginInfoName);
            _uiRegisterInfoLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, RegisterInfoName);
            _uiRegisterModeInfoLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, RegisterModeInfoName);
            _uiLoginButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, LoginButtonName);
            _uiRegisterPanelButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, RegisterPanelButtonName);
            _uiQuitButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, QuitButtonName);
            _uiRegisterSubmitButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, RegisterSubmitButtonName);
            _uiReturnButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, ReturnButtonName);

            RegisterUiToolkitCallbacks();
            ApplyAuthModePresentation();
            SetPanelState(_isLoginPanelVisible);
        }

        private void RegisterUiToolkitCallbacks()
        {
            if (_uiToolkitCallbacksRegistered)
            {
                return;
            }

            if (_uiLoginButton != null)
            {
                _uiLoginButton.clicked += OnLoginClicked;
            }

            if (_uiRegisterPanelButton != null)
            {
                _uiRegisterPanelButton.clicked += OnRegisterPanelClicked;
            }

            if (_uiQuitButton != null)
            {
                _uiQuitButton.clicked += OnQuitClicked;
            }

            if (_uiRegisterSubmitButton != null)
            {
                _uiRegisterSubmitButton.clicked += OnRegisterSubmitClicked;
            }

            if (_uiReturnButton != null)
            {
                _uiReturnButton.clicked += OnReturnToLoginClicked;
            }

            _uiToolkitCallbacksRegistered = true;
        }

        private void UnregisterUiToolkitCallbacks()
        {
            if (!_uiToolkitCallbacksRegistered)
            {
                return;
            }

            if (_uiLoginButton != null)
            {
                _uiLoginButton.clicked -= OnLoginClicked;
            }

            if (_uiRegisterPanelButton != null)
            {
                _uiRegisterPanelButton.clicked -= OnRegisterPanelClicked;
            }

            if (_uiQuitButton != null)
            {
                _uiQuitButton.clicked -= OnQuitClicked;
            }

            if (_uiRegisterSubmitButton != null)
            {
                _uiRegisterSubmitButton.clicked -= OnRegisterSubmitClicked;
            }

            if (_uiReturnButton != null)
            {
                _uiReturnButton.clicked -= OnReturnToLoginClicked;
            }

            _uiToolkitCallbacksRegistered = false;
        }

        private static void TryAddStyle(UiVisualElement root, string resourcePath)
        {
            if (root == null || string.IsNullOrWhiteSpace(resourcePath))
            {
                return;
            }

            var styleSheet = Resources.Load<StyleSheet>(resourcePath);
            if (styleSheet == null)
            {
                return;
            }

            if (!root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }
        }

        private async System.Threading.Tasks.Task SyncPlayerProfileAsync()
        {
            if (matchRegistryClient == null)
            {
                matchRegistryClient = FindObjectOfType<MatchRegistryClient>(true);
            }

            if (matchRegistryClient == null)
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

            var username = ResolveProfileUsername(playerId);
            if (string.IsNullOrWhiteSpace(username))
            {
                return;
            }

            await matchRegistryClient.UpsertPlayerAsync(playerId, username);
        }

        private static string ResolveProfileUsername(string fallback)
        {
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

            return string.IsNullOrWhiteSpace(fallback) ? string.Empty : fallback.Trim();
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

            var message = $"Update required. Current {currentVersion}, minimum {release.minSupportedVersion}.";
            if (!string.IsNullOrWhiteSpace(release.downloadUrl))
            {
                message = $"{message} Download latest client.";
            }

            setInfo?.Invoke(message);
            return false;
        }
    }
}

