using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Services.Authentication;
using Peribind.Unity.Networking;
using Unity.Services.Authentication.PlayerAccounts;
using System;

namespace Peribind.Unity.UI
{
    public class StarterMenu : MonoBehaviour
    {
        [SerializeField] private string lobbySceneName = "LobbyScene";
        [SerializeField] private string profileSceneName = "PlayerProfileScene";
        [SerializeField] private string loginSceneName = "LoginScene";
        [SerializeField] private DirectConnectionController directConnection;
        [SerializeField] private string localhostAddress = "127.0.0.1";
        [SerializeField] private int localhostPort = 7777;
        [SerializeField] private string localHostIdentity = "local-host";
        [SerializeField] private string localClientIdentity = "local-client";

        public void LoadLobbyScene()
        {
            SceneManager.LoadScene(lobbySceneName);
        }

        public void LoadProfileScene()
        {
            SceneManager.LoadScene(profileSceneName);
        }

        public void Logout()
        {
            TryLogout();
            SceneManager.LoadScene(loginSceneName);
        }

        public void StartLocalHostTest()
        {
            EnsureDirectConnection();
            if (directConnection == null)
            {
                UnityEngine.Debug.LogWarning("[StarterMenu] DirectConnectionController not found for local host test.");
                return;
            }

            Environment.SetEnvironmentVariable("PERIBIND_LOCAL_TEST_ID", localHostIdentity);
            directConnection.StartHostWithIdentity(localHostIdentity);
        }

        public void JoinLocalHostTest()
        {
            EnsureDirectConnection();
            if (directConnection == null)
            {
                UnityEngine.Debug.LogWarning("[StarterMenu] DirectConnectionController not found for local client test.");
                return;
            }

            Environment.SetEnvironmentVariable("PERIBIND_LOCAL_TEST_ID", localClientIdentity);
            directConnection.StartClientWithIdentity(localhostAddress, localhostPort, localClientIdentity);
        }

        private static void TryLogout()
        {
            try
            {
                UgsBootstrap.SignOutAll();
            }
            catch
            {
                // Fallback path if UgsBootstrap static helpers are unavailable.
                try
                {
                    AuthenticationService.Instance.SignOut(true);
                    AuthenticationService.Instance.ClearSessionToken();
                }
                catch
                {
                    // Best effort only; missing/disabled services should not block scene navigation.
                }

                try
                {
                    if (PlayerAccountService.Instance != null && PlayerAccountService.Instance.IsSignedIn)
                    {
                        PlayerAccountService.Instance.SignOut();
                    }
                }
                catch
                {
                    // Best effort only.
                }
            }
        }

        private void EnsureDirectConnection()
        {
            if (directConnection == null)
            {
                directConnection = FindObjectOfType<DirectConnectionController>(true);
            }

            if (directConnection != null)
            {
                return;
            }

            var controllerObject = new GameObject("DirectConnectionController");
            DontDestroyOnLoad(controllerObject);
            directConnection = controllerObject.AddComponent<DirectConnectionController>();
        }
    }
}
