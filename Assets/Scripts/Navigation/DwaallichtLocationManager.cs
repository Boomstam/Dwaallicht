using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Dwaallicht.Navigation
{
    /// <summary>
    /// Runtime bridge for iOS-specific location authorization upgrades and
    /// background updates.
    /// </summary>
    public sealed class DwaallichtLocationManager : MonoBehaviour
    {
        public enum AuthorizationStatus
        {
            NotDetermined = 0,
            Restricted = 1,
            Denied = 2,
            AuthorizedAlways = 3,
            AuthorizedWhenInUse = 4
        }

        public static event Action<AuthorizationStatus> OnAuthorizationChanged;

        private static DwaallichtLocationManager _instance;

        public static DwaallichtLocationManager Instance
        {
            get
            {
                EnsureInitialized();
                return _instance;
            }
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void _Dwaallicht_RequestWhenInUseAuthorization();
        [DllImport("__Internal")] private static extern void _Dwaallicht_RequestAlwaysAuthorization();
        [DllImport("__Internal")] private static extern int _Dwaallicht_GetAuthorizationStatus();
        [DllImport("__Internal")] private static extern void _Dwaallicht_StartBackgroundLocationUpdates();
        [DllImport("__Internal")] private static extern void _Dwaallicht_StopBackgroundLocationUpdates();
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeOnLoad()
        {
            EnsureInitialized();
        }

        private static void EnsureInitialized()
        {
            if (_instance != null || !Application.isPlaying)
            {
                return;
            }

            var go = new GameObject("DwaallichtLocationManager");
            _instance = go.AddComponent<DwaallichtLocationManager>();
            DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void RequestWhenInUseAuthorization()
        {
#if UNITY_IOS && !UNITY_EDITOR
            _Dwaallicht_RequestWhenInUseAuthorization();
#else
            Debug.Log("[DwaallichtLocationManager] RequestWhenInUseAuthorization is a no-op outside iOS.");
#endif
        }

        public void RequestAlwaysAuthorization()
        {
#if UNITY_IOS && !UNITY_EDITOR
            _Dwaallicht_RequestAlwaysAuthorization();
#else
            Debug.Log("[DwaallichtLocationManager] RequestAlwaysAuthorization is a no-op outside iOS.");
#endif
        }

        public AuthorizationStatus GetAuthorizationStatus()
        {
#if UNITY_IOS && !UNITY_EDITOR
            return (AuthorizationStatus)_Dwaallicht_GetAuthorizationStatus();
#else
            return AuthorizationStatus.NotDetermined;
#endif
        }

        public void StartBackgroundLocationUpdates()
        {
#if UNITY_IOS && !UNITY_EDITOR
            _Dwaallicht_StartBackgroundLocationUpdates();
#else
            Debug.Log("[DwaallichtLocationManager] StartBackgroundLocationUpdates is a no-op outside iOS.");
#endif
        }

        public void StopBackgroundLocationUpdates()
        {
#if UNITY_IOS && !UNITY_EDITOR
            _Dwaallicht_StopBackgroundLocationUpdates();
#else
            Debug.Log("[DwaallichtLocationManager] StopBackgroundLocationUpdates is a no-op outside iOS.");
#endif
        }

        private void OnAuthorizationStatusChanged(string statusString)
        {
            if (!int.TryParse(statusString, out int statusInt))
            {
                return;
            }

            var status = (AuthorizationStatus)statusInt;
            Debug.Log($"[DwaallichtLocationManager] Authorization status changed: {status}");
            OnAuthorizationChanged?.Invoke(status);
        }
    }
}
