using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Peribind.Unity.Rendering
{
    /// <summary>
    /// Applies scene-based X/Z skybox tilt and continuously spins Y.
    /// Auto-bootstraps before first scene load and survives scene changes.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class SkyboxRotationController : MonoBehaviour
    {
        private const string RotationPropertyName = "_Rotation";
        private const string GameSceneName = "GameScene";

        private const float NonGameX = 0f;
        private const float NonGameZ = 180f;
        private const float GameX = -45f;
        private const float GameZ = 180f;
        private const float SpinSpeedYDegreesPerSecond = 2.5f;

        private static SkyboxRotationController _instance;

        private enum RotationPropertyMode
        {
            None = 0,
            Float = 1,
            Vector = 2
        }

        private Material _sourceSkybox;
        private Material _runtimeSkybox;
        private RotationPropertyMode _rotationMode = RotationPropertyMode.None;
        private float _currentY;
        private bool _isGameScene;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureInstance();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BootstrapAfterSceneLoad()
        {
            EnsureInstance();
            if (_instance != null)
            {
                _instance.ForceRefreshFromActiveScene();
            }
        }

        private static void EnsureInstance()
        {
            if (_instance != null)
            {
                return;
            }

            var existing = FindObjectOfType<SkyboxRotationController>();
            if (existing != null)
            {
                _instance = existing;
                DontDestroyOnLoad(existing.gameObject);
                return;
            }

            var go = new GameObject(nameof(SkyboxRotationController));
            _instance = go.AddComponent<SkyboxRotationController>();
            DontDestroyOnLoad(go);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            UpdateSceneState(SceneManager.GetActiveScene());
            EnsureRuntimeSkyboxInstance();
            SyncCurrentYFromMaterial();
            ApplyRotation(force: true);
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

            if (_runtimeSkybox != null)
            {
                Destroy(_runtimeSkybox);
                _runtimeSkybox = null;
            }
        }

        private void Update()
        {
            // Keep scene state in sync even if an event is missed.
            UpdateSceneState(SceneManager.GetActiveScene());

            if (!EnsureRuntimeSkyboxInstance())
            {
                return;
            }

            _currentY = Mathf.Repeat(_currentY + SpinSpeedYDegreesPerSecond * Time.unscaledDeltaTime, 360f);
            ApplyRotation(force: false);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            UpdateSceneState(scene);
            EnsureRuntimeSkyboxInstance(forceClone: true);
            // Keep spinning Y across scenes; only reset X/Z by scene type.
            ApplyRotation(force: true);
        }

        private void OnActiveSceneChanged(Scene previousScene, Scene nextScene)
        {
            UpdateSceneState(nextScene);
            EnsureRuntimeSkyboxInstance(forceClone: true);
            ApplyRotation(force: true);
        }

        private void ForceRefreshFromActiveScene()
        {
            UpdateSceneState(SceneManager.GetActiveScene());
            EnsureRuntimeSkyboxInstance(forceClone: true);
            ApplyRotation(force: true);
        }

        private void UpdateSceneState(Scene scene)
        {
            _isGameScene = string.Equals(scene.name, GameSceneName, System.StringComparison.Ordinal);
        }

        private bool EnsureRuntimeSkyboxInstance(bool forceClone = false)
        {
            var assigned = RenderSettings.skybox;
            if (assigned == null)
            {
                _sourceSkybox = null;
                _rotationMode = RotationPropertyMode.None;
                return false;
            }

            var sourceChanged = forceClone ||
                                _runtimeSkybox == null ||
                                (!ReferenceEquals(assigned, _runtimeSkybox) && !ReferenceEquals(assigned, _sourceSkybox));

            if (sourceChanged)
            {
                if (_runtimeSkybox != null)
                {
                    Destroy(_runtimeSkybox);
                    _runtimeSkybox = null;
                }

                _sourceSkybox = assigned;
                _runtimeSkybox = new Material(_sourceSkybox)
                {
                    name = $"{_sourceSkybox.name} (Runtime)"
                };
                RenderSettings.skybox = _runtimeSkybox;

                _rotationMode = ResolveRotationMode(_runtimeSkybox);
            }

            return _runtimeSkybox != null && _rotationMode != RotationPropertyMode.None;
        }

        private static RotationPropertyMode ResolveRotationMode(Material material)
        {
            if (material == null || !material.HasProperty(RotationPropertyName))
            {
                return RotationPropertyMode.None;
            }

            var shader = material.shader;
            if (shader == null)
            {
                return RotationPropertyMode.Vector;
            }

            var index = shader.FindPropertyIndex(RotationPropertyName);
            if (index < 0)
            {
                return RotationPropertyMode.Vector;
            }

            var propType = shader.GetPropertyType(index);
            if (propType == ShaderPropertyType.Vector)
            {
                return RotationPropertyMode.Vector;
            }

            if (propType == ShaderPropertyType.Float || propType == ShaderPropertyType.Range)
            {
                return RotationPropertyMode.Float;
            }

            return RotationPropertyMode.None;
        }

        private void SyncCurrentYFromMaterial()
        {
            if (_runtimeSkybox == null)
            {
                return;
            }

            if (_rotationMode == RotationPropertyMode.Vector)
            {
                _currentY = Mathf.Repeat(_runtimeSkybox.GetVector(RotationPropertyName).y, 360f);
                return;
            }

            if (_rotationMode == RotationPropertyMode.Float)
            {
                _currentY = Mathf.Repeat(_runtimeSkybox.GetFloat(RotationPropertyName), 360f);
                return;
            }

            _currentY = 0f;
        }

        private void ApplyRotation(bool force)
        {
            if (_runtimeSkybox == null)
            {
                return;
            }

            if (_rotationMode == RotationPropertyMode.Vector)
            {
                var target = new Vector4(
                    _isGameScene ? GameX : NonGameX,
                    _currentY,
                    _isGameScene ? GameZ : NonGameZ,
                    0f);

                if (force || _runtimeSkybox.GetVector(RotationPropertyName) != target)
                {
                    _runtimeSkybox.SetVector(RotationPropertyName, target);
                }

                return;
            }

            if (_rotationMode == RotationPropertyMode.Float)
            {
                if (force || !Mathf.Approximately(_runtimeSkybox.GetFloat(RotationPropertyName), _currentY))
                {
                    _runtimeSkybox.SetFloat(RotationPropertyName, _currentY);
                }
            }
        }
    }
}
