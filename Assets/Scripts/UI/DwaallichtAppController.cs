using System;
using System.Collections.Generic;
using System.IO;
using Dwaallicht.AR;
using Dwaallicht.Cloud;
using Dwaallicht.Navigation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Networking;
using UnityEngine.Serialization;
using UnityEngine.UI;

[AddComponentMenu("Dwaallicht/App Controller")]
public sealed class DwaallichtAppController : MonoBehaviour
{
    private static readonly Color AppBackground = Rgb(229, 229, 226);
    private static readonly Color Ink = Rgb(32, 30, 31);
    private static readonly Color Paper = Rgb(248, 248, 246);
    private static readonly Color Green = Rgb(54, 184, 61);
    private static readonly Color Blue = Rgb(86, 177, 232);
    private static readonly Color Red = Rgb(222, 22, 32);
    private static readonly Color Gold = Rgb(187, 137, 20);
    private static readonly Color Yellow = Rgb(255, 203, 34);
    private static readonly Color TranslucentPaper = new Color(248f / 255f, 248f / 255f, 246f / 255f, 0.88f);
    private static readonly Color TranslucentInk = new Color(32f / 255f, 30f / 255f, 31f / 255f, 0.76f);
    private const string MapCalibrationPrefsPrefix = "Dwaallicht.MapCalibration.";
    private const float MapScaleBarPixels = 96f;
    private const float MapOffsetNudgePixels = 8f;
    private const float MapScaleStep = 1.02f;
    private const float MapScrollZoomStep = 1.04f;
    private const float MapViewScrollZoomStep = 1.12f;
    private const float MapRotationStepDegrees = 0.1f;
    private const float MapScrollRotationStepDegrees = 0.05f;
    private const float MapMinScaleBarMeters = 10f;
    private const float MapMaxScaleBarMeters = 500f;
    private const float MapCalibrationHandleRadius = 34f;
    private const string PoiTextFileStem = "Text";
    private const string PoiArFolderName = "AR";
    private static readonly Vector2[] DefaultMapCalibrationLatLons =
    {
        new Vector2(51.094750f, 4.347785f),
        new Vector2(51.107604f, 4.369738f),
        new Vector2(51.086803f, 4.360948f),
    };
    private static readonly string[] DefaultMapCalibrationLabels =
    {
        "Museum",
        "Banaan",
        "Rond",
    };

    private readonly string[] tabIds = { "K", "M", "L", "S" };
    private readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();
    private readonly List<PoiAudioPlayer> poiAudioPlayers = new List<PoiAudioPlayer>();

    [SerializeField, Range(0, 3)]
    private int activeTab;
    [SerializeField]
    private bool showDeviceDebug = true;
    [Header("Map Underlay")]
    [SerializeField]
    private Texture2D mapUnderlayTexture;
    [SerializeField]
    private string mapImageFolderName = "DriveSync";
    [SerializeField]
    private string mapImageFileName = "Map.png";
    [SerializeField]
    private bool reloadMapAfterDriveSync = true;
    [SerializeField]
    private Vector2 mapCenterLatLon = new Vector2(51.096465f, 4.344778f);
    [SerializeField]
    private Vector2 mapUnderlayOffsetPixels = new Vector2(58.10565f, 26.5378056f);
    [SerializeField, Min(0.01f)]
    private float mapUnderlayMetersPerPixel = 3.92135358f;
    [SerializeField, Min(0.05f)]
    private float mapZoomMultiplier = 1.10365629f;
    [SerializeField]
    private float mapUnderlayRotationDegrees = 1.000061f;
    [SerializeField]
    private bool useThreePointMapCalibration = true;
    [SerializeField]
    private Vector2[] mapCalibrationTargetPixels =
    {
        new Vector2(40.7300949f, -56.62777f),
        new Vector2(488.487732f, 346.362976f),
        new Vector2(319.271454f, -301.0084f),
    };
    [SerializeField]
    private bool showMapPoiPins = true;
    [SerializeField, FormerlySerializedAs("showMapCalibrationControls")]
    private bool mapCalibrationMode;

    private RectTransform contentRoot;
    private RectTransform tabRoot;
    private RectTransform compassRose;
    private RectTransform mapFacingArrow;
    private RectTransform mapViewport;
    private RectTransform mapContentRoot;
    private RectTransform mapUnderlayRect;
    private Image appBackgroundImage;
    private Text debugText;
    private Text navigationText;
    private Text headingText;
    private Text mapCalibrationText;
    private Text mapScaleBarText;
    private DwaallichtArScanner arScanner;
    private CompassHeadingProvider headingProvider;
    private PoiManager poiManager;
    private GoogleDriveFolderSync driveSync;
    private Font font;
    private Texture2D syncedMapUnderlayTexture;
    private Vector2 lastMapDragLocalPosition;
    private Vector2 lastMapViewDragLocalPosition;
    private Vector2 mapViewOffsetPixels;
    private float mapViewZoomMultiplier = 1f;
    private float lastMapPinchDistance;
    private bool isDraggingMapUnderlay;
    private bool isDraggingMapView;
    private bool isPinchingMapView;
    private int activeMapCalibrationHandle = -1;
    private RectTransform[] mapCalibrationHandleRects;

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        SubscribeToDriveSync();
        TryLoadSyncedMapUnderlay();
        Build();
    }

    private void OnDisable()
    {
        CleanupPoiAudioPlayers();

        if (driveSync != null)
        {
            driveSync.SyncFinished -= HandleDriveSyncFinished;
            driveSync = null;
        }

        if (syncedMapUnderlayTexture != null)
        {
            Destroy(syncedMapUnderlayTexture);
            syncedMapUnderlayTexture = null;
        }
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        EnsureHeadingProvider();
        EnsurePoiManager();
        SubscribeToDriveSync();

        RefreshDynamicText();
        HandleMapCalibrationHandleDrag();
        HandleMapUnderlayDrag();
        HandleMapScrollCalibration();
        HandleMapUseGestures();

        if (headingProvider == null || !headingProvider.IsReady)
        {
            return;
        }

        var heading = headingProvider.Heading;
        if (compassRose != null)
        {
            compassRose.localEulerAngles = new Vector3(0f, 0f, heading);
        }

        if (mapFacingArrow != null)
        {
            mapFacingArrow.localEulerAngles = new Vector3(0f, 0f, -heading);
        }

        if (headingText != null)
        {
            headingText.text = $"{heading:000} graden";
        }

    }

    private void Build()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
        {
            font = Font.CreateDynamicFontFromOSFont("Arial", 18);
        }

        ClearChildren(transform);
        EnsureEventSystem();

        var canvasGo = new GameObject("DwaallichtAppCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);

        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(390f, 844f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        var root = canvasGo.GetComponent<RectTransform>();
        Stretch(root, Vector2.zero, Vector2.zero);
        appBackgroundImage = AddImage(root, "Background", AppBackground, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).GetComponent<Image>();

        contentRoot = AddRect(root, "ScreenContent", new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 104f), new Vector2(0f, 0f));
        tabRoot = AddRect(root, "PermanentTabs", new Vector2(0f, 0f), new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 104f));

        LoadMapCalibration();
        EnsureMapCalibrationTargets();
#if !UNITY_EDITOR
        mapCalibrationMode = false;
#endif
        BuildTabs();
        ShowTab(activeTab);
    }

    private void ShowTab(int index)
    {
        activeTab = Mathf.Clamp(index, 0, tabIds.Length - 1);
        if (contentRoot == null)
        {
            return;
        }

        CleanupPoiAudioPlayers();
        ClearChildren(contentRoot);
        compassRose = null;
        mapFacingArrow = null;
        debugText = null;
        navigationText = null;
        headingText = null;
        mapViewport = null;
        mapContentRoot = null;
        mapUnderlayRect = null;
        mapCalibrationText = null;
        mapScaleBarText = null;
        mapCalibrationHandleRects = null;
        isDraggingMapUnderlay = false;
        isDraggingMapView = false;
        isPinchingMapView = false;
        activeMapCalibrationHandle = -1;

        switch (tabIds[activeTab])
        {
            case "K":
                BuildCompassScreen(contentRoot);
                break;
            case "M":
                BuildMapScreen(contentRoot);
                break;
            case "L":
                BuildLegendScreen(contentRoot);
                break;
            default:
                BuildScopeScreen(contentRoot);
                break;
        }

        var scanActive = tabIds[activeTab] == "S" && SelectedPoiHasArMap();
        if (appBackgroundImage != null)
        {
            appBackgroundImage.color = scanActive ? Color.clear : AppBackground;
        }

        EnsureArScanner();
        if (arScanner != null)
        {
            arScanner.SetScanningActive(scanActive);
        }

        foreach (var pair in buttons)
        {
            var active = pair.Key == tabIds[activeTab];
            var circle = pair.Value.targetGraphic as AppCircleGraphic;
            if (circle != null)
            {
                circle.color = active ? Ink : Paper;
                circle.SetVerticesDirty();
            }

            var label = pair.Value.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.color = active ? Paper : Ink;
            }
        }

        RefreshDynamicText();
    }

    private void BuildTabs()
    {
        buttons.Clear();
        ClearChildren(tabRoot);

        AddImage(tabRoot, "TabBarBackground", AppBackground, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        for (var i = 0; i < tabIds.Length; i++)
        {
            var index = i;
            var x = Mathf.Lerp(58f, 332f, i / 3f);
            var holder = AddRect(tabRoot, "Tab_" + tabIds[i], new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x - 32f, 20f), new Vector2(64f, 64f));
            var circle = holder.gameObject.AddComponent<AppCircleGraphic>();
            circle.color = Paper;
            circle.fillCenter = true;
            circle.strokeColor = Ink;
            circle.strokeWidth = 2f;

            var button = holder.gameObject.AddComponent<Button>();
            button.targetGraphic = circle;
            button.transition = Selectable.Transition.ColorTint;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => ShowTab(index));

            AddText(holder, tabIds[i], 32, FontStyle.Normal, Ink, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            buttons.Add(tabIds[i], button);
        }
    }

    private void BuildCompassScreen(RectTransform parent)
    {
        AddText(parent, "Eigenwijzer", 26, FontStyle.Normal, Ink, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(28f, -90f), new Vector2(-28f, -34f));

        var compass = AddRect(parent, "CompassGraphic", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -18f), new Vector2(300f, 300f));
        AddCircle(compass, "OuterRing", Ink, Vector2.one * 286f, Vector2.zero, true, Ink, 0f, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        AddCircle(compass, "InnerPaper", Paper, Vector2.one * 266f, Vector2.zero, true, Paper, 0f, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));

        compassRose = AddRect(compass, "RotatingCompassRose", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260f, 260f));
        for (var i = 0; i < 24; i++)
        {
            var major = i % 6 == 0;
            var angle = i * 15f;
            var radians = angle * Mathf.Deg2Rad;
            var radius = major ? 110f : 114f;
            var tickPosition = new Vector2(Mathf.Sin(radians) * radius, Mathf.Cos(radians) * radius);
            var tick = AddImage(compassRose, "Tick_" + i, Ink, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), tickPosition, new Vector2(4f, major ? 26f : 14f));
            tick.localEulerAngles = new Vector3(0f, 0f, -angle);
        }

        AddImage(compassRose, "NorthNeedle", Green, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 48f), new Vector2(12f, 96f));
        AddImage(compassRose, "SouthNeedle", Red, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -36f), new Vector2(10f, 72f));

        AddText(compassRose, "N", 28, FontStyle.Bold, Ink, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-24f, 78f), new Vector2(48f, 48f));
        AddText(compassRose, "O", 24, FontStyle.Bold, Ink, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(80f, -24f), new Vector2(48f, 48f));
        AddText(compassRose, "Z", 24, FontStyle.Bold, Ink, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-24f, -126f), new Vector2(48f, 48f));
        AddText(compassRose, "W", 24, FontStyle.Bold, Ink, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-128f, -24f), new Vector2(48f, 48f));

        AddCircle(compass, "GoldCenter", Gold, Vector2.one * 56f, Vector2.zero, true, Gold, 0f, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        headingText = AddText(parent, "000 graden", 20, FontStyle.Bold, Ink, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 84f), new Vector2(0f, 124f));
        AddText(parent, "34 m", 28, FontStyle.Bold, Gold, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 20f), new Vector2(0f, 60f));
        debugText = AddText(parent, "", 13, FontStyle.Normal, Ink, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(28f, 128f), new Vector2(-28f, 210f));
    }

    private void BuildMapScreen(RectTransform parent)
    {
        EnsurePoiManager();
        EnsureHeadingProvider();
        var map = AddRect(parent, "MapGraphic", Vector2.zero, Vector2.one, new Vector2(24f, 24f), new Vector2(-24f, -20f));
        AddImage(map, "MapFill", AppBackground, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        mapViewport = AddRect(map, "MapViewport", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var mask = mapViewport.gameObject.AddComponent<RectMask2D>();
        mask.padding = Vector4.zero;
        mapContentRoot = AddRect(mapViewport, "MapContent", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        ApplyMapViewTransform();
        AddMapUnderlay(mapContentRoot);

        var selectedPoi = poiManager != null ? poiManager.SelectedPoi : null;
        if (selectedPoi != null)
        {
            var selectedPosition = MapLatLonToAnchoredPosition(selectedPoi.LatLon);
            AddPolyline(mapContentRoot, "RouteToSelectedPoi", Ink, 4f, new[]
            {
                ToMapNormalized(MapLatLonToAnchoredPosition(headingProvider.CurrentLatLon), mapContentRoot.rect.size),
                ToMapNormalized(selectedPosition, mapContentRoot.rect.size),
            });
        }

        if (showMapPoiPins)
        {
            AddMapPois(mapContentRoot);
        }

        if (selectedPoi != null)
        {
            AddSelectedPoiCard(mapContentRoot, selectedPoi, MapLatLonToAnchoredPosition(selectedPoi.LatLon));
        }

        if (mapCalibrationMode)
        {
            AddMapCalibrationHandles(mapContentRoot);
        }

        var phonePosition = MapLatLonToAnchoredPosition(headingProvider.CurrentLatLon);
        AddCircle(mapContentRoot, "PhonePosition", Paper, Vector2.one * 42f, phonePosition, true, Ink, 3f, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        mapFacingArrow = AddArrow(mapContentRoot, "PhoneFacingDirection", Ink, phonePosition + new Vector2(4f, 28f), new Vector2(34f, 88f), 0f);
        AddScaleBar(map);
        AddMapModeControls(map);
        navigationText = AddText(map, "", 15, FontStyle.Bold, Ink, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -600f));
        if (showDeviceDebug)
        {
            debugText = AddText(map, "", 12, FontStyle.Normal, Ink, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, new Vector2(10f, 54f), new Vector2(-10f, -548f));
        }

        if (mapCalibrationMode)
        {
            AddMapCalibrationControls(map);
        }
    }

    private void BuildLegendScreen(RectTransform parent)
    {
        AddTrophy(parent, new Vector2(0f, -76f));
        AddPin(parent, Blue, new Vector2(78f, -196f), 30f, new Vector2(0f, 1f), new Vector2(0f, 1f));
        AddText(parent, "storyline 1\n2/10 completed", 24, FontStyle.Normal, Ink, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(118f, -246f), new Vector2(-42f, -170f));
        AddPin(parent, Yellow, new Vector2(78f, -314f), 30f, new Vector2(0f, 1f), new Vector2(0f, 1f));
        AddText(parent, "storyline 2\n0/10 completed", 24, FontStyle.Normal, Ink, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(118f, -364f), new Vector2(-42f, -288f));
        AddPin(parent, Red, new Vector2(78f, -442f), 30f, new Vector2(0f, 1f), new Vector2(0f, 1f));
        AddText(parent, "live event", 24, FontStyle.Normal, Ink, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(118f, -480f), new Vector2(-42f, -428f));
    }

    private void BuildScopeScreen(RectTransform parent)
    {
        EnsurePoiManager();
        var selectedPoi = poiManager != null ? poiManager.SelectedPoi : null;
        if (selectedPoi == null)
        {
            AddText(parent, "Geen POI geselecteerd", 24, FontStyle.Bold, Ink, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, new Vector2(28f, 0f), new Vector2(-28f, 0f));
            return;
        }

        var hasFolder = TryGetPoiFolder(selectedPoi, out var poiFolder);
        var hasArMap = hasFolder && HasArMap(poiFolder);
        if (hasArMap)
        {
            AddCircle(parent, "ScopeOuter", Color.clear, new Vector2(292f, 430f), new Vector2(0f, -58f), false, Ink, 4f, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            AddCircle(parent, "ScopeInner", Color.clear, new Vector2(260f, 386f), new Vector2(0f, -80f), false, Paper, 2f, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            AddPolyline(parent, "ScanReticleHorizontal", Paper, 3f, new[]
            {
                new Vector2(0.32f, 0.50f),
                new Vector2(0.68f, 0.50f),
            });
            AddPolyline(parent, "ScanReticleVertical", Paper, 3f, new[]
            {
                new Vector2(0.50f, 0.34f),
                new Vector2(0.50f, 0.66f),
            });
        }

        var viewport = AddRect(parent, "PoiDetailScrollViewport", Vector2.zero, Vector2.one, new Vector2(24f, 24f), new Vector2(-24f, -20f));
        var viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = Color.clear;
        viewportImage.raycastTarget = true;
        viewport.gameObject.AddComponent<RectMask2D>();
        var scrollRect = viewport.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var content = AddRect(viewport, "PoiDetailContent", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(0f, 1f));
        content.pivot = new Vector2(0.5f, 1f);
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 20);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = content;
        scrollRect.viewport = viewport;

        AddDetailLabel(content, selectedPoi.title, 28, FontStyle.Bold, hasArMap ? Paper : Ink, 52f);
        if (!hasFolder)
        {
            return;
        }

        if (hasArMap)
        {
            AddDetailLabel(content, "Scan", 18, FontStyle.Bold, Paper, 34f);
        }

        foreach (var audioPath in GetPoiAudioPaths(poiFolder))
        {
            AddPoiAudioControl(content, audioPath, hasArMap);
        }

        if (TryReadPoiText(poiFolder, out var text))
        {
            AddDetailLabel(content, text, 18, FontStyle.Normal, hasArMap ? Paper : Ink, Mathf.Clamp(MeasureTextHeight(text, 18, 318f), 80f, 520f));
        }

        if (showDeviceDebug)
        {
            debugText = AddText(parent, "", 12, FontStyle.Normal, hasArMap ? Paper : Ink, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, new Vector2(28f, 24f), new Vector2(-28f, -520f));
        }
    }

    private void AddTreeCluster(RectTransform parent)
    {
        AddTree(parent, new Vector2(48f, -116f), 40f);
        AddTree(parent, new Vector2(86f, -112f), 44f);
        AddTree(parent, new Vector2(66f, -154f), 42f);
        AddTree(parent, new Vector2(110f, -148f), 40f);
        AddTree(parent, new Vector2(26f, -146f), 36f);
    }

    private void AddFactory(RectTransform parent)
    {
        var factory = AddRect(parent, "Factory", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-134f, -260f), new Vector2(86f, 96f));
        AddPolyline(factory, "FactoryOutline", Ink, 3f, new[]
        {
            new Vector2(0.04f, 0.04f),
            new Vector2(0.04f, 0.92f),
            new Vector2(0.23f, 0.92f),
            new Vector2(0.25f, 0.28f),
            new Vector2(0.48f, 0.54f),
            new Vector2(0.48f, 0.28f),
            new Vector2(0.70f, 0.54f),
            new Vector2(0.70f, 0.28f),
            new Vector2(0.95f, 0.56f),
            new Vector2(0.95f, 0.04f),
            new Vector2(0.04f, 0.04f),
        });
    }

    private void AddTree(RectTransform parent, Vector2 anchoredPosition, float size)
    {
        var tree = AddRect(parent, "Tree", new Vector2(0f, 1f), new Vector2(0f, 1f), anchoredPosition, Vector2.one * size);
        AddPolyline(tree, "TreeStem", Ink, 2f, new[] { new Vector2(0.5f, 0f), new Vector2(0.5f, 0.44f) });
        AddPolyline(tree, "TreeTop", Ink, 2f, new[]
        {
            new Vector2(0.17f, 0.58f),
            new Vector2(0.38f, 0.36f),
            new Vector2(0.49f, 0.68f),
            new Vector2(0.62f, 0.36f),
            new Vector2(0.83f, 0.58f),
            new Vector2(0.64f, 0.88f),
            new Vector2(0.51f, 0.62f),
            new Vector2(0.38f, 0.88f),
            new Vector2(0.17f, 0.58f),
        });
    }

    private void AddTrophy(RectTransform parent, Vector2 anchoredPosition)
    {
        var trophy = AddRect(parent, "StoryTrophy", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), anchoredPosition, new Vector2(68f, 92f));
        AddPolyline(trophy, "Cup", Ink, 3f, new[]
        {
            new Vector2(0.14f, 0.92f),
            new Vector2(0.86f, 0.92f),
            new Vector2(0.78f, 0.52f),
            new Vector2(0.64f, 0.38f),
            new Vector2(0.55f, 0.34f),
            new Vector2(0.55f, 0.12f),
            new Vector2(0.74f, 0.08f),
            new Vector2(0.26f, 0.08f),
            new Vector2(0.45f, 0.12f),
            new Vector2(0.45f, 0.34f),
            new Vector2(0.36f, 0.38f),
            new Vector2(0.22f, 0.52f),
            new Vector2(0.14f, 0.92f),
        });
    }

    private RectTransform AddPin(RectTransform parent, Color color, Vector2 anchoredPosition, float size)
    {
        return AddPin(parent, color, anchoredPosition, size, new Vector2(0f, 1f), new Vector2(0f, 1f));
    }

    private RectTransform AddPin(RectTransform parent, Color color, Vector2 anchoredPosition, float size, Vector2 anchorMin, Vector2 anchorMax)
    {
        var pin = AddRect(parent, "Pin", anchorMin, anchorMax, anchoredPosition, new Vector2(size, size * 1.35f));
        var border = pin.gameObject.AddComponent<AppPinGraphic>();
        border.color = Ink;

        var fill = AddRect(pin, "PinFill", Vector2.zero, Vector2.one, new Vector2(size * 0.09f, size * 0.12f), new Vector2(-size * 0.18f, -size * 0.22f));
        var fillGraphic = fill.gameObject.AddComponent<AppPinGraphic>();
        fillGraphic.color = color;
        return pin;
    }

    private RectTransform AddArrow(RectTransform parent, string name, Color color, Vector2 anchoredPosition, Vector2 size, float rotation)
    {
        var arrow = AddRect(parent, name, Vector2.zero, Vector2.zero, anchoredPosition, size);
        var graphic = arrow.gameObject.AddComponent<AppArrowGraphic>();
        graphic.color = color;
        arrow.localEulerAngles = new Vector3(0f, 0f, rotation);
        return arrow;
    }

    private RectTransform AddCircle(RectTransform parent, string name, Color color, Vector2 size, Vector2 anchoredPosition, bool fillCenter, Color strokeColor, float strokeWidth)
    {
        return AddCircle(parent, name, color, size, anchoredPosition, fillCenter, strokeColor, strokeWidth, Vector2.zero, Vector2.zero);
    }

    private RectTransform AddCircle(RectTransform parent, string name, Color color, Vector2 size, Vector2 anchoredPosition, bool fillCenter, Color strokeColor, float strokeWidth, Vector2 anchorMin, Vector2 anchorMax)
    {
        var rect = AddRect(parent, name, anchorMin, anchorMax, anchoredPosition, size);
        var circle = rect.gameObject.AddComponent<AppCircleGraphic>();
        circle.color = color;
        circle.fillCenter = fillCenter;
        circle.strokeColor = strokeColor;
        circle.strokeWidth = strokeWidth;
        return rect;
    }

    private RectTransform AddPolyline(RectTransform parent, string name, Color color, float thickness, Vector2[] points)
    {
        var rect = AddRect(parent, name, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var line = rect.gameObject.AddComponent<AppPolylineGraphic>();
        line.color = color;
        line.thickness = thickness;
        line.SetPoints(points);
        return rect;
    }

    private Text AddText(RectTransform parent, string value, int size, FontStyle style, Color color, TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var rect = AddRect(parent, "Text_" + value.Replace("\n", "_"), anchorMin, anchorMax, offsetMin, offsetMax);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private Button AddCommandButton(RectTransform parent, string label, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, UnityEngine.Events.UnityAction action)
    {
        var rect = AddImage(parent, "Button_" + label, Paper, anchorMin, anchorMax, offsetMin, offsetMax);
        rect.GetComponent<Image>().raycastTarget = true;
        var outline = rect.gameObject.AddComponent<Outline>();
        outline.effectColor = Ink;
        outline.effectDistance = new Vector2(2f, -2f);

        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        button.onClick.AddListener(action);

        AddText(rect, label, 15, FontStyle.Bold, Ink, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return button;
    }

    private RectTransform AddImage(RectTransform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var rect = AddRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    private RectTransform AddRawImage(RectTransform parent, string name, Texture texture, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var rect = AddRect(parent, name, anchorMin, anchorMax, offsetMin, offsetMax);
        var image = rect.gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.color = Color.white;
        image.raycastTarget = false;
        return rect;
    }

    private RectTransform AddRect(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;

        if (anchorMin == anchorMax)
        {
            rect.pivot = anchorMin;
            rect.anchoredPosition = offsetMin;
            rect.sizeDelta = offsetMax;
        }
        else
        {
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        return rect;
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>() != null)
        {
            return;
        }

        var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.Undo.RegisterCreatedObjectUndo(eventSystem, "Create EventSystem");
        }
#endif
    }

    private static void ClearChildren(Transform parent)
    {
        for (var i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i).gameObject;
            if (Application.isPlaying)
            {
                child.SetActive(false);
                Destroy(child);
            }
            else
            {
                DestroyImmediate(child);
            }
        }
    }

    private static Color Rgb(int r, int g, int b)
    {
        return new Color(r / 255f, g / 255f, b / 255f, 1f);
    }

    private void EnsureHeadingProvider()
    {
        if (headingProvider != null)
        {
            return;
        }

        headingProvider = FindFirstObjectByType<CompassHeadingProvider>();
        if (headingProvider == null)
        {
            var provider = new GameObject("Compass Heading Provider");
            headingProvider = provider.AddComponent<CompassHeadingProvider>();
        }
    }

    private void EnsurePoiManager()
    {
        if (poiManager != null)
        {
            return;
        }

        poiManager = FindFirstObjectByType<PoiManager>();
        if (poiManager == null)
        {
            var provider = new GameObject("POI Manager");
            poiManager = provider.AddComponent<PoiManager>();
        }

    }

    private void EnsureArScanner()
    {
        if (arScanner != null)
        {
            return;
        }

        arScanner = FindFirstObjectByType<DwaallichtArScanner>(FindObjectsInactive.Include);
    }

    private void SubscribeToDriveSync()
    {
        if (!reloadMapAfterDriveSync || driveSync != null)
        {
            return;
        }

        driveSync = FindFirstObjectByType<GoogleDriveFolderSync>();
        if (driveSync != null)
        {
            driveSync.SyncFinished += HandleDriveSyncFinished;
        }
    }

    private void HandleDriveSyncFinished(bool success)
    {
        if (!success || !TryLoadSyncedMapUnderlay())
        {
            return;
        }

        ApplyMapUnderlayTexture();
    }

    private void RefreshDynamicText()
    {
        UpdatePoiAudioPlayers();

        if (debugText != null)
        {
            debugText.text = BuildDebugText();
        }

        if (navigationText != null)
        {
            navigationText.text = BuildNavigationText();
        }
    }

    private void AddMapUnderlay(RectTransform parent)
    {
        var texture = GetActiveMapUnderlayTexture();
        if (texture == null)
        {
            AddText(parent, "Map texture missing", 18, FontStyle.Bold, Ink, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return;
        }

        mapUnderlayRect = AddRawImage(parent, "MapUnderlay", texture, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), mapUnderlayOffsetPixels, GetMapUnderlaySize());
        mapUnderlayRect.localEulerAngles = new Vector3(0f, 0f, mapUnderlayRotationDegrees);
        mapUnderlayRect.SetAsFirstSibling();
    }

    private void AddScaleBar(RectTransform parent)
    {
        var holder = AddImage(parent, "ScaleBarPanel", TranslucentPaper, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(12f, 12f), new Vector2(132f, 36f));
        AddImage(holder, "ScaleBarTrack", TranslucentInk, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 10f), new Vector2(MapScaleBarPixels, 4f));
        AddImage(holder, "ScaleBarFill", Ink, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 10f), new Vector2(MapScaleBarPixels, 4f));
        mapScaleBarText = AddText(holder, $"{GetScaleBarMeters():0} m", 11, FontStyle.Bold, Ink, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(14f, 14f), new Vector2(-8f, -2f));
    }

    private void AddMapModeControls(RectTransform parent)
    {
#if !UNITY_EDITOR
        mapCalibrationMode = false;
        var buildPanel = AddImage(parent, "MapModePanel", TranslucentPaper, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -48f), new Vector2(96f, 36f));
        AddCommandButton(buildPanel, showMapPoiPins ? "Pins on" : "Pins off", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(8f, 6f), new Vector2(80f, 24f), ToggleMapPoiPins);
#else
        var panel = AddImage(parent, "MapModePanel", TranslucentPaper, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(10f, -48f), new Vector2(264f, 36f));
        AddCommandButton(panel, mapCalibrationMode ? "Use" : "Use on", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(8f, 6f), new Vector2(74f, 24f), () => SetMapCalibrationMode(false));
        AddCommandButton(panel, mapCalibrationMode ? "Cal on" : "Cal", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(88f, 6f), new Vector2(82f, 24f), () => SetMapCalibrationMode(true));
        AddCommandButton(panel, showMapPoiPins ? "Pins on" : "Pins off", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(176f, 6f), new Vector2(80f, 24f), ToggleMapPoiPins);
#endif
    }

    private void AddMapCalibrationControls(RectTransform parent)
    {
        var panel = AddImage(parent, "MapCalibrationPanel", TranslucentPaper, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-168f, -334f), new Vector2(156f, 288f));
        mapCalibrationText = AddText(panel, BuildMapCalibrationText(), 10, FontStyle.Normal, Ink, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(8f, 112f), new Vector2(-8f, -8f));

        AddCommandButton(panel, "10m", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -104f), new Vector2(42f, 24f), () => SetMapScaleBarMeters(10f));
        AddCommandButton(panel, "50m", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(56f, -104f), new Vector2(42f, 24f), () => SetMapScaleBarMeters(50f));
        AddCommandButton(panel, "500m", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(104f, -104f), new Vector2(44f, 24f), () => SetMapScaleBarMeters(500f));

        AddCommandButton(panel, "-", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -134f), new Vector2(32f, 24f), () => ScaleMapUnderlay(1f / MapScaleStep));
        AddCommandButton(panel, "+", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(44f, -134f), new Vector2(32f, 24f), () => ScaleMapUnderlay(MapScaleStep));
        AddCommandButton(panel, "Save", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(82f, -134f), new Vector2(66f, 24f), SaveMapCalibration);

        AddCommandButton(panel, "Rot -", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -164f), new Vector2(66f, 24f), () => RotateMapUnderlay(-MapRotationStepDegrees));
        AddCommandButton(panel, "Rot +", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(82f, -164f), new Vector2(66f, 24f), () => RotateMapUnderlay(MapRotationStepDegrees));
        AddCommandButton(panel, useThreePointMapCalibration ? "Fit3 on" : "Fit3 off", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -194f), new Vector2(66f, 24f), ToggleThreePointMapCalibration);
        AddCommandButton(panel, "Reset3", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(82f, -194f), new Vector2(66f, 24f), ResetThreePointMapCalibration);
        AddCommandButton(panel, "All -", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -224f), new Vector2(66f, 24f), () => SetMapZoomMultiplier(mapZoomMultiplier / MapScaleStep));
        AddCommandButton(panel, "All +", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(82f, -224f), new Vector2(66f, 24f), () => SetMapZoomMultiplier(mapZoomMultiplier * MapScaleStep));

        AddCommandButton(panel, "Up", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(62f, -74f), new Vector2(34f, 24f), () => NudgeMapUnderlay(new Vector2(0f, MapOffsetNudgePixels)));
        AddCommandButton(panel, "Left", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -46f), new Vector2(40f, 24f), () => NudgeMapUnderlay(new Vector2(-MapOffsetNudgePixels, 0f)));
        AddCommandButton(panel, "Right", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(100f, -46f), new Vector2(48f, 24f), () => NudgeMapUnderlay(new Vector2(MapOffsetNudgePixels, 0f)));
        AddCommandButton(panel, "Down", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(58f, -18f), new Vector2(42f, 24f), () => NudgeMapUnderlay(new Vector2(0f, -MapOffsetNudgePixels)));
    }

    private void AddMapCalibrationHandles(RectTransform parent)
    {
        EnsureMapCalibrationTargets();
        mapCalibrationHandleRects = new RectTransform[DefaultMapCalibrationLatLons.Length];

        for (var i = 0; i < DefaultMapCalibrationLatLons.Length; i++)
        {
            var handle = AddCircle(parent, "Calibrate_" + DefaultMapCalibrationLabels[i], Color.clear, Vector2.one * MapCalibrationHandleRadius, mapCalibrationTargetPixels[i], false, Gold, 3f, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            mapCalibrationHandleRects[i] = handle;
            AddCircle(handle, "Center", Gold, Vector2.one * 8f, Vector2.zero, true, Gold, 0f, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            AddText(handle, (i + 1).ToString(), 13, FontStyle.Bold, Ink, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }
    }

    private void HandleMapCalibrationHandleDrag()
    {
        if (tabIds[activeTab] != "M" || mapViewport == null || !mapCalibrationMode)
        {
            activeMapCalibrationHandle = -1;
            return;
        }

        if (Dwaallicht.Input.DwaallichtInput.TryGetPrimaryPointerDown(out var pointerPosition)
            && RectTransformUtility.ScreenPointToLocalPointInRectangle(mapViewport, pointerPosition, null, out var localPosition))
        {
            activeMapCalibrationHandle = FindNearestMapCalibrationHandle(ViewLocalToMapContentPosition(localPosition));
        }

        if (activeMapCalibrationHandle < 0)
        {
            return;
        }

        if (Dwaallicht.Input.DwaallichtInput.PrimaryPointerReleasedThisFrame())
        {
            activeMapCalibrationHandle = -1;
            ShowTab(activeTab);
            return;
        }

        if (!Dwaallicht.Input.DwaallichtInput.TryGetPrimaryPointer(out pointerPosition)
            || !RectTransformUtility.ScreenPointToLocalPointInRectangle(mapViewport, pointerPosition, null, out localPosition))
        {
            return;
        }

        var contentPosition = ViewLocalToMapContentPosition(localPosition);
        mapCalibrationTargetPixels[activeMapCalibrationHandle] = contentPosition;
        useThreePointMapCalibration = true;
        if (mapCalibrationHandleRects != null && activeMapCalibrationHandle < mapCalibrationHandleRects.Length && mapCalibrationHandleRects[activeMapCalibrationHandle] != null)
        {
            mapCalibrationHandleRects[activeMapCalibrationHandle].anchoredPosition = contentPosition;
        }

        if (mapCalibrationText != null)
        {
            mapCalibrationText.text = BuildMapCalibrationText();
        }
    }

    private int FindNearestMapCalibrationHandle(Vector2 localPosition)
    {
        EnsureMapCalibrationTargets();
        var nearest = -1;
        var nearestDistance = MapCalibrationHandleRadius * 0.65f;

        for (var i = 0; i < mapCalibrationTargetPixels.Length; i++)
        {
            var distance = Vector2.Distance(localPosition, mapCalibrationTargetPixels[i]);
            if (distance < nearestDistance)
            {
                nearest = i;
                nearestDistance = distance;
            }
        }

        return nearest;
    }

    private void HandleMapUnderlayDrag()
    {
        if (tabIds[activeTab] != "M" || mapViewport == null || !mapCalibrationMode || activeMapCalibrationHandle >= 0)
        {
            isDraggingMapUnderlay = false;
            return;
        }

        if (Dwaallicht.Input.DwaallichtInput.TryGetPrimaryPointerDown(out var pointerPosition)
            && !IsPointerOverMapControl(pointerPosition)
            && RectTransformUtility.RectangleContainsScreenPoint(mapViewport, pointerPosition))
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(mapViewport, pointerPosition, null, out var dragStartLocalPosition))
            {
                lastMapDragLocalPosition = ViewLocalToMapContentPosition(dragStartLocalPosition);
                isDraggingMapUnderlay = true;
            }
        }

        if (Dwaallicht.Input.DwaallichtInput.PrimaryPointerReleasedThisFrame())
        {
            isDraggingMapUnderlay = false;
        }

        if (!isDraggingMapUnderlay || !Dwaallicht.Input.DwaallichtInput.TryGetPrimaryPointer(out pointerPosition))
        {
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapViewport, pointerPosition, null, out var localPosition))
        {
            return;
        }

        var contentPosition = ViewLocalToMapContentPosition(localPosition);
        NudgeMapUnderlay(contentPosition - lastMapDragLocalPosition);
        lastMapDragLocalPosition = contentPosition;
    }

    private void HandleMapScrollCalibration()
    {
        if (tabIds[activeTab] != "M" || mapViewport == null || !mapCalibrationMode)
        {
            return;
        }

        var scroll = Dwaallicht.Input.DwaallichtInput.ReadScrollSteps();
        if (Mathf.Abs(scroll) < 0.001f)
        {
            return;
        }

        if (Dwaallicht.Input.DwaallichtInput.IsAnyKeyPressed(Key.LeftShift, Key.RightShift))
        {
            RotateMapUnderlay(scroll * MapScrollRotationStepDegrees);
            return;
        }

        SetMapZoomMultiplier(mapZoomMultiplier * Mathf.Pow(MapScrollZoomStep, scroll));
    }

    private void HandleMapUseGestures()
    {
        if (tabIds[activeTab] != "M" || mapViewport == null || mapCalibrationMode)
        {
            isDraggingMapView = false;
            isPinchingMapView = false;
            return;
        }

        if (Dwaallicht.Input.DwaallichtInput.TryGetPinch(out var pinchDistance, out var pinchCenter))
        {
            isDraggingMapView = false;
            if (isPinchingMapView && lastMapPinchDistance > 1f)
            {
                ZoomMapView(pinchDistance / lastMapPinchDistance, pinchCenter);
            }

            lastMapPinchDistance = pinchDistance;
            isPinchingMapView = true;
            return;
        }

        isPinchingMapView = false;

        var scroll = Dwaallicht.Input.DwaallichtInput.ReadScrollSteps();
        if (Mathf.Abs(scroll) >= 0.001f)
        {
            ZoomMapView(Mathf.Pow(MapViewScrollZoomStep, scroll), GetMouseOrViewportCenterScreenPosition());
        }

        if (Dwaallicht.Input.DwaallichtInput.TryGetPrimaryPointerDown(out var pointerPosition)
            && !IsPointerOverMapControl(pointerPosition)
            && RectTransformUtility.RectangleContainsScreenPoint(mapViewport, pointerPosition)
            && RectTransformUtility.ScreenPointToLocalPointInRectangle(mapViewport, pointerPosition, null, out lastMapViewDragLocalPosition))
        {
            isDraggingMapView = true;
        }

        if (Dwaallicht.Input.DwaallichtInput.PrimaryPointerReleasedThisFrame())
        {
            isDraggingMapView = false;
        }

        if (!isDraggingMapView || !Dwaallicht.Input.DwaallichtInput.TryGetPrimaryPointer(out pointerPosition))
        {
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapViewport, pointerPosition, null, out var localPosition))
        {
            return;
        }

        mapViewOffsetPixels += localPosition - lastMapViewDragLocalPosition;
        lastMapViewDragLocalPosition = localPosition;
        ApplyMapViewTransform();
    }

    private void ZoomMapView(float factor, Vector2 screenPivot)
    {
        if (mapViewport == null || factor <= 0f)
        {
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(mapViewport, screenPivot, null, out var pivotLocal)
            || !RectTransformUtility.RectangleContainsScreenPoint(mapViewport, screenPivot))
        {
            pivotLocal = Vector2.zero;
        }

        var previousZoom = Mathf.Max(0.0001f, mapViewZoomMultiplier);
        var contentUnderPivot = (pivotLocal - mapViewOffsetPixels) / previousZoom;
        mapViewZoomMultiplier *= factor;
        ClampMapViewZoomMultiplier();
        mapViewOffsetPixels = pivotLocal - contentUnderPivot * mapViewZoomMultiplier;
        ApplyMapViewTransform();
    }

    private void ApplyMapViewTransform()
    {
        ClampMapViewZoomMultiplier();
        if (mapContentRoot != null)
        {
            mapContentRoot.anchoredPosition = mapViewOffsetPixels;
            mapContentRoot.localScale = Vector3.one * mapViewZoomMultiplier;
        }

        if (mapScaleBarText != null)
        {
            mapScaleBarText.text = $"{GetScaleBarMeters():0} m";
        }
    }

    private void ClampMapViewZoomMultiplier()
    {
        var minZoom = GetZoomMultiplierForScaleBarMeters(MapMaxScaleBarMeters) / Mathf.Max(0.0001f, mapZoomMultiplier);
        var maxZoom = GetZoomMultiplierForScaleBarMeters(MapMinScaleBarMeters) / Mathf.Max(0.0001f, mapZoomMultiplier);
        mapViewZoomMultiplier = Mathf.Clamp(mapViewZoomMultiplier, Mathf.Min(minZoom, maxZoom), Mathf.Max(minZoom, maxZoom));
    }

    private Vector2 ViewLocalToMapContentPosition(Vector2 viewLocalPosition)
    {
        return (viewLocalPosition - mapViewOffsetPixels) / Mathf.Max(0.0001f, mapViewZoomMultiplier);
    }

    private Vector2 GetMouseOrViewportCenterScreenPosition()
    {
        if (Mouse.current != null)
        {
            return Mouse.current.position.ReadValue();
        }

        return RectTransformUtility.WorldToScreenPoint(null, mapViewport.position);
    }

    private bool IsPointerOverMapControl(Vector2 screenPosition)
    {
        if (EventSystem.current == null)
        {
            return false;
        }

        var eventData = new PointerEventData(EventSystem.current)
        {
            position = screenPosition,
        };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(eventData, results);
        for (var i = 0; i < results.Count; i++)
        {
            var button = results[i].gameObject.GetComponentInParent<Button>();
            if (button != null && !button.gameObject.name.StartsWith("Pin"))
            {
                return true;
            }
        }

        return false;
    }

    private void NudgeMapUnderlay(Vector2 deltaPixels)
    {
        mapUnderlayOffsetPixels += deltaPixels;
        OffsetThreePointTargets(deltaPixels);
        ApplyMapUnderlayCalibration();
    }

    private void ScaleMapUnderlay(float factor)
    {
        mapUnderlayMetersPerPixel = Mathf.Clamp(mapUnderlayMetersPerPixel / factor, 0.01f, 100f);
        ClampMapZoomMultiplier();
        ShowTab(activeTab);
    }

    private void RotateMapUnderlay(float deltaDegrees)
    {
        mapUnderlayRotationDegrees = Mathf.Repeat(mapUnderlayRotationDegrees + deltaDegrees + 180f, 360f) - 180f;
        RotateThreePointTargets(deltaDegrees);
        ApplyMapUnderlayCalibration();
    }

    private void SetMapScaleBarMeters(float meters)
    {
        SetMapZoomMultiplier(GetZoomMultiplierForScaleBarMeters(meters));
    }

    private void SetMapZoomMultiplier(float zoomMultiplier)
    {
        var previousZoom = Mathf.Max(0.0001f, mapZoomMultiplier);
        mapZoomMultiplier = zoomMultiplier;
        ClampMapZoomMultiplier();
        var zoomFactor = mapZoomMultiplier / previousZoom;
        mapUnderlayOffsetPixels *= zoomFactor;
        ScaleThreePointTargets(zoomFactor);
        ShowTab(activeTab);
    }

    private void ClampMapZoomMultiplier()
    {
        var minZoom = GetZoomMultiplierForScaleBarMeters(MapMaxScaleBarMeters);
        var maxZoom = GetZoomMultiplierForScaleBarMeters(MapMinScaleBarMeters);
        mapZoomMultiplier = Mathf.Clamp(mapZoomMultiplier, minZoom, maxZoom);
    }

    private float GetZoomMultiplierForScaleBarMeters(float meters)
    {
        return MapScaleBarPixels * mapUnderlayMetersPerPixel / Mathf.Max(1f, meters);
    }

    private void ToggleThreePointMapCalibration()
    {
        EnsureMapCalibrationTargets();
        useThreePointMapCalibration = !useThreePointMapCalibration;
        ShowTab(activeTab);
    }

    private void ToggleMapPoiPins()
    {
        showMapPoiPins = !showMapPoiPins;
        ShowTab(activeTab);
    }

    private void SetMapCalibrationMode(bool enabled)
    {
        mapCalibrationMode = enabled;
        if (enabled)
        {
            mapViewOffsetPixels = Vector2.zero;
            mapViewZoomMultiplier = 1f;
        }

        isDraggingMapUnderlay = false;
        isDraggingMapView = false;
        isPinchingMapView = false;
        activeMapCalibrationHandle = -1;
        ShowTab(activeTab);
    }

    private void ResetThreePointMapCalibration()
    {
        EnsureMapCalibrationTargets(true);
        useThreePointMapCalibration = true;
        ShowTab(activeTab);
    }

    private void EnsureMapCalibrationTargets(bool reset = false)
    {
        if (mapCalibrationTargetPixels == null || mapCalibrationTargetPixels.Length != DefaultMapCalibrationLatLons.Length)
        {
            mapCalibrationTargetPixels = new Vector2[DefaultMapCalibrationLatLons.Length];
            reset = true;
        }

        if (!reset)
        {
            var hasAnyTarget = false;
            for (var i = 0; i < mapCalibrationTargetPixels.Length; i++)
            {
                if (mapCalibrationTargetPixels[i] != Vector2.zero)
                {
                    hasAnyTarget = true;
                    break;
                }
            }

            reset = !hasAnyTarget;
        }

        if (!reset)
        {
            return;
        }

        for (var i = 0; i < DefaultMapCalibrationLatLons.Length; i++)
        {
            mapCalibrationTargetPixels[i] = BaseMapLatLonToAnchoredPosition(DefaultMapCalibrationLatLons[i]);
        }
    }

    private void OffsetThreePointTargets(Vector2 deltaPixels)
    {
        EnsureMapCalibrationTargets();
        for (var i = 0; i < mapCalibrationTargetPixels.Length; i++)
        {
            mapCalibrationTargetPixels[i] += deltaPixels;
        }
    }

    private void ScaleThreePointTargets(float factor)
    {
        EnsureMapCalibrationTargets();
        for (var i = 0; i < mapCalibrationTargetPixels.Length; i++)
        {
            mapCalibrationTargetPixels[i] *= factor;
        }
    }

    private void RotateThreePointTargets(float deltaDegrees)
    {
        EnsureMapCalibrationTargets();
        var radians = deltaDegrees * Mathf.Deg2Rad;
        var cos = Mathf.Cos(radians);
        var sin = Mathf.Sin(radians);

        for (var i = 0; i < mapCalibrationTargetPixels.Length; i++)
        {
            var point = mapCalibrationTargetPixels[i];
            mapCalibrationTargetPixels[i] = new Vector2(
                point.x * cos - point.y * sin,
                point.x * sin + point.y * cos);
        }
    }

    private void ApplyMapUnderlayCalibration()
    {
        if (mapUnderlayRect != null)
        {
            mapUnderlayRect.anchoredPosition = mapUnderlayOffsetPixels;
            mapUnderlayRect.sizeDelta = GetMapUnderlaySize();
            mapUnderlayRect.localEulerAngles = new Vector3(0f, 0f, mapUnderlayRotationDegrees);
        }

        if (mapCalibrationText != null)
        {
            mapCalibrationText.text = BuildMapCalibrationText();
        }
    }

    private void ApplyMapUnderlayTexture()
    {
        var texture = GetActiveMapUnderlayTexture();
        if (mapUnderlayRect != null)
        {
            var rawImage = mapUnderlayRect.GetComponent<RawImage>();
            if (rawImage != null)
            {
                rawImage.texture = texture;
            }

            ApplyMapUnderlayCalibration();
        }

        if (tabIds[activeTab] == "M")
        {
            ShowTab(activeTab);
        }
    }

    private bool TryLoadSyncedMapUnderlay()
    {
        foreach (var path in GetMapImageCandidatePaths())
        {
            if (string.IsNullOrWhiteSpace(path) || path.Contains("://") || !File.Exists(path))
            {
                continue;
            }

            try
            {
                var imageBytes = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, imageBytes, false))
                {
                    Destroy(texture);
                    Debug.LogWarning($"[DwaallichtAppController] Could not decode map image from {path}.");
                    continue;
                }

                texture.name = Path.GetFileNameWithoutExtension(path);
                if (syncedMapUnderlayTexture != null)
                {
                    Destroy(syncedMapUnderlayTexture);
                }

                syncedMapUnderlayTexture = texture;
                Debug.Log($"[DwaallichtAppController] Loaded synced map underlay from {path}.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DwaallichtAppController] Could not load synced map underlay from {path}: {ex.Message}");
            }
        }

        return false;
    }

    private IEnumerable<string> GetMapImageCandidatePaths()
    {
        if (driveSync != null)
        {
            yield return Path.Combine(driveSync.LocalRootPath, mapImageFileName);
        }

        yield return Path.Combine(Application.persistentDataPath, mapImageFolderName, mapImageFileName);
        yield return Path.Combine(Application.streamingAssetsPath, mapImageFolderName, mapImageFileName);
    }

    private void SaveMapCalibration()
    {
        EnsureMapCalibrationTargets();
        useThreePointMapCalibration = true;
        PlayerPrefs.SetFloat(MapCalibrationPrefsPrefix + "OffsetX", mapUnderlayOffsetPixels.x);
        PlayerPrefs.SetFloat(MapCalibrationPrefsPrefix + "OffsetY", mapUnderlayOffsetPixels.y);
        PlayerPrefs.SetFloat(MapCalibrationPrefsPrefix + "MetersPerPixel", mapUnderlayMetersPerPixel);
        PlayerPrefs.SetFloat(MapCalibrationPrefsPrefix + "Zoom", mapZoomMultiplier);
        PlayerPrefs.SetFloat(MapCalibrationPrefsPrefix + "Rotation", mapUnderlayRotationDegrees);
        PlayerPrefs.SetInt(MapCalibrationPrefsPrefix + "UseThreePoint", useThreePointMapCalibration ? 1 : 0);
        PlayerPrefs.SetInt(MapCalibrationPrefsPrefix + "ShowPins", showMapPoiPins ? 1 : 0);
        PlayerPrefs.SetInt(MapCalibrationPrefsPrefix + "Saved", 1);
        for (var i = 0; i < mapCalibrationTargetPixels.Length; i++)
        {
            PlayerPrefs.SetFloat(MapCalibrationPrefsPrefix + "Target" + i + "X", mapCalibrationTargetPixels[i].x);
            PlayerPrefs.SetFloat(MapCalibrationPrefsPrefix + "Target" + i + "Y", mapCalibrationTargetPixels[i].y);
        }

        PlayerPrefs.Save();
        ApplyMapUnderlayCalibration();
    }

    private void LoadMapCalibration()
    {
        if (!PlayerPrefs.HasKey(MapCalibrationPrefsPrefix + "Saved"))
        {
            EnsureMapCalibrationTargets();
            ClampMapZoomMultiplier();
            return;
        }

        mapUnderlayOffsetPixels = new Vector2(
            PlayerPrefs.GetFloat(MapCalibrationPrefsPrefix + "OffsetX", mapUnderlayOffsetPixels.x),
            PlayerPrefs.GetFloat(MapCalibrationPrefsPrefix + "OffsetY", mapUnderlayOffsetPixels.y));
        mapUnderlayMetersPerPixel = PlayerPrefs.GetFloat(MapCalibrationPrefsPrefix + "MetersPerPixel", mapUnderlayMetersPerPixel);
        mapZoomMultiplier = PlayerPrefs.GetFloat(MapCalibrationPrefsPrefix + "Zoom", mapZoomMultiplier);
        mapUnderlayRotationDegrees = PlayerPrefs.GetFloat(MapCalibrationPrefsPrefix + "Rotation", mapUnderlayRotationDegrees);
        useThreePointMapCalibration = PlayerPrefs.GetInt(MapCalibrationPrefsPrefix + "UseThreePoint", useThreePointMapCalibration ? 1 : 0) == 1;
        showMapPoiPins = PlayerPrefs.GetInt(MapCalibrationPrefsPrefix + "ShowPins", showMapPoiPins ? 1 : 0) == 1;
        EnsureMapCalibrationTargets();
        for (var i = 0; i < mapCalibrationTargetPixels.Length; i++)
        {
            mapCalibrationTargetPixels[i] = new Vector2(
                PlayerPrefs.GetFloat(MapCalibrationPrefsPrefix + "Target" + i + "X", mapCalibrationTargetPixels[i].x),
                PlayerPrefs.GetFloat(MapCalibrationPrefsPrefix + "Target" + i + "Y", mapCalibrationTargetPixels[i].y));
        }

        ClampMapZoomMultiplier();
    }

    private string BuildMapCalibrationText()
    {
        return $"offset {mapUnderlayOffsetPixels.x:0}, {mapUnderlayOffsetPixels.y:0}\n" +
               $"scale {mapUnderlayMetersPerPixel:0.###} m/px\n" +
               $"rot {mapUnderlayRotationDegrees:0.00} deg\n" +
               $"bar {GetScaleBarMeters():0} m\n" +
               $"fit3 {(useThreePointMapCalibration ? "on" : "off")}";
    }

    private Vector2 GetMapUnderlaySize()
    {
        var texture = GetActiveMapUnderlayTexture();
        if (texture == null)
        {
            return Vector2.zero;
        }

        var sizeTexture = GetMapUnderlaySizeTexture(texture);
        return new Vector2(sizeTexture.width, sizeTexture.height) * mapZoomMultiplier;
    }

    private Texture2D GetActiveMapUnderlayTexture()
    {
        return syncedMapUnderlayTexture != null ? syncedMapUnderlayTexture : mapUnderlayTexture;
    }

    private Texture2D GetMapUnderlaySizeTexture(Texture2D activeTexture)
    {
        return syncedMapUnderlayTexture != null && mapUnderlayTexture != null
            ? mapUnderlayTexture
            : activeTexture;
    }

    private float GetScaleBarMeters()
    {
        return MapScaleBarPixels * mapUnderlayMetersPerPixel / Mathf.Max(0.01f, mapZoomMultiplier * mapViewZoomMultiplier);
    }

    private void AddMapPois(RectTransform map)
    {
        if (poiManager == null)
        {
            return;
        }

        foreach (var poi in poiManager.Pois)
        {
            if (!poi.active)
            {
                continue;
            }

            var isSelected = poiManager.SelectedPoi == poi;
            var position = MapLatLonToAnchoredPosition(poi.LatLon);
            var pin = AddPin(map, poi.color, position, isSelected ? 38f : 30f, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
            var button = pin.gameObject.AddComponent<Button>();
            button.targetGraphic = pin.GetComponent<Graphic>();
            button.onClick.AddListener(() =>
            {
                poiManager.SelectPoi(poi);
                ShowTab(activeTab);
            });
        }
    }

    private void AddSelectedPoiCard(RectTransform map, PointOfInterest poi, Vector2 pinPosition)
    {
        var title = string.IsNullOrWhiteSpace(poi.title) ? "POI" : poi.title.Trim();
        var width = Mathf.Clamp(title.Length * 8.5f + 32f, 96f, 252f);
        var card = AddImage(map, "SelectedPoiCard", TranslucentPaper, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), pinPosition + new Vector2(0f, 48f), new Vector2(width, 34f));
        var image = card.GetComponent<Image>();
        image.raycastTarget = true;
        var outline = card.gameObject.AddComponent<Outline>();
        outline.effectColor = Ink;
        outline.effectDistance = new Vector2(1.5f, -1.5f);

        var button = card.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => ShowTab(3));

        AddText(card, title, 15, FontStyle.Bold, Ink, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, new Vector2(10f, 0f), new Vector2(-10f, 0f));
        AddCircle(map, "SelectedPoiRing", Color.clear, Vector2.one * 56f, pinPosition + new Vector2(0f, 4f), false, Gold, 3f, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        card.SetAsLastSibling();
    }

    private bool SelectedPoiHasArMap()
    {
        EnsurePoiManager();
        var selectedPoi = poiManager != null ? poiManager.SelectedPoi : null;
        return selectedPoi != null && TryGetPoiFolder(selectedPoi, out var folderPath) && HasArMap(folderPath);
    }

    private bool TryGetPoiFolder(PointOfInterest poi, out string folderPath)
    {
        folderPath = "";
        if (poi == null)
        {
            return false;
        }

        var title = string.IsNullOrWhiteSpace(poi.title) ? "" : poi.title.Trim();
        var sanitizedTitle = SanitizeFileSystemName(title);
        foreach (var rootPath in GetDriveRootCandidatePaths())
        {
            if (string.IsNullOrWhiteSpace(rootPath) || rootPath.Contains("://") || !Directory.Exists(rootPath))
            {
                continue;
            }

            foreach (var name in new[] { title, sanitizedTitle, poi.id })
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var candidate = Path.Combine(rootPath, name);
                if (Directory.Exists(candidate))
                {
                    folderPath = candidate;
                    return true;
                }
            }

            var normalizedTitle = NormalizePoiFolderName(title);
            var directories = Directory.GetDirectories(rootPath);
            for (var i = 0; i < directories.Length; i++)
            {
                if (NormalizePoiFolderName(Path.GetFileName(directories[i])) == normalizedTitle)
                {
                    folderPath = directories[i];
                    return true;
                }
            }
        }

        return false;
    }

    private IEnumerable<string> GetDriveRootCandidatePaths()
    {
        if (driveSync != null)
        {
            yield return driveSync.LocalRootPath;
        }

        yield return Path.Combine(Application.persistentDataPath, mapImageFolderName);
        yield return Path.Combine(Application.streamingAssetsPath, mapImageFolderName);
    }

    private static bool HasArMap(string poiFolder)
    {
        if (string.IsNullOrWhiteSpace(poiFolder) || !Directory.Exists(poiFolder))
        {
            return false;
        }

        if (Directory.Exists(Path.Combine(poiFolder, PoiArFolderName)))
        {
            return true;
        }

        var entries = Directory.GetFileSystemEntries(poiFolder);
        for (var i = 0; i < entries.Length; i++)
        {
            var name = Path.GetFileNameWithoutExtension(entries[i]);
            if (string.Equals(name, PoiArFolderName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<string> GetPoiAudioPaths(string poiFolder)
    {
        if (string.IsNullOrWhiteSpace(poiFolder) || !Directory.Exists(poiFolder))
        {
            yield break;
        }

        var files = Directory.GetFiles(poiFolder, "*.mp3", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < files.Length; i++)
        {
            if (!files[i].EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                yield return files[i];
            }
        }
    }

    private bool TryReadPoiText(string poiFolder, out string text)
    {
        text = "";
        if (string.IsNullOrWhiteSpace(poiFolder) || !Directory.Exists(poiFolder))
        {
            return false;
        }

        var preferredPath = Path.Combine(poiFolder, PoiTextFileStem + ".txt");
        if (File.Exists(preferredPath))
        {
            text = File.ReadAllText(preferredPath).Trim();
            return !string.IsNullOrWhiteSpace(text);
        }

        var files = Directory.GetFiles(poiFolder, "*.txt", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < files.Length; i++)
        {
            if (files[i].EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            text = File.ReadAllText(files[i]).Trim();
            return !string.IsNullOrWhiteSpace(text);
        }

        return false;
    }

    private Text AddDetailLabel(RectTransform parent, string value, int fontSize, FontStyle style, Color color, float preferredHeight)
    {
        var holder = AddRect(parent, "DetailLabel", new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, preferredHeight));
        var layoutElement = holder.gameObject.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = preferredHeight;
        var text = AddText(holder, value, fontSize, style, color, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(4f, 0f), new Vector2(-4f, 0f));
        text.raycastTarget = false;
        return text;
    }

    private void AddPoiAudioControl(RectTransform parent, string audioPath, bool overCamera)
    {
        var title = Path.GetFileNameWithoutExtension(audioPath);
        var holder = AddImage(parent, "Audio_" + title, overCamera ? TranslucentPaper : Paper, new Vector2(0f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(0f, 82f));
        var layoutElement = holder.gameObject.AddComponent<LayoutElement>();
        layoutElement.preferredHeight = 82f;
        holder.GetComponent<Image>().raycastTarget = false;

        AddText(holder, title, 16, FontStyle.Bold, Ink, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(14f, 48f), new Vector2(-14f, -8f));

        var buttonRect = AddImage(holder, "AudioPlayStop", Ink, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 14f), new Vector2(48f, 28f));
        var buttonImage = buttonRect.GetComponent<Image>();
        buttonImage.raycastTarget = true;
        var button = buttonRect.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        var buttonLabel = AddText(buttonRect, "...", 14, FontStyle.Bold, Paper, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var track = AddImage(holder, "AudioProgressTrack", TranslucentInk, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(74f, 24f), new Vector2(-14f, 28f));
        var fill = AddImage(track, "AudioProgressFill", Gold, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
        fill.anchorMax = new Vector2(0f, 1f);

        var sourceGo = new GameObject("AudioSource_" + title);
        sourceGo.transform.SetParent(transform, false);
        var source = sourceGo.AddComponent<AudioSource>();
        source.playOnAwake = false;

        var player = new PoiAudioPlayer
        {
            filePath = audioPath,
            source = source,
            progressFill = fill.GetComponent<Image>(),
            buttonLabel = buttonLabel,
            loading = true,
        };
        poiAudioPlayers.Add(player);
        button.onClick.AddListener(() => TogglePoiAudio(player));
        StartCoroutine(LoadPoiAudio(player));
    }

    private void TogglePoiAudio(PoiAudioPlayer player)
    {
        if (player == null || player.loadFailed || player.source == null || player.source.clip == null)
        {
            return;
        }

        if (player.source.isPlaying)
        {
            player.source.Stop();
            return;
        }

        for (var i = 0; i < poiAudioPlayers.Count; i++)
        {
            var other = poiAudioPlayers[i];
            if (other != player && other.source != null && other.source.isPlaying)
            {
                other.source.Stop();
            }
        }

        player.source.Play();
    }

    private System.Collections.IEnumerator LoadPoiAudio(PoiAudioPlayer player)
    {
        var uri = new Uri(player.filePath).AbsoluteUri;
        using (var request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.MPEG))
        {
            yield return request.SendWebRequest();

            if (player.source == null)
            {
                yield break;
            }

            if (HasRequestError(request))
            {
                player.loadFailed = true;
                player.loading = false;
                if (player.buttonLabel != null)
                {
                    player.buttonLabel.text = "!";
                }

                Debug.LogWarning($"[DwaallichtAppController] Could not load audio {player.filePath}: {request.error}");
                yield break;
            }

            player.source.clip = DownloadHandlerAudioClip.GetContent(request);
            player.loading = false;
        }
    }

    private void UpdatePoiAudioPlayers()
    {
        for (var i = 0; i < poiAudioPlayers.Count; i++)
        {
            var player = poiAudioPlayers[i];
            if (player == null)
            {
                continue;
            }

            if (player.buttonLabel != null)
            {
                player.buttonLabel.text = player.loadFailed ? "!" : player.loading ? "..." : player.source != null && player.source.isPlaying ? "Stop" : "Play";
            }

            if (player.progressFill != null)
            {
                var amount = 0f;
                if (player.source != null && player.source.clip != null && player.source.clip.length > 0.01f)
                {
                    amount = Mathf.Clamp01(player.source.time / player.source.clip.length);
                }

                player.progressFill.rectTransform.anchorMax = new Vector2(amount, 1f);
            }
        }
    }

    private void CleanupPoiAudioPlayers()
    {
        StopAllCoroutines();
        for (var i = 0; i < poiAudioPlayers.Count; i++)
        {
            var player = poiAudioPlayers[i];
            if (player == null || player.source == null)
            {
                continue;
            }

            if (player.source.clip != null)
            {
                Destroy(player.source.clip);
            }

            Destroy(player.source.gameObject);
        }

        poiAudioPlayers.Clear();
    }

    private static bool HasRequestError(UnityWebRequest request)
    {
#if UNITY_2020_2_OR_NEWER
        return request.result == UnityWebRequest.Result.ConnectionError
            || request.result == UnityWebRequest.Result.ProtocolError
            || request.result == UnityWebRequest.Result.DataProcessingError;
#else
        return request.isNetworkError || request.isHttpError;
#endif
    }

    private static float MeasureTextHeight(string text, int fontSize, float width)
    {
        var lines = Mathf.Max(1, (text ?? "").Split('\n').Length);
        var wrappedCharactersPerLine = Mathf.Max(16, Mathf.FloorToInt(width / Mathf.Max(1f, fontSize * 0.48f)));
        var wrappedLines = Mathf.CeilToInt((text ?? "").Length / (float)wrappedCharactersPerLine);
        return Mathf.Max(lines, wrappedLines) * (fontSize + 7f) + 20f;
    }

    private static string SanitizeFileSystemName(string name)
    {
        var safeName = string.IsNullOrWhiteSpace(name) ? "untitled" : name.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalid, '_');
        }

        return safeName;
    }

    private static string NormalizePoiFolderName(string name)
    {
        return SanitizeFileSystemName(name).Replace(" ", "").Replace("_", "").ToLowerInvariant();
    }

    private string BuildNavigationText()
    {
        EnsurePoiManager();
        if (headingProvider == null || poiManager == null || poiManager.SelectedPoi == null)
        {
            return AddCompassWarning("Geen POI geselecteerd");
        }

        var poi = poiManager.SelectedPoi;
        var bearing = GeoMath.BearingTo(headingProvider.CurrentLatLon, poi.LatLon);
        var turn = GeoMath.SignedDeltaDegrees(headingProvider.Heading, bearing);
        var distance = GeoMath.DistanceMeters(headingProvider.CurrentLatLon, poi.LatLon);
        return AddCompassWarning($"{poi.title}  {distance:0} m  {turn:+0;-0;0} graden");
    }

    private string AddCompassWarning(string text)
    {
        if (headingProvider == null || !headingProvider.CompassMayBeUnreliable)
        {
            return text;
        }

        return text + "\nKompas werkt mogelijk niet op deze telefoon";
    }

    private string BuildDebugText()
    {
        if (!showDeviceDebug)
        {
            return "";
        }

        if (tabIds[activeTab] == "S")
        {
            return BuildArScannerDebugText();
        }

        if (headingProvider == null)
        {
            return "Debug: geen heading provider";
        }

        var latLon = headingProvider.CurrentLatLon;
        var mode = headingProvider.IsSimulated ? "sim" : "device";
        var selected = poiManager != null && poiManager.SelectedPoi != null ? poiManager.SelectedPoi.title : "-";
        return $"Debug {mode}: {headingProvider.Status}\n" +
               $"raw {headingProvider.RawHeading:0.0}  smooth {headingProvider.Heading:0.0}  acc {headingProvider.HeadingAccuracy:0.0}\n" +
               $"lat {latLon.x:0.000000}  lon {latLon.y:0.000000}  poi {selected}";
    }

    private string BuildArScannerDebugText()
    {
        EnsureArScanner();
        return arScanner != null ? arScanner.DebugStatus : "AR scanner missing";
    }

    private Vector2 MapLatLonToAnchoredPosition(Vector2 latLon)
    {
        if (useThreePointMapCalibration && TryThreePointMapLatLonToAnchoredPosition(latLon, out var calibratedPosition))
        {
            return calibratedPosition;
        }

        return BaseMapLatLonToAnchoredPosition(latLon);
    }

    private Vector2 BaseMapLatLonToAnchoredPosition(Vector2 latLon)
    {
        var meters = MapLatLonToMeters(latLon);
        var pixelsPerMeter = mapZoomMultiplier / Mathf.Max(0.01f, mapUnderlayMetersPerPixel);
        return meters * pixelsPerMeter;
    }

    private Vector2 MapLatLonToMeters(Vector2 latLon)
    {
        const float metersPerDegreeLatitude = 111320f;
        var northMeters = (latLon.x - mapCenterLatLon.x) * metersPerDegreeLatitude;
        var eastMeters = (latLon.y - mapCenterLatLon.y) * Mathf.Cos(mapCenterLatLon.x * Mathf.Deg2Rad) * metersPerDegreeLatitude;
        return new Vector2(eastMeters, northMeters);
    }

    private bool TryThreePointMapLatLonToAnchoredPosition(Vector2 latLon, out Vector2 anchoredPosition)
    {
        EnsureMapCalibrationTargets();
        var p0 = MapLatLonToMeters(DefaultMapCalibrationLatLons[0]);
        var p1 = MapLatLonToMeters(DefaultMapCalibrationLatLons[1]);
        var p2 = MapLatLonToMeters(DefaultMapCalibrationLatLons[2]);
        var target0 = mapCalibrationTargetPixels[0];
        var target1 = mapCalibrationTargetPixels[1];
        var target2 = mapCalibrationTargetPixels[2];

        var determinant = p0.x * (p1.y - p2.y)
            + p1.x * (p2.y - p0.y)
            + p2.x * (p0.y - p1.y);

        if (Mathf.Abs(determinant) < 0.001f)
        {
            anchoredPosition = default;
            return false;
        }

        var meters = MapLatLonToMeters(latLon);
        var xCoefficient = AffineCoefficient(p0, p1, p2, target0.x, target1.x, target2.x, determinant);
        var yCoefficient = AffineCoefficient(p0, p1, p2, target0.y, target1.y, target2.y, determinant);
        anchoredPosition = new Vector2(
            xCoefficient.x * meters.x + xCoefficient.y * meters.y + xCoefficient.z,
            yCoefficient.x * meters.x + yCoefficient.y * meters.y + yCoefficient.z);
        return true;
    }

    private static Vector3 AffineCoefficient(Vector2 p0, Vector2 p1, Vector2 p2, float value0, float value1, float value2, float determinant)
    {
        return new Vector3(
            (value0 * (p1.y - p2.y) + value1 * (p2.y - p0.y) + value2 * (p0.y - p1.y)) / determinant,
            (value0 * (p2.x - p1.x) + value1 * (p0.x - p2.x) + value2 * (p1.x - p0.x)) / determinant,
            (value0 * (p1.x * p2.y - p2.x * p1.y) + value1 * (p2.x * p0.y - p0.x * p2.y) + value2 * (p0.x * p1.y - p1.x * p0.y)) / determinant);
    }

    private static Vector2 ToMapNormalized(Vector2 anchoredFromCenter, Vector2 rectSize)
    {
        return new Vector2(
            0.5f + anchoredFromCenter.x / Mathf.Max(1f, rectSize.x),
            0.5f + anchoredFromCenter.y / Mathf.Max(1f, rectSize.y));
    }

    private sealed class PoiAudioPlayer
    {
        public string filePath;
        public AudioSource source;
        public Image progressFill;
        public Text buttonLabel;
        public bool loading;
        public bool loadFailed;
    }
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class AppCircleGraphic : MaskableGraphic
{
    public bool fillCenter = true;
    public Color strokeColor = Color.black;
    public float strokeWidth;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var rect = GetPixelAdjustedRect();
        var center = rect.center;
        var rx = rect.width * 0.5f;
        var ry = rect.height * 0.5f;
        var segments = 72;

        if (fillCenter)
        {
            AddFan(vh, center, rx, ry, color, segments);
        }

        if (strokeWidth > 0f)
        {
            AddRing(vh, center, rx, ry, Mathf.Max(0f, rx - strokeWidth), Mathf.Max(0f, ry - strokeWidth), strokeColor, segments);
        }
    }

    private static void AddFan(VertexHelper vh, Vector2 center, float rx, float ry, Color32 col, int segments)
    {
        var centerIndex = vh.currentVertCount;
        vh.AddVert(center, col, Vector2.zero);
        for (var i = 0; i <= segments; i++)
        {
            var a = i / (float)segments * Mathf.PI * 2f;
            vh.AddVert(new Vector2(center.x + Mathf.Cos(a) * rx, center.y + Mathf.Sin(a) * ry), col, Vector2.zero);
        }

        for (var i = 1; i <= segments; i++)
        {
            vh.AddTriangle(centerIndex, centerIndex + i, centerIndex + i + 1);
        }
    }

    private static void AddRing(VertexHelper vh, Vector2 center, float outerRx, float outerRy, float innerRx, float innerRy, Color32 col, int segments)
    {
        for (var i = 0; i < segments; i++)
        {
            var a0 = i / (float)segments * Mathf.PI * 2f;
            var a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
            var start = vh.currentVertCount;
            vh.AddVert(new Vector2(center.x + Mathf.Cos(a0) * outerRx, center.y + Mathf.Sin(a0) * outerRy), col, Vector2.zero);
            vh.AddVert(new Vector2(center.x + Mathf.Cos(a1) * outerRx, center.y + Mathf.Sin(a1) * outerRy), col, Vector2.zero);
            vh.AddVert(new Vector2(center.x + Mathf.Cos(a1) * innerRx, center.y + Mathf.Sin(a1) * innerRy), col, Vector2.zero);
            vh.AddVert(new Vector2(center.x + Mathf.Cos(a0) * innerRx, center.y + Mathf.Sin(a0) * innerRy), col, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class AppArrowGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var rect = GetPixelAdjustedRect();
        var points = new[]
        {
            new Vector2(0.43f, 0.02f),
            new Vector2(0.57f, 0.02f),
            new Vector2(0.57f, 0.67f),
            new Vector2(0.86f, 0.58f),
            new Vector2(0.50f, 0.98f),
            new Vector2(0.14f, 0.58f),
            new Vector2(0.43f, 0.67f),
        };

        var start = vh.currentVertCount;
        for (var i = 0; i < points.Length; i++)
        {
            var p = points[i];
            vh.AddVert(new Vector2(rect.xMin + p.x * rect.width, rect.yMin + p.y * rect.height), color, Vector2.zero);
        }

        for (var i = 1; i < points.Length - 1; i++)
        {
            vh.AddTriangle(start, start + i, start + i + 1);
        }
    }
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class AppPinGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        var rect = GetPixelAdjustedRect();
        var center = new Vector2(rect.center.x, rect.yMin + rect.height * 0.67f);
        var radius = Mathf.Min(rect.width, rect.height * 0.74f) * 0.42f;
        var segments = 34;
        var tip = new Vector2(rect.center.x, rect.yMin + rect.height * 0.02f);

        var start = vh.currentVertCount;
        vh.AddVert(center, color, Vector2.zero);
        for (var i = 0; i <= segments; i++)
        {
            var a = Mathf.Lerp(20f, 340f, i / (float)segments) * Mathf.Deg2Rad;
            vh.AddVert(new Vector2(center.x + Mathf.Cos(a) * radius, center.y + Mathf.Sin(a) * radius), color, Vector2.zero);
        }

        for (var i = 1; i <= segments; i++)
        {
            vh.AddTriangle(start, start + i, start + i + 1);
        }

        var left = new Vector2(center.x - radius * 0.66f, center.y - radius * 0.56f);
        var right = new Vector2(center.x + radius * 0.66f, center.y - radius * 0.56f);
        var triStart = vh.currentVertCount;
        vh.AddVert(left, color, Vector2.zero);
        vh.AddVert(right, color, Vector2.zero);
        vh.AddVert(tip, color, Vector2.zero);
        vh.AddTriangle(triStart, triStart + 1, triStart + 2);
    }
}

[RequireComponent(typeof(CanvasRenderer))]
public sealed class AppPolylineGraphic : MaskableGraphic
{
    public float thickness = 2f;
    [SerializeField]
    private List<Vector2> points = new List<Vector2>();

    public void SetPoints(IEnumerable<Vector2> newPoints)
    {
        points.Clear();
        points.AddRange(newPoints);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (points.Count < 2)
        {
            return;
        }

        var rect = GetPixelAdjustedRect();
        for (var i = 0; i < points.Count - 1; i++)
        {
            var a = ToRectPoint(rect, points[i]);
            var b = ToRectPoint(rect, points[i + 1]);
            var direction = (b - a).normalized;
            var normal = new Vector2(-direction.y, direction.x) * thickness * 0.5f;
            var start = vh.currentVertCount;
            vh.AddVert(a - normal, color, Vector2.zero);
            vh.AddVert(a + normal, color, Vector2.zero);
            vh.AddVert(b + normal, color, Vector2.zero);
            vh.AddVert(b - normal, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start, start + 2, start + 3);
        }
    }

    private static Vector2 ToRectPoint(Rect rect, Vector2 normalized)
    {
        return new Vector2(rect.xMin + normalized.x * rect.width, rect.yMin + normalized.y * rect.height);
    }
}
