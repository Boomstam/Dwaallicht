using System.Collections;
using Dwaallicht.Input;
using UnityEngine;
using UnityInput = UnityEngine.Input;
using UnityEngine.InputSystem;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Dwaallicht.Navigation
{
    [AddComponentMenu("Dwaallicht/Navigation/Compass Heading Provider")]
    public sealed class CompassHeadingProvider : MonoBehaviour
    {
        public enum HeadingSource
        {
            Auto,
            DeviceCompass,
            Simulated
        }

        [Header("Source")]
        [SerializeField] private HeadingSource source = HeadingSource.Auto;
        [SerializeField] private bool simulateInEditor = true;

        [Header("Smoothing")]
        [SerializeField, Min(0.01f)] private float smoothTime = 0.18f;

        [Header("Editor Simulation")]
        [SerializeField, Range(0f, 360f)] private float simulatedHeading = 25f;
        [SerializeField] private Vector2 simulatedLatLon = new Vector2(51.096465f, 4.344778f);
        [SerializeField] private float simulatedTurnSpeedDegrees = 100f;
        [SerializeField] private float simulatedWalkSpeedMetersPerSecond = 1.4f;
        [SerializeField] private bool keyboardSimulation = true;

        private float smoothedHeading;
        private float headingVelocity;
        private bool deviceStartupAttempted;
        private AttitudeSensor attitudeSensor;

#if UNITY_ANDROID && !UNITY_EDITOR
        private bool? androidLocationPermissionGranted;
        private int androidLocationPermissionResponses;
        private PermissionCallbacks androidLocationPermissionCallbacks;
        private AndroidLocationBridge androidLocation;
#endif

#if UNITY_IOS && !UNITY_EDITOR
        private DwaallichtLocationManager iosLocationManager;
#endif

        public bool IsReady { get; private set; }
        public bool IsSimulated => ResolveSource() == HeadingSource.Simulated;
        public float Heading => GeoMath.NormalizeDegrees(smoothedHeading);
        public float RawHeading { get; private set; }
        public float HeadingAccuracy { get; private set; } = -1f;
        public Vector2 CurrentLatLon { get; private set; }
        public string Status { get; private set; } = "Starting";
        public bool CompassMayBeUnreliable { get; private set; }

        private void OnEnable()
        {
            CurrentLatLon = simulatedLatLon;
            RawHeading = simulatedHeading;
            smoothedHeading = simulatedHeading;
            CompassMayBeUnreliable = false;

            if (ResolveSource() == HeadingSource.DeviceCompass)
            {
                StartCoroutine(StartDeviceSensors());
            }
            else
            {
                IsReady = true;
                Status = "Editor compass simulation";
            }
        }

        private void OnDisable()
        {
            if (deviceStartupAttempted)
            {
                DisableDeviceSensors();
            }
        }

        private void Update()
        {
            if (ResolveSource() == HeadingSource.Simulated)
            {
                UpdateSimulation();
            }
            else
            {
                UpdateDeviceHeading();
            }

            smoothedHeading = Mathf.SmoothDampAngle(
                smoothedHeading,
                RawHeading,
                ref headingVelocity,
                smoothTime);
        }

        public void SetSimulatedHeading(float heading)
        {
            simulatedHeading = GeoMath.NormalizeDegrees(heading);
            RawHeading = simulatedHeading;
        }

        public void SetSimulatedLocation(Vector2 latLon)
        {
            simulatedLatLon = latLon;
            CurrentLatLon = latLon;
        }

        private HeadingSource ResolveSource()
        {
            if (source == HeadingSource.Simulated)
            {
                return HeadingSource.Simulated;
            }

#if UNITY_EDITOR
            if (source == HeadingSource.Auto && simulateInEditor)
            {
                return HeadingSource.Simulated;
            }
#endif

            return source == HeadingSource.Auto ? HeadingSource.DeviceCompass : source;
        }

        private IEnumerator StartDeviceSensors()
        {
            deviceStartupAttempted = true;
            Status = "Starting location and compass";

#if UNITY_ANDROID && !UNITY_EDITOR
            yield return RequestAndroidLocationPermission();

            if (!HasAndroidLocationPermission())
            {
                IsReady = false;
                Status = "Location permission denied";
                yield break;
            }
#endif

#if UNITY_IOS && !UNITY_EDITOR
            yield return RequestIosLocationPermission();

            if (!HasIosLocationAuthorization())
            {
                IsReady = false;
                Status = "Location permission denied";
                yield break;
            }
#endif

            attitudeSensor = ResolveAttitudeSensor();
            if (attitudeSensor == null)
            {
                IsReady = false;
                Status = "Attitude sensor unavailable";
                yield break;
            }

            InputSystem.EnableDevice(attitudeSensor);
            CompassMayBeUnreliable = !IsNorthReferencedAttitudeSensor(attitudeSensor);

#if UNITY_ANDROID && !UNITY_EDITOR
            androidLocation = new AndroidLocationBridge();
            if (!androidLocation.Start(1f, 1f, out var locationStatus))
            {
                IsReady = false;
                Status = locationStatus;
                yield break;
            }

            var maxWaitSeconds = 12;
            while (!androidLocation.HasLocation && maxWaitSeconds > 0)
            {
                yield return new WaitForSeconds(1f);
                maxWaitSeconds--;
            }

            if (!androidLocation.HasLocation)
            {
                IsReady = false;
                Status = "Android location unavailable";
                yield break;
            }

            CurrentLatLon = androidLocation.LatLon;
#elif UNITY_IOS && !UNITY_EDITOR
            if (!UnityInput.location.isEnabledByUser)
            {
                IsReady = false;
                Status = "Location disabled by user";
                yield break;
            }

            UnityInput.location.Start(1f, 1f);

            var maxWaitSeconds = 12;
            while (UnityInput.location.status == LocationServiceStatus.Initializing && maxWaitSeconds > 0)
            {
                yield return new WaitForSeconds(1f);
                maxWaitSeconds--;
            }

            if (UnityInput.location.status != LocationServiceStatus.Running)
            {
                IsReady = false;
                Status = UnityInput.location.status == LocationServiceStatus.Failed
                    ? "iOS location unavailable"
                    : "iOS location timed out";
                yield break;
            }

            var data = UnityInput.location.lastData;
            CurrentLatLon = new Vector2(data.latitude, data.longitude);
#else
            CurrentLatLon = simulatedLatLon;
#endif

            IsReady = true;
            Status = CompassMayBeUnreliable
                ? "Kompas werkt mogelijk niet op deze telefoon"
                : "Device sensors ready";
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator RequestAndroidLocationPermission()
        {
            if (HasAndroidLocationPermission())
            {
                yield break;
            }

            androidLocationPermissionGranted = null;
            androidLocationPermissionResponses = 0;
            androidLocationPermissionCallbacks = new PermissionCallbacks();
            androidLocationPermissionCallbacks.PermissionGranted += OnAndroidLocationPermissionGranted;
            androidLocationPermissionCallbacks.PermissionDenied += OnAndroidLocationPermissionDenied;
            androidLocationPermissionCallbacks.PermissionDeniedAndDontAskAgain += OnAndroidLocationPermissionDenied;

            Status = "Requesting location permission";
            Permission.RequestUserPermissions(
                new[] { Permission.CoarseLocation, Permission.FineLocation },
                androidLocationPermissionCallbacks);

            while (!androidLocationPermissionGranted.HasValue)
            {
                yield return null;
            }
        }

        private static bool HasAndroidLocationPermission()
        {
            return Permission.HasUserAuthorizedPermission(Permission.FineLocation)
                || Permission.HasUserAuthorizedPermission(Permission.CoarseLocation);
        }

        private void OnAndroidLocationPermissionGranted(string permissionName)
        {
            if (!IsAndroidLocationPermission(permissionName))
            {
                return;
            }

            androidLocationPermissionGranted = true;
        }

        private void OnAndroidLocationPermissionDenied(string permissionName)
        {
            if (!IsAndroidLocationPermission(permissionName))
            {
                return;
            }

            androidLocationPermissionResponses++;
            if (androidLocationPermissionResponses >= 2)
            {
                androidLocationPermissionGranted = false;
            }
        }

        private static bool IsAndroidLocationPermission(string permissionName)
        {
            return permissionName == Permission.FineLocation
                || permissionName == Permission.CoarseLocation;
        }
#endif

#if UNITY_IOS && !UNITY_EDITOR
        private IEnumerator RequestIosLocationPermission()
        {
            iosLocationManager = DwaallichtLocationManager.Instance;
            var status = iosLocationManager.GetAuthorizationStatus();
            if (status == DwaallichtLocationManager.AuthorizationStatus.AuthorizedAlways
                || status == DwaallichtLocationManager.AuthorizationStatus.AuthorizedWhenInUse)
            {
                yield break;
            }

            if (status == DwaallichtLocationManager.AuthorizationStatus.Denied
                || status == DwaallichtLocationManager.AuthorizationStatus.Restricted)
            {
                yield break;
            }

            Status = "Requesting location permission";
            iosLocationManager.RequestWhenInUseAuthorization();

            float timeoutAt = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < timeoutAt)
            {
                status = iosLocationManager.GetAuthorizationStatus();
                if (status != DwaallichtLocationManager.AuthorizationStatus.NotDetermined)
                {
                    break;
                }

                yield return null;
            }
        }

        private bool HasIosLocationAuthorization()
        {
            var status = iosLocationManager != null
                ? iosLocationManager.GetAuthorizationStatus()
                : DwaallichtLocationManager.Instance.GetAuthorizationStatus();
            return status == DwaallichtLocationManager.AuthorizationStatus.AuthorizedAlways
                || status == DwaallichtLocationManager.AuthorizationStatus.AuthorizedWhenInUse;
        }
#endif

        private void UpdateDeviceHeading()
        {
            if (!IsReady || attitudeSensor == null)
            {
                return;
            }

            RawHeading = HeadingFromAttitude(attitudeSensor.attitude.ReadValue());
            HeadingAccuracy = -1f;

#if UNITY_ANDROID && !UNITY_EDITOR
            if (androidLocation != null && androidLocation.HasLocation)
            {
                CurrentLatLon = androidLocation.LatLon;
            }
#endif

#if UNITY_IOS && !UNITY_EDITOR
            if (UnityInput.location.status == LocationServiceStatus.Running)
            {
                var data = UnityInput.location.lastData;
                CurrentLatLon = new Vector2(data.latitude, data.longitude);
            }
#endif
        }

        private void UpdateSimulation()
        {
            IsReady = true;
            Status = "Editor compass simulation";

            if (keyboardSimulation)
            {
                float turn = 0f;
                if (DwaallichtInput.IsAnyKeyPressed(Key.LeftArrow, Key.A))
                {
                    turn -= 1f;
                }

                if (DwaallichtInput.IsAnyKeyPressed(Key.RightArrow, Key.D))
                {
                    turn += 1f;
                }

                if (Mathf.Abs(turn) > 0f)
                {
                    simulatedHeading = GeoMath.NormalizeDegrees(simulatedHeading + turn * simulatedTurnSpeedDegrees * Time.deltaTime);
                }

                float walk = 0f;
                if (DwaallichtInput.IsAnyKeyPressed(Key.UpArrow, Key.W))
                {
                    walk += 1f;
                }

                if (DwaallichtInput.IsAnyKeyPressed(Key.DownArrow, Key.S))
                {
                    walk -= 1f;
                }

                if (Mathf.Abs(walk) > 0f)
                {
                    simulatedLatLon = MoveLatLon(simulatedLatLon, simulatedHeading, walk * simulatedWalkSpeedMetersPerSecond * Time.deltaTime);
                }
            }

            RawHeading = simulatedHeading;
            CurrentLatLon = simulatedLatLon;
            HeadingAccuracy = 0f;
        }

        private static AttitudeSensor ResolveAttitudeSensor()
        {
            AttitudeSensor fallback = null;

            foreach (var device in InputSystem.devices)
            {
                if (device is not AttitudeSensor sensor)
                {
                    continue;
                }

                fallback ??= sensor;
                if (device.layout.Contains("RotationVector") && !device.layout.Contains("GameRotationVector"))
                {
                    return sensor;
                }
            }

            return fallback ?? AttitudeSensor.current;
        }

        private static bool IsNorthReferencedAttitudeSensor(InputDevice device)
        {
            return device != null
                && device.layout.Contains("RotationVector")
                && !device.layout.Contains("GameRotationVector");
        }

        private void DisableDeviceSensors()
        {
            if (attitudeSensor != null)
            {
                InputSystem.DisableDevice(attitudeSensor);
                attitudeSensor = null;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            androidLocation?.Dispose();
            androidLocation = null;
#endif

#if UNITY_IOS && !UNITY_EDITOR
            if (UnityInput.location.isEnabledByUser)
            {
                UnityInput.location.Stop();
            }
#endif
        }

        private static float HeadingFromAttitude(Quaternion attitude)
        {
            var phoneTop = attitude * Vector3.up;
            phoneTop.y = 0f;

            if (phoneTop.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            phoneTop.Normalize();
            return GeoMath.NormalizeDegrees(Mathf.Atan2(phoneTop.x, phoneTop.z) * Mathf.Rad2Deg);
        }

        private static Vector2 MoveLatLon(Vector2 latLon, float bearingDegrees, float meters)
        {
            const float metersPerDegreeLatitude = 111320f;
            float bearing = bearingDegrees * Mathf.Deg2Rad;
            float northMeters = Mathf.Cos(bearing) * meters;
            float eastMeters = Mathf.Sin(bearing) * meters;
            float latitude = latLon.x + northMeters / metersPerDegreeLatitude;
            float longitudeScale = Mathf.Cos(latitude * Mathf.Deg2Rad) * metersPerDegreeLatitude;
            float longitude = latLon.y + eastMeters / Mathf.Max(1f, longitudeScale);
            return new Vector2(latitude, longitude);
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private sealed class AndroidLocationBridge : AndroidJavaProxy, System.IDisposable
        {
            private readonly AndroidJavaObject activity;
            private readonly AndroidJavaObject locationManager;
            private bool disposed;

            public AndroidLocationBridge()
                : base("android.location.LocationListener")
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                locationManager = activity.Call<AndroidJavaObject>("getSystemService", "location");
            }

            public bool HasLocation { get; private set; }
            public Vector2 LatLon { get; private set; }

            public bool Start(float minDistanceMeters, float minTimeSeconds, out string status)
            {
                if (locationManager == null)
                {
                    status = "Android location manager unavailable";
                    return false;
                }

                var started = false;
                started |= TryStartProvider("gps", minDistanceMeters, minTimeSeconds);
                started |= TryStartProvider("network", minDistanceMeters, minTimeSeconds);

                status = started ? "Waiting for Android location" : "Location disabled by user";
                return started;
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;

                try
                {
                    locationManager?.Call("removeUpdates", this);
                }
                catch (AndroidJavaException)
                {
                }

                locationManager?.Dispose();
                activity?.Dispose();
            }

            public void onLocationChanged(AndroidJavaObject location)
            {
                CaptureLocation(location);
            }

            public void onProviderEnabled(string provider)
            {
            }

            public void onProviderDisabled(string provider)
            {
            }

            public void onStatusChanged(string provider, int status, AndroidJavaObject extras)
            {
            }

            private bool TryStartProvider(string provider, float minDistanceMeters, float minTimeSeconds)
            {
                try
                {
                    if (!locationManager.Call<bool>("isProviderEnabled", provider))
                    {
                        return false;
                    }

                    using (var lastKnownLocation = locationManager.Call<AndroidJavaObject>("getLastKnownLocation", provider))
                    {
                        CaptureLocation(lastKnownLocation);
                    }

                    using var looperClass = new AndroidJavaClass("android.os.Looper");
                    using var mainLooper = looperClass.CallStatic<AndroidJavaObject>("getMainLooper");
                    var minTimeMilliseconds = (long)Mathf.RoundToInt(minTimeSeconds * 1000f);
                    locationManager.Call("requestLocationUpdates", provider, minTimeMilliseconds, minDistanceMeters, this, mainLooper);
                    return true;
                }
                catch (AndroidJavaException)
                {
                    return false;
                }
            }

            private void CaptureLocation(AndroidJavaObject location)
            {
                if (location == null)
                {
                    return;
                }

                if (TryCaptureSingleLocation(location))
                {
                    return;
                }

                TryCaptureLocationList(location);
            }

            private bool TryCaptureSingleLocation(AndroidJavaObject location)
            {
                try
                {
                    LatLon = new Vector2(
                        (float)location.Call<double>("getLatitude"),
                        (float)location.Call<double>("getLongitude"));
                    HasLocation = true;
                    return true;
                }
                catch (AndroidJavaException)
                {
                    return false;
                }
            }

            private void TryCaptureLocationList(AndroidJavaObject locations)
            {
                try
                {
                    var count = locations.Call<int>("size");
                    if (count <= 0)
                    {
                        return;
                    }

                    using var latestLocation = locations.Call<AndroidJavaObject>("get", count - 1);
                    TryCaptureSingleLocation(latestLocation);
                }
                catch (AndroidJavaException)
                {
                }
            }
        }
#endif
    }
}
