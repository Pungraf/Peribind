using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Peribind.Unity.Board;
using Peribind.Unity.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace Peribind.Unity.UI
{
    public class GameHudView : MonoBehaviour
    {
        [SerializeField] private BoardPresenter boardPresenter;
        [SerializeField] private TMP_Text playerOneScoreText;
        [SerializeField] private TMP_Text playerTwoScoreText;
        [SerializeField] private TMP_Text roundText;
        [SerializeField] private TMP_Text turnText;
        [SerializeField] private TMP_Text infoText;
        [SerializeField] private Button finishRoundButton;
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private string starterSceneName = "StarterScene";
        [SerializeField] private string gameSceneName = "GameScene";
        [SerializeField] private string lobbySceneName = "LobbyScene";
        private const float ExitTimeoutSeconds = 3f;
        [SerializeField] private TMP_Text playAgainButtonText;
        [SerializeField] private string playAgainLabel = "Play Again";
        [SerializeField] private string restartLabel = "Restart";
        [SerializeField] private MultiplayerSessionController sessionController;

        private bool _menuOpen;
        private bool _isExiting;
        private Button[] _menuButtons;

        private void Awake()
        {
            if (sessionController == null)
            {
                sessionController = FindObjectOfType<MultiplayerSessionController>();
            }

            if (finishRoundButton != null)
            {
                finishRoundButton.onClick.AddListener(OnFinishRoundClicked);
            }

            if (gameOverPanel != null)
            {
                _menuButtons = gameOverPanel.GetComponentsInChildren<Button>(true);
            }
        }

        private void OnDestroy()
        {
            if (finishRoundButton != null)
            {
                finishRoundButton.onClick.RemoveListener(OnFinishRoundClicked);
            }
        }

        private void Update()
        {
            if (boardPresenter == null)
            {
                return;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !boardPresenter.IsGameOver)
            {
                _menuOpen = !_menuOpen;
            }

            if (playerOneScoreText != null)
            {
                playerOneScoreText.text = $"P1: {boardPresenter.GetTotalScore(0)}";
            }

            if (playerTwoScoreText != null)
            {
                playerTwoScoreText.text = $"P2: {boardPresenter.GetTotalScore(1)}";
            }

            if (roundText != null)
            {
                roundText.text = $"Round {boardPresenter.CurrentRound}/2";
            }

            if (turnText != null)
            {
                if (boardPresenter.IsGameOver)
                {
                    turnText.text = "Game Over";
                }
                else
                {
                    var current = boardPresenter.CurrentPlayerId + 1;
                    var finished = boardPresenter.HasFinishedRound(boardPresenter.CurrentPlayerId) ? " (Finished)" : string.Empty;
                    turnText.text = $"Turn: P{current}{finished}";
                }
            }

            if (gameOverPanel != null)
            {
                var shouldShow = boardPresenter.IsGameOver || _menuOpen;
                gameOverPanel.SetActive(shouldShow);
                UpdateMenuButtons(shouldShow);
            }

            if (playAgainButtonText != null)
            {
                playAgainButtonText.text = boardPresenter.IsGameOver ? playAgainLabel : restartLabel;
            }
        }

        public void ShowInfo(string message)
        {
            if (infoText == null)
            {
                return;
            }

            infoText.text = message;
            infoText.gameObject.SetActive(true);
        }

        public void HideInfo()
        {
            if (infoText == null)
            {
                return;
            }

            infoText.text = string.Empty;
            infoText.gameObject.SetActive(false);
        }

        private void OnFinishRoundClicked()
        {
            if (boardPresenter == null)
            {
                return;
            }

            boardPresenter.FinishRoundForCurrentPlayer();
        }

        public void PlayAgain()
        {
            if (_isExiting)
            {
                return;
            }

            if (sessionController != null)
            {
                _ = sessionController.LeaveAndShutdownAsync(true);
            }
            SceneManager.LoadScene(gameSceneName);
        }

        public void ExitToStarter()
        {
            if (_isExiting)
            {
                return;
            }

            StartCoroutine(ExitFlow(starterSceneName));
        }

        private void UpdateMenuButtons(bool menuVisible)
        {
            if (_menuButtons == null || _menuButtons.Length == 0)
            {
                return;
            }

            foreach (var button in _menuButtons)
            {
                if (button != null)
                {
                    button.interactable = menuVisible;
                }
            }
        }

        public void OpenMenu()
        {
            _menuOpen = true;
        }

        public void CloseMenu()
        {
            _menuOpen = false;
        }

        private System.Collections.IEnumerator ExitFlow(string targetScene)
        {
            _isExiting = true;
            _menuOpen = false;

            if (gameOverPanel != null)
            {
                gameOverPanel.SetActive(false);
            }

            if (sessionController != null)
            {
                var leaveTask = sessionController.LeaveAndShutdownAsync(true);
                var endTime = Time.unscaledTime + ExitTimeoutSeconds;
                while (!leaveTask.IsCompleted && Time.unscaledTime < endTime)
                {
                    yield return null;
                }
            }

            SceneManager.LoadScene(targetScene);
        }
    }
}
