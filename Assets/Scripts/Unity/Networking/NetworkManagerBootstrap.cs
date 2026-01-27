using Unity.Netcode;
using UnityEngine;

namespace Peribind.Unity.Networking
{
    public class NetworkManagerBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            var current = NetworkManager.Singleton;
            if (current != null && current != GetComponent<NetworkManager>())
            {
                Destroy(gameObject);
                return;
            }

            DontDestroyOnLoad(gameObject);
        }
    }
}
