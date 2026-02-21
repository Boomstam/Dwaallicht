using UnityEngine;

public class MapCameraController : MonoBehaviour
{
    [Header("Map Reference")]
    public MapTest mapTest;

    [Header("Drag")]
    public float dragSpeed = 1f;

    [Header("Zoom")]
    public float zoomSpeed = 1f;
    public float minHeight = 100f;
    public float maxHeight = 5000f;

    private Vector3 _dragOrigin;
    private bool _isDragging;
    private bool _positioned = false;

    void Awake()
    {
        transform.rotation = Quaternion.Euler(90, 0, 0);
    }

    void Update()
    {
        if (!_positioned && mapTest != null && mapTest.isLoaded)
        {
            PositionOnMap();
            _positioned = true;
        }

        HandleDrag();
        HandleZoom();
    }

    void PositionOnMap()
    {
        float centerX = mapTest.mapCenter.x;
        float centerZ = mapTest.mapCenter.z;

        float largerDimension = Mathf.Max(mapTest.mapWidth, mapTest.mapHeight);
        float height = largerDimension * 0.7f;

        // Scale min/max height to tileWorldSize
        minHeight = mapTest.tileWorldSize * 0.1f;
        maxHeight = mapTest.tileWorldSize * 20f;

        height = Mathf.Clamp(height, minHeight, maxHeight);

        transform.position = new Vector3(centerX, height, centerZ);
        transform.rotation = Quaternion.Euler(90, 0, 0);
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

            // Scale by current height and tileWorldSize so panning feels consistent
            float scale = (transform.position.y / mapTest.tileWorldSize) * dragSpeed * 0.01f;
            Vector3 move = new Vector3(-delta.x * scale, 0, -delta.y * scale);
            transform.position += move;
        }
    }

    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.001f) return;

        // Scale zoom step by tileWorldSize
        float step = scroll * zoomSpeed * mapTest.tileWorldSize;
        float newY = transform.position.y - step;
        newY = Mathf.Clamp(newY, minHeight, maxHeight);
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }
}