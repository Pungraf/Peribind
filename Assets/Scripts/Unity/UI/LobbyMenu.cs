using System.Collections;
using Peribind.Unity.Networking;
using TMPro;
using Unity.Netcode;
using UnityEngine;
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
        [SerializeField] private string gameSceneName = "GameScene";
        [SerializeField] private string starterSceneName = "StarterScene";
        [SerializeField] private float waitForSessionSeconds = 10f;
        [SerializeField] private bool waitForNetworkStart = true;

        private Coroutine _waitRoutine;

        private void Awake()
        {
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
        }

        private void OnHostClicked()
        {
            if (sessionController == null)
            {
                return;
            }

            sessionController.CreateSession();
            StartWaitForSession();
        }

        private void OnJoinClicked()
        {
            if (sessionController == null)
            {
                return;
            }

            var code = joinCodeInput != null ? joinCodeInput.text : string.Empty;
            sessionController.JoinSessionByCode(code);
            StartWaitForSession();
        }

        private void OnQuickJoinClicked()
        {
            if (sessionController == null)
            {
                return;
            }

            sessionController.QuickJoinOrCreate();
            StartWaitForSession();
        }

        private void OnReturnClicked()
        {
            SceneManager.LoadScene(starterSceneName);
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

        private static bool IsNetworkReady()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null)
            {
                return false;
            }

            return manager.IsListening || manager.IsClient || manager.IsServer;
        }
    }
}
