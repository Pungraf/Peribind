using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Peribind.Unity.Networking;
using Unity.Services.Lobbies.Models;

namespace Peribind.Unity.UI
{
    public class LobbyUgsMenu : MonoBehaviour
    {
        [SerializeField] private LobbyServiceController lobbyService;
        [Header("Create")]
        [SerializeField] private TMP_InputField lobbyNameInput;
        [SerializeField] private TMP_InputField mapInput;
        [SerializeField] private TMP_InputField modeInput;
        [SerializeField] private TMP_InputField regionInput;
        [SerializeField] private Button createButton;

        [Header("Join")]
        [SerializeField] private TMP_InputField joinCodeInput;
        [SerializeField] private Button joinCodeButton;
        [SerializeField] private Button quickJoinButton;
        [SerializeField] private Button refreshButton;

        [Header("List")]
        [SerializeField] private Transform listContent;
        [SerializeField] private Button lobbyRowButtonPrefab;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button exitButton;

        private void Awake()
        {
            if (lobbyService == null)
            {
                lobbyService = FindObjectOfType<LobbyServiceController>();
            }

            if (createButton != null) createButton.onClick.AddListener(OnCreateClicked);
            if (joinCodeButton != null) joinCodeButton.onClick.AddListener(OnJoinCodeClicked);
            if (quickJoinButton != null) quickJoinButton.onClick.AddListener(OnQuickJoinClicked);
            if (refreshButton != null) refreshButton.onClick.AddListener(OnRefreshClicked);
            if (exitButton != null) exitButton.onClick.AddListener(OnExitClicked);

            if (lobbyService != null)
            {
                lobbyService.LobbiesQueried += UpdateLobbyList;
                lobbyService.LobbyUpdated += OnLobbyUpdated;
                lobbyService.LobbyError += OnLobbyError;
            }
        }

        private void OnDestroy()
        {
            if (createButton != null) createButton.onClick.RemoveListener(OnCreateClicked);
            if (joinCodeButton != null) joinCodeButton.onClick.RemoveListener(OnJoinCodeClicked);
            if (quickJoinButton != null) quickJoinButton.onClick.RemoveListener(OnQuickJoinClicked);
            if (refreshButton != null) refreshButton.onClick.RemoveListener(OnRefreshClicked);
            if (exitButton != null) exitButton.onClick.RemoveListener(OnExitClicked);

            if (lobbyService != null)
            {
                lobbyService.LobbiesQueried -= UpdateLobbyList;
                lobbyService.LobbyUpdated -= OnLobbyUpdated;
                lobbyService.LobbyError -= OnLobbyError;
            }
        }

        private async void OnCreateClicked()
        {
            if (lobbyService == null) return;

            var name = lobbyNameInput != null && !string.IsNullOrWhiteSpace(lobbyNameInput.text)
                ? lobbyNameInput.text
                : "Match";
            await lobbyService.CreateLobbyAsync(name, 2, GetText(mapInput), GetText(modeInput), GetText(regionInput));
        }

        private async void OnJoinCodeClicked()
        {
            if (lobbyService == null) return;
            var code = GetText(joinCodeInput);
            if (string.IsNullOrWhiteSpace(code)) return;
            await lobbyService.JoinLobbyByCodeAsync(code);
        }

        private async void OnQuickJoinClicked()
        {
            if (lobbyService == null) return;
            await lobbyService.QuickJoinAsync(GetText(mapInput), GetText(modeInput), GetText(regionInput));
        }

        private async void OnRefreshClicked()
        {
            if (lobbyService == null) return;
            await lobbyService.QueryLobbiesAsync(GetText(mapInput), GetText(modeInput), GetText(regionInput));
        }

        private async void OnExitClicked()
        {
            if (lobbyService == null) return;
            await lobbyService.LeaveLobbyAsync();
        }

        private void UpdateLobbyList(List<Lobby> lobbies)
        {
            if (listContent == null || lobbyRowButtonPrefab == null) return;

            ClearList();

            if (lobbies == null || lobbies.Count == 0)
            {
                if (statusText != null)
                {
                    statusText.text = "No lobbies found.";
                }

                return;
            }

            foreach (var lobby in lobbies)
            {
                var row = Instantiate(lobbyRowButtonPrefab, listContent);
                var label = row.GetComponentInChildren<TMP_Text>(true);
                if (label != null)
                {
                    var map = lobby.Data != null && lobby.Data.TryGetValue("map", out var mapObj) ? mapObj.Value : "";
                    var mode = lobby.Data != null && lobby.Data.TryGetValue("mode", out var modeObj) ? modeObj.Value : "";
                    var region = lobby.Data != null && lobby.Data.TryGetValue("region", out var regionObj) ? regionObj.Value : "";
                    label.text = $"{lobby.Name} | {lobby.Players.Count}/{lobby.MaxPlayers} | {map}/{mode}/{region} | Code: {lobby.LobbyCode}";
                }

                row.onClick.AddListener(() => OnLobbyRowClicked(lobby));
            }
        }

        private void OnLobbyUpdated(Lobby lobby)
        {
            if (statusText == null) return;
            if (lobby == null)
            {
                statusText.text = "Left lobby.";
                return;
            }

            statusText.text = $"In lobby: {lobby.Name} | {lobby.Players.Count}/{lobby.MaxPlayers} | Code: {lobby.LobbyCode}";

            if (lobbyService != null)
            {
                _ = lobbyService.QueryLobbiesAsync(GetText(mapInput), GetText(modeInput), GetText(regionInput));
            }
        }

        private void OnLobbyError(string message)
        {
            if (statusText == null) return;
            statusText.text = $"Lobby error: {message}";
        }

        private static string GetText(TMP_InputField input)
        {
            return input != null ? input.text : string.Empty;
        }

        private async void OnLobbyRowClicked(Lobby lobby)
        {
            if (lobbyService == null || lobby == null)
            {
                return;
            }

            await lobbyService.JoinLobbyByIdAsync(lobby.Id);
        }

        private void ClearList()
        {
            if (listContent == null) return;
            for (var i = listContent.childCount - 1; i >= 0; i--)
            {
                Destroy(listContent.GetChild(i).gameObject);
            }
        }
    }
}
