using UnityEngine;
using UnityEngine.UIElements;

namespace Peribind.Unity.UI
{
    [RequireComponent(typeof(UIDocument))]
    public class StarterMenuToolkitController : MonoBehaviour
    {
        private const string StarterUxmlResourcePath = "UI/Toolkit/Starter/StarterMenu";
        private const string CommonStyleResourcePath = "UI/Toolkit/Common/PeribindTheme";
        private const string StarterStyleResourcePath = "UI/Toolkit/Starter/StarterMenu";

        private const string TitleLabelName = "title-label";
        private const string PlayButtonName = "play-button";
        private const string ProfileButtonName = "profile-button";
        private const string LogoutButtonName = "logout-button";

        [Header("Dependencies")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private StarterMenu starterMenu;

        [Header("Auto Setup")]
        [SerializeField] private bool autoAssignVisualTreeFromResources = true;
        [SerializeField] private bool autoAssignStylesFromResources = true;

        private Button _playButton;
        private Button _profileButton;
        private Button _logoutButton;
        private bool _callbacksRegistered;

        private void Awake()
        {
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }

            if (starterMenu == null)
            {
                starterMenu = FindObjectOfType<StarterMenu>(true);
            }

            if (uiDocument == null)
            {
                Debug.LogError("[StarterMenuUITK] UIDocument is missing.");
                enabled = false;
                return;
            }

            if (autoAssignVisualTreeFromResources && uiDocument.visualTreeAsset == null)
            {
                var tree = Resources.Load<VisualTreeAsset>(StarterUxmlResourcePath);
                if (tree == null)
                {
                    Debug.LogError($"[StarterMenuUITK] Missing UXML at Resources/{StarterUxmlResourcePath}.uxml");
                    enabled = false;
                    return;
                }

                uiDocument.visualTreeAsset = tree;
            }
        }

        private void OnEnable()
        {
            if (uiDocument == null)
            {
                return;
            }

            var root = uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[StarterMenuUITK] rootVisualElement is null.");
                return;
            }

            if (autoAssignStylesFromResources)
            {
                TryAddStyle(root, CommonStyleResourcePath);
                TryAddStyle(root, StarterStyleResourcePath);
            }

            var titleLabel = root.Q<Label>(TitleLabelName);
            if (titleLabel != null && string.IsNullOrWhiteSpace(titleLabel.text))
            {
                titleLabel.text = "Peribind";
            }

            _playButton = root.Q<Button>(PlayButtonName);
            _profileButton = root.Q<Button>(ProfileButtonName);
            _logoutButton = root.Q<Button>(LogoutButtonName);

            RegisterCallbacks();
        }

        private void OnDisable()
        {
            UnregisterCallbacks();
        }

        private void RegisterCallbacks()
        {
            if (_callbacksRegistered)
            {
                return;
            }

            if (_playButton != null)
            {
                _playButton.clicked += OnPlayClicked;
            }
            else
            {
                Debug.LogWarning("[StarterMenuUITK] Missing play button element.");
            }

            if (_profileButton != null)
            {
                _profileButton.clicked += OnProfileClicked;
            }
            else
            {
                Debug.LogWarning("[StarterMenuUITK] Missing profile button element.");
            }

            if (_logoutButton != null)
            {
                _logoutButton.clicked += OnLogoutClicked;
            }
            else
            {
                Debug.LogWarning("[StarterMenuUITK] Missing logout button element.");
            }

            _callbacksRegistered = true;
        }

        private void UnregisterCallbacks()
        {
            if (!_callbacksRegistered)
            {
                return;
            }

            if (_playButton != null)
            {
                _playButton.clicked -= OnPlayClicked;
            }

            if (_profileButton != null)
            {
                _profileButton.clicked -= OnProfileClicked;
            }

            if (_logoutButton != null)
            {
                _logoutButton.clicked -= OnLogoutClicked;
            }

            _callbacksRegistered = false;
        }

        private void OnPlayClicked()
        {
            if (starterMenu == null)
            {
                starterMenu = FindObjectOfType<StarterMenu>(true);
            }

            if (starterMenu == null)
            {
                Debug.LogError("[StarterMenuUITK] StarterMenu is missing. Cannot load lobby scene.");
                return;
            }

            starterMenu.LoadLobbyScene();
        }

        private void OnProfileClicked()
        {
            if (starterMenu == null)
            {
                starterMenu = FindObjectOfType<StarterMenu>(true);
            }

            if (starterMenu == null)
            {
                Debug.LogError("[StarterMenuUITK] StarterMenu is missing. Cannot load profile scene.");
                return;
            }

            starterMenu.LoadProfileScene();
        }

        private void OnLogoutClicked()
        {
            if (starterMenu == null)
            {
                starterMenu = FindObjectOfType<StarterMenu>(true);
            }

            if (starterMenu == null)
            {
                Debug.LogError("[StarterMenuUITK] StarterMenu is missing. Cannot logout.");
                return;
            }

            starterMenu.Logout();
        }

        private static void TryAddStyle(VisualElement root, string resourcePath)
        {
            if (root == null)
            {
                return;
            }

            var style = Resources.Load<StyleSheet>(resourcePath);
            if (style == null)
            {
                Debug.LogWarning($"[StarterMenuUITK] Missing USS at Resources/{resourcePath}.uss");
                return;
            }

            if (!root.styleSheets.Contains(style))
            {
                root.styleSheets.Add(style);
            }
        }
    }
}
