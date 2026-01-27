using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

namespace Peribind.Unity.Networking
{
    public class ServicesBootstrap : MonoBehaviour
    {
        [SerializeField] private bool autoInitialize = true;

        public bool IsInitialized { get; private set; }
        public bool IsSignedIn => AuthenticationService.Instance.IsSignedIn;
        public string PlayerId => AuthenticationService.Instance.PlayerId;

        private async void Start()
        {
            if (autoInitialize)
            {
                await InitializeAsync();
            }
        }

        public async Task InitializeAsync()
        {
            if (IsInitialized)
            {
                return;
            }

            try
            {
                await UnityServices.InitializeAsync();
                IsInitialized = true;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return;
            }

            try
            {
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }
}
