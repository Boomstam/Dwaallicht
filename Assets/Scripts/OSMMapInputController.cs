using System;
using UnityEngine;

/// <summary>
/// OSMMapInputController - Allows the player to pan (drag) and zoom (scroll wheel / pinch)
/// the map at runtime.
/// 
/// Attach to the same GameObject as OSMMapManager, or any active GameObject.
/// Uses a Camera looking straight down at the tile plane (Y-up, tiles on XZ plane).
/// </summary>
[RequireComponent(typeof(OSMMapManager))]
public class OSMMapInputController : MonoBehaviour
{
    [Header("Camera")]
    [Tooltip("Camera used for raycasting. Defaults to Camera.main.")]
    public Camera mapCamera;

    [Header("Pan Settings")]
    [Tooltip("Multiplier for mouse-drag panning speed.")]
    public float panSensitivity = 0.01f;

    [Tooltip("After releasing the mouse, apply this much inertia (0 = none, 0.95 = lots).")]
    [Range(0f, 0.99f)]
    public float inertiaDamping = 0.85f;

    [Header("Zoom Settings")]
    [Tooltip("Scroll wheel zoom sensitivity.")]
    public float scrollZoomSensitivity = 1f;

    [Tooltip("Seconds to wait after the last scroll event before reloading tiles.")]
    public float zoomReloadDelay = 0.5f;

    // -------------------------------------------------------------------------

    private OSMMapManager _map;
    private Vector3 _dragOrigin;
    private bool _isDragging;
    private Vector3 _inertiaVelocity;

    private float _zoomTimer;
    private bool _zoomDirty;

    // Track a virtual lat/lon offset (we move the tile root for smooth panning,
    // then snap to new coordinates on mouse-up or zoom).
    private Vector3 _tileRootOffset = Vector3.zero;

    private void Awake()
    {
        _map = GetComponent<OSMMapManager>();
        if (mapCamera == null) mapCamera = Camera.main;
    }

    private void Update()
    {
        HandleMousePan();
        HandleScrollZoom();
        ApplyInertia();
        HandleZoomReloadTimer();
    }

    // -------------------------------------------------------------------------

    private void HandleMousePan()
    {
        if (Input.GetMouseButtonDown(0))
        {
            _dragOrigin = GetWorldPoint(Input.mousePosition);
            _isDragging = true;
            _inertiaVelocity = Vector3.zero;
        }

        if (Input.GetMouseButton(0) && _isDragging)
        {
            Vector3 current = GetWorldPoint(Input.mousePosition);
            Vector3 delta = _dragOrigin - current;

            // Move the tiles root to create a panning feel
            Transform tilesRoot = _map.transform.Find("OSM_Tiles");
            if (tilesRoot != null)
            {
                tilesRoot.position -= delta * panSensitivity;
                _inertiaVelocity = -delta * panSensitivity;
            }

            _dragOrigin = current;
        }

        if (Input.GetMouseButtonUp(0))
        {
            _isDragging = false;
            // Commit panned offset back to lat/lon and reload
            CommitPanOffset();
        }
    }

    private void ApplyInertia()
    {
        if (_isDragging) return;
        if (_inertiaVelocity.sqrMagnitude < 0.0001f) return;

        Transform tilesRoot = _map.transform.Find("OSM_Tiles");
        if (tilesRoot != null)
            tilesRoot.position += _inertiaVelocity;

        _inertiaVelocity *= inertiaDamping;

        if (_inertiaVelocity.sqrMagnitude < 0.0001f)
        {
            _inertiaVelocity = Vector3.zero;
            CommitPanOffset();
        }
    }

    private void CommitPanOffset()
    {
        Transform tilesRoot = _map.transform.Find("OSM_Tiles");
        if (tilesRoot == null) return;

        // Convert tile-plane offset to lat/lon delta
        Vector3 offset = tilesRoot.position;
        double latDelta = -offset.z * LatDegreesPerUnit();
        double lonDelta =  offset.x * LonDegreesPerUnit();

        _map.latitude  += latDelta;
        _map.longitude += lonDelta;

        tilesRoot.position = Vector3.zero;
        _map.ReloadMap();
    }

    private void HandleScrollZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.001f) return;

        int delta = scroll > 0 ? 1 : -1;
        _map.zoomLevel = Mathf.Clamp(_map.zoomLevel + delta, 1, 19);
        _zoomDirty = true;
        _zoomTimer = zoomReloadDelay;
    }

    private void HandleZoomReloadTimer()
    {
        if (!_zoomDirty) return;
        _zoomTimer -= Time.deltaTime;
        if (_zoomTimer <= 0f)
        {
            _zoomDirty = false;
            _map.ReloadMap();
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private Vector3 GetWorldPoint(Vector3 screenPos)
    {
        Ray ray = mapCamera.ScreenPointToRay(screenPos);
        // Tiles lie on the XZ plane (y = 0 in world space, or the tile root's Y)
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out float dist))
            return ray.GetPoint(dist);
        return Vector3.zero;
    }

    /// <summary>How many latitude degrees correspond to one Unity world unit at the current zoom?</summary>
    private double LatDegreesPerUnit()
    {
        // One OSM tile covers 256 pixels, and we map it to tileSize Unity units.
        // At zoom z, the world is divided into 2^z tiles vertically (approx, Mercator).
        double tilesInHeight = Math.Pow(2, _map.zoomLevel);
        double degreesPerTile = 170.0 / tilesInHeight; // approx lat degrees per tile
        return degreesPerTile / _map.tileSize;
    }

    /// <summary>How many longitude degrees correspond to one Unity world unit at the current zoom?</summary>
    private double LonDegreesPerUnit()
    {
        double tilesInWidth = Math.Pow(2, _map.zoomLevel);
        double degreesPerTile = 360.0 / tilesInWidth;
        return degreesPerTile / _map.tileSize;
    }
}