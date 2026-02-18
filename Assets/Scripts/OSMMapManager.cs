using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// OSMMapManager - Main controller for loading and displaying OpenStreetMap tiles in Unity.
/// Attach this to a GameObject in your scene.
/// </summary>
public class OSMMapManager : MonoBehaviour
{
    [Header("Map Settings")]
    [Tooltip("Latitude of the map center")]
    public double latitude = 48.8566f;  // Paris default

    [Tooltip("Longitude of the map center")]
    public double longitude = 2.3522f;

    [Range(1, 19)]
    [Tooltip("Zoom level (1=world, 19=street level)")]
    public int zoomLevel = 14;

    [Tooltip("Number of tiles to load in each direction from center")]
    [Range(1, 5)]
    public int tileRadius = 2;

    [Header("Tile Source")]
    [Tooltip("Tile server URL. Use {z}/{x}/{y} placeholders.")]
    public string tileUrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    [Header("Tile Appearance")]
    [Tooltip("Size of each tile in Unity world units")]
    public float tileSize = 10f;

    [Tooltip("Material to use for tiles. Leave null to auto-create.")]
    public Material tileMaterial;

    [Tooltip("Shader to use when auto-creating tile material")]
    public string tileShader = "Universal Render Pipeline/Lit";

    [Header("Tile Caching")]
    [Tooltip("Enable in-memory texture caching")]
    public bool enableCache = true;

    [Header("Events")]
    public UnityEngine.Events.UnityEvent onMapLoaded;

    // Internal state
    private Dictionary<Vector2Int, GameObject> _activeTiles = new Dictionary<Vector2Int, GameObject>();
    private Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>();
    private Transform _tilesRoot;
    private int _pendingLoads = 0;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Set a new center location and reload the map.</summary>
    public void SetLocation(double lat, double lon)
    {
        latitude = lat;
        longitude = lon;
        ReloadMap();
    }

    /// <summary>Set zoom level and reload the map.</summary>
    public void SetZoom(int zoom)
    {
        zoomLevel = Mathf.Clamp(zoom, 1, 19);
        ReloadMap();
    }

    /// <summary>Change the tile URL template (e.g., switch to a different map style) and reload.</summary>
    public void SetTileSource(string urlTemplate)
    {
        tileUrlTemplate = urlTemplate;
        _textureCache.Clear(); // cached textures are from the old source
        ReloadMap();
    }

    /// <summary>Apply a new material to all existing tiles.</summary>
    public void ApplyMaterialToAllTiles(Material mat)
    {
        tileMaterial = mat;
        foreach (var tile in _activeTiles.Values)
        {
            var renderer = tile.GetComponent<Renderer>();
            if (renderer != null)
                renderer.material = mat;
        }
    }

    /// <summary>Fully reload all tiles.</summary>
    public void ReloadMap()
    {
        StopAllCoroutines();
        ClearTiles();
        LoadVisibleTiles();
    }

    // -------------------------------------------------------------------------
    // Unity Lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        // Create a root object to keep the hierarchy tidy
        _tilesRoot = new GameObject("OSM_Tiles").transform;
        _tilesRoot.SetParent(transform);

        // Auto-create material if none supplied
        if (tileMaterial == null)
            tileMaterial = CreateDefaultMaterial();

        LoadVisibleTiles();
    }

    // -------------------------------------------------------------------------
    // Tile Loading
    // -------------------------------------------------------------------------

    private void LoadVisibleTiles()
    {
        Vector2Int centerTile = LatLonToTile(latitude, longitude, zoomLevel);
        int total = 0;

        for (int dx = -tileRadius; dx <= tileRadius; dx++)
        {
            for (int dy = -tileRadius; dy <= tileRadius; dy++)
            {
                Vector2Int tileCoord = new Vector2Int(centerTile.x + dx, centerTile.y + dy);
                if (!_activeTiles.ContainsKey(tileCoord))
                {
                    SpawnTilePlaceholder(tileCoord, dx, dy);
                    StartCoroutine(LoadTileTexture(tileCoord));
                    total++;
                }
            }
        }

        _pendingLoads = total;
        if (total == 0) onMapLoaded?.Invoke();
    }

    private void SpawnTilePlaceholder(Vector2Int tileCoord, int dx, int dy)
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
        tile.name = $"Tile_{tileCoord.x}_{tileCoord.y}";
        tile.transform.SetParent(_tilesRoot);
        tile.transform.localPosition = new Vector3(dx * tileSize, 0f, -dy * tileSize);
        tile.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        tile.transform.localScale = new Vector3(tileSize, tileSize, 1f);

        // Remove the collider (not needed for a map)
        Destroy(tile.GetComponent<Collider>());

        var renderer = tile.GetComponent<Renderer>();
        renderer.material = new Material(tileMaterial); // unique instance per tile

        _activeTiles[tileCoord] = tile;
    }

    private IEnumerator LoadTileTexture(Vector2Int tileCoord)
    {
        string url = BuildTileUrl(tileCoord.x, tileCoord.y, zoomLevel);

        // Check cache first
        if (enableCache && _textureCache.TryGetValue(url, out Texture2D cached))
        {
            ApplyTextureToTile(tileCoord, cached);
            CheckAllLoaded();
            yield break;
        }

        using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
        {
            // OSM requires a User-Agent header
            req.SetRequestHeader("User-Agent", "UnityOSMLoader/1.0 (contact@example.com)");
            yield return req.SendWebRequest();

            if (req.result == UnityWebRequest.Result.Success)
            {
                Texture2D tex = DownloadHandlerTexture.GetContent(req);
                tex.wrapMode = TextureWrapMode.Clamp;

                if (enableCache)
                    _textureCache[url] = tex;

                ApplyTextureToTile(tileCoord, tex);
            }
            else
            {
                Debug.LogWarning($"[OSMMapManager] Failed to load tile {url}: {req.error}");
            }
        }

        CheckAllLoaded();
    }

    private void ApplyTextureToTile(Vector2Int tileCoord, Texture2D tex)
    {
        if (_activeTiles.TryGetValue(tileCoord, out GameObject tile))
        {
            var renderer = tile.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.mainTexture = tex;
            }
        }
    }

    private void CheckAllLoaded()
    {
        _pendingLoads--;
        if (_pendingLoads <= 0)
            onMapLoaded?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Utility
    // -------------------------------------------------------------------------

    private void ClearTiles()
    {
        foreach (var tile in _activeTiles.Values)
            Destroy(tile);
        _activeTiles.Clear();
    }

    private string BuildTileUrl(int x, int y, int z)
    {
        return tileUrlTemplate
            .Replace("{z}", z.ToString())
            .Replace("{x}", x.ToString())
            .Replace("{y}", y.ToString());
    }

    /// <summary>Convert lat/lon to OSM tile coordinates at the given zoom level.</summary>
    public static Vector2Int LatLonToTile(double lat, double lon, int zoom)
    {
        int n = 1 << zoom; // 2^zoom
        int x = (int)Math.Floor((lon + 180.0) / 360.0 * n);
        double latRad = lat * Math.PI / 180.0;
        int y = (int)Math.Floor((1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * n);
        return new Vector2Int(x, y);
    }

    /// <summary>Convert an OSM tile coordinate back to lat/lon (NW corner).</summary>
    public static Vector2 TileToLatLon(int x, int y, int zoom)
    {
        int n = 1 << zoom;
        double lon = x / (double)n * 360.0 - 180.0;
        double latRad = Math.Atan(Math.Sinh(Math.PI * (1 - 2.0 * y / n)));
        double lat = latRad * 180.0 / Math.PI;
        return new Vector2((float)lat, (float)lon);
    }

    private Material CreateDefaultMaterial()
    {
        Shader shader = Shader.Find(tileShader);
        if (shader == null)
        {
            // Fallback to Standard or Unlit
            shader = Shader.Find("Standard") ?? Shader.Find("Unlit/Texture");
        }
        var mat = new Material(shader);
        mat.name = "OSM_TileMaterial";
        return mat;
    }

    /// <summary>Returns the material instance of every currently active tile.</summary>
    public IEnumerable<Material> GetActiveTileMaterials()
    {
        foreach (var tile in _activeTiles.Values)
        {
            var r = tile.GetComponent<Renderer>();
            if (r != null) yield return r.material;
        }
    }

    private void OnDestroy()
    {
        // Clean up cached textures from memory
        foreach (var tex in _textureCache.Values)
            Destroy(tex);
        _textureCache.Clear();
    }
}