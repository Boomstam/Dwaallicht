using UnityEngine;
using System.Collections.Generic;
using System.Linq;
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

        // ── Step 1: Fetch all tile bytes in parallel ──────────────────────────
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

        // ── Step 2: Parse MVT + collect geometry in parallel (no Unity API) ───
        var collectTasks = allData.Select((data, i) =>
        {
            var (x, y)  = tiles[i];
            float offX  = (x - MIN_X) * tileWorldSize;
            float offZ  = (y - MIN_Y) * tileWorldSize;

            return Task.Run(() =>
            {
                if (data == null || data.Length == 0) return null;
                var layers = MVTParser.Parse(data);
                return TileGeometryCollector.Collect(layers, x, y, tileWorldSize, offX, offZ);
            });
        });

        TileGeometryCollector.TileGeometryData[] geometries =
            await Task.WhenAll(collectTasks);

        long collectMs = sw.ElapsedMilliseconds;
        Debug.Log($"[MapTest] All collect done in {collectMs}ms");
        sw.Restart();

        // ── Step 3: Upload geometry on main thread (Unity API) ────────────────
        int minTileX = int.MaxValue, maxTileX = int.MinValue;
        int minTileY = int.MaxValue, maxTileY = int.MinValue;

        for (int i = 0; i < geometries.Length; i++)
        {
            var geo = geometries[i];
            if (geo == null) continue;

            var tileGO = Instantiate(tilePrefab.gameObject);
            tileGO.name             = $"Tile_{geo.tileX}_{geo.tileY}";
            tileGO.transform.parent = mapRoot.transform;
            tileGO.SetActive(true);

            var tr = tileGO.GetComponent<TileRenderer>();
            tr.mapZoom = mapZoom;
            tr.UploadGeometry(geo);

            if (geo.tileX < minTileX) minTileX = geo.tileX;
            if (geo.tileX > maxTileX) maxTileX = geo.tileX;
            if (geo.tileY < minTileY) minTileY = geo.tileY;
            if (geo.tileY > maxTileY) maxTileY = geo.tileY;
        }

        long uploadMs = sw.ElapsedMilliseconds;

        float worldMinX = (minTileX - MIN_X) * tileWorldSize;
        float worldMaxX = (maxTileX - MIN_X + 1) * tileWorldSize;
        float worldMinZ = (minTileY - MIN_Y) * tileWorldSize;
        float worldMaxZ = (maxTileY - MIN_Y + 1) * tileWorldSize;

        mapWidth  = worldMaxX - worldMinX;
        mapHeight = worldMaxZ - worldMinZ;
        mapCenter = new Vector3(worldMinX + mapWidth * 0.5f, 0, worldMinZ + mapHeight * 0.5f);
        isLoaded  = true;

        Debug.Log($"[MapTest] TOTAL {tiles.Count} tiles | io={ioMs}ms collect={collectMs}ms upload={uploadMs}ms");
        Debug.Log($"[MapTest] Center={mapCenter} Width={mapWidth} Height={mapHeight}");

        reader.Dispose();
    }
}