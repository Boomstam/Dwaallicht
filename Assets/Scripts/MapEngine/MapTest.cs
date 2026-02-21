using UnityEngine;

public class MapTest : MonoBehaviour
{
    public TileRenderer tilePrefab;
    public float tileWorldSize = 1000f;

    private const int DEBUG_ZOOM = 14;
    private const int DEBUG_X = 8389;
    private const int DEBUG_Y = 5479;

    private GameObject mapRoot;

    async void Start()
    {
        if (mapRoot != null)
            Destroy(mapRoot);

        mapRoot = new GameObject("MapRoot");

        var reader = new PMTilesReader();
        await reader.Initialize();

        if (mapRoot == null) return;

        byte[] tileData = await reader.GetTile(DEBUG_ZOOM, DEBUG_X, DEBUG_Y);

        if (mapRoot == null) return;
        if (tileData == null) { Debug.LogError("Tile is null."); return; }

        var layers = MVTParser.Parse(tileData);

        var tileGO = Instantiate(tilePrefab.gameObject);
        tileGO.name = $"Tile_{DEBUG_X}_{DEBUG_Y}";
        tileGO.transform.parent = mapRoot.transform;
        tileGO.transform.localPosition = Vector3.zero;
        tileGO.SetActive(true);

        var tr = tileGO.GetComponent<TileRenderer>();
        tr.Render(layers, DEBUG_ZOOM, DEBUG_X, DEBUG_Y, tileWorldSize);

        // Log all labels and their positions
        Debug.Log("=== LABELS ===");
        foreach (Transform child in tileGO.transform)
            if (child.name.StartsWith("label:"))
                Debug.Log($"LABEL '{child.name}' localPos={child.localPosition}");
    }
}