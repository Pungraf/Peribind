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
        [SerializeField] private TMP_Text stateText;
        [SerializeField] private Button finishRoundButton;
        [SerializeField] private Button exitButton;
        [SerializeField] private TMP_Text exitButtonText;
        [SerializeField] private GameObject gameOverPanel;
        [SerializeField] private string starterSceneName = "StarterScene";
        [SerializeField] private string gameSceneName = "GameScene";
        [SerializeField] private string lobbySceneName = "LobbyScene";
        private const float ExitTimeoutSeconds = 8f;
        [SerializeField] private MultiplayerSessionController sessionController;
        [SerializeField] private NetworkGameController networkController;
        [SerializeField] private string surrenderingInfo = "You surrendered. Leaving match...";
        [SerializeField] private string opponentSurrenderedInfo = "Opponent surrendered. You win.";
        [SerializeField] private string surrenderButtonLabel = "Surrender";
        [SerializeField] private string acknowledgeButtonLabel = "OK";
        [SerializeField] private float surrenderExitDelaySeconds = 0.35f;

        private bool _menuOpen;
        private bool _isExiting;
        private bool _awaitingSurrenderAcknowledge;
        private bool _surrenderRequested;
        private bool _gameOverHandled;
        private bool _networkEventsBound;
        private Button[] _menuButtons;

        private void Awake()
        {
            if (sessionController == null)
            {
                sessionController = FindObjectOfType<MultiplayerSessionController>();
            }

            if (networkController == null)
            {
                networkController = FindObjectOfType<NetworkGameController>();
            }
            BindNetworkEventsIfNeeded();

            if (stateText != null)
            {
                stateText.text = string.Empty;
                stateText.gameObject.SetActive(false);
            }
            UpdateExitButtonState();

            if (finishRoundButton != null)
            {
                finishRoundButton.onClick.AddListener(OnFinishRoundClicked);
            }

            if (gameOverPanel != null)
            {
                _menuButtons = gameOverPanel.GetComponentsInChildren<Button>(true);
            }
        }

        private void OnEnable()
        {
            if (networkController == null)
            {
                networkController = FindObjectOfType<NetworkGameController>();
            }
            BindNetworkEventsIfNeeded();
        }

        private void OnDisable()
        {
            UnbindNetworkEvents();
        }

        private void OnDestroy()
        {
            if (finishRoundButton != null)
            {
                finishRoundButton.onClick.RemoveListener(OnFinishRoundClicked);
            }

            UnbindNetworkEvents();
        }

        private void Update()
        {
            if (boardPresenter == null)
            {
                return;
            }

            BindNetworkEventsIfNeeded();

            if (!_awaitingSurrenderAcknowledge && !_surrenderRequested && !_isExiting &&
                networkController != null && networkController.WasSurrendered && boardPresenter.IsGameOver)
            {
                var localPlayerId = networkController.LocalPlayerId;
                var localWon = networkController.WinningPlayerId >= 0 && localPlayerId == networkController.WinningPlayerId;
                ShowInfo(localWon ? opponentSurrenderedInfo : "Match ended.");
                _awaitingSurrenderAcknowledge = true;
                _menuOpen = true;
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
            if (!boardPresenter.IsGameOver)
            {
                _gameOverHandled = false;
            }
            else if (!_gameOverHandled && networkController != null && !networkController.WasSurrendered)
            {
                var winner = networkController.WinningPlayerId;
                if (winner >= 0)
                {
                    ShowInfo($"Game over. Player {winner + 1} won.");
                }
                else
                {
                    ShowInfo("Game over. Draw.");
                }

                _menuOpen = true;
                _surrenderRequested = false;
                _awaitingSurrenderAcknowledge = true;
                _gameOverHandled = true;
            }
            UpdateExitButtonState();
        }

        public void ShowInfo(string message)
        {
            if (stateText == null)
            {
                return;
            }

            stateText.text = message;
            stateText.gameObject.SetActive(true);
        }

        public void HideInfo()
        {
            if (stateText == null)
            {
                return;
            }

            stateText.text = string.Empty;
            stateText.gameObject.SetActive(false);
        }

        private void OnFinishRoundClicked()
        {
            if (boardPresenter == null)
            {
                return;
            }

            boardPresenter.FinishRoundForCurrentPlayer();
        }

        public void ExitToStarter()
        {
            if (_isExiting)
            {
                return;
            }

            if (_awaitingSurrenderAcknowledge && boardPresenter != null && boardPresenter.IsGameOver)
            {
                StartCoroutine(ExitFlow(starterSceneName));
                return;
            }

            if (boardPresenter != null && boardPresenter.IsGameOver)
            {
                StartCoroutine(ExitFlow(starterSceneName));
                return;
            }

            if (networkController == null)
            {
                StartCoroutine(ExitFlow(starterSceneName));
                return;
            }

            ShowInfo(surrenderingInfo);
            _surrenderRequested = true;
            UpdateExitButtonState();
            networkController.RequestSurrender();
            StartCoroutine(LeaveAfterSurrenderRequest());
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

        private void OnSurrenderResolved(int surrenderingPlayerId, int winningPlayerId)
        {
            if (networkController == null)
            {
                return;
            }

            var localPlayerId = networkController.LocalPlayerId;
            if (localPlayerId == surrenderingPlayerId)
            {
                ShowInfo(surrenderingInfo);
                if (!_isExiting)
                {
                    var manager = global::Unity.Netcode.NetworkManager.Singleton;
                    if (manager != null && manager.IsServer)
                    {
                        StartCoroutine(LeaveAfterSurrenderRequest());
                    }
                    else
                    {
                        StartCoroutine(ExitFlow(starterSceneName));
                    }
                }
                return;
            }

            var localWon = winningPlayerId >= 0 && localPlayerId == winningPlayerId;
            ShowInfo(localWon ? opponentSurrenderedInfo : "Match ended.");
            _surrenderRequested = false;
            _awaitingSurrenderAcknowledge = true;
            _menuOpen = true;
            UpdateExitButtonState();
        }

        private System.Collections.IEnumerator LeaveAfterSurrenderRequest()
        {
            var delay = Mathf.Max(0f, surrenderExitDelaySeconds);
            var manager = global::Unity.Netcode.NetworkManager.Singleton;
            var endTime = Time.unscaledTime + Mathf.Max(delay, 0.5f);
            if (manager != null && manager.IsServer)
            {
                endTime = Time.unscaledTime + Mathf.Max(delay, 2.0f);
            }

            while (Time.unscaledTime < endTime)
            {
                if (manager != null && manager.IsServer && networkController != null && networkController.SurrenderAckReceived)
                {
                    break;
                }
                yield return null;
            }

            if (!_isExiting)
            {
                StartCoroutine(ExitFlow(starterSceneName));
            }
        }

        private System.Collections.IEnumerator ExitFlow(string targetScene)
        {
            _isExiting = true;
            _menuOpen = false;
            _awaitingSurrenderAcknowledge = false;
            UpdateExitButtonState();

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
            else
            {
                var manager = global::Unity.Netcode.NetworkManager.Singleton;
                if (manager != null)
                {
                    if (manager.IsListening || manager.IsClient || manager.IsServer)
                    {
                        manager.Shutdown();
                    }

                    while (manager.ShutdownInProgress)
                    {
                        yield return null;
                    }

                    yield return null;
                    Destroy(manager.gameObject);
                }
            }

            SceneManager.LoadScene(targetScene);
        }

        private void UpdateExitButtonState()
        {
            if (exitButtonText != null)
            {
                var label = (_awaitingSurrenderAcknowledge || _isExiting) ? acknowledgeButtonLabel : surrenderButtonLabel;
                exitButtonText.text = label;
            }

            if (exitButton != null)
            {
                var menuVisible = gameOverPanel == null || gameOverPanel.activeSelf;
                var shouldShow = menuVisible && (!_surrenderRequested || _awaitingSurrenderAcknowledge);
                exitButton.gameObject.SetActive(shouldShow);
                exitButton.enabled = shouldShow;
            }
        }

        private void BindNetworkEventsIfNeeded()
        {
            if (_networkEventsBound)
            {
                return;
            }

            if (networkController == null)
            {
                networkController = FindObjectOfType<NetworkGameController>();
            }

            if (networkController == null)
            {
                return;
            }

            networkController.SurrenderResolved += OnSurrenderResolved;
            _networkEventsBound = true;
        }

        private void UnbindNetworkEvents()
        {
            if (!_networkEventsBound || networkController == null)
            {
                return;
            }

            networkController.SurrenderResolved -= OnSurrenderResolved;
            _networkEventsBound = false;
        }
    }
}
