using Peribind.Unity.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Peribind.Unity.UI
{
    public class LobbyMenu : MonoBehaviour
    {
        [SerializeField] private TMP_InputField joinCodeInput;
        [SerializeField] private Button joinButton;
        [SerializeField] private Button returnButton;
        [SerializeField] private DirectConnectionController directConnection;
        [SerializeField] private string addressPlaceholder = "127.0.0.1";
        [SerializeField] private string defaultAddress = "127.0.0.1";
        [SerializeField] private string starterSceneName = "StarterScene";
        private bool _isTransitionBusy;

        private void Awake()
        {
            if (joinCodeInput != null)
            {
                if (joinCodeInput.placeholder is TMP_Text placeholderText)
                {
                    placeholderText.text = addressPlaceholder;
                }

                if (string.IsNullOrWhiteSpace(joinCodeInput.text))
                {
                    joinCodeInput.text = defaultAddress;
                }
            }

            if (joinButton != null)
            {
                joinButton.onClick.AddListener(OnJoinClicked);
            }

            if (returnButton != null)
            {
                returnButton.onClick.AddListener(OnReturnClicked);
            }
        }

        private void OnDestroy()
        {
            if (joinButton != null)
            {
                joinButton.onClick.RemoveListener(OnJoinClicked);
            }

            if (returnButton != null)
            {
                returnButton.onClick.RemoveListener(OnReturnClicked);
            }
        }

        private void OnJoinClicked()
        {
            if (directConnection == null)
            {
                directConnection = FindObjectOfType<DirectConnectionController>(true);
            }

            if (directConnection == null)
            {
                Debug.LogWarning("[LobbyMenu] Join blocked: DirectConnectionController missing.");
                return;
            }

            ClearSelection();
            var address = joinCodeInput != null ? joinCodeInput.text : string.Empty;
            Debug.Log($"[LobbyMenu] Join clicked. Address='{address}'");
            if (string.IsNullOrWhiteSpace(address))
            {
                Debug.LogWarning("[LobbyMenu] Join blocked: empty address.");
                return;
            }

            BeginLeaveThen(() =>
            {
                directConnection.StartClient(address);
            });
        }

        private void OnReturnClicked()
        {
            BeginLeaveThen(() =>
            {
                SceneManager.LoadScene(starterSceneName);
            });
        }

        private void BeginLeaveThen(System.Action next)
        {
            if (_isTransitionBusy)
            {
                return;
            }

            _isTransitionBusy = true;
            StartCoroutine(LeaveThen(next));
        }

        private System.Collections.IEnumerator LeaveThen(System.Action next)
        {
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
