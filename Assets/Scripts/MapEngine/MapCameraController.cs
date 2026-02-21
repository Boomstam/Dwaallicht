using UnityEngine;

public class MapCameraController : MonoBehaviour
{
    [Header("Start Position")]
    public float startX = 6000f;
    public float startZ = 6000f;
    public float startHeight = 2000f;

    [Header("Drag")]
    public float dragSpeed = 1f;

    [Header("Zoom")]
    public float zoomSpeed = 100f;
    public float minHeight = 100f;
    public float maxHeight = 5000f;

    private Vector3 _dragOrigin;
    private bool _isDragging;

    void Awake()
    {
        Debug.Log("Camera awake");
        // Point straight down and set start position
        transform.position = new Vector3(startX, startHeight, startZ);
        transform.rotation = Quaternion.Euler(90, 0, 0);
    }

    void Update()
    {
        HandleDrag();
        HandleZoom();
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

            float scale = transform.position.y * dragSpeed * 0.001f;
            Vector3 move = new Vector3(-delta.x * scale, 0, -delta.y * scale);
            transform.position += move;
        }
    }

    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) < 0.001f) return;

        float newY = transform.position.y - scroll * zoomSpeed;
        newY = Mathf.Clamp(newY, minHeight, maxHeight);
        transform.position = new Vector3(transform.position.x, newY, transform.position.z);
    }
}