using Peribind.Unity.Networking;
using TMPro;
using Unity.Netcode;
using UnityEngine;

namespace Peribind.Unity.Networking
{
    public class GameSceneNetworkStatus : MonoBehaviour
    {
        [SerializeField] private MultiplayerSessionController sessionController;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text sessionCodeText;
        [SerializeField] private float refreshInterval = 0.5f;

        private float _nextRefresh;

        private void Awake()
        {
            if (sessionController == null)
            {
                sessionController = FindObjectOfType<MultiplayerSessionController>();
            }
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh)
            {
                return;
            }

            _nextRefresh = Time.unscaledTime + Mathf.Max(0.1f, refreshInterval);
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            var manager = NetworkManager.Singleton;
            var isHost = manager != null && manager.IsHost;
            var isServer = manager != null && manager.IsServer;
            var isClient = manager != null && manager.IsClient;
            var clientCount = manager != null ? manager.ConnectedClientsIds.Count : 0;

            if (statusText != null)
            {
                statusText.text = $"Host:{isHost} Server:{isServer} Client:{isClient} Clients:{clientCount}";
            }

            if (sessionCodeText != null && sessionController != null)
            {
                sessionCodeText.text = $"Code: {sessionController.CurrentSessionCode}";
            }
        }
    }
}
