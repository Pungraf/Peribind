using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using System.Text;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Services.Authentication;
using Unity.Services.Core;

namespace Peribind.Unity.Networking
{
    public class DirectConnectionController : MonoBehaviour
    {
        [SerializeField] private ushort port = 7777;
        [SerializeField] private string gameSceneName = "GameScene";
        [SerializeField] private string serverPortEnvironmentKey = "PERIBIND_SERVER_PORT";
        [SerializeField] private string serverPortArgumentName = "-port";
        [SerializeField] private bool allowLocalTestIdentityWithoutAuthentication = true;
        [SerializeField] private string localTestIdentityEnvironmentKey = "PERIBIND_LOCAL_TEST_ID";
        [SerializeField] private string localTestIdentityArgumentName = "-localTestId";
        [SerializeField] private string defaultHostLocalTestIdentity = "local-host";
        [SerializeField] private string defaultClientLocalTestIdentity = "local-client";
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

            if (!TryGetPlayerIdentity(out var playerId))
            {
                Debug.LogWarning("[DirectConnection] StartClient blocked: no authenticated or local test identity available.");
                return false;
            }

            manager.NetworkConfig.ConnectionApproval = true;
            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(playerId);
            transport.SetConnectionData(address, (ushort)portOverride);
            var started = manager.StartClient();
            Debug.Log($"[DirectConnection] NetworkManager.StartClient returned {started}.");
            return started;
        }

        public bool StartLocalHost()
        {
            return StartHostWithIdentity(defaultHostLocalTestIdentity);
        }

        public bool StartLocalClient()
        {
            return StartClientWithIdentity("127.0.0.1", port, defaultClientLocalTestIdentity);
        }

        public bool StartClientWithIdentity(string address, int portOverride, string identity)
        {
            return StartClientInternal(address, portOverride, identity);
        }

        public bool StartHostWithIdentity(string identity)
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

            if (manager.IsListening || manager.IsClient || manager.IsServer || manager.IsHost)
            {
                Debug.LogWarning(
                    $"[DirectConnection] Host start skipped: NetworkManager already active " +
                    $"(IsListening={manager.IsListening}, IsClient={manager.IsClient}, IsServer={manager.IsServer}, IsHost={manager.IsHost}).");
                return false;
            }

            var resolvedIdentity = ResolvePlayerIdentity(identity);
            if (string.IsNullOrWhiteSpace(resolvedIdentity))
            {
                Debug.LogWarning("[DirectConnection] StartHost blocked: no authenticated or local test identity available.");
                return false;
            }

            var listenPort = ResolveServerPort();
            transport.SetConnectionData("0.0.0.0", listenPort, "0.0.0.0");
            manager.NetworkConfig.ConnectionApproval = true;
            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(resolvedIdentity);
            NetworkGameController.ConfigureConnectionApproval(manager);
            if (manager.GetComponent<MatchLifecycleServer>() == null)
            {
                manager.gameObject.AddComponent<MatchLifecycleServer>();
            }

            var started = manager.StartHost();
            Debug.Log($"[DirectConnection] NetworkManager.StartHost returned {started}. Identity='{resolvedIdentity}'.");
            if (started && manager.SceneManager != null)
            {
                manager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
            }

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

        private bool StartClientInternal(string address, int portOverride, string identityOverride)
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

            var playerId = ResolvePlayerIdentity(identityOverride);
            if (string.IsNullOrWhiteSpace(playerId))
            {
                Debug.LogWarning("[DirectConnection] StartClient blocked: no authenticated or local test identity available.");
                return false;
            }

            manager.NetworkConfig.ConnectionApproval = true;
            manager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(playerId);
            transport.SetConnectionData(address, (ushort)portOverride);
            var started = manager.StartClient();
            Debug.Log($"[DirectConnection] NetworkManager.StartClient returned {started}. Identity='{playerId}'.");
            return started;
        }

        private bool TryGetPlayerIdentity(out string playerId)
        {
            playerId = ResolvePlayerIdentity(string.Empty);
            return !string.IsNullOrWhiteSpace(playerId);
        }

        private string ResolvePlayerIdentity(string explicitIdentity)
        {
            if (!string.IsNullOrWhiteSpace(explicitIdentity))
            {
                return explicitIdentity.Trim();
            }

            if (TryGetAuthenticatedPlayerId(out var authenticatedIdentity))
            {
                return authenticatedIdentity;
            }

            if (!allowLocalTestIdentityWithoutAuthentication)
            {
                return string.Empty;
            }

            var overrideIdentity = ResolveLocalTestIdentityOverride();
            if (!string.IsNullOrWhiteSpace(overrideIdentity))
            {
                return overrideIdentity;
            }

            return string.Empty;
        }

        private string ResolveLocalTestIdentityOverride()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(localTestIdentityEnvironmentKey);
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return fromEnvironment.Trim();
            }

            var args = Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (!string.Equals(args[i], localTestIdentityArgumentName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    return args[i + 1].Trim();
                }
            }

            return string.Empty;
        }

        private static bool TryGetAuthenticatedPlayerId(out string playerId)
        {
            playerId = string.Empty;
            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                return false;
            }

            try
            {
                if (AuthenticationService.Instance != null && AuthenticationService.Instance.IsSignedIn)
                {
                    playerId = AuthenticationService.Instance.PlayerId ?? string.Empty;
                    return !string.IsNullOrWhiteSpace(playerId);
                }
            }
            catch
            {
                return false;
            }

            return false;
        }
    }
}
