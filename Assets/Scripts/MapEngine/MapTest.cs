using UnityEngine;

public class MapTest : MonoBehaviour
{
    public TileRenderer tilePrefab;
    public float tileWorldSize = 10f;

    private GameObject mapRoot;

    async void Start()
    {
        if (mapRoot != null)
            Destroy(mapRoot);

        mapRoot = new GameObject("MapRoot");

        var reader = new PMTilesReader();
        await reader.Initialize();

        if (mapRoot == null) return;

        var tiles = reader.GetAllTilesAtZoom(14);
        Debug.Log($"Loading {tiles.Count} tiles...");

        int minX = 8384, minY = 5471, zoom = 14;

        foreach (var (x, y) in tiles)
        {
            if (mapRoot == null) return;

            byte[] tileData = await reader.GetTile(zoom, x, y);

            if (mapRoot == null) return;
            if (tileData == null) continue;

            var layers = MVTParser.Parse(tileData);

            var tileGO = Instantiate(tilePrefab.gameObject);
            tileGO.name = $"Tile_{x}_{y}";
            tileGO.transform.parent = mapRoot.transform;
            tileGO.transform.localPosition = new Vector3(
                (x - minX) * tileWorldSize,
                0,
                (y - minY) * tileWorldSize
            );
            tileGO.SetActive(true);

            var tr = tileGO.GetComponent<TileRenderer>();
            tr.Render(layers, zoom, x, y, tileWorldSize);
        }

        Debug.Log($"All tiles loaded. tileWorldSize={tileWorldSize}");
    }
}