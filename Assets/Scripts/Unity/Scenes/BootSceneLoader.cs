using UnityEngine;
using UnityEngine.SceneManagement;

namespace Peribind.Unity.Scenes
{
    public class BootSceneLoader : MonoBehaviour
    {
        [SerializeField] private string starterSceneName = "StarterScene";

        private void Awake()
        {
            if (string.IsNullOrWhiteSpace(starterSceneName))
            {
                Debug.LogWarning("[BootSceneLoader] Starter scene name is empty.");
                return;
            }

            if (SceneManager.GetActiveScene().name == starterSceneName)
            {
                return;
            }

            SceneManager.LoadScene(starterSceneName, LoadSceneMode.Single);
        }
    }
}
