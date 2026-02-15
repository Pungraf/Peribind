using Peribind.Unity.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.InputSystem;

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

        [Header("Client Version Gate")]
        [SerializeField] private bool enforceMinClientVersion = true;
        [SerializeField] private string releaseChannel = "stable";
        [SerializeField] private string releasePlatform = "win64";

        private bool _isSubmitting;

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
            SetPasswordMode(registerPasswordInput);
            SetPasswordMode(registerConfirmPasswordInput);

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
                return;
            }

            var login = loginInput != null ? loginInput.text : string.Empty;
            var password = passwordInput != null ? passwordInput.text : string.Empty;
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                SetLoginInfo("Enter login and password.");
                return;
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
            SetLoginInfo("Signing in...");
            try
            {
                var result = await ugsBootstrap.SignInWithUsernamePasswordAsync(login, password);
                if (!result.Success)
                {
                    SetLoginInfo(string.IsNullOrWhiteSpace(result.Message) ? "Invalid login or password." : result.Message);
                    return;
                }

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

        private void OnRegisterPanelClicked()
        {
            if (_isSubmitting)
            {
                return;
            }

            SetPanelState(showLogin: false);
            SetRegisterInfo(string.Empty);
            ClearSelection();
        }

        private async void OnRegisterSubmitClicked()
        {
            if (_isSubmitting)
            {
                return;
            }

            var login = registerLoginInput != null ? registerLoginInput.text : string.Empty;
            var password = registerPasswordInput != null ? registerPasswordInput.text : string.Empty;
            var confirm = registerConfirmPasswordInput != null ? registerConfirmPasswordInput.text : string.Empty;

            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(confirm))
            {
                SetRegisterInfo("Fill all fields.");
                return;
            }

            if (!string.Equals(password, confirm, System.StringComparison.Ordinal))
            {
                SetRegisterInfo("Password and confirmation do not match.");
                return;
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
            SetRegisterInfo("Creating account...");
            try
            {
                var result = await ugsBootstrap.RegisterWithUsernamePasswordAsync(login, password);
                if (!result.Success)
                {
                    SetRegisterInfo(string.IsNullOrWhiteSpace(result.Message) ? "Registration failed." : result.Message);
                    return;
                }

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
                return;
            }

            if (registerLoginInput != null) registerLoginInput.text = string.Empty;
            if (registerPasswordInput != null) registerPasswordInput.text = string.Empty;
            if (registerConfirmPasswordInput != null) registerConfirmPasswordInput.text = string.Empty;
            SetRegisterInfo(string.Empty);
            SetPanelState(showLogin: true);
            FocusInput(loginInput);
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
