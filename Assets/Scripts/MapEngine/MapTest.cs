using UnityEngine;

public class MapTest : MonoBehaviour
{
    public TileRenderer tilePrefab;
    public float tileWorldSize = 1000f;

    private const int ZOOM = 14;
    private const int MIN_X = 8384;
    private const int MIN_Y = 5471;

    private GameObject mapRoot;

    async void Start()
    {
        if (mapRoot != null)
            Destroy(mapRoot);

        mapRoot = new GameObject("MapRoot");

        var reader = new PMTilesReader();
        await reader.Initialize();

        if (mapRoot == null) return;

        var tiles = reader.GetAllTilesAtZoom(ZOOM);

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

            // Pass world offset directly into Render — geometry is baked in world space
            float offsetX = (x - MIN_X) * tileWorldSize;
            float offsetZ = (y - MIN_Y) * tileWorldSize;

            var tr = tileGO.GetComponent<TileRenderer>();
            tr.Render(layers, ZOOM, x, y, tileWorldSize, offsetX, offsetZ);
        }

        Debug.Log("All tiles loaded.");
    }
}