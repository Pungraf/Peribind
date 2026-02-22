using System;
using System.Collections.Generic;
using Peribind.Unity.Networking;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.SceneManagement;
using UIDocument = UnityEngine.UIElements.UIDocument;
using UiButton = UnityEngine.UIElements.Button;
using UiLabel = UnityEngine.UIElements.Label;
using UiTextField = UnityEngine.UIElements.TextField;
using UiScrollView = UnityEngine.UIElements.ScrollView;
using UiVisualElement = UnityEngine.UIElements.VisualElement;
using UiDisplayStyle = UnityEngine.UIElements.DisplayStyle;
using VisualTreeAsset = UnityEngine.UIElements.VisualTreeAsset;
using StyleSheet = UnityEngine.UIElements.StyleSheet;

namespace Peribind.Unity.UI
{
    public class PlayerProfileMenu : MonoBehaviour
    {
        private const string DisplayNameCooldownPrefPrefix = "profile_display_name_last_change_";

        [Header("Services")]
        [SerializeField] private MatchRegistryClient matchRegistryClient;

        [Header("Settings")]
        [SerializeField] private float displayNameChangeCooldownHours = 24f;
        [SerializeField] private int leaderboardLimit = 20;
        [SerializeField] private int historyLimit = 20;
        [SerializeField] private string returnSceneName = "StarterScene";

        [Header("UI Toolkit")]
        [SerializeField] private bool enableUiToolkit = true;
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private bool autoAssignUiDocument = true;
        [SerializeField] private bool autoAssignVisualTreeFromResources = true;
        [SerializeField] private bool autoAssignStylesFromResources = true;

        private const string ProfileUxmlResourcePath = "UI/Toolkit/Profile/ProfileMenu";
        private const string CommonStyleResourcePath = "UI/Toolkit/Common/PeribindTheme";
        private const string ProfileStyleResourcePath = "UI/Toolkit/Profile/ProfileMenu";
        private const string AccountInfoLabelName = "account-info-label";
        private const string DisplayNameInputName = "display-name-input";
        private const string ProfileInfoLabelName = "profile-info-label";
        private const string SaveDisplayNameButtonName = "save-display-name-button";
        private const string RefreshButtonName = "refresh-button";
        private const string StatGamesValueName = "stat-games-value";
        private const string StatWinsValueName = "stat-wins-value";
        private const string StatLossesValueName = "stat-losses-value";
        private const string StatDrawsValueName = "stat-draws-value";
        private const string StatWinRateValueName = "stat-winrate-value";
        private const string StatRankPointsValueName = "stat-rankpoints-value";
        private const string StatScoreTotalValueName = "stat-scoretotal-value";
        private const string StatSurrendersValueName = "stat-surrenders-value";
        private const string LeaderboardScrollName = "leaderboard-scroll";
        private const string HistoryScrollName = "history-scroll";
        private const string RecordsTabLeaderboardButtonName = "records-tab-leaderboard-button";
        private const string RecordsTabHistoryButtonName = "records-tab-history-button";
        private const string ActiveTabClassName = "records-tab-button-active";
        private const string ReturnButtonName = "return-button";

        private bool _isBusy;
        private MatchRegistryClient.PlayerProfileResponse _cachedProfile;
        private bool _uiToolkitCallbacksRegistered;

        private UiVisualElement _uiRoot;
        private UiLabel _uiAccountInfoLabel;
        private UiTextField _uiDisplayNameInput;
        private UiLabel _uiProfileInfoLabel;
        private UiButton _uiSaveDisplayNameButton;
        private UiButton _uiRefreshButton;
        private UiLabel _uiStatGamesValueLabel;
        private UiLabel _uiStatWinsValueLabel;
        private UiLabel _uiStatLossesValueLabel;
        private UiLabel _uiStatDrawsValueLabel;
        private UiLabel _uiStatWinRateValueLabel;
        private UiLabel _uiStatRankPointsValueLabel;
        private UiLabel _uiStatScoreTotalValueLabel;
        private UiLabel _uiStatSurrendersValueLabel;
        private UiScrollView _uiLeaderboardScroll;
        private UiScrollView _uiHistoryScroll;
        private UiButton _uiRecordsTabLeaderboardButton;
        private UiButton _uiRecordsTabHistoryButton;
        private UiButton _uiReturnButton;
        private bool _showLeaderboardTab = true;

        private void Awake()
        {
            TryBindUiToolkit();
            SetProfileInfo(string.Empty);
        }

        private void OnEnable()
        {
            if (enableUiToolkit && _uiRoot == null)
            {
                TryBindUiToolkit();
            }

            _ = RefreshAllAsync();
        }

        private void OnDestroy()
        {
            UnregisterUiToolkitCallbacks();
        }

        private async void OnRefreshClicked()
        {
            await RefreshAllAsync();
        }

        private async void OnSaveDisplayNameClicked()
        {
            if (_isBusy)
            {
                return;
            }

            if (!TryGetPlayerId(out var playerId))
            {
                SetProfileInfo("Not signed in.");
                return;
            }

            if (matchRegistryClient == null)
            {
                matchRegistryClient = FindObjectOfType<MatchRegistryClient>(true);
            }

            if (matchRegistryClient == null)
            {
                SetProfileInfo("Match registry is unavailable.");
                return;
            }

            if (_cachedProfile == null || _cachedProfile.player == null)
            {
                SetProfileInfo("Profile data is not loaded yet.");
                return;
            }

            var nextDisplayName = GetDisplayNameInputValue();
            if (string.IsNullOrWhiteSpace(nextDisplayName))
            {
                SetProfileInfo("Display name cannot be empty.");
                return;
            }

            if (nextDisplayName.Length < 3 || nextDisplayName.Length > 24)
            {
                SetProfileInfo("Display name must be 3-24 characters.");
                return;
            }

            if (!IsDisplayNameChangeAllowed(playerId, out var cooldownRemaining))
            {
                SetProfileInfo($"You can change display name again in {cooldownRemaining}.");
                return;
            }

            var username = _cachedProfile.player.username;
            if (string.IsNullOrWhiteSpace(username))
            {
                username = _cachedProfile.player.ugsPlayerId;
            }

            SetBusy(true);
            try
            {
                SetProfileInfo("Checking display name...");
                var isAvailable = await matchRegistryClient.IsDisplayNameAvailableAsync(nextDisplayName, playerId);
                if (!isAvailable)
                {
                    SetProfileInfo(ResolveDisplayNameErrorMessage(matchRegistryClient.LastErrorMessage));
                    return;
                }

                SetProfileInfo("Updating display name...");
                var updated = await matchRegistryClient.UpsertPlayerAsync(playerId, username, nextDisplayName);
                if (updated == null)
                {
                    SetProfileInfo(ResolveDisplayNameErrorMessage(matchRegistryClient.LastErrorMessage));
                    return;
                }

                WriteDisplayNameChangeTimestamp(playerId);
                SetProfileInfo("Display name updated.");
                await RefreshAllAsync();
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void OnReturnClicked()
        {
            if (string.IsNullOrWhiteSpace(returnSceneName))
            {
                return;
            }

            SceneManager.LoadScene(returnSceneName);
        }

        private void OnRecordsTabLeaderboardClicked()
        {
            SetRecordsTab(true);
        }

        private void OnRecordsTabHistoryClicked()
        {
            SetRecordsTab(false);
        }

        private async System.Threading.Tasks.Task RefreshAllAsync()
        {
            if (_isBusy)
            {
                return;
            }

            if (!TryGetPlayerId(out var playerId))
            {
                SetProfileInfo("Not signed in.");
                return;
            }

            if (matchRegistryClient == null)
            {
                matchRegistryClient = FindObjectOfType<MatchRegistryClient>(true);
            }

            if (matchRegistryClient == null)
            {
                SetProfileInfo("Match registry is unavailable.");
                return;
            }

            SetBusy(true);
            SetProfileInfo("Loading profile...");
            try
            {
                var profileTask = matchRegistryClient.GetPlayerProfileAsync(playerId);
                var historyTask = matchRegistryClient.GetPlayerMatchHistoryAsync(playerId, Mathf.Max(1, historyLimit));
                var leaderboardTask = matchRegistryClient.GetLeaderboardAsync(Mathf.Max(1, leaderboardLimit));

                await System.Threading.Tasks.Task.WhenAll(profileTask, historyTask, leaderboardTask);

                _cachedProfile = profileTask.Result;
                RenderProfile(_cachedProfile, playerId);
                RenderHistory(historyTask.Result);
                RenderLeaderboard(leaderboardTask.Result, playerId);
                SetProfileInfo(string.Empty);
            }
            catch (Exception ex)
            {
                SetProfileInfo($"Failed to load profile: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void RenderProfile(MatchRegistryClient.PlayerProfileResponse profile, string playerId)
        {
            var usernameOrEmail = profile?.player?.username;
            if (string.IsNullOrWhiteSpace(usernameOrEmail))
            {
                usernameOrEmail = playerId;
            }

            if (_uiAccountInfoLabel != null)
            {
                _uiAccountInfoLabel.text = $"Player ID: {playerId}";
            }

            var displayName = profile?.player?.displayName;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = usernameOrEmail;
            }

            if (_uiDisplayNameInput != null)
            {
                _uiDisplayNameInput.value = displayName;
            }

            var stats = profile?.stats;
            if (stats == null)
            {
                SetStatValues(0, 0, 0, 0, 0f, 0, 0, 0);
                return;
            }

            var gamesPlayed = Mathf.Max(0, stats.gamesPlayed);
            var winRate = gamesPlayed > 0 ? (stats.wins * 100f) / gamesPlayed : 0f;
            SetStatValues(
                gamesPlayed,
                stats.wins,
                stats.losses,
                stats.draws,
                winRate,
                stats.rankPoints,
                stats.scoreTotal,
                stats.surrenders);
        }

        private void RenderHistory(MatchRegistryClient.PlayerMatchHistoryResponse history)
        {
            _uiHistoryScroll?.Clear();

            var entries = history?.results ?? new List<MatchRegistryClient.PlayerMatchHistoryEntry>();
            if (entries.Count == 0)
            {
                AddEmptyRow(_uiHistoryScroll, "No match history yet.", "history-row-card");
                return;
            }

            foreach (var entry in entries)
            {
                var opponents = BuildOpponentsText(entry?.opponents);
                var when = string.IsNullOrWhiteSpace(entry?.completedAt) ? "-" : entry.completedAt;
                var result = string.IsNullOrWhiteSpace(entry?.result) ? "Draw" : entry.result;
                var score = entry != null ? entry.score : 0;
                AddHistoryRow(_uiHistoryScroll, result, score, when, opponents);
            }
        }

        private void RenderLeaderboard(MatchRegistryClient.LeaderboardResponse leaderboard, string localPlayerId)
        {
            _uiLeaderboardScroll?.Clear();

            var entries = leaderboard?.leaderboard ?? new List<MatchRegistryClient.LeaderboardEntry>();
            if (entries.Count == 0)
            {
                AddEmptyRow(_uiLeaderboardScroll, "Leaderboard is empty.", "leaderboard-row-card");
                return;
            }

            foreach (var entry in entries)
            {
                var stats = entry?.stats;
                var displayName = !string.IsNullOrWhiteSpace(entry?.displayName) ? entry.displayName : entry?.username;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = entry?.playerId ?? "-";
                }

                var isLocalPlayer = string.Equals(entry?.playerId, localPlayerId, StringComparison.Ordinal);
                AddLeaderboardRow(
                    _uiLeaderboardScroll,
                    entry?.rank ?? 0,
                    displayName,
                    stats?.rankPoints ?? 0,
                    stats?.wins ?? 0,
                    stats?.losses ?? 0,
                    stats?.draws ?? 0,
                    isLocalPlayer);
            }
        }

        private static string BuildOpponentsText(List<MatchRegistryClient.MatchHistoryOpponent> opponents)
        {
            if (opponents == null || opponents.Count == 0)
            {
                return "-";
            }

            var names = new List<string>(opponents.Count);
            foreach (var opponent in opponents)
            {
                if (opponent == null)
                {
                    continue;
                }

                var name = !string.IsNullOrWhiteSpace(opponent.displayName)
                    ? opponent.displayName
                    : (!string.IsNullOrWhiteSpace(opponent.username) ? opponent.username : opponent.playerId);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                names.Add(name);
            }

            return names.Count > 0 ? string.Join(", ", names) : "-";
        }

        private void SetStatValues(
            int games,
            int wins,
            int losses,
            int draws,
            float winRatePercent,
            int rankPoints,
            int scoreTotal,
            int surrenders)
        {
            SetStatText(_uiStatGamesValueLabel, games.ToString());
            SetStatText(_uiStatWinsValueLabel, wins.ToString());
            SetStatText(_uiStatLossesValueLabel, losses.ToString());
            SetStatText(_uiStatDrawsValueLabel, draws.ToString());
            SetStatText(_uiStatWinRateValueLabel, $"{winRatePercent:0.#}%");
            SetStatText(_uiStatRankPointsValueLabel, rankPoints.ToString());
            SetStatText(_uiStatScoreTotalValueLabel, scoreTotal.ToString());
            SetStatText(_uiStatSurrendersValueLabel, surrenders.ToString());
        }

        private static void SetStatText(UiLabel label, string value)
        {
            if (label != null)
            {
                label.text = value ?? "0";
            }
        }

        private static void AddLeaderboardRow(
            UiScrollView scrollView,
            int rank,
            string displayName,
            int rankPoints,
            int wins,
            int losses,
            int draws,
            bool isLocalPlayer)
        {
            if (scrollView == null)
            {
                return;
            }

            var row = new UiVisualElement();
            row.AddToClassList("leaderboard-row-card");

            var left = new UiVisualElement();
            left.AddToClassList("leaderboard-row-left");
            var rankLabel = new UiLabel { text = $"#{rank}" };
            rankLabel.AddToClassList("leaderboard-rank");
            left.Add(rankLabel);

            var nameText = displayName;
            var nameLabel = new UiLabel { text = nameText };
            nameLabel.AddToClassList("leaderboard-name");
            if (isLocalPlayer)
            {
                nameLabel.AddToClassList("leaderboard-name-local");
            }
            left.Add(nameLabel);

            var right = new UiVisualElement();
            right.AddToClassList("leaderboard-row-right");

            right.Add(CreateLeaderboardMetricToken("RP", rankPoints.ToString()));
            right.Add(CreateLeaderboardMetricToken("W", wins.ToString()));
            right.Add(CreateLeaderboardMetricToken("L", losses.ToString()));
            right.Add(CreateLeaderboardMetricToken("D", draws.ToString()));

            row.Add(left);
            row.Add(right);
            scrollView.Add(row);
        }

        private static UiVisualElement CreateLeaderboardMetricToken(string prefix, string value)
        {
            var token = new UiVisualElement();
            token.AddToClassList("leaderboard-metric-token");

            var prefixLabel = new UiLabel { text = prefix };
            prefixLabel.AddToClassList("leaderboard-metric-prefix");
            token.Add(prefixLabel);

            var valueLabel = new UiLabel { text = value };
            valueLabel.AddToClassList("leaderboard-metric-number");
            token.Add(valueLabel);

            return token;
        }

        private static void AddHistoryRow(
            UiScrollView scrollView,
            string rawResult,
            int score,
            string completedAtRaw,
            string opponentsText)
        {
            if (scrollView == null)
            {
                return;
            }

            var row = new UiVisualElement();
            row.AddToClassList("history-row-card");

            var top = new UiVisualElement();
            top.AddToClassList("history-row-top");

            var result = NormalizeResult(rawResult);
            var resultLabel = new UiLabel { text = result };
            resultLabel.AddToClassList("history-result");
            resultLabel.AddToClassList(result.ToLowerInvariant());
            top.Add(resultLabel);

            var scoreLabel = new UiLabel { text = $"Score {score}" };
            scoreLabel.AddToClassList("history-score");
            top.Add(scoreLabel);

            var meta = new UiLabel
            {
                text = $"{FormatCompletedAt(completedAtRaw)}  |  vs {opponentsText}"
            };
            meta.AddToClassList("history-meta");

            row.Add(top);
            row.Add(meta);
            scrollView.Add(row);
        }

        private static void AddEmptyRow(UiScrollView scrollView, string message, string rowClassName)
        {
            if (scrollView == null)
            {
                return;
            }

            var row = new UiVisualElement();
            if (!string.IsNullOrWhiteSpace(rowClassName))
            {
                row.AddToClassList(rowClassName);
            }

            var label = new UiLabel { text = message ?? string.Empty };
            label.AddToClassList("profile-empty-row");

            row.Add(label);
            scrollView.Add(row);
        }

        private static string NormalizeResult(string rawResult)
        {
            if (string.IsNullOrWhiteSpace(rawResult))
            {
                return "Draw";
            }

            var normalized = rawResult.Trim().ToLowerInvariant();
            if (normalized == "win" || normalized == "won" || normalized == "victory")
            {
                return "Win";
            }

            if (normalized == "loss" || normalized == "lose" || normalized == "defeat")
            {
                return "Loss";
            }

            return "Draw";
        }

        private static string FormatCompletedAt(string completedAtRaw)
        {
            if (string.IsNullOrWhiteSpace(completedAtRaw))
            {
                return "-";
            }

            if (DateTimeOffset.TryParse(completedAtRaw, out var parsed))
            {
                return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }

            return completedAtRaw;
        }

        private void SetRecordsTab(bool showLeaderboard)
        {
            _showLeaderboardTab = showLeaderboard;

            if (_uiLeaderboardScroll != null)
            {
                _uiLeaderboardScroll.style.display = showLeaderboard ? UiDisplayStyle.Flex : UiDisplayStyle.None;
            }

            if (_uiHistoryScroll != null)
            {
                _uiHistoryScroll.style.display = showLeaderboard ? UiDisplayStyle.None : UiDisplayStyle.Flex;
            }

            if (_uiRecordsTabLeaderboardButton != null)
            {
                _uiRecordsTabLeaderboardButton.EnableInClassList(ActiveTabClassName, showLeaderboard);
            }

            if (_uiRecordsTabHistoryButton != null)
            {
                _uiRecordsTabHistoryButton.EnableInClassList(ActiveTabClassName, !showLeaderboard);
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
                var tree = Resources.Load<VisualTreeAsset>(ProfileUxmlResourcePath);
                if (tree == null)
                {
                    Debug.LogWarning($"[ProfileUITK] Missing UXML at Resources/{ProfileUxmlResourcePath}.uxml");
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
                TryAddStyle(_uiRoot, ProfileStyleResourcePath);
            }

            _uiAccountInfoLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, AccountInfoLabelName);
            _uiDisplayNameInput = UnityEngine.UIElements.UQueryExtensions.Q<UiTextField>(_uiRoot, DisplayNameInputName);
            _uiProfileInfoLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, ProfileInfoLabelName);
            _uiSaveDisplayNameButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, SaveDisplayNameButtonName);
            _uiRefreshButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, RefreshButtonName);
            _uiStatGamesValueLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, StatGamesValueName);
            _uiStatWinsValueLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, StatWinsValueName);
            _uiStatLossesValueLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, StatLossesValueName);
            _uiStatDrawsValueLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, StatDrawsValueName);
            _uiStatWinRateValueLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, StatWinRateValueName);
            _uiStatRankPointsValueLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, StatRankPointsValueName);
            _uiStatScoreTotalValueLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, StatScoreTotalValueName);
            _uiStatSurrendersValueLabel = UnityEngine.UIElements.UQueryExtensions.Q<UiLabel>(_uiRoot, StatSurrendersValueName);
            _uiLeaderboardScroll = UnityEngine.UIElements.UQueryExtensions.Q<UiScrollView>(_uiRoot, LeaderboardScrollName);
            _uiHistoryScroll = UnityEngine.UIElements.UQueryExtensions.Q<UiScrollView>(_uiRoot, HistoryScrollName);
            _uiRecordsTabLeaderboardButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, RecordsTabLeaderboardButtonName);
            _uiRecordsTabHistoryButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, RecordsTabHistoryButtonName);
            _uiReturnButton = UnityEngine.UIElements.UQueryExtensions.Q<UiButton>(_uiRoot, ReturnButtonName);

            RegisterUiToolkitCallbacks();
            SetRecordsTab(_showLeaderboardTab);
        }

        private void RegisterUiToolkitCallbacks()
        {
            if (_uiToolkitCallbacksRegistered)
            {
                return;
            }

            if (_uiSaveDisplayNameButton != null)
            {
                _uiSaveDisplayNameButton.clicked += OnSaveDisplayNameClicked;
            }

            if (_uiRefreshButton != null)
            {
                _uiRefreshButton.clicked += OnRefreshClicked;
            }

            if (_uiReturnButton != null)
            {
                _uiReturnButton.clicked += OnReturnClicked;
            }

            if (_uiRecordsTabLeaderboardButton != null)
            {
                _uiRecordsTabLeaderboardButton.clicked += OnRecordsTabLeaderboardClicked;
            }

            if (_uiRecordsTabHistoryButton != null)
            {
                _uiRecordsTabHistoryButton.clicked += OnRecordsTabHistoryClicked;
            }

            _uiToolkitCallbacksRegistered = true;
        }

        private void UnregisterUiToolkitCallbacks()
        {
            if (!_uiToolkitCallbacksRegistered)
            {
                return;
            }

            if (_uiSaveDisplayNameButton != null)
            {
                _uiSaveDisplayNameButton.clicked -= OnSaveDisplayNameClicked;
            }

            if (_uiRefreshButton != null)
            {
                _uiRefreshButton.clicked -= OnRefreshClicked;
            }

            if (_uiReturnButton != null)
            {
                _uiReturnButton.clicked -= OnReturnClicked;
            }

            if (_uiRecordsTabLeaderboardButton != null)
            {
                _uiRecordsTabLeaderboardButton.clicked -= OnRecordsTabLeaderboardClicked;
            }

            if (_uiRecordsTabHistoryButton != null)
            {
                _uiRecordsTabHistoryButton.clicked -= OnRecordsTabHistoryClicked;
            }

            _uiToolkitCallbacksRegistered = false;
        }

        private string GetDisplayNameInputValue()
        {
            if (_uiDisplayNameInput != null)
            {
                return _uiDisplayNameInput.value != null ? _uiDisplayNameInput.value.Trim() : string.Empty;
            }

            return string.Empty;
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

        private bool IsDisplayNameChangeAllowed(string playerId, out string remainingText)
        {
            remainingText = string.Empty;
            if (displayNameChangeCooldownHours <= 0f || string.IsNullOrWhiteSpace(playerId))
            {
                return true;
            }

            var key = DisplayNameCooldownPrefPrefix + playerId;
            var raw = PlayerPrefs.GetString(key, string.Empty);
            if (!long.TryParse(raw, out var ticks))
            {
                return true;
            }

            var lastChangeUtc = new DateTime(ticks, DateTimeKind.Utc);
            var cooldown = TimeSpan.FromHours(displayNameChangeCooldownHours);
            var elapsed = DateTime.UtcNow - lastChangeUtc;
            if (elapsed >= cooldown)
            {
                return true;
            }

            var remaining = cooldown - elapsed;
            remainingText = remaining.TotalHours >= 1d
                ? $"{Mathf.CeilToInt((float)remaining.TotalHours)}h"
                : $"{Mathf.CeilToInt((float)remaining.TotalMinutes)}m";

            return false;
        }

        private static void WriteDisplayNameChangeTimestamp(string playerId)
        {
            if (string.IsNullOrWhiteSpace(playerId))
            {
                return;
            }

            var key = DisplayNameCooldownPrefPrefix + playerId;
            PlayerPrefs.SetString(key, DateTime.UtcNow.Ticks.ToString());
            PlayerPrefs.Save();
        }

        private void SetBusy(bool isBusy)
        {
            _isBusy = isBusy;

            if (_uiSaveDisplayNameButton != null)
            {
                _uiSaveDisplayNameButton.SetEnabled(!isBusy);
            }

            if (_uiRefreshButton != null)
            {
                _uiRefreshButton.SetEnabled(!isBusy);
            }
        }

        private void SetProfileInfo(string message)
        {
            if (_uiProfileInfoLabel != null)
            {
                _uiProfileInfoLabel.text = message ?? string.Empty;
            }
        }

        private static string ResolveDisplayNameErrorMessage(string rawError)
        {
            if (string.Equals(rawError, "display_name_taken", StringComparison.OrdinalIgnoreCase))
            {
                return "Display name is already taken. Pick another one.";
            }

            if (string.Equals(rawError, "missing_display_name", StringComparison.OrdinalIgnoreCase))
            {
                return "Display name cannot be empty.";
            }

            if (string.Equals(rawError, "display_name_check_failed", StringComparison.OrdinalIgnoreCase))
            {
                return "Could not verify display name uniqueness.";
            }

            if (string.Equals(rawError, "display_name_check_unavailable", StringComparison.OrdinalIgnoreCase))
            {
                return "Display name check is unavailable. Try again in a moment.";
            }

            return string.IsNullOrWhiteSpace(rawError) ? "Failed to update display name." : rawError;
        }

        private static bool TryGetPlayerId(out string playerId)
        {
            playerId = string.Empty;
            try
            {
                if (AuthenticationService.Instance == null || !AuthenticationService.Instance.IsSignedIn)
                {
                    return false;
                }

                playerId = AuthenticationService.Instance.PlayerId;
                return !string.IsNullOrWhiteSpace(playerId);
            }
            catch
            {
                return false;
            }
        }
    }
}

