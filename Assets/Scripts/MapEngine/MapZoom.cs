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

    // Screen width in meters at current camera height
    // Standard OSM: 1 screen pixel = X meters at zoom Z
    // At zoom 0: 156543m/px, each zoom doubles resolution
    private const float MetersPerPixelAtZoom0 = 156543f;

    void Start()
    {
        _metersPerUnit = RealWorldTileWidthMeters / mapTest.tileWorldSize;
    }

    void Update()
    {
        if (mapTest == null || !mapTest.isLoaded) return;

        float cameraHeight = transform.position.y;

        // Camera height in world units → height in meters
        float heightMeters = cameraHeight * _metersPerUnit;

        // Derive meters per pixel (assuming screen width ~1000px for simplicity,
        // use actual screen width for accuracy)
        float screenWidthPx = Screen.width;
        float metersPerPixel = (heightMeters * 2f) / screenWidthPx;

        // OSM zoom formula: zoom = log2(MetersPerPixelAtZoom0 / metersPerPixel)
        float zoom = Mathf.Log(MetersPerPixelAtZoom0 / metersPerPixel, 2f);
        VisualZoom = Mathf.Clamp(zoom, minVisualZoom, maxVisualZoom);
    }

    // Utility: get a value interpolated between two zoom levels
    // e.g. GetZoomValue(14, 1f, 18, 8f) returns line width scaled between zoom 14 and 18
    public float GetZoomValue(float zoomA, float valueA, float zoomB, float valueB)
    {
        float t = Mathf.InverseLerp(zoomA, zoomB, VisualZoom);
        return Mathf.Lerp(valueA, valueB, t);
    }
}