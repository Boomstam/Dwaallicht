using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Dwaallicht.Cloud;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Dwaallicht.AR
{
    [AddComponentMenu("Dwaallicht/AR/AR Scanner")]
    public sealed class DwaallichtArScanner : MonoBehaviour
    {
        private static readonly string[] SupportedReferenceImageExtensions = { ".png", ".jpg", ".jpeg" };

        public const string ReferenceImageName = "Dwaallicht QR";
        public const float ReferenceImageWidthMeters = 0.078f;
        public const float ReferenceImageHeightMeters = 0.078f;
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
        private GameObject cubeVisualPrefab;
        [SerializeField]
        private bool simulateInEditor = true;
        [SerializeField]
        private float rotationDegreesPerSecond = 55f;
        [Header("Dynamic Reference Image")]
        [SerializeField]
        private bool useSyncedReferenceImage = true;
        [SerializeField]
        private string syncedReferenceImageFolderName = "DriveSync";
        [SerializeField]
        private string syncedReferenceImageSubfolder = "AR";
        [SerializeField]
        private string syncedReferenceImageFileName = "QRCode.jpg";
        [SerializeField]
        private bool reloadReferenceImageAfterDriveSync = true;

        private GameObject cube;
        private GameObject simulatedImage;
        private bool scanningActive;
        private bool subscribed;
        private ARCameraManager subscribedCameraManager;
        private int cameraFrameCount;
        private int lastCameraTextureCount;
        private float lastCameraFrameRealtime = -1f;
        private Rect lastCameraViewport = new Rect(0f, 0f, 1f, 1f);
        private bool lastCameraViewportVisible = true;
        private GoogleDriveFolderSync driveSync;
        private Coroutine referenceImageRefreshRoutine;
        private Texture2D runtimeReferenceImageTexture;
        private string activeReferenceImageSource = "static";

        public bool IsScanningActive => scanningActive;
        public bool HasVisibleCube => cube != null && cube.activeInHierarchy;
        public string DebugStatus => BuildDebugStatus();

        private void Awake()
        {
            ResolveReferences();
            SetScanningActive(false);
        }

        private void OnEnable()
        {
            ResolveReferences();
            ApplySubsystemState();
            SubscribeToDriveSync();
            if (scanningActive)
            {
                Subscribe();
                SubscribeCameraFrames();
                BeginReferenceImageRefresh();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
            UnsubscribeCameraFrames();
            UnsubscribeFromDriveSync();
            StopReferenceImageRefresh();
        }

        private void OnDestroy()
        {
            if (runtimeReferenceImageTexture != null)
            {
                DestroyUnityObject(runtimeReferenceImageTexture);
                runtimeReferenceImageTexture = null;
            }
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
            Debug.Log($"[DwaallichtArScanner] SetScanningActive({active}) origin={(xrOrigin != null)} session={(arSession != null)} imageManager={(trackedImageManager != null)} arCamera={(arCamera != null)} background={(arCameraBackground != null)}");

            ApplySubsystemState();

            if (arCameraBackground != null)
            {
                arCameraBackground.enabled = false;
            }

            if (appCamera != null)
            {
                appCamera.enabled = !active;
            }

            ApplyCameraState();

            if (active)
            {
                Subscribe();
                SubscribeCameraFrames();
                SubscribeToDriveSync();
                BeginReferenceImageRefresh();
                StartEditorSimulationIfNeeded();
            }
            else
            {
                Unsubscribe();
                UnsubscribeCameraFrames();
                StopReferenceImageRefresh();
                HideCube();
                DestroySimulation();
            }
        }

        public void SetCameraViewport(Rect normalizedViewport, bool visible)
        {
            ResolveReferences();

            normalizedViewport = ClampNormalizedViewport(normalizedViewport);
            visible = visible && IsViewportRenderable(normalizedViewport);

            var changed = visible != lastCameraViewportVisible || (visible && !Approximately(normalizedViewport, lastCameraViewport));
            lastCameraViewportVisible = visible;
            if (visible)
            {
                lastCameraViewport = normalizedViewport;
            }

            ApplyCameraState();

            if (changed)
            {
                Debug.Log($"[DwaallichtArScanner] AR viewport visible={visible} rect={(visible ? normalizedViewport : lastCameraViewport)}");
            }
        }

        public void ResetCameraViewport()
        {
            lastCameraViewport = new Rect(0f, 0f, 1f, 1f);
            lastCameraViewportVisible = true;

            ApplyCameraState();
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
                if (args.removed[i].Value.referenceImage.name == ReferenceImageName)
                {
                    HideCube();
                }
            }
        }

        private void HandleTrackedImage(ARTrackedImage trackedImage)
        {
            if (trackedImage == null || trackedImage.referenceImage.name != ReferenceImageName)
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
            simulatedImage.name = "Simulated Dwaallicht QR Image";
            simulatedImage.transform.SetParent(transform, false);
            simulatedImage.transform.localPosition = new Vector3(0f, 0f, 0.65f);
            simulatedImage.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            simulatedImage.transform.localScale = new Vector3(ReferenceImageWidthMeters, ReferenceImageHeightMeters, 1f);

            var renderer = simulatedImage.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (runtimeReferenceImageTexture != null)
                {
                    renderer.sharedMaterial.mainTexture = runtimeReferenceImageTexture;
                }
            }
        }

        private void DestroySimulation()
        {
            if (simulatedImage == null)
            {
                return;
            }

            DestroyUnityObject(simulatedImage);
            simulatedImage = null;
        }

        private void EnsureCube()
        {
            if (cube != null)
            {
                return;
            }

            cube = cubeVisualPrefab != null
                ? Instantiate(cubeVisualPrefab, transform, false)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Dwaallicht Recognition Cube";
            cube.SetActive(false);
        }

        private void HideCube()
        {
            if (cube != null)
            {
                cube.SetActive(false);
            }
        }

        private static void DestroyUnityObject(UnityEngine.Object target)
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

        private void SubscribeToDriveSync()
        {
            if (!reloadReferenceImageAfterDriveSync || driveSync != null)
            {
                return;
            }

            driveSync = FindFirstObjectByType<GoogleDriveFolderSync>();
            if (driveSync != null)
            {
                driveSync.SyncFinished += HandleDriveSyncFinished;
            }
        }

        private void UnsubscribeFromDriveSync()
        {
            if (driveSync == null)
            {
                return;
            }

            driveSync.SyncFinished -= HandleDriveSyncFinished;
            driveSync = null;
        }

        private void HandleDriveSyncFinished(bool success)
        {
            if (success && scanningActive)
            {
                BeginReferenceImageRefresh();
            }
        }

        private void BeginReferenceImageRefresh()
        {
            if (!useSyncedReferenceImage || !isActiveAndEnabled)
            {
                return;
            }

            StopReferenceImageRefresh();
            referenceImageRefreshRoutine = StartCoroutine(RefreshReferenceImageLibraryRoutine());
        }

        private void StopReferenceImageRefresh()
        {
            if (referenceImageRefreshRoutine == null)
            {
                return;
            }

            StopCoroutine(referenceImageRefreshRoutine);
            referenceImageRefreshRoutine = null;
        }

        private IEnumerator RefreshReferenceImageLibraryRoutine()
        {
            try
            {
                ResolveReferences();

                if (trackedImageManager == null)
                {
                    yield break;
                }

                var syncedImagePath = FindReferenceImagePath();
                if (string.IsNullOrEmpty(syncedImagePath))
                {
                    activeReferenceImageSource = "static";
                    yield break;
                }

                const float waitTimeoutSeconds = 12f;
                var waitDeadline = Time.realtimeSinceStartup + waitTimeoutSeconds;
                while (scanningActive
                       && isActiveAndEnabled
                       && ARSession.state < ARSessionState.Ready
                       && Time.realtimeSinceStartup < waitDeadline)
                {
                    yield return null;
                }

                if (!scanningActive || !isActiveAndEnabled)
                {
                    yield break;
                }

                if (ARSession.state < ARSessionState.Ready)
                {
                    Debug.LogWarning($"[DwaallichtArScanner] AR session did not reach a ready state in time, keeping the static reference image library. State={ARSession.state}");
                    activeReferenceImageSource = "static";
                    yield break;
                }

                RuntimeReferenceImageLibrary runtimeLibrary;
                try
                {
                    runtimeLibrary = trackedImageManager.CreateRuntimeLibrary();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[DwaallichtArScanner] Could not create a runtime reference image library: {ex.Message}");
                    activeReferenceImageSource = "static";
                    yield break;
                }

                if (!(runtimeLibrary is MutableRuntimeReferenceImageLibrary mutableLibrary))
                {
                    Debug.LogWarning("[DwaallichtArScanner] This device does not support mutable runtime reference image libraries, keeping the static reference image library.");
                    activeReferenceImageSource = "static";
                    yield break;
                }

                var texture = TryLoadReferenceImageTexture(syncedImagePath);
                if (texture == null)
                {
                    activeReferenceImageSource = "static";
                    yield break;
                }

                AddReferenceImageJobState addImageJobState;
                try
                {
                    addImageJobState = mutableLibrary.ScheduleAddImageWithValidationJob(texture, ReferenceImageName, ReferenceImageWidthMeters);
                }
                catch (Exception ex)
                {
                    DestroyUnityObject(texture);
                    Debug.LogWarning($"[DwaallichtArScanner] Could not add synced reference image '{syncedImagePath}' to the runtime library: {ex.Message}");
                    activeReferenceImageSource = "static";
                    yield break;
                }

                while (!addImageJobState.jobHandle.IsCompleted)
                {
                    yield return null;
                }

                addImageJobState.jobHandle.Complete();
                if (addImageJobState.status != AddReferenceImageJobStatus.Success)
                {
                    DestroyUnityObject(texture);
                    Debug.LogWarning($"[DwaallichtArScanner] Runtime reference image job failed for '{syncedImagePath}' with status {addImageJobState.status}. Keeping the static reference image library.");
                    activeReferenceImageSource = "static";
                    yield break;
                }

                var previousTexture = runtimeReferenceImageTexture;
                runtimeReferenceImageTexture = texture;
                activeReferenceImageSource = syncedImagePath;
                HideCube();
                trackedImageManager.referenceLibrary = runtimeLibrary;
                Debug.Log($"[DwaallichtArScanner] Using synced reference image from {syncedImagePath}.");

                if (previousTexture != null && previousTexture != runtimeReferenceImageTexture)
                {
                    DestroyUnityObject(previousTexture);
                }

                if (simulatedImage != null)
                {
                    DestroySimulation();
                    StartEditorSimulationIfNeeded();
                }
            }
            finally
            {
                referenceImageRefreshRoutine = null;
            }
        }

        private string FindReferenceImagePath()
        {
            foreach (var candidatePath in GetPreferredReferenceImagePaths())
            {
                if (!string.IsNullOrWhiteSpace(candidatePath) && !candidatePath.Contains("://") && File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            foreach (var rootPath in GetDriveRootCandidatePaths())
            {
                if (string.IsNullOrWhiteSpace(rootPath) || rootPath.Contains("://"))
                {
                    continue;
                }

                var folderPath = Path.Combine(rootPath, syncedReferenceImageSubfolder);
                if (!Directory.Exists(folderPath))
                {
                    continue;
                }

                var files = Directory.GetFiles(folderPath, "*", SearchOption.TopDirectoryOnly);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < files.Length; i++)
                {
                    if (IsSupportedReferenceImagePath(files[i]))
                    {
                        return files[i];
                    }
                }
            }

            return "";
        }

        private IEnumerable<string> GetPreferredReferenceImagePaths()
        {
            if (string.IsNullOrWhiteSpace(syncedReferenceImageFileName))
            {
                yield break;
            }

            foreach (var rootPath in GetDriveRootCandidatePaths())
            {
                if (string.IsNullOrWhiteSpace(rootPath))
                {
                    continue;
                }

                yield return Path.Combine(rootPath, syncedReferenceImageSubfolder, syncedReferenceImageFileName);
            }
        }

        private IEnumerable<string> GetDriveRootCandidatePaths()
        {
            if (driveSync != null)
            {
                yield return driveSync.LocalRootPath;
            }

            yield return Path.Combine(Application.persistentDataPath, syncedReferenceImageFolderName);
            yield return Path.Combine(Application.streamingAssetsPath, syncedReferenceImageFolderName);
        }

        private static bool IsSupportedReferenceImagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var extension = Path.GetExtension(path);
            for (var i = 0; i < SupportedReferenceImageExtensions.Length; i++)
            {
                if (string.Equals(extension, SupportedReferenceImageExtensions[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static Texture2D TryLoadReferenceImageTexture(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Contains("://") || !File.Exists(path))
            {
                return null;
            }

            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false))
                {
                    DestroyUnityObject(texture);
                    Debug.LogWarning($"[DwaallichtArScanner] Could not decode synced reference image from {path}.");
                    return null;
                }

                texture.name = Path.GetFileNameWithoutExtension(path);
                return texture;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DwaallichtArScanner] Could not load synced reference image {path}: {ex.Message}");
                return null;
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

        private void ApplySubsystemState()
        {
            if (arSession != null)
            {
                arSession.enabled = scanningActive;
            }

            if (trackedImageManager != null)
            {
                trackedImageManager.enabled = scanningActive;
            }

            if (arCameraManager != null)
            {
                arCameraManager.enabled = scanningActive;
            }
        }

        private void ApplyCameraState()
        {
            var cameraVisible = scanningActive && lastCameraViewportVisible && IsViewportRenderable(lastCameraViewport);

            if (arCamera != null)
            {
                if (cameraVisible)
                {
                    arCamera.rect = lastCameraViewport;
                }

                arCamera.enabled = cameraVisible;
            }

            if (arCameraBackground != null)
            {
                arCameraBackground.enabled = cameraVisible;
            }
        }

        private static Rect ClampNormalizedViewport(Rect viewport)
        {
            var xMin = Mathf.Clamp01(Mathf.Min(viewport.xMin, viewport.xMax));
            var yMin = Mathf.Clamp01(Mathf.Min(viewport.yMin, viewport.yMax));
            var xMax = Mathf.Clamp01(Mathf.Max(viewport.xMin, viewport.xMax));
            var yMax = Mathf.Clamp01(Mathf.Max(viewport.yMin, viewport.yMax));
            return xMax <= xMin || yMax <= yMin ? Rect.zero : Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static bool IsViewportRenderable(Rect viewport)
        {
            if (viewport.width <= 0f || viewport.height <= 0f || Screen.width <= 0 || Screen.height <= 0)
            {
                return false;
            }

            return viewport.width * Screen.width >= 1f && viewport.height * Screen.height >= 1f;
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
                   $"background {backgroundStatus}\n" +
                   $"reference {activeReferenceImageSource}";
        }

        private static string EnabledStatus(bool enabled)
        {
            return enabled ? "on" : "off";
        }

        private static bool Approximately(Rect a, Rect b)
        {
            const float epsilon = 0.005f;
            return Mathf.Abs(a.x - b.x) < epsilon
                && Mathf.Abs(a.y - b.y) < epsilon
                && Mathf.Abs(a.width - b.width) < epsilon
                && Mathf.Abs(a.height - b.height) < epsilon;
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
