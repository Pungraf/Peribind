using UnityEngine;
using UnityEngine.SceneManagement;

namespace Peribind.Unity.UI
{
    public class StarterMenu : MonoBehaviour
    {
        [SerializeField] private string gameSceneName = "GameScene";

        public void LoadGameScene()
        {
            SceneManager.LoadScene(gameSceneName);
        }

        public void QuitGame()
        {
            UnityEngine.Application.Quit();
        }
    }
}
