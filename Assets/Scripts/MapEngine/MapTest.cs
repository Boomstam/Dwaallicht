using UnityEngine;

public class MapTest : MonoBehaviour
{
    public TileRenderer tilePrefab;
    public float tileWorldSize = 1000f;

    private const int ZOOM = 14;
    private const int MIN_X = 8384;
    private const int MIN_Y = 5471;

    private GameObject mapRoot;

    // Exposed for camera to read after loading
    [HideInInspector] public Vector3 mapCenter;
    [HideInInspector] public float mapWidth;
    [HideInInspector] public float mapHeight;
    [HideInInspector] public bool isLoaded = false;

    async void Start()
    {
        if (mapRoot != null)
            Destroy(mapRoot);

        mapRoot = new GameObject("MapRoot");

        var reader = new PMTilesReader();
        await reader.Initialize();

        if (mapRoot == null) return;

        var tiles = reader.GetAllTilesAtZoom(ZOOM);

        int minTileX = int.MaxValue, maxTileX = int.MinValue;
        int minTileY = int.MaxValue, maxTileY = int.MinValue;

        foreach (var (x, y) in tiles)
        {
            if (mapRoot == null) return;

            byte[] tileData = await reader.GetTile(ZOOM, x, y);

            if (mapRoot == null) return;
            if (tileData == null) continue;

            var layers = MVTParser.Parse(tileData);

            var tileGO = Instantiate(tilePrefab.gameObject);
            tileGO.name = $"Tile_{x}_{y}";
            tileGO.transform.parent = mapRoot.transform;
            tileGO.transform.localPosition = Vector3.zero;
            tileGO.SetActive(true);

            float offsetX = (x - MIN_X) * tileWorldSize;
            float offsetZ = (y - MIN_Y) * tileWorldSize;

            var tr = tileGO.GetComponent<TileRenderer>();
            tr.Render(layers, ZOOM, x, y, tileWorldSize, offsetX, offsetZ);

            if (x < minTileX) minTileX = x;
            if (x > maxTileX) maxTileX = x;
            if (y < minTileY) minTileY = y;
            if (y > maxTileY) maxTileY = y;
        }

        // Calculate map bounds in world space
        float worldMinX = (minTileX - MIN_X) * tileWorldSize;
        float worldMaxX = (maxTileX - MIN_X + 1) * tileWorldSize;
        float worldMinZ = (minTileY - MIN_Y) * tileWorldSize;
        float worldMaxZ = (maxTileY - MIN_Y + 1) * tileWorldSize;

        mapWidth  = worldMaxX - worldMinX;
        mapHeight = worldMaxZ - worldMinZ;
        mapCenter = new Vector3(
            worldMinX + mapWidth * 0.5f,
            0,
            worldMinZ + mapHeight * 0.5f
        );

        isLoaded = true;
        Debug.Log($"All tiles loaded. Center={mapCenter} Width={mapWidth} Height={mapHeight}");
    }
}