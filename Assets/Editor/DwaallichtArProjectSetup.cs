using Dwaallicht.AR;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.ARSubsystems;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;

public static class DwaallichtArProjectSetup
{
    private const string MainScenePath = "Assets/Scenes/Main.unity";
    private const string ReferenceImagePath = "Assets/Docs/QRCode.jpg";
    private const string ArFolder = "Assets/AR";
    private const string ReferenceLibraryPath = ArFolder + "/DwaallichtReferenceImageLibrary.asset";
    private static readonly string[] RendererDataPaths =
    {
        "Assets/Settings/Mobile_Renderer.asset",
        "Assets/Settings/PC_Renderer.asset",
    };

    [MenuItem("Dwaallicht/AR/Setup Image Tracking")]
    public static void Setup()
    {
        EnsureFolders();
        ConfigureTextureImporter();
        var library = CreateOrUpdateReferenceImageLibrary();
        ConfigureXrLoaders();
        ConfigureUrpArBackground();
        ConfigureMainScene(library);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Debug.Log("Dwaallicht AR setup: image tracking configured.");
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(ArFolder))
        {
            AssetDatabase.CreateFolder("Assets", "AR");
        }
    }

    private static void ConfigureTextureImporter()
    {
        var importer = AssetImporter.GetAtPath(ReferenceImagePath) as TextureImporter;
        if (importer == null)
        {
            throw new MissingReferenceException("Could not find reference image at " + ReferenceImagePath);
        }

        importer.textureType = TextureImporterType.Default;
        importer.isReadable = true;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    private static XRReferenceImageLibrary CreateOrUpdateReferenceImageLibrary()
    {
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ReferenceImagePath);
        if (texture == null)
        {
            throw new MissingReferenceException("Could not load reference texture at " + ReferenceImagePath);
        }

        var library = AssetDatabase.LoadAssetAtPath<XRReferenceImageLibrary>(ReferenceLibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<XRReferenceImageLibrary>();
            AssetDatabase.CreateAsset(library, ReferenceLibraryPath);
        }

        var imageIndex = -1;
        for (var i = 0; i < library.count; i++)
        {
            if (library[i].name == DwaallichtArScanner.ReferenceImageName)
            {
                imageIndex = i;
                break;
            }
        }

        if (imageIndex < 0)
        {
            library.Add();
            imageIndex = library.count - 1;
        }

        library.SetName(imageIndex, DwaallichtArScanner.ReferenceImageName);
        library.SetTexture(imageIndex, texture, true);
        library.SetSpecifySize(imageIndex, true);
        library.SetSize(imageIndex, new Vector2(
            DwaallichtArScanner.ReferenceImageWidthMeters,
            DwaallichtArScanner.ReferenceImageHeightMeters));

        EditorUtility.SetDirty(library);
        return library;
    }

    private static void ConfigureXrLoaders()
    {
        var perBuildTarget = GetOrCreateXrSettings();
        AssignLoader(perBuildTarget, BuildTargetGroup.Android, "UnityEngine.XR.ARCore.ARCoreLoader");
        AssignLoader(perBuildTarget, BuildTargetGroup.iOS, "UnityEngine.XR.ARKit.ARKitLoader");
        AssignLoader(perBuildTarget, BuildTargetGroup.Standalone, "UnityEngine.XR.Simulation.SimulationLoader");
        EditorUtility.SetDirty(perBuildTarget);
    }

    private static XRGeneralSettingsPerBuildTarget GetOrCreateXrSettings()
    {
        if (EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.k_SettingsKey, out var perBuildTarget)
            && perBuildTarget != null)
        {
            return perBuildTarget;
        }

        perBuildTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
        AssetDatabase.CreateAsset(perBuildTarget, ArFolder + "/XRGeneralSettingsPerBuildTarget.asset");
        EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perBuildTarget, true);
        return perBuildTarget;
    }

    private static void AssignLoader(XRGeneralSettingsPerBuildTarget perBuildTarget, BuildTargetGroup targetGroup, string loaderTypeName)
    {
        if (!perBuildTarget.HasSettingsForBuildTarget(targetGroup))
        {
            perBuildTarget.CreateDefaultSettingsForBuildTarget(targetGroup);
        }

        if (!perBuildTarget.HasManagerSettingsForBuildTarget(targetGroup))
        {
            perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(targetGroup);
        }

        var manager = perBuildTarget.ManagerSettingsForBuildTarget(targetGroup);
        if (manager == null)
        {
            throw new MissingReferenceException("Could not create XR manager settings for " + targetGroup);
        }

        XRPackageMetadataStore.AssignLoader(manager, loaderTypeName, targetGroup);
    }

    private static void ConfigureMainScene(XRReferenceImageLibrary library)
    {
        var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

        var scannerGo = EnsureSceneObject("Dwaallicht AR Scanner", null);
        var scanner = scannerGo.GetComponent<DwaallichtArScanner>();
        if (scanner == null)
        {
            scanner = scannerGo.AddComponent<DwaallichtArScanner>();
        }

        var sessionGo = EnsureSceneObject("AR Session", scannerGo.transform);
        var session = EnsureComponent<ARSession>(sessionGo);
        EnsureComponent<ARInputManager>(sessionGo);

        var originGo = EnsureSceneObject("XR Origin", scannerGo.transform);
        var origin = EnsureComponent<XROrigin>(originGo);
        var trackedImageManager = EnsureComponent<ARTrackedImageManager>(originGo);
        trackedImageManager.referenceLibrary = library;
        trackedImageManager.requestedMaxNumberOfMovingImages = 1;

        var cameraOffsetGo = EnsureSceneObject("Camera Offset", originGo.transform);
        var cameraGo = EnsureSceneObject("AR Camera", cameraOffsetGo.transform);
        var arCamera = EnsureComponent<Camera>(cameraGo);
        EnsureComponent<AudioListener>(cameraGo);
        var cameraManager = EnsureComponent<ARCameraManager>(cameraGo);
        var cameraBackground = EnsureComponent<ARCameraBackground>(cameraGo);
        ConfigureTrackedPoseDriver(cameraGo);

        cameraGo.tag = "MainCamera";
        arCamera.clearFlags = CameraClearFlags.Color;
        arCamera.backgroundColor = Color.black;
        arCamera.nearClipPlane = 0.1f;
        arCamera.farClipPlane = 20f;
        arCamera.enabled = false;

        origin.CameraFloorOffsetObject = cameraOffsetGo;
        origin.Camera = arCamera;

        var appCamera = GameObject.Find("App Camera")?.GetComponent<Camera>();
        AssignScannerReferences(scanner, origin, session, trackedImageManager, arCamera, cameraManager, cameraBackground, appCamera);
        scanner.SetScanningActive(false);
        session.enabled = false;
        cameraManager.enabled = false;
        trackedImageManager.enabled = false;
        cameraBackground.enabled = false;
        arCamera.enabled = false;
        if (appCamera != null)
        {
            appCamera.enabled = true;
            EditorUtility.SetDirty(appCamera);
        }

        EditorUtility.SetDirty(session);
        EditorUtility.SetDirty(cameraManager);
        EditorUtility.SetDirty(trackedImageManager);
        EditorUtility.SetDirty(cameraBackground);
        EditorUtility.SetDirty(arCamera);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static void ConfigureUrpArBackground()
    {
        var rendererDatas = new HashSet<ScriptableRendererData>();

        for (var i = 0; i < RendererDataPaths.Length; i++)
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererDataPaths[i]);
            if (rendererData != null)
            {
                rendererDatas.Add(rendererData);
            }
        }

        var urpAssetGuids = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
        for (var i = 0; i < urpAssetGuids.Length; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(urpAssetGuids[i]);
            var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (pipelineAsset == null)
            {
                continue;
            }

            foreach (var rendererData in pipelineAsset.rendererDataList)
            {
                if (rendererData != null)
                {
                    rendererDatas.Add(rendererData);
                }
            }
        }

        foreach (var rendererData in rendererDatas)
        {
            AddRendererFeatureIfMissing<ARBackgroundRendererFeature>(
                rendererData,
                "AR Background Renderer Feature");
            AddRendererFeatureIfMissing<ARCommandBufferSupportRendererFeature>(
                rendererData,
                "AR Command Buffer Support Renderer Feature");
        }
    }

    private static void AddRendererFeatureIfMissing<T>(ScriptableRendererData rendererData, string featureName)
        where T : ScriptableRendererFeature
    {
        if (rendererData.TryGetRendererFeature<T>(out _))
        {
            return;
        }

        var feature = ScriptableObject.CreateInstance<T>();
        feature.name = featureName;
        AssetDatabase.AddObjectToAsset(feature, rendererData);
        rendererData.rendererFeatures.Add(feature);
        feature.Create();
        rendererData.SetDirty();
        EditorUtility.SetDirty(feature);
        EditorUtility.SetDirty(rendererData);
    }

    private static GameObject EnsureSceneObject(string name, Transform parent)
    {
        var existing = parent == null ? GameObject.Find(name) : parent.Find(name)?.gameObject;
        if (existing != null)
        {
            return existing;
        }

        var go = new GameObject(name);
        if (parent != null)
        {
            go.transform.SetParent(parent, false);
        }

        return go;
    }

    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        var component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }

    private static void ConfigureTrackedPoseDriver(GameObject cameraGo)
    {
        var trackedPoseDriver = EnsureComponent<TrackedPoseDriver>(cameraGo);
        var positionAction = new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
        positionAction.AddBinding("<HandheldARInputDevice>/devicePosition");

        var rotationAction = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
        rotationAction.AddBinding("<HandheldARInputDevice>/deviceRotation");

        trackedPoseDriver.positionInput = new InputActionProperty(positionAction);
        trackedPoseDriver.rotationInput = new InputActionProperty(rotationAction);
    }

    private static void AssignScannerReferences(
        DwaallichtArScanner scanner,
        XROrigin origin,
        ARSession session,
        ARTrackedImageManager imageManager,
        Camera arCamera,
        ARCameraManager cameraManager,
        ARCameraBackground cameraBackground,
        Camera appCamera)
    {
        var serializedScanner = new SerializedObject(scanner);
        serializedScanner.FindProperty("xrOrigin").objectReferenceValue = origin;
        serializedScanner.FindProperty("arSession").objectReferenceValue = session;
        serializedScanner.FindProperty("trackedImageManager").objectReferenceValue = imageManager;
        serializedScanner.FindProperty("arCamera").objectReferenceValue = arCamera;
        serializedScanner.FindProperty("arCameraManager").objectReferenceValue = cameraManager;
        serializedScanner.FindProperty("arCameraBackground").objectReferenceValue = cameraBackground;
        serializedScanner.FindProperty("appCamera").objectReferenceValue = appCamera;
        serializedScanner.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(scanner);
    }
}
