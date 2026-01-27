using UnityEngine;
using UnityEngine.SceneManagement;

namespace Peribind.Unity.UI
{
    public class StarterMenu : MonoBehaviour
    {
        [SerializeField] private string gameSceneName = "GameScene";
        [SerializeField] private string lobbySceneName = "LobbyScene";

        public void LoadGameScene()
        {
            SceneManager.LoadScene(gameSceneName);
        }

        public void LoadLobbyScene()
        {
            SceneManager.LoadScene(lobbySceneName);
        }

        public void QuitGame()
        {
            UnityEngine.Application.Quit();
        }
    }
}
