using System.Collections.Generic;
using Dwaallicht.AR;
using Dwaallicht.Navigation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
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
    private const float MapRotationStepDegrees = 0.1f;
    private const float MapScrollRotationStepDegrees = 0.05f;
    private const float MapMinScaleBarMeters = 10f;
    private const float MapMaxScaleBarMeters = 500f;
    private const float MapCalibrationHandleRadius = 34f;
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

    [SerializeField, Range(0, 3)]
    private int activeTab;
    [SerializeField]
    private bool showDeviceDebug = true;
    [SerializeField]
    private bool showAdminPoiControls = true;
    [Header("Map Underlay")]
    [SerializeField]
    private Texture2D mapUnderlayTexture;
    [SerializeField]
    private Vector2 mapCenterLatLon = new Vector2(51.096465f, 4.344778f);
    [SerializeField]
    private Vector2 mapUnderlayOffsetPixels = Vector2.zero;
    [SerializeField, Min(0.01f)]
    private float mapUnderlayMetersPerPixel = 2f;
    [SerializeField, Min(0.05f)]
    private float mapZoomMultiplier = 1f;
    [SerializeField]
    private float mapUnderlayRotationDegrees;
    [SerializeField]
    private bool useThreePointMapCalibration;
    [SerializeField]
    private Vector2[] mapCalibrationTargetPixels = new Vector2[3];
    [SerializeField]
    private bool showMapPoiPins = true;
    [SerializeField]
    private bool showMapCalibrationControls = true;

    private RectTransform contentRoot;
    private RectTransform tabRoot;
    private RectTransform compassRose;
    private RectTransform mapFacingArrow;
    private RectTransform mapViewport;
    private RectTransform mapUnderlayRect;
    private Image appBackgroundImage;
    private Text debugText;
    private Text navigationText;
    private Text headingText;
    private Text mapCalibrationText;
    private DwaallichtArScanner arScanner;
    private CompassHeadingProvider headingProvider;
    private PoiManager poiManager;
    private Font font;
    private Vector2 lastMapDragLocalPosition;
    private bool isDraggingMapUnderlay;
    private int activeMapCalibrationHandle = -1;
    private RectTransform[] mapCalibrationHandleRects;

    private void OnEnable()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        Build();
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        EnsureHeadingProvider();
        EnsurePoiManager();

        RefreshDynamicText();
        HandleMapCalibrationHandleDrag();
        HandleMapUnderlayDrag();
        HandleMapScrollCalibration();

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

        ClearChildren(contentRoot);
        compassRose = null;
        mapFacingArrow = null;
        debugText = null;
        navigationText = null;
        headingText = null;
        mapViewport = null;
        mapUnderlayRect = null;
        mapCalibrationText = null;
        mapCalibrationHandleRects = null;
        isDraggingMapUnderlay = false;
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

        var scanActive = tabIds[activeTab] == "S";
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
        AddMapUnderlay(mapViewport);

        var selectedPoi = poiManager != null ? poiManager.SelectedPoi : null;
        if (selectedPoi != null)
        {
            var selectedPosition = MapLatLonToAnchoredPosition(selectedPoi.LatLon);
            AddPolyline(mapViewport, "RouteToSelectedPoi", Ink, 4f, new[]
            {
                ToMapNormalized(MapLatLonToAnchoredPosition(headingProvider.CurrentLatLon), mapViewport.rect.size),
                ToMapNormalized(selectedPosition, mapViewport.rect.size),
            });
        }

        if (showMapPoiPins)
        {
            AddMapPois(mapViewport);
        }

        if (showMapCalibrationControls)
        {
            AddMapCalibrationHandles(mapViewport);
        }

        var phonePosition = MapLatLonToAnchoredPosition(headingProvider.CurrentLatLon);
        AddCircle(mapViewport, "PhonePosition", Paper, Vector2.one * 42f, phonePosition, true, Ink, 3f, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
        mapFacingArrow = AddArrow(mapViewport, "PhoneFacingDirection", Ink, phonePosition + new Vector2(4f, 28f), new Vector2(34f, 88f), 0f);
        AddScaleBar(map);
        navigationText = AddText(map, "", 15, FontStyle.Bold, Ink, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -600f));
        if (showDeviceDebug)
        {
            debugText = AddText(map, "", 12, FontStyle.Normal, Ink, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, new Vector2(10f, 54f), new Vector2(-10f, -548f));
        }

        if (showAdminPoiControls)
        {
            AddCommandButton(map, "POI +", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-82f, -54f), new Vector2(72f, 34f), AddPoiAhead);
        }

        if (showMapCalibrationControls)
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
        AddText(parent, "play/scan", 26, FontStyle.Normal, Paper, TextAnchor.MiddleCenter, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 120f), new Vector2(0f, 172f));

        if (showDeviceDebug)
        {
            debugText = AddText(parent, "", 12, FontStyle.Normal, Paper, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, new Vector2(28f, 24f), new Vector2(-28f, -520f));
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

        if (poiManager.SelectedPoi == null && poiManager.Pois.Count > 0)
        {
            poiManager.SelectPoi(poiManager.Pois[0]);
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

    private void RefreshDynamicText()
    {
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
        if (mapUnderlayTexture == null)
        {
            AddText(parent, "Map texture missing", 18, FontStyle.Bold, Ink, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            return;
        }

        mapUnderlayRect = AddRawImage(parent, "MapUnderlay", mapUnderlayTexture, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), mapUnderlayOffsetPixels, GetMapUnderlaySize());
        mapUnderlayRect.localEulerAngles = new Vector3(0f, 0f, mapUnderlayRotationDegrees);
        mapUnderlayRect.SetAsFirstSibling();
    }

    private void AddScaleBar(RectTransform parent)
    {
        var holder = AddImage(parent, "ScaleBarPanel", TranslucentPaper, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(12f, 12f), new Vector2(132f, 36f));
        AddImage(holder, "ScaleBarTrack", TranslucentInk, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 10f), new Vector2(MapScaleBarPixels, 4f));
        AddImage(holder, "ScaleBarFill", Ink, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(14f, 10f), new Vector2(MapScaleBarPixels, 4f));
        AddText(holder, $"{GetScaleBarMeters():0} m", 11, FontStyle.Bold, Ink, TextAnchor.UpperLeft, Vector2.zero, Vector2.one, new Vector2(14f, 14f), new Vector2(-8f, -2f));
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
        AddCommandButton(panel, showMapPoiPins ? "Pins on" : "Pins off", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -254f), new Vector2(140f, 24f), ToggleMapPoiPins);

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
        if (tabIds[activeTab] != "M" || mapViewport == null || !showMapCalibrationControls)
        {
            activeMapCalibrationHandle = -1;
            return;
        }

        if (Dwaallicht.Input.DwaallichtInput.TryGetPrimaryPointerDown(out var pointerPosition)
            && RectTransformUtility.ScreenPointToLocalPointInRectangle(mapViewport, pointerPosition, null, out var localPosition))
        {
            activeMapCalibrationHandle = FindNearestMapCalibrationHandle(localPosition);
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

        mapCalibrationTargetPixels[activeMapCalibrationHandle] = localPosition;
        useThreePointMapCalibration = true;
        if (mapCalibrationHandleRects != null && activeMapCalibrationHandle < mapCalibrationHandleRects.Length && mapCalibrationHandleRects[activeMapCalibrationHandle] != null)
        {
            mapCalibrationHandleRects[activeMapCalibrationHandle].anchoredPosition = localPosition;
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
        if (tabIds[activeTab] != "M" || mapViewport == null || !showMapCalibrationControls || activeMapCalibrationHandle >= 0)
        {
            isDraggingMapUnderlay = false;
            return;
        }

        if (Dwaallicht.Input.DwaallichtInput.TryGetPrimaryPointerDown(out var pointerPosition)
            && RectTransformUtility.RectangleContainsScreenPoint(mapViewport, pointerPosition))
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(mapViewport, pointerPosition, null, out lastMapDragLocalPosition))
            {
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

        NudgeMapUnderlay(localPosition - lastMapDragLocalPosition);
        lastMapDragLocalPosition = localPosition;
    }

    private void HandleMapScrollCalibration()
    {
        if (tabIds[activeTab] != "M" || mapViewport == null)
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
        if (mapUnderlayTexture == null)
        {
            return Vector2.zero;
        }

        return new Vector2(mapUnderlayTexture.width, mapUnderlayTexture.height) * mapZoomMultiplier;
    }

    private float GetScaleBarMeters()
    {
        return MapScaleBarPixels * mapUnderlayMetersPerPixel / Mathf.Max(0.01f, mapZoomMultiplier);
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

    private void AddPoiAhead()
    {
        EnsureHeadingProvider();
        EnsurePoiManager();
        if (headingProvider == null || poiManager == null)
        {
            return;
        }

        var latLon = ProjectLatLon(headingProvider.CurrentLatLon, headingProvider.Heading, 80f);
        poiManager.AddPoi("Device testpunt", latLon, "Event", "Aangemaakt vanuit de app-debugknop.");
        ShowTab(activeTab);
    }

    private static Vector2 ProjectLatLon(Vector2 latLon, float bearingDegrees, float meters)
    {
        const float metersPerDegreeLatitude = 111320f;
        var bearing = bearingDegrees * Mathf.Deg2Rad;
        var northMeters = Mathf.Cos(bearing) * meters;
        var eastMeters = Mathf.Sin(bearing) * meters;
        var latitude = latLon.x + northMeters / metersPerDegreeLatitude;
        var longitudeScale = Mathf.Cos(latitude * Mathf.Deg2Rad) * metersPerDegreeLatitude;
        var longitude = latLon.y + eastMeters / Mathf.Max(1f, longitudeScale);
        return new Vector2(latitude, longitude);
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
