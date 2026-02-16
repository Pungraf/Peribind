using System;
using System.Collections.Generic;
using Peribind.Unity.Networking;
using TMPro;
using Unity.Services.Authentication;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Peribind.Unity.UI
{
    public class PlayerProfileMenu : MonoBehaviour
    {
        private const string DisplayNameCooldownPrefPrefix = "profile_display_name_last_change_";

        [Header("Services")]
        [SerializeField] private MatchRegistryClient matchRegistryClient;

        [Header("Player Profile")]
        [SerializeField] private TMP_Text accountInfoText;
        [SerializeField] private TMP_InputField displayNameInput;
        [SerializeField] private TMP_Text profileInfoText;
        [SerializeField] private Button saveDisplayNameButton;
        [SerializeField] private Button refreshButton;
        [SerializeField] private float displayNameChangeCooldownHours = 24f;

        [Header("Stats")]
        [SerializeField] private TMP_Text statsText;

        [Header("Leaderboard")]
        [SerializeField] private Transform leaderboardContent;
        [SerializeField] private TMP_Text leaderboardRowPrefab;
        [SerializeField] private int leaderboardLimit = 20;

        [Header("History")]
        [SerializeField] private Transform historyContent;
        [SerializeField] private TMP_Text historyRowPrefab;
        [SerializeField] private int historyLimit = 20;

        [Header("Navigation")]
        [SerializeField] private Button returnButton;
        [SerializeField] private string returnSceneName = "StarterScene";

        private bool _isBusy;
        private MatchRegistryClient.PlayerProfileResponse _cachedProfile;

        private void Awake()
        {
            if (saveDisplayNameButton != null)
            {
                saveDisplayNameButton.onClick.AddListener(OnSaveDisplayNameClicked);
            }

            if (refreshButton != null)
            {
                refreshButton.onClick.AddListener(OnRefreshClicked);
            }

            if (returnButton != null)
            {
                returnButton.onClick.AddListener(OnReturnClicked);
            }

            SetProfileInfo(string.Empty);
        }

        private void OnEnable()
        {
            _ = RefreshAllAsync();
        }

        private void OnDestroy()
        {
            if (saveDisplayNameButton != null)
            {
                saveDisplayNameButton.onClick.RemoveListener(OnSaveDisplayNameClicked);
            }

            if (refreshButton != null)
            {
                refreshButton.onClick.RemoveListener(OnRefreshClicked);
            }

            if (returnButton != null)
            {
                returnButton.onClick.RemoveListener(OnReturnClicked);
            }
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

            var nextDisplayName = displayNameInput != null ? displayNameInput.text?.Trim() : string.Empty;
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
            SetProfileInfo("Updating display name...");
            try
            {
                var updated = await matchRegistryClient.UpsertPlayerAsync(playerId, username, nextDisplayName);
                if (updated == null)
                {
                    SetProfileInfo(string.IsNullOrWhiteSpace(matchRegistryClient.LastErrorMessage)
                        ? "Failed to update display name."
                        : matchRegistryClient.LastErrorMessage);
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

            if (accountInfoText != null)
            {
                accountInfoText.text = $"Account: {usernameOrEmail}\nPlayer ID: {playerId}";
            }

            if (displayNameInput != null)
            {
                var displayName = profile?.player?.displayName;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = usernameOrEmail;
                }

                displayNameInput.text = displayName;
            }

            var stats = profile?.stats;
            if (statsText != null)
            {
                if (stats == null)
                {
                    statsText.text = "No stats yet.";
                }
                else
                {
                    statsText.text =
                        $"Games: {stats.gamesPlayed}  W: {stats.wins}  L: {stats.losses}  D: {stats.draws}\n" +
                        $"Surrenders: {stats.surrenders}  Rank Points: {stats.rankPoints}  Score Total: {stats.scoreTotal}";
                }
            }
        }

        private void RenderHistory(MatchRegistryClient.PlayerMatchHistoryResponse history)
        {
            ClearList(historyContent);
            if (historyContent == null || historyRowPrefab == null)
            {
                return;
            }

            var entries = history?.results ?? new List<MatchRegistryClient.PlayerMatchHistoryEntry>();
            if (entries.Count == 0)
            {
                var row = Instantiate(historyRowPrefab, historyContent);
                row.text = "No match history yet.";
                return;
            }

            foreach (var entry in entries)
            {
                var row = Instantiate(historyRowPrefab, historyContent);
                var opponents = BuildOpponentsText(entry?.opponents);
                var when = string.IsNullOrWhiteSpace(entry?.completedAt) ? "-" : entry.completedAt;
                var result = string.IsNullOrWhiteSpace(entry?.result) ? "draw" : entry.result.ToUpperInvariant();
                var score = entry != null ? entry.score : 0;
                row.text = $"{when} | {result} | score {score} | vs {opponents}";
            }
        }

        private void RenderLeaderboard(MatchRegistryClient.LeaderboardResponse leaderboard, string localPlayerId)
        {
            ClearList(leaderboardContent);
            if (leaderboardContent == null || leaderboardRowPrefab == null)
            {
                return;
            }

            var entries = leaderboard?.leaderboard ?? new List<MatchRegistryClient.LeaderboardEntry>();
            if (entries.Count == 0)
            {
                var row = Instantiate(leaderboardRowPrefab, leaderboardContent);
                row.text = "Leaderboard is empty.";
                return;
            }

            foreach (var entry in entries)
            {
                var row = Instantiate(leaderboardRowPrefab, leaderboardContent);
                var stats = entry?.stats;
                var displayName = !string.IsNullOrWhiteSpace(entry?.displayName) ? entry.displayName : entry?.username;
                if (string.IsNullOrWhiteSpace(displayName))
                {
                    displayName = entry?.playerId ?? "-";
                }

                var marker = string.Equals(entry?.playerId, localPlayerId, StringComparison.Ordinal) ? " <- you" : string.Empty;
                row.text =
                    $"#{entry?.rank ?? 0} {displayName} | RP {stats?.rankPoints ?? 0} | " +
                    $"W {stats?.wins ?? 0} L {stats?.losses ?? 0} D {stats?.draws ?? 0}{marker}";
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

        private static void ClearList(Transform content)
        {
            if (content == null)
            {
                return;
            }

            for (var i = content.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
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
            if (remaining.TotalHours >= 1d)
            {
                remainingText = $"{Mathf.CeilToInt((float)remaining.TotalHours)}h";
            }
            else
            {
                remainingText = $"{Mathf.CeilToInt((float)remaining.TotalMinutes)}m";
            }

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
            if (saveDisplayNameButton != null)
            {
                saveDisplayNameButton.interactable = !isBusy;
            }

            if (refreshButton != null)
            {
                refreshButton.interactable = !isBusy;
            }
        }

        private void SetProfileInfo(string message)
        {
            if (profileInfoText != null)
            {
                profileInfoText.text = message ?? string.Empty;
            }
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
