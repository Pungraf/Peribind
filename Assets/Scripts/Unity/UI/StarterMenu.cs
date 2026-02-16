using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Services.Authentication;
using Peribind.Unity.Networking;
using Unity.Services.Authentication.PlayerAccounts;

namespace Peribind.Unity.UI
{
    public class StarterMenu : MonoBehaviour
    {
        [SerializeField] private string lobbySceneName = "LobbyScene";
        [SerializeField] private string profileSceneName = "PlayerProfileScene";
        [SerializeField] private string loginSceneName = "LoginScene";

        public void LoadLobbyScene()
        {
            SceneManager.LoadScene(lobbySceneName);
        }

        public void LoadProfileScene()
        {
            SceneManager.LoadScene(profileSceneName);
        }

        public void Logout()
        {
            TryLogout();
            SceneManager.LoadScene(loginSceneName);
        }

        private static void TryLogout()
        {
            try
            {
                UgsBootstrap.SignOutAll();
            }
            catch
            {
                // Fallback path if UgsBootstrap static helpers are unavailable.
                try
                {
                    AuthenticationService.Instance.SignOut(true);
                    AuthenticationService.Instance.ClearSessionToken();
                }
                catch
                {
                    // Best effort only; missing/disabled services should not block scene navigation.
                }

                try
                {
                    if (PlayerAccountService.Instance != null && PlayerAccountService.Instance.IsSignedIn)
                    {
                        PlayerAccountService.Instance.SignOut();
                    }
                }
                catch
                {
                    // Best effort only.
                }
            }
        }
    }
}
