using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Services.Authentication;

namespace Peribind.Unity.UI
{
    public class StarterMenu : MonoBehaviour
    {
        [SerializeField] private string lobbySceneName = "LobbyScene";
        [SerializeField] private string loginSceneName = "LoginScene";

        public void LoadLobbyScene()
        {
            SceneManager.LoadScene(lobbySceneName);
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
                AuthenticationService.Instance.SignOut(true);
                AuthenticationService.Instance.ClearSessionToken();
            }
            catch
            {
                // Best effort only; missing/disabled services should not block scene navigation.
            }
        }
    }
}
