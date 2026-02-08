using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Peribind.Unity.Networking
{
    public class DirectConnectionController : MonoBehaviour
    {
        [SerializeField] private ushort port = 7777;
        [SerializeField] private string gameSceneName = "GameScene";

        public bool StartServer()
        {
            var manager = EnsureNetworkManager();
            if (manager == null)
            {
                Debug.LogWarning("[DirectConnection] NetworkManager missing.");
                return false;
            }

            var transport = manager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogWarning("[DirectConnection] UnityTransport missing.");
                return false;
            }

            transport.SetConnectionData("0.0.0.0", port, "0.0.0.0");

            if (manager.IsListening)
            {
                Debug.LogWarning("[DirectConnection] Server already listening.");
                return true;
            }

            var started = manager.StartServer();
            Debug.Log($"[DirectConnection] NetworkManager.StartServer returned {started}.");
            if (started && manager.SceneManager != null)
            {
                manager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
            }

            return started;
        }

        public bool StartClient(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                Debug.LogWarning("[DirectConnection] StartClient called with empty address.");
                return false;
            }

            var manager = EnsureNetworkManager();
            if (manager == null)
            {
                Debug.LogWarning("[DirectConnection] NetworkManager missing.");
                return false;
            }

            var transport = manager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogWarning("[DirectConnection] UnityTransport missing.");
                return false;
            }

            if (manager.IsListening || manager.IsClient || manager.IsServer || manager.IsHost)
            {
                Debug.LogWarning("[DirectConnection] Client start skipped: NetworkManager already active.");
                return false;
            }

            transport.SetConnectionData(address, port);
            var started = manager.StartClient();
            Debug.Log($"[DirectConnection] NetworkManager.StartClient returned {started}.");
            return started;
        }

        private NetworkManager EnsureNetworkManager()
        {
            if (NetworkManager.Singleton != null)
            {
                return NetworkManager.Singleton;
            }

            var managerObject = new GameObject("NetworkManager");
            managerObject.AddComponent<NetworkManagerBootstrap>();

            var transport = managerObject.AddComponent<UnityTransport>();
            var manager = managerObject.AddComponent<NetworkManager>();

            if (manager.NetworkConfig == null)
            {
                manager.NetworkConfig = new NetworkConfig();
            }

            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.EnableSceneManagement = true;
            return manager;
        }
    }
}
