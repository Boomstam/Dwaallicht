using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Dwaallicht.AR
{
    [AddComponentMenu("Dwaallicht/AR/Krefel AR Scanner")]
    public sealed class DwaallichtKrefelArScanner : MonoBehaviour
    {
        public const string KrefelReferenceImageName = "Krefel aankoop";
        public const float KrefelImageWidthMeters = 0.10f;
        public const float KrefelImageHeightMeters = 0.15f;
        public const float CubeSizeMeters = 0.05f;
        public const float CubeCenterHeightMeters = 0.05f;

        [SerializeField]
        private XROrigin xrOrigin;
        [SerializeField]
        private ARSession arSession;
        [SerializeField]
        private ARTrackedImageManager trackedImageManager;
        [SerializeField]
        private Camera arCamera;
        [SerializeField]
        private ARCameraManager arCameraManager;
        [SerializeField]
        private ARCameraBackground arCameraBackground;
        [SerializeField]
        private Camera appCamera;
        [SerializeField]
        private bool simulateInEditor = true;
        [SerializeField]
        private float rotationDegreesPerSecond = 55f;

        private GameObject cube;
        private GameObject simulatedImage;
        private bool scanningActive;
        private bool subscribed;
        private ARCameraManager subscribedCameraManager;
        private int cameraFrameCount;
        private int lastCameraTextureCount;
        private float lastCameraFrameRealtime = -1f;

        public bool IsScanningActive => scanningActive;
        public bool HasVisibleCube => cube != null && cube.activeInHierarchy;
        public string DebugStatus => BuildDebugStatus();

        private void Awake()
        {
            ResolveReferences();
            EnsureArSubsystemsRunning();
            SetScanningActive(false);
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            SubscribeCameraFrames();
            EnsureArSubsystemsRunning();
        }

        private void OnDisable()
        {
            Unsubscribe();
            UnsubscribeCameraFrames();
        }

        private void Update()
        {
            if (cube != null && cube.activeSelf)
            {
                cube.transform.Rotate(Vector3.up, rotationDegreesPerSecond * Time.deltaTime, Space.Self);
            }
        }

        public void SetScanningActive(bool active)
        {
            ResolveReferences();
            scanningActive = active;

            EnsureArSubsystemsRunning();

            if (arSession != null)
            {
                arSession.enabled = true;
            }

            if (trackedImageManager != null)
            {
                trackedImageManager.enabled = active;
            }

            if (arCameraManager != null)
            {
                arCameraManager.enabled = true;
            }

            if (arCameraBackground != null)
            {
                arCameraBackground.enabled = active;
            }

            if (arCamera != null)
            {
                arCamera.enabled = active;
            }

            if (appCamera != null)
            {
                appCamera.enabled = !active;
            }

            if (active)
            {
                Subscribe();
                SubscribeCameraFrames();
                StartEditorSimulationIfNeeded();
            }
            else
            {
                HideCube();
                DestroySimulation();
            }
        }

        internal void ShowCubeAt(Transform anchor)
        {
            if (anchor == null)
            {
                HideCube();
                return;
            }

            EnsureCube();
            cube.transform.SetParent(anchor, false);
            cube.transform.localPosition = Vector3.up * CubeCenterHeightMeters;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = Vector3.one * CubeSizeMeters;
            cube.SetActive(true);
        }

        private void OnTrackedImagesChanged(ARTrackablesChangedEventArgs<ARTrackedImage> args)
        {
            if (!scanningActive)
            {
                return;
            }

            for (var i = 0; i < args.added.Count; i++)
            {
                HandleTrackedImage(args.added[i]);
            }

            for (var i = 0; i < args.updated.Count; i++)
            {
                HandleTrackedImage(args.updated[i]);
            }

            for (var i = 0; i < args.removed.Count; i++)
            {
                if (args.removed[i].Value.referenceImage.name == KrefelReferenceImageName)
                {
                    HideCube();
                }
            }
        }

        private void HandleTrackedImage(ARTrackedImage trackedImage)
        {
            if (trackedImage == null || trackedImage.referenceImage.name != KrefelReferenceImageName)
            {
                return;
            }

            if (trackedImage.trackingState == TrackingState.Tracking || trackedImage.trackingState == TrackingState.Limited)
            {
                ShowCubeAt(trackedImage.transform);
            }
            else
            {
                HideCube();
            }
        }

        private void StartEditorSimulationIfNeeded()
        {
#if UNITY_EDITOR
            if (!simulateInEditor || Application.isMobilePlatform)
            {
                return;
            }

            EnsureSimulationAnchor();
            ShowCubeAt(simulatedImage.transform);
#endif
        }

        private void EnsureSimulationAnchor()
        {
            if (simulatedImage != null)
            {
                return;
            }

            simulatedImage = GameObject.CreatePrimitive(PrimitiveType.Quad);
            simulatedImage.name = "Simulated Krefel aankoop Image";
            simulatedImage.transform.SetParent(transform, false);
            simulatedImage.transform.localPosition = new Vector3(0f, 0f, 0.65f);
            simulatedImage.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            simulatedImage.transform.localScale = new Vector3(KrefelImageWidthMeters, KrefelImageHeightMeters, 1f);

            var renderer = simulatedImage.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateRuntimeMaterial(new Color(0.92f, 0.92f, 0.88f, 1f));
        }

        private void DestroySimulation()
        {
            if (simulatedImage == null)
            {
                return;
            }

            DestroyObject(simulatedImage);
            simulatedImage = null;
        }

        private void EnsureCube()
        {
            if (cube != null)
            {
                return;
            }

            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Krefel Recognition Cube";
            var renderer = cube.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateRuntimeMaterial(new Color(0.13f, 0.7f, 0.95f, 1f));
            cube.SetActive(false);
        }

        private void HideCube()
        {
            if (cube != null)
            {
                cube.SetActive(false);
            }
        }

        private static Material CreateRuntimeMaterial(Color color)
        {
            var shader = GraphicsSettings.defaultRenderPipeline != null
                ? Shader.Find("Universal Render Pipeline/Lit")
                : Shader.Find("Standard");
            var material = new Material(shader);
            material.color = color;
            return material;
        }

        private static void DestroyObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private void Subscribe()
        {
            if (subscribed || trackedImageManager == null)
            {
                return;
            }

            trackedImageManager.trackablesChanged.AddListener(OnTrackedImagesChanged);
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || trackedImageManager == null)
            {
                return;
            }

            trackedImageManager.trackablesChanged.RemoveListener(OnTrackedImagesChanged);
            subscribed = false;
        }

        private void SubscribeCameraFrames()
        {
            if (subscribedCameraManager == arCameraManager)
            {
                return;
            }

            UnsubscribeCameraFrames();

            if (arCameraManager == null)
            {
                return;
            }

            arCameraManager.frameReceived += OnCameraFrameReceived;
            subscribedCameraManager = arCameraManager;
        }

        private void UnsubscribeCameraFrames()
        {
            if (subscribedCameraManager == null)
            {
                return;
            }

            subscribedCameraManager.frameReceived -= OnCameraFrameReceived;
            subscribedCameraManager = null;
        }

        private void OnCameraFrameReceived(ARCameraFrameEventArgs args)
        {
            cameraFrameCount++;
            lastCameraTextureCount = args.textures?.Count ?? 0;
            lastCameraFrameRealtime = Time.realtimeSinceStartup;
        }

        private void EnsureArSubsystemsRunning()
        {
            if (arSession != null)
            {
                arSession.enabled = true;
            }

            if (arCameraManager != null)
            {
                arCameraManager.enabled = true;
            }
        }

        private void ResolveReferences()
        {
            if (xrOrigin == null)
            {
                xrOrigin = FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            }

            if (arSession == null)
            {
                arSession = FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
            }

            if (trackedImageManager == null)
            {
                trackedImageManager = FindFirstObjectByType<ARTrackedImageManager>(FindObjectsInactive.Include);
            }

            if (arCamera == null)
            {
                arCamera = xrOrigin != null ? xrOrigin.Camera : Camera.main;
            }

            if (arCamera != null)
            {
                if (arCameraManager == null)
                {
                    arCameraManager = arCamera.GetComponent<ARCameraManager>();
                }

                if (arCameraBackground == null)
                {
                    arCameraBackground = arCamera.GetComponent<ARCameraBackground>();
                }
            }

            if (appCamera == null)
            {
                var cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (var i = 0; i < cameras.Length; i++)
                {
                    if (cameras[i] != arCamera)
                    {
                        appCamera = cameras[i];
                        break;
                    }
                }
            }
        }

        private string BuildDebugStatus()
        {
            ResolveReferences();

            var sessionState = ARSession.state.ToString();
            var notTrackingReason = ARSession.notTrackingReason.ToString();
            var frameAge = lastCameraFrameRealtime >= 0f
                ? $"{Time.realtimeSinceStartup - lastCameraFrameRealtime:0.0}s"
                : "-";
            var originStatus = xrOrigin != null ? "ok" : "missing";
            var sessionStatus = arSession != null
                ? $"{EnabledStatus(arSession.enabled)}, active {EnabledStatus(arSession.isActiveAndEnabled)}, {SessionSubsystemStatus(arSession)}"
                : "missing";
            var imageStatus = trackedImageManager != null
                ? $"{EnabledStatus(trackedImageManager.enabled)}, active {EnabledStatus(trackedImageManager.isActiveAndEnabled)}"
                : "missing";
            var arCameraStatus = arCamera != null
                ? $"{EnabledStatus(arCamera.enabled)}, active {EnabledStatus(arCamera.isActiveAndEnabled)}"
                : "missing";
            var cameraManagerStatus = arCameraManager != null
                ? $"{EnabledStatus(arCameraManager.enabled)}, active {EnabledStatus(arCameraManager.isActiveAndEnabled)}, permission {EnabledStatus(arCameraManager.permissionGranted)}, mode {arCameraManager.currentRenderingMode}, {CameraSubsystemStatus(arCameraManager)}"
                : "missing";
            var backgroundStatus = arCameraBackground != null
                ? $"{EnabledStatus(arCameraBackground.enabled)}, rendering {EnabledStatus(arCameraBackground.backgroundRenderingEnabled)}, material {EnabledStatus(arCameraBackground.material != null)}"
                : "missing";

            return $"AR scan {(scanningActive ? "active" : "inactive")}  session {sessionState}  reason {notTrackingReason}\n" +
                   $"origin {originStatus}  session component {sessionStatus}  images {imageStatus}\n" +
                   $"camera {arCameraStatus}  manager {cameraManagerStatus}\n" +
                   $"frames {cameraFrameCount}  tex {lastCameraTextureCount}  age {frameAge}\n" +
                   $"background {backgroundStatus}";
        }

        private static string EnabledStatus(bool enabled)
        {
            return enabled ? "on" : "off";
        }

        private static string SessionSubsystemStatus(ARSession session)
        {
            return session.subsystem != null
                ? $"sub on, run {EnabledStatus(session.subsystem.running)}"
                : "sub missing";
        }

        private static string CameraSubsystemStatus(ARCameraManager cameraManager)
        {
            return cameraManager.subsystem != null
                ? $"sub on, run {EnabledStatus(cameraManager.subsystem.running)}"
                : "sub missing";
        }
    }
}
