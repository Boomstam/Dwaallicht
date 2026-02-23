using UnityEngine;

public class MapCameraController : MonoBehaviour
{
    [Header("Map Reference")]
    public MapController mapController;
    public MapZoom mapZoom;

    [Header("Drag")]
    public float dragSpeed = 1f;

    [Header("Zoom")]
    public float zoomSpeed = 1f;
    public float minHeight = 100f;
    public float maxHeight = 5000f;

    private Vector3 _dragOrigin;
    private bool _isDragging;
    private bool _positioned = false;

    // Throttle logging to avoid spam
    private float _logTimer = 0f;
    private const float LogInterval = 1f;

    void Awake()
    {
        transform.rotation = Quaternion.Euler(90, 0, 180);
    }

    void Update()
    {
        if (!_positioned && mapController != null && mapController.isLoaded)
        {
            PositionOnMap();
            _positioned = true;
        }

        HandleDrag();
        HandleZoom();

        // Log zoom level once per second while moving
        _logTimer -= Time.deltaTime;
        if (_logTimer <= 0f)
        {            
            _logTimer = LogInterval;
        }
    }

    void PositionOnMap()
    {
        float centerX = mapController.mapCenter.x;
        float centerZ = mapController.mapCenter.z;

        float largerDimension = Mathf.Max(mapController.mapWidth, mapController.mapHeight);
        float height = largerDimension * 0.7f;

        minHeight = mapController.tileWorldSize * 0.1f;
        maxHeight = mapController.tileWorldSize * 20f;

        height = Mathf.Clamp(height, minHeight, maxHeight);

        transform.position = new Vector3(centerX, height, centerZ);
        transform.rotation = Quaternion.Euler(90, 0, 180);

        Debug.Log($"[Coords] Camera pos={transform.position}  screen-right (world)={transform.right}  screen-up (world)={transform.up}");

        Debug.Log($"[Camera] Positioned at center={mapController.mapCenter} height={height:F0}");
    }

    void HandleDrag()
    {
        if (Input.GetMouseButtonDown(0))
        {
            _dragOrigin = Input.mousePosition;
            _isDragging = true;
        }

        if (Input.GetMouseButtonUp(0))
            _isDragging = false;

        if (_isDragging && Input.GetMouseButton(0))
        {
            Vector3 delta = Input.mousePosition - _dragOrigin;
            _dragOrigin = Input.mousePosition;

            float scale = (transform.position.y / mapController.tileWorldSize) * dragSpeed * 0.01f;
            // Euler(90,0,180): screen-right=world+X, screen-up=world-Z
            Vector3 move = new Vector3(delta.x * scale, 0, delta.y * scale);
            transform.position += move;
        }
    }

    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.001f) return;

        float step = scroll * zoomSpeed * mapController.tileWorldSize;
        float newY = transform.position.y - step;
        newY = Mathf.Clamp(newY, minHeight, maxHeight);
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }
}