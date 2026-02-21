using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;

public class MapTest : MonoBehaviour
{
    public TileRenderer tilePrefab;
    public MapZoom mapZoom;
    public float tileWorldSize = 1000f;

    private const int ZOOM  = 14;
    private const int MIN_X = 8384;
    private const int MIN_Y = 5471;

    private GameObject mapRoot;

    [HideInInspector] public Vector3 mapCenter;
    [HideInInspector] public float   mapWidth;
    [HideInInspector] public float   mapHeight;
    [HideInInspector] public bool    isLoaded = false;

    async void Start()
    {
        Destroy(mapRoot);
        mapRoot = new GameObject("MapRoot");

        var reader = new PMTilesReader();
        await reader.Initialize();

        var tiles = reader.GetAllTilesAtZoom(ZOOM);
        var sw    = System.Diagnostics.Stopwatch.StartNew();

        // ── Fetch all tile bytes in parallel ────────────────────────────────
        // IO is the bottleneck (49s sequential vs ~1s parallel).
        // PMTilesReader.ReadBytesAsync is thread-safe via its internal lock.
        Debug.Log($"[MapTest] Fetching {tiles.Count} tiles in parallel...");

        var fetchTasks = new Task<byte[]>[tiles.Count];
        for (int i = 0; i < tiles.Count; i++)
        {
            var (x, y) = tiles[i];
            fetchTasks[i] = reader.GetTile(ZOOM, x, y);
        }
        byte[][] allData = await Task.WhenAll(fetchTasks);

        long ioMs = sw.ElapsedMilliseconds;
        Debug.Log($"[MapTest] All IO done in {ioMs}ms");
        sw.Restart();

        // ── Parse + render on main thread (Unity API requires main thread) ──
        int minTileX = int.MaxValue, maxTileX = int.MinValue;
        int minTileY = int.MaxValue, maxTileY = int.MinValue;

        for (int i = 0; i < tiles.Count; i++)
        {
            var (x, y)   = tiles[i];
            byte[] data  = allData[i];
            if (data == null || data.Length == 0) continue;

            var layers = MVTParser.Parse(data);

            var tileGO = Instantiate(tilePrefab.gameObject);
            tileGO.name             = $"Tile_{x}_{y}";
            tileGO.transform.parent = mapRoot.transform;
            tileGO.SetActive(true);

            float offsetX = (x - MIN_X) * tileWorldSize;
            float offsetZ = (y - MIN_Y) * tileWorldSize;

            var tr = tileGO.GetComponent<TileRenderer>();
            tr.mapZoom = mapZoom;
            tr.Render(layers, ZOOM, x, y, tileWorldSize, offsetX, offsetZ);

            if (x < minTileX) minTileX = x; if (x > maxTileX) maxTileX = x;
            if (y < minTileY) minTileY = y; if (y > maxTileY) maxTileY = y;
        }

        long renderMs = sw.ElapsedMilliseconds;

        float worldMinX = (minTileX - MIN_X) * tileWorldSize;
        float worldMaxX = (maxTileX - MIN_X + 1) * tileWorldSize;
        float worldMinZ = (minTileY - MIN_Y) * tileWorldSize;
        float worldMaxZ = (maxTileY - MIN_Y + 1) * tileWorldSize;

        mapWidth  = worldMaxX - worldMinX;
        mapHeight = worldMaxZ - worldMinZ;
        mapCenter = new Vector3(worldMinX + mapWidth * 0.5f, 0, worldMinZ + mapHeight * 0.5f);
        isLoaded  = true;

        Debug.Log($"[MapTest] TOTAL {tiles.Count} tiles | io={ioMs}ms render={renderMs}ms");
        Debug.Log($"[MapTest] Center={mapCenter} Width={mapWidth} Height={mapHeight}");

        reader.Dispose();
    }
}