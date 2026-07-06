using System.Collections;
using UnityEngine;

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
        [SerializeField] private Vector2 simulatedLatLon = new Vector2(51.18623f, 4.22974f);
        [SerializeField] private float simulatedTurnSpeedDegrees = 100f;
        [SerializeField] private float simulatedWalkSpeedMetersPerSecond = 1.4f;
        [SerializeField] private bool keyboardSimulation = true;

        private float smoothedHeading;
        private float headingVelocity;
        private bool deviceStartupAttempted;

        public bool IsReady { get; private set; }
        public bool IsSimulated => ResolveSource() == HeadingSource.Simulated;
        public float Heading => GeoMath.NormalizeDegrees(smoothedHeading);
        public float RawHeading { get; private set; }
        public float HeadingAccuracy { get; private set; } = -1f;
        public Vector2 CurrentLatLon { get; private set; }
        public string Status { get; private set; } = "Starting";

        private void OnEnable()
        {
            CurrentLatLon = simulatedLatLon;
            RawHeading = simulatedHeading;
            smoothedHeading = simulatedHeading;

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
                Input.compass.enabled = false;
                Input.location.Stop();
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

            if (!Input.location.isEnabledByUser)
            {
                IsReady = false;
                Status = "Location disabled by user";
                yield break;
            }

            Input.location.Start(1f, 1f);
            Input.compass.enabled = true;

            var maxWaitSeconds = 12;
            while (Input.location.status == LocationServiceStatus.Initializing && maxWaitSeconds > 0)
            {
                yield return new WaitForSeconds(1f);
                maxWaitSeconds--;
            }

            if (Input.location.status != LocationServiceStatus.Running)
            {
                IsReady = false;
                Status = "Location service unavailable";
                yield break;
            }

            IsReady = true;
            Status = "Device compass ready";
        }

        private void UpdateDeviceHeading()
        {
            if (!IsReady)
            {
                return;
            }

            var data = Input.location.lastData;
            CurrentLatLon = new Vector2(data.latitude, data.longitude);
            HeadingAccuracy = Input.compass.headingAccuracy;

            float trueHeading = Input.compass.trueHeading;
            RawHeading = trueHeading > 0.001f ? trueHeading : Input.compass.magneticHeading;
        }

        private void UpdateSimulation()
        {
            IsReady = true;
            Status = "Editor compass simulation";

            if (keyboardSimulation)
            {
                float turn = 0f;
                if (Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.A))
                {
                    turn -= 1f;
                }

                if (Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.D))
                {
                    turn += 1f;
                }

                if (Mathf.Abs(turn) > 0f)
                {
                    simulatedHeading = GeoMath.NormalizeDegrees(simulatedHeading + turn * simulatedTurnSpeedDegrees * Time.deltaTime);
                }

                float walk = 0f;
                if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W))
                {
                    walk += 1f;
                }

                if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S))
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
    }
}
