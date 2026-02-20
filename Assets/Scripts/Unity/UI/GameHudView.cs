using System;
using System.Collections.Generic;
using Peribind.Unity.Board;
using Peribind.Unity.Networking;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Unity.Services.Authentication;
using UIDocument = UnityEngine.UIElements.UIDocument;
using UiButton = UnityEngine.UIElements.Button;
using UiLabel = UnityEngine.UIElements.Label;
using UiVisualElement = UnityEngine.UIElements.VisualElement;
using UiDisplayStyle = UnityEngine.UIElements.DisplayStyle;
using VisualTreeAsset = UnityEngine.UIElements.VisualTreeAsset;
using StyleSheet = UnityEngine.UIElements.StyleSheet;

namespace Peribind.Unity.UI
{
    public class GameHudView : MonoBehaviour
    {
        [SerializeField] private BoardPresenter boardPresenter;
        [SerializeField] private string starterSceneName = "StarterScene";
        private const float ExitTimeoutSeconds = 8f;
        [SerializeField] private MultiplayerSessionController sessionController;
        [SerializeField] private NetworkGameController networkController;
        [SerializeField] private string surrenderingInfo = "You surrendered. Leaving match...";
        [SerializeField] private string surrenderConfirmInfo = "Surrender and leave this match?";
        [SerializeField] private string surrenderButtonLabel = "Surrender";
        [SerializeField] private string acknowledgeButtonLabel = "OK";
        [SerializeField] private float surrenderExitDelaySeconds = 0.35f;

        [Header("UI Toolkit")]
        [SerializeField] private bool enableUiToolkit = true;
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private bool autoAssignUiDocument = true;
        [SerializeField] private bool autoAssignVisualTreeFromResources = true;
        [SerializeField] private bool autoAssignStylesFromResources = true;

        private const string GameHudUxmlResourcePath = "UI/Toolkit/Game/GameHud";
        private const string CommonStyleResourcePath = "UI/Toolkit/Common/PeribindTheme";
        private const string GameHudStyleResourcePath = "UI/Toolkit/Game/GameHud";
        private const string PlayerOneScoreLabelName = "player-one-score-label";
        private const string PlayerTwoScoreLabelName = "player-two-score-label";
        private const string RoundLabelName = "round-label";
        private const string TurnLabelName = "turn-label";
        private const string LocalPlayerNameLabelName = "local-player-name-label";
        private const string OpponentPlayerNameLabelName = "opponent-player-name-label";
        private const string StateLabelName = "state-label";
        private const string FinishRoundButtonName = "finish-round-button";
        private const string ExitButtonName = "exit-button";
        private const string GameOverPanelName = "game-over-panel";
        private const string MenuBackdropName = "menu-backdrop";
        private const string ModalOkStateClassName = "hud-modal-action-button-ok-state";
        private const string UnknownPlayerName = "-";

        private const string LastMatchIdPrefsKey = "last_match_id";

        private bool _menuOpen;
        private bool _isExiting;
        private bool _awaitingSurrenderAcknowledge;
        private bool _surrenderRequested;
        private bool _gameOverHandled;
        private bool _networkEventsBound;
        private bool _uiToolkitCallbacksRegistered;
        private UiButton[] _uiMenuButtons;

        private UiVisualElement _uiRoot;
        private UiLabel _uiPlayerOneScoreLabel;
        private UiLabel _uiPlayerTwoScoreLabel;
        private UiLabel _uiRoundLabel;
        private UiLabel _uiTurnLabel;
        private UiLabel _uiLocalPlayerNameLabel;
        private UiLabel _uiOpponentPlayerNameLabel;
        private UiLabel _uiStateLabel;
        private UiButton _uiFinishRoundButton;
        private UiButton _uiExitButton;
        private UiVisualElement _uiGameOverPanel;
        private UiVisualElement _uiMenuBackdrop;
        private MatchRegistryClient _matchRegistryClient;
        private readonly Dictionary<string, string> _displayNamesByAuthId = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _requestedAuthIds = new HashSet<string>(StringComparer.Ordinal);
        private string _localAuthId = string.Empty;
        private string _opponentAuthId = string.Empty;

        private void Awake()
        {
            TryBindUiToolkit();

            if (sessionController == null)
            {
                sessionController = FindObjectOfType<MultiplayerSessionController>();
            }

            if (networkController == null)
            {
                networkController = FindObjectOfType<NetworkGameController>();
            }

            BindNetworkEventsIfNeeded();
            HideInfo();
            UpdateExitButtonState();
            TryRefreshPlayerNames();
        }

        private void OnEnable()
        {
            if (enableUiToolkit && _uiRoot == null)
            {
                TryBindUiToolkit();
            }

            if (networkController == null)
            {
                networkController = FindObjectOfType<NetworkGameController>();
            }

            BindNetworkEventsIfNeeded();
            TryRefreshPlayerNames();
        }

        private void OnDisable()
        {
            UnbindNetworkEvents();
        }

        private void OnDestroy()
        {
            UnregisterUiToolkitCallbacks();
            UnbindNetworkEvents();
        }

        private void Update()
        {
            if (boardPresenter == null)
            {
                return;
            }

            BindNetworkEventsIfNeeded();
            TryRefreshPlayerNames();
            UpdatePlayerNameLabels();

            if (!_awaitingSurrenderAcknowledge && !_surrenderRequested && !_isExiting &&
                networkController != null && networkController.WasSurrendered && boardPresenter.IsGameOver)
            {
                ShowInfo(BuildSurrenderInfoMessage(networkController.SurrenderingPlayerId, networkController.WinningPlayerId));
                _awaitingSurrenderAcknowledge = true;
                _menuOpen = true;
            }

            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && !boardPresenter.IsGameOver)
            {
                _menuOpen = !_menuOpen;
                if (_menuOpen && !_awaitingSurrenderAcknowledge && !_surrenderRequested)
                {
                    ShowInfo(surrenderConfirmInfo);
                }
                else if (!_menuOpen && !_awaitingSurrenderAcknowledge)
                {
                    HideInfo();
                }
            }

            if (_uiPlayerOneScoreLabel != null)
            {
                var playerOneName = GetDisplayNameForPlayerSlot(0);
                _uiPlayerOneScoreLabel.text = $"{playerOneName}: {boardPresenter.GetTotalScore(0)}";
            }

            if (_uiPlayerTwoScoreLabel != null)
            {
                var playerTwoName = GetDisplayNameForPlayerSlot(1);
                _uiPlayerTwoScoreLabel.text = $"{playerTwoName}: {boardPresenter.GetTotalScore(1)}";
            }

            if (_uiRoundLabel != null)
            {
                _uiRoundLabel.text = $"Round {boardPresenter.CurrentRound}/2";
            }

            if (_uiTurnLabel != null)
            {
                if (boardPresenter.IsGameOver)
                {
                    _uiTurnLabel.text = "Game Over";
                }
                else
                {
                    var currentPlayerName = GetDisplayNameForPlayerSlot(boardPresenter.CurrentPlayerId);
                    var finished = boardPresenter.HasFinishedRound(boardPresenter.CurrentPlayerId) ? " (Finished)" : string.Empty;
                    _uiTurnLabel.text = $"Turn: {currentPlayerName}{finished}";
                }
            }

            var shouldShowMenu = boardPresenter.IsGameOver || _menuOpen;
            if (_uiGameOverPanel != null)
            {
                _uiGameOverPanel.style.display = shouldShowMenu ? UiDisplayStyle.Flex : UiDisplayStyle.None;
            }
            if (_uiMenuBackdrop != null)
            {
                _uiMenuBackdrop.style.display = shouldShowMenu ? UiDisplayStyle.Flex : UiDisplayStyle.None;
            }

            UpdateMenuButtons(shouldShowMenu);

            if (!boardPresenter.IsGameOver)
            {
                _gameOverHandled = false;
            }
            else if (!_gameOverHandled && networkController != null && !networkController.WasSurrendered)
            {
                var winner = networkController.WinningPlayerId;
                ShowInfo(BuildGameOverInfoMessage(winner));
                _menuOpen = true;
                _surrenderRequested = false;
                _awaitingSurrenderAcknowledge = true;
                _gameOverHandled = true;
            }

            UpdateExitButtonState();
        }

        public void ShowInfo(string message)
        {
            if (_uiStateLabel != null)
            {
                _uiStateLabel.text = message ?? string.Empty;
                _uiStateLabel.style.display = UiDisplayStyle.Flex;
            }
        }

        public void HideInfo()
        {
            if (_uiStateLabel != null)
            {
                _uiStateLabel.text = string.Empty;
                _uiStateLabel.style.display = UiDisplayStyle.None;
            }
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
            if (_uiMenuButtons == null || _uiMenuButtons.Length == 0)
            {
                return;
            }

            foreach (var button in _uiMenuButtons)
            {
                if (button != null)
                {
                    button.SetEnabled(menuVisible);
                }
            }
        }

        public void OpenMenu()
        {
            _menuOpen = true;
            if (!_awaitingSurrenderAcknowledge && !_surrenderRequested)
            {
                ShowInfo(surrenderConfirmInfo);
            }
        }

        public void CloseMenu()
        {
            _menuOpen = false;
            if (!_awaitingSurrenderAcknowledge)
            {
                HideInfo();
            }
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

            ShowInfo(BuildSurrenderInfoMessage(surrenderingPlayerId, winningPlayerId));
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

            if (boardPresenter != null && boardPresenter.IsGameOver)
            {
                PlayerPrefs.DeleteKey(GetScopedMatchIdPrefsKey());
                PlayerPrefs.DeleteKey(LastMatchIdPrefsKey);
                PlayerPrefs.Save();
            }

            if (_uiGameOverPanel != null)
            {
                _uiGameOverPanel.style.display = UiDisplayStyle.None;
            }
            if (_uiMenuBackdrop != null)
            {
                _uiMenuBackdrop.style.display = UiDisplayStyle.None;
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
            if (_uiExitButton == null)
            {
                return;
            }

            var label = (_awaitingSurrenderAcknowledge || _isExiting) ? acknowledgeButtonLabel : surrenderButtonLabel;
            var menuVisible = _uiGameOverPanel == null || _uiGameOverPanel.style.display != UiDisplayStyle.None;
            var shouldShow = menuVisible && (!_surrenderRequested || _awaitingSurrenderAcknowledge);
            var isOkState = _awaitingSurrenderAcknowledge || _isExiting;
            _uiExitButton.text = label;
            _uiExitButton.EnableInClassList(ModalOkStateClassName, isOkState);
            _uiExitButton.style.display = shouldShow ? UiDisplayStyle.Flex : UiDisplayStyle.None;
            _uiExitButton.SetEnabled(shouldShow);
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
                var tree = Resources.Load<VisualTreeAsset>(GameHudUxmlResourcePath);
                if (tree == null)
                {
                    Debug.LogWarning($"[GameHudUITK] Missing UXML at Resources/{GameHudUxmlResourcePath}.uxml");
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
                TryAddStyle(_uiRoot, GameHudStyleResourcePath);
            }

            _uiPlayerOneScoreLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, PlayerOneScoreLabelName);
            _uiPlayerTwoScoreLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, PlayerTwoScoreLabelName);
            _uiRoundLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, RoundLabelName);
            _uiTurnLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, TurnLabelName);
            _uiLocalPlayerNameLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, LocalPlayerNameLabelName);
            _uiOpponentPlayerNameLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, OpponentPlayerNameLabelName);
            _uiStateLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, StateLabelName);
            _uiFinishRoundButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, FinishRoundButtonName);
            _uiExitButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, ExitButtonName);
            _uiGameOverPanel = UnityEngine.UIElements.UQueryExtensions.Q<UiVisualElement>(_uiRoot, GameOverPanelName);
            _uiMenuBackdrop = UnityEngine.UIElements.UQueryExtensions.Q<UiVisualElement>(_uiRoot, MenuBackdropName);

            _uiMenuButtons = new[] { _uiExitButton };

            RegisterUiToolkitCallbacks();
            UpdateExitButtonState();
            TryRefreshPlayerNames();
            UpdatePlayerNameLabels();
        }

        private void RegisterUiToolkitCallbacks()
        {
            if (_uiToolkitCallbacksRegistered)
            {
                return;
            }

            if (_uiFinishRoundButton != null)
            {
                _uiFinishRoundButton.clicked += OnFinishRoundClicked;
            }

            if (_uiExitButton != null)
            {
                _uiExitButton.clicked += ExitToStarter;
            }

            _uiToolkitCallbacksRegistered = true;
        }

        private void UnregisterUiToolkitCallbacks()
        {
            if (!_uiToolkitCallbacksRegistered)
            {
                return;
            }

            if (_uiFinishRoundButton != null)
            {
                _uiFinishRoundButton.clicked -= OnFinishRoundClicked;
            }

            if (_uiExitButton != null)
            {
                _uiExitButton.clicked -= ExitToStarter;
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

        private void TryRefreshPlayerNames()
        {
            var localAuthId = TryGetAuthenticatedPlayerId();
            if (!string.IsNullOrWhiteSpace(localAuthId))
            {
                _localAuthId = localAuthId;
            }

            _opponentAuthId = ResolveOpponentAuthId(_localAuthId);

            if (_matchRegistryClient == null)
            {
                _matchRegistryClient = FindObjectOfType<MatchRegistryClient>();
            }

            if (_matchRegistryClient == null)
            {
                return;
            }

            TryQueueDisplayNameLookup(_localAuthId);
            TryQueueDisplayNameLookup(_opponentAuthId);
        }

        private void TryQueueDisplayNameLookup(string authId)
        {
            if (string.IsNullOrWhiteSpace(authId))
            {
                return;
            }

            if (_displayNamesByAuthId.ContainsKey(authId))
            {
                return;
            }

            if (!_requestedAuthIds.Add(authId))
            {
                return;
            }

            _ = RefreshDisplayNameForAuthIdAsync(authId);
        }

        private async System.Threading.Tasks.Task RefreshDisplayNameForAuthIdAsync(string authId)
        {
            var fallbackName = BuildFallbackName(authId);

            try
            {
                var response = await _matchRegistryClient.GetPlayerProfileAsync(authId);
                var displayName = ResolveDisplayName(response, fallbackName);
                _displayNamesByAuthId[authId] = displayName;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[GameHudView] Failed to load player profile for '{authId}': {exception.Message}");
                _displayNamesByAuthId[authId] = fallbackName;
            }
            finally
            {
                _requestedAuthIds.Remove(authId);
                UpdatePlayerNameLabels();
            }
        }

        private void UpdatePlayerNameLabels()
        {
            if (_uiLocalPlayerNameLabel != null)
            {
                _uiLocalPlayerNameLabel.text = GetLocalDisplayName();
            }

            if (_uiOpponentPlayerNameLabel != null)
            {
                _uiOpponentPlayerNameLabel.text = GetOpponentDisplayName();
            }
        }

        private string GetDisplayNameForPlayerSlot(int playerSlot, bool allowSlotFallback = true)
        {
            if (networkController != null)
            {
                var localPlayerSlot = networkController.LocalPlayerId;
                if (localPlayerSlot >= 0)
                {
                    if (playerSlot == localPlayerSlot)
                    {
                        return GetLocalDisplayName();
                    }

                    if (playerSlot == 1 - localPlayerSlot)
                    {
                        return GetOpponentDisplayName();
                    }
                }
            }

            return allowSlotFallback ? $"P{playerSlot + 1}" : UnknownPlayerName;
        }

        private string BuildGameOverInfoMessage(int winnerPlayerId)
        {
            if (winnerPlayerId < 0)
            {
                return "Game over. Draw.";
            }

            var winnerName = GetDisplayNameForPlayerSlot(winnerPlayerId, false);
            return $"Game over. {winnerName} won.";
        }

        private string BuildSurrenderInfoMessage(int surrenderingPlayerId, int winningPlayerId)
        {
            var surrenderingName = surrenderingPlayerId >= 0
                ? GetDisplayNameForPlayerSlot(surrenderingPlayerId, false)
                : UnknownPlayerName;

            if (winningPlayerId >= 0)
            {
                var winnerName = GetDisplayNameForPlayerSlot(winningPlayerId, false);
                return $"{surrenderingName} surrendered. {winnerName} wins.";
            }

            return $"{surrenderingName} surrendered.";
        }

        private string GetLocalDisplayName()
        {
            return ResolveFriendlyName(_localAuthId);
        }

        private string GetOpponentDisplayName()
        {
            return ResolveFriendlyName(_opponentAuthId);
        }

        private string ResolveFriendlyName(string authId)
        {
            if (string.IsNullOrWhiteSpace(authId))
            {
                return UnknownPlayerName;
            }

            if (_displayNamesByAuthId.TryGetValue(authId, out var resolved) && !string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }

            return BuildFallbackName(authId);
        }

        private string ResolveOpponentAuthId(string localAuthId)
        {
            if (networkController != null)
            {
                if (networkController.TryGetAuthIdForPlayerId(0, out var slotZeroAuthId) &&
                    !string.IsNullOrWhiteSpace(slotZeroAuthId) &&
                    !string.Equals(slotZeroAuthId, localAuthId, StringComparison.Ordinal))
                {
                    return slotZeroAuthId;
                }

                if (networkController.TryGetAuthIdForPlayerId(1, out var slotOneAuthId) &&
                    !string.IsNullOrWhiteSpace(slotOneAuthId) &&
                    !string.Equals(slotOneAuthId, localAuthId, StringComparison.Ordinal))
                {
                    return slotOneAuthId;
                }
            }

            if (sessionController != null && sessionController.CurrentSession != null && sessionController.CurrentSession.Players != null)
            {
                var players = sessionController.CurrentSession.Players;
                foreach (var player in players)
                {
                    string playerId;
                    try
                    {
                        playerId = player.Id;
                    }
                    catch
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(playerId))
                    {
                        continue;
                    }

                    if (!string.Equals(playerId, localAuthId, StringComparison.Ordinal))
                    {
                        return playerId;
                    }
                }
            }

            var assignments = NetworkGameController.GetAuthPlayerAssignmentsSnapshot();
            if (assignments != null && assignments.Count > 0)
            {
                if (networkController != null)
                {
                    var localPlayerSlot = networkController.LocalPlayerId;
                    if (localPlayerSlot >= 0)
                    {
                        foreach (var pair in assignments)
                        {
                            if (pair.Value != localPlayerSlot)
                            {
                                return pair.Key;
                            }
                        }
                    }
                }

                foreach (var pair in assignments)
                {
                    if (!string.Equals(pair.Key, localAuthId, StringComparison.Ordinal))
                    {
                        return pair.Key;
                    }
                }
            }

            return string.Empty;
        }

        private static string ResolveDisplayName(MatchRegistryClient.PlayerProfileResponse response, string fallbackName)
        {
            if (response == null || response.player == null)
            {
                return fallbackName;
            }

            var displayName = response.player.displayName;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = response.player.username;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = response.player.ugsPlayerId;
            }

            return string.IsNullOrWhiteSpace(displayName) ? fallbackName : displayName;
        }

        private static string BuildFallbackName(string authId)
        {
            if (string.IsNullOrWhiteSpace(authId))
            {
                return UnknownPlayerName;
            }

            return authId.Length <= 12 ? authId : authId.Substring(0, 12);
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

        private string GetScopedMatchIdPrefsKey()
        {
            var playerId = TryGetAuthenticatedPlayerId();
            if (string.IsNullOrWhiteSpace(playerId))
            {
                return LastMatchIdPrefsKey;
            }

            var suffix = playerId.Length <= 16 ? playerId : playerId.Substring(0, 16);
            return $"{LastMatchIdPrefsKey}_{suffix}";
        }

        private static string TryGetAuthenticatedPlayerId()
        {
            try
            {
                if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
                {
                    return AuthenticationService.Instance.PlayerId ?? string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }

            return string.Empty;
        }
    }
}

