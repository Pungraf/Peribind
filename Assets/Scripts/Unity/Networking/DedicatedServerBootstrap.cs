using UnityEngine;

namespace Peribind.Unity.Networking
{
    public class DedicatedServerBootstrap : MonoBehaviour
    {
        [SerializeField] private DirectConnectionController directConnection;

        private void Start()
        {
            if (!IsServerRuntime())
            {
                return;
            }

            if (directConnection == null)
            {
                directConnection = FindObjectOfType<DirectConnectionController>();
            }

            if (directConnection == null)
            {
                Debug.LogWarning("[DedicatedServerBootstrap] DirectConnectionController not found.");
                return;
            }

            directConnection.StartServer();
        }

        private static bool IsServerRuntime()
        {
#if UNITY_SERVER
            return true;
#else
            return global::UnityEngine.Application.isBatchMode;
#endif
        }
    }
}
