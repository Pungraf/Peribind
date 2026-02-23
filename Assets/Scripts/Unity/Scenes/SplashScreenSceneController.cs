using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Peribind.Unity.Scenes
{
    /// <summary>
    /// Displays a fullscreen splash image in SplashScreenScene and advances to LoginScene.
    /// Auto-bootstraps on scene load so no manual scene wiring is required.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class SplashScreenSceneController : MonoBehaviour
    {
        private const string SplashSceneName = "SplashScreenScene";
        private const string NextSceneName = "LoginScene";
        private const float DisplayDurationSeconds = 2f;
        private const string SplashImageResourcePath = "UI/Toolkit/Images/Peribind_MainScreen";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (!string.Equals(activeScene.name, SplashSceneName, System.StringComparison.Ordinal))
            {
                return;
            }

            if (FindObjectOfType<SplashScreenSceneController>() != null)
            {
                return;
            }

            var go = new GameObject(nameof(SplashScreenSceneController));
            go.AddComponent<SplashScreenSceneController>();
        }

        private void Awake()
        {
            if (!string.Equals(SceneManager.GetActiveScene().name, SplashSceneName, System.StringComparison.Ordinal))
            {
                Destroy(gameObject);
                return;
            }

            BuildFullscreenSplash();
        }

        private void Start()
        {
            StartCoroutine(AdvanceToLoginAfterDelay());
        }

        private void BuildFullscreenSplash()
        {
            var canvasGo = new GameObject("SplashCanvas");
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;

            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            var imageGo = new GameObject("SplashImage");
            imageGo.transform.SetParent(canvasGo.transform, false);

            var rect = imageGo.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = imageGo.AddComponent<RawImage>();
            image.color = Color.white;

            var texture = Resources.Load<Texture2D>(SplashImageResourcePath);
            if (texture != null)
            {
                image.texture = texture;
                return;
            }

            // Fallback in case texture is missing in Resources.
            image.color = Color.black;
            Debug.LogWarning($"[SplashScreenSceneController] Could not load texture at Resources/{SplashImageResourcePath}");
        }

        private static IEnumerator AdvanceToLoginAfterDelay()
        {
            yield return new WaitForSecondsRealtime(DisplayDurationSeconds);

            if (!string.Equals(SceneManager.GetActiveScene().name, SplashSceneName, System.StringComparison.Ordinal))
            {
                yield break;
            }

            SceneManager.LoadScene(NextSceneName, LoadSceneMode.Single);
        }
    }
}
