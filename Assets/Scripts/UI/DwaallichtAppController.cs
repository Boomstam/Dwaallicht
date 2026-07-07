using System.Collections.Generic;
using Dwaallicht.AR;
using Dwaallicht.Navigation;
using UnityEngine;
using UnityEngine.EventSystems;
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
    private static readonly Vector2 AppMapCenterLatLon = new Vector2(51.18623f, 4.22974f);
    private static readonly Vector2 PhoneMapPosition = new Vector2(172f, -306f);
    private static readonly Vector2 MapDesignSize = new Vector2(342f, 696f);
    private const float MapMetersToPixels = 0.18f;

    private readonly string[] tabIds = { "K", "M", "L", "S" };
    private readonly Dictionary<string, Button> buttons = new Dictionary<string, Button>();

    [SerializeField, Range(0, 3)]
    private int activeTab;
    [SerializeField]
    private bool showDeviceDebug = true;
    [SerializeField]
    private bool showAdminPoiControls = true;

    private RectTransform contentRoot;
    private RectTransform tabRoot;
    private RectTransform compassRose;
    private RectTransform mapFacingArrow;
    private Image appBackgroundImage;
    private Text debugText;
    private Text navigationText;
    private Text headingText;
    private DwaallichtKrefelArScanner arScanner;
    private CompassHeadingProvider headingProvider;
    private PoiManager poiManager;
    private Font font;

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
        var map = AddRect(parent, "MapGraphic", Vector2.zero, Vector2.one, new Vector2(24f, 24f), new Vector2(-24f, -20f));
        AddImage(map, "MapFill", AppBackground, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        AddPolyline(map, "RiverStroke", Ink, 9f, new[]
        {
            new Vector2(-0.05f, 0.62f),
            new Vector2(0.16f, 0.58f),
            new Vector2(0.24f, 0.48f),
            new Vector2(0.34f, 0.43f),
            new Vector2(0.61f, 0.43f),
            new Vector2(0.73f, 0.37f),
            new Vector2(0.88f, 0.23f),
            new Vector2(1.06f, 0.16f),
        });
        AddPolyline(map, "RiverFill", Paper, 6f, new[]
        {
            new Vector2(-0.05f, 0.62f),
            new Vector2(0.16f, 0.58f),
            new Vector2(0.24f, 0.48f),
            new Vector2(0.34f, 0.43f),
            new Vector2(0.61f, 0.43f),
            new Vector2(0.73f, 0.37f),
            new Vector2(0.88f, 0.23f),
            new Vector2(1.06f, 0.16f),
        });
        AddPolyline(map, "RoadStroke", Ink, 7f, new[]
        {
            new Vector2(-0.08f, 0.52f),
            new Vector2(0.14f, 0.49f),
            new Vector2(0.22f, 0.36f),
            new Vector2(0.34f, 0.34f),
            new Vector2(0.49f, 0.29f),
            new Vector2(0.64f, 0.18f),
            new Vector2(0.67f, 0.11f),
            new Vector2(0.85f, 0.04f),
            new Vector2(1.04f, 0.01f),
        });
        AddPolyline(map, "RoadFill", AppBackground, 4f, new[]
        {
            new Vector2(-0.08f, 0.52f),
            new Vector2(0.14f, 0.49f),
            new Vector2(0.22f, 0.36f),
            new Vector2(0.34f, 0.34f),
            new Vector2(0.49f, 0.29f),
            new Vector2(0.64f, 0.18f),
            new Vector2(0.67f, 0.11f),
            new Vector2(0.85f, 0.04f),
            new Vector2(1.04f, 0.01f),
        });

        var selectedPoi = poiManager != null ? poiManager.SelectedPoi : null;
        if (selectedPoi != null)
        {
            var selectedPosition = MapLatLonToAnchoredPosition(selectedPoi.LatLon);
            AddPolyline(map, "RouteToSelectedPoi", Ink, 4f, new[]
            {
                ToMapNormalized(PhoneMapPosition),
                ToMapNormalized(selectedPosition),
            });
        }

        AddTreeCluster(map);
        AddFactory(map);
        AddMapPois(map);
        AddCircle(map, "PhonePosition", Paper, Vector2.one * 42f, new Vector2(172f, -306f), true, Ink, 3f, new Vector2(0f, 1f), new Vector2(0f, 1f));
        mapFacingArrow = AddArrow(map, "PhoneFacingDirection", Ink, new Vector2(176f, -278f), new Vector2(34f, 88f), 0f);
        AddText(map, "you are here", 24, FontStyle.Normal, Ink, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-58f, 110f), new Vector2(190f, 50f));
        navigationText = AddText(map, "", 15, FontStyle.Bold, Ink, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, new Vector2(10f, 10f), new Vector2(-10f, -600f));
        if (showDeviceDebug)
        {
            debugText = AddText(map, "", 12, FontStyle.Normal, Ink, TextAnchor.LowerLeft, Vector2.zero, Vector2.one, new Vector2(10f, 54f), new Vector2(-10f, -548f));
        }

        if (showAdminPoiControls)
        {
            AddCommandButton(map, "POI +", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-82f, -54f), new Vector2(72f, 34f), AddPoiAhead);
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

        arScanner = FindFirstObjectByType<DwaallichtKrefelArScanner>(FindObjectsInactive.Include);
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
            var pin = AddPin(map, poi.color, position, isSelected ? 38f : 30f);
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

    private static Vector2 MapLatLonToAnchoredPosition(Vector2 latLon)
    {
        const float metersPerDegreeLatitude = 111320f;
        var northMeters = (latLon.x - AppMapCenterLatLon.x) * metersPerDegreeLatitude;
        var eastMeters = (latLon.y - AppMapCenterLatLon.y) * Mathf.Cos(AppMapCenterLatLon.x * Mathf.Deg2Rad) * metersPerDegreeLatitude;
        return new Vector2(PhoneMapPosition.x + eastMeters * MapMetersToPixels, PhoneMapPosition.y - northMeters * MapMetersToPixels);
    }

    private static Vector2 ToMapNormalized(Vector2 anchoredFromTopLeft)
    {
        return new Vector2(
            Mathf.Clamp01(anchoredFromTopLeft.x / MapDesignSize.x),
            Mathf.Clamp01((MapDesignSize.y + anchoredFromTopLeft.y) / MapDesignSize.y));
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
