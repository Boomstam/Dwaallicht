using UnityEngine;

/// <summary>
/// Converts camera height to an OSM-style visual zoom level (float).
/// At tileWorldSize=1000, one tile represents a real-world tile at zoom 14.
/// A real zoom-14 tile is 9784m wide at the latitude of Belgium (~51°N).
/// We use this to derive a continuous visual zoom from camera height.
///
/// Zoom direction: LOWER number = more zoomed OUT (overview).
///                 HIGHER number = more zoomed IN (street level).
/// minVisualZoom (~10) corresponds to the overview camera height.
/// maxVisualZoom (~18) corresponds to maximum zoom-in.
/// </summary>
public class MapZoom : MonoBehaviour
{
    [Header("Map Reference")]
    public MapController mapController;

    [Header("Zoom Range")]
    public float minVisualZoom = 10f;
    public float maxVisualZoom = 18f;

    // The current visual zoom level - read by other components
    public float VisualZoom { get; private set; }

    // Real-world width of a zoom-14 tile at 51°N latitude in meters
    private const float RealWorldTileWidthMeters = 9784f;

    // Meters per world unit (derived from tileWorldSize and real tile width)
    private float _metersPerUnit;

    // Standard OSM: 1 screen pixel = X meters at zoom Z
    // At zoom 0: 156543m/px, each zoom doubles resolution
    private const float MetersPerPixelAtZoom0 = 156543f;

    void Start()
    {
        _metersPerUnit = RealWorldTileWidthMeters / mapController.tileWorldSize;
        Debug.Log($"[MapZoom] metersPerUnit={_metersPerUnit:F3}");
    }

    private bool _loggedStartupZoom = false;

    void Update()
    {
        if (mapController == null || !mapController.isLoaded) return;

        float heightMeters   = transform.position.y * _metersPerUnit;
        float metersPerPixel = (heightMeters * 2f) / Screen.width;
        float rawZoom        = Mathf.Log(MetersPerPixelAtZoom0 / metersPerPixel, 2f);

        float snapped = Mathf.Round(Mathf.Clamp(rawZoom, minVisualZoom, maxVisualZoom) * 100f) / 100f;

        if (!_loggedStartupZoom)
        {
            _loggedStartupZoom = true;
            Debug.Log($"[MapZoom] First zoom calc: cameraY={transform.position.y:F0} heightMeters={heightMeters:F0} rawZoom={rawZoom:F2} snapped={snapped:F2} (range {minVisualZoom}-{maxVisualZoom})");
        }

        if (snapped != VisualZoom)
            VisualZoom = snapped;
    }

    // Utility: get a value interpolated between two zoom levels
    public float GetZoomValue(float zoomA, float valueA, float zoomB, float valueB)
    {
        float t = Mathf.InverseLerp(zoomA, zoomB, VisualZoom);
        return Mathf.Lerp(valueA, valueB, t);
    }
}