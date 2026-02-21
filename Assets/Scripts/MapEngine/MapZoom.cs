using UnityEngine;

/// <summary>
/// Converts camera height to an OSM-style visual zoom level (float).
/// At tileWorldSize=1000, one tile represents a real-world tile at zoom 14.
/// A real zoom-14 tile is 9784m wide at the latitude of Belgium (~51°N).
/// We use this to derive a continuous visual zoom from camera height.
/// </summary>
public class MapZoom : MonoBehaviour
{
    [Header("Map Reference")]
    public MapTest mapTest;

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
        _metersPerUnit = RealWorldTileWidthMeters / mapTest.tileWorldSize;
        Debug.Log($"[MapZoom] metersPerUnit={_metersPerUnit:F3}");
    }

    void Update()
    {
        if (mapTest == null || !mapTest.isLoaded) return;

        float heightMeters   = transform.position.y * _metersPerUnit;
        float metersPerPixel = (heightMeters * 2f) / Screen.width;
        float rawZoom        = Mathf.Log(MetersPerPixelAtZoom0 / metersPerPixel, 2f);

        // Snap to 2 decimal places.
        // This kills floating-point noise from Mathf.Log that would otherwise
        // produce a continuously drifting value, causing TileRenderer to
        // trigger ribbon rebuilds on every frame even at rest.
        float snapped = Mathf.Round(Mathf.Clamp(rawZoom, minVisualZoom, maxVisualZoom) * 100f) / 100f;

        // Only write when actually changed — downstream comparisons stay stable.
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