using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System.Text;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Peribind.Unity.Networking
{
    public class DirectConnectionController : MonoBehaviour
    {
        [SerializeField] private ushort port = 7777;
        [SerializeField] private string gameSceneName = "GameScene";
        [SerializeField] private string serverPortEnvironmentKey = "PERIBIND_SERVER_PORT";
        [SerializeField] private string serverPortArgumentName = "-port";
        private bool _callbacksRegistered;

        public bool StartServer()
        {
            var manager = EnsureNetworkManager();
            if (manager == null)
            {
                Debug.LogWarning("[DirectConnection] NetworkManager missing.");
                return false;
            }

            EnsureNetworkCallbacks(manager);

            var transport = manager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogWarning("[DirectConnection] UnityTransport missing.");
                return false;
            }

            var listenPort = ResolveServerPort();
            transport.SetConnectionData("0.0.0.0", listenPort, "0.0.0.0");
            Debug.Log($"[DirectConnection] Server listen port: {listenPort}");

            NetworkGameController.ConfigureConnectionApproval(manager);
            manager.NetworkConfig.ConnectionApproval = true;
            if (manager.GetComponent<MatchLifecycleServer>() == null)
            {
                manager.gameObject.AddComponent<MatchLifecycleServer>();
            }

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
            return StartClient(address, port);
        }

        public bool StartClient(string address, int portOverride)
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

            EnsureNetworkCallbacks(manager);

            var transport = manager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogWarning("[DirectConnection] UnityTransport missing.");
                return false;
            }

            if (manager.IsListening || manager.IsClient || manager.IsServer || manager.IsHost)
            {
                Debug.LogWarning(
                    $"[DirectConnection] Client start skipped: NetworkManager already active " +
                    $"(IsListening={manager.IsListening}, IsClient={manager.IsClient}, IsServer={manager.IsServer}, IsHost={manager.IsHost}).");
                return false;
            }

            var identityProvider = FindObjectOfType<PlayerIdentityProvider>(true);
            if (identityProvider == null || string.IsNullOrWhiteSpace(identityProvider.PlayerId))
            {
                Debug.LogWarning("[DirectConnection] StartClient blocked: missing credentials.");
                return false;
            }

            manager.NetworkConfig.ConnectionApproval = true;
            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(identityProvider.PlayerId);
            transport.SetConnectionData(address, (ushort)portOverride);
            var started = manager.StartClient();
            Debug.Log($"[DirectConnection] NetworkManager.StartClient returned {started}.");
            return started;
        }

        private void EnsureNetworkCallbacks(NetworkManager manager)
        {
            if (_callbacksRegistered || manager == null)
            {
                return;
            }

            manager.OnClientConnectedCallback += OnClientConnected;
            manager.OnClientDisconnectCallback += OnClientDisconnected;
            _callbacksRegistered = true;
        }

        private void OnClientConnected(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null)
            {
                return;
            }

            if (!manager.IsServer && clientId == manager.LocalClientId)
            {
                Debug.Log($"[DirectConnection] Client connected to server. LocalClientId={clientId}");
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            var manager = NetworkManager.Singleton;
            if (manager == null)
            {
                return;
            }

            if (!manager.IsServer && clientId == manager.LocalClientId)
            {
                var reason = manager.DisconnectReason;
                Debug.LogWarning($"[DirectConnection] Client disconnected. Reason='{reason}'");
            }
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

        private ushort ResolveServerPort()
        {
            var fromEnv = Environment.GetEnvironmentVariable(serverPortEnvironmentKey);
            if (ushort.TryParse(fromEnv, out var envPort) && envPort > 0)
            {
                return envPort;
            }

            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], serverPortArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (ushort.TryParse(args[i + 1], out var argPort) && argPort > 0)
                {
                    return argPort;
                }
            }

            return port;
        }
    }
}
