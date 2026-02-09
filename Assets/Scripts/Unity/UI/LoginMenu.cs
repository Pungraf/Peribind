using Peribind.Unity.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Peribind.Unity.UI
{
    public class LoginMenu : MonoBehaviour
    {
        [SerializeField] private TMP_InputField loginInput;
        [SerializeField] private TMP_InputField passwordInput;
        [SerializeField] private Button loginButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private PlayerIdentityProvider identityProvider;
        [SerializeField] private string nextSceneName = "StarterScene";

        private void Awake()
        {
            if (loginButton != null)
            {
                loginButton.onClick.AddListener(OnLoginClicked);
            }

            if (quitButton != null)
            {
                quitButton.onClick.AddListener(OnQuitClicked);
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
        }

        private void OnLoginClicked()
        {
            if (identityProvider == null)
            {
                identityProvider = FindObjectOfType<PlayerIdentityProvider>(true);
            }

            if (identityProvider == null)
            {
                Debug.LogWarning("[LoginMenu] PlayerIdentityProvider missing.");
                return;
            }

            var login = loginInput != null ? loginInput.text : string.Empty;
            var password = passwordInput != null ? passwordInput.text : string.Empty;
            if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(password))
            {
                Debug.LogWarning("[LoginMenu] Login blocked: empty credentials.");
                return;
            }

            identityProvider.SetFromCredentials(login, password);
            ClearSelection();

            if (!string.IsNullOrWhiteSpace(nextSceneName))
            {
                SceneManager.LoadScene(nextSceneName);
            }
        }

        private void OnQuitClicked()
        {
            ClearSelection();
            UnityEngine.Application.Quit();
        }

        private void ClearSelection()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                eventSystem.SetSelectedGameObject(null);
            }
        }
    }
}
