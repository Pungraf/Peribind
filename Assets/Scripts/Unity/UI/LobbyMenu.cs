using System.Collections;
using Peribind.Unity.Networking;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Peribind.Unity.UI
{
    public class LobbyMenu : MonoBehaviour
    {
        [SerializeField] private MultiplayerSessionController sessionController;
        [SerializeField] private TMP_InputField joinCodeInput;
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private Button quickJoinButton;
        [SerializeField] private Button returnButton;
        [SerializeField] private Button startGameButton;
        [SerializeField] private string lobbySceneName = "LobbyScene";
        [SerializeField] private string gameSceneName = "GameScene";
        [SerializeField] private string starterSceneName = "StarterScene";
        [SerializeField] private float waitForSessionSeconds = 10f;
        [SerializeField] private bool waitForNetworkStart = true;
        private Coroutine _waitRoutine;
        private bool _autoLoadTriggered;
        private bool _isTransitionBusy;

        private void Awake()
        {
            var lobbyScene = SceneManager.GetSceneByName(lobbySceneName);
            if (lobbyScene.IsValid() && lobbyScene.isLoaded)
            {
                SceneManager.SetActiveScene(lobbyScene);
            }
            var gameScene = SceneManager.GetSceneByName(gameSceneName);
            if (gameScene.IsValid() && gameScene.isLoaded && gameScene != lobbyScene)
            {
                SceneManager.UnloadSceneAsync(gameScene);
            }
            Debug.Log($"[LobbyMenu] Awake ActiveScene={SceneManager.GetActiveScene().name}");

            if (sessionController == null)
            {
                sessionController = FindObjectOfType<MultiplayerSessionController>(true);
                if (sessionController == null)
                {
                    Debug.LogWarning("[LobbyMenu] MultiplayerSessionController not found in scene.");
                }
            }

            if (hostButton != null)
            {
                hostButton.onClick.AddListener(OnHostClicked);
            }

            if (joinButton != null)
            {
                joinButton.onClick.AddListener(OnJoinClicked);
            }

            if (quickJoinButton != null)
            {
                quickJoinButton.onClick.AddListener(OnQuickJoinClicked);
            }

            if (returnButton != null)
            {
                returnButton.onClick.AddListener(OnReturnClicked);
            }

            if (startGameButton != null)
            {
                startGameButton.onClick.AddListener(OnStartGameClicked);
            }
        }

        private void OnDestroy()
        {
            if (hostButton != null)
            {
                hostButton.onClick.RemoveListener(OnHostClicked);
            }

            if (joinButton != null)
            {
                joinButton.onClick.RemoveListener(OnJoinClicked);
            }

            if (quickJoinButton != null)
            {
                quickJoinButton.onClick.RemoveListener(OnQuickJoinClicked);
            }

            if (returnButton != null)
            {
                returnButton.onClick.RemoveListener(OnReturnClicked);
            }

            if (startGameButton != null)
            {
                startGameButton.onClick.RemoveListener(OnStartGameClicked);
            }
        }

        private void OnEnable()
        {
            if (sessionController == null)
            {
                sessionController = FindObjectOfType<MultiplayerSessionController>(true);
            }

            _autoLoadTriggered = false;
        }

        private void OnHostClicked()
        {
            if (sessionController == null)
            {
                sessionController = FindObjectOfType<MultiplayerSessionController>(true);
                if (sessionController == null)
                {
                    Debug.LogWarning("[LobbyMenu] Host blocked: sessionController missing.");
                    return;
                }
            }
            if (sessionController.IsLeaving)
            {
                Debug.Log("[LobbyMenu] Host blocked: leave in progress.");
                return;
            }

            ClearSelection();
            BeginLeaveThen(() =>
            {
                sessionController.CreateSession();
                StartWaitForSession();
            }, true);
        }

        private void OnJoinClicked()
        {
            if (sessionController == null)
            {
                sessionController = FindObjectOfType<MultiplayerSessionController>(true);
                if (sessionController == null)
                {
                    Debug.LogWarning("[LobbyMenu] Join blocked: sessionController missing.");
                    return;
                }
            }
            if (sessionController.IsLeaving)
            {
                Debug.Log("[LobbyMenu] Join blocked: leave in progress.");
                return;
            }

            ClearSelection();
            var code = joinCodeInput != null ? joinCodeInput.text : string.Empty;
            Debug.Log($"[LobbyMenu] Join clicked. Code='{code}'");
            BeginLeaveThen(() =>
            {
                sessionController.JoinSessionByCode(code);
                StartWaitForSession();
            }, true);
        }

        private void OnQuickJoinClicked()
        {
            if (sessionController == null)
            {
                sessionController = FindObjectOfType<MultiplayerSessionController>(true);
                if (sessionController == null)
                {
                    Debug.LogWarning("[LobbyMenu] QuickJoin blocked: sessionController missing.");
                    return;
                }
            }
            if (sessionController.IsLeaving)
            {
                Debug.Log("[LobbyMenu] QuickJoin blocked: leave in progress.");
                return;
            }

            ClearSelection();
            Debug.Log("[LobbyMenu] QuickJoin clicked.");
            BeginLeaveThen(() =>
            {
                sessionController.QuickJoinOrCreate();
                StartWaitForSession();
            }, true);
        }

        private void OnReturnClicked()
        {
            BeginLeaveThen(() =>
            {
                SceneManager.LoadScene(starterSceneName);
            }, false);
        }

        private void OnStartGameClicked()
        {
            if (sessionController == null)
            {
                return;
            }

            if (!sessionController.HasSession)
            {
                return;
            }

            sessionController.StartGameAsHost();
            StartWaitForSession();
        }

        private void StartWaitForSession()
        {
            if (_waitRoutine != null)
            {
                StopCoroutine(_waitRoutine);
            }

            _waitRoutine = StartCoroutine(WaitForSessionThenLoad());
        }

        private IEnumerator WaitForSessionThenLoad()
        {
            var endTime = Time.realtimeSinceStartup + Mathf.Max(1f, waitForSessionSeconds);
            while (Time.realtimeSinceStartup < endTime)
            {
                if (sessionController != null && sessionController.HasSession)
                {
                    if (!waitForNetworkStart || IsNetworkReady())
                    {
                        if (!sessionController.IsGameStarted)
                        {
                            yield return null;
                            continue;
                        }

                        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                        {
                            NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
                        }
                        yield break;
                    }
                }

                yield return null;
            }
        }

        private void Update()
        {
            if (_autoLoadTriggered)
            {
                return;
            }

            if (sessionController == null || !sessionController.HasSession || !sessionController.IsGameStarted)
            {
                return;
            }

            var manager = NetworkManager.Singleton;
            if (manager != null && manager.IsServer)
            {
                _autoLoadTriggered = true;
                Debug.Log("[LobbyMenu] Auto-loading game scene for host (game already started).");
                manager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
            }
        }

        private static bool IsNetworkReady()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null)
            {
                return false;
            }

            return manager.IsListening || manager.IsClient || manager.IsServer;
        }

        private void BeginLeaveThen(System.Action next, bool destroyNetworkManager)
        {
            if (_isTransitionBusy)
            {
                return;
            }

            _isTransitionBusy = true;
            StartCoroutine(LeaveThen(next, destroyNetworkManager));
        }

        private IEnumerator LeaveThen(System.Action next, bool destroyNetworkManager)
        {
            if (sessionController != null)
            {
                var task = sessionController.LeaveAndShutdownAsync(destroyNetworkManager);
                while (!task.IsCompleted)
                {
                    yield return null;
                }
            }

            // Give transport a brief moment to fully release.
            yield return null;

            _isTransitionBusy = false;
            next?.Invoke();
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
