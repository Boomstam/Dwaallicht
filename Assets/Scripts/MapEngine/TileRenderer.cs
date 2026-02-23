using UnityEngine;
using System.Collections.Generic;
using TMPro;
using EarcutNet;

/// <summary>
/// Renders a single MVT tile.
///
/// Geometry collection (CPU-heavy) is now done off the main thread by
/// TileGeometryCollector.Collect(). This class only handles the Unity API
/// upload phase (UploadGeometry) and the per-frame zoom-driven ribbon/label
/// updates (Update).
/// </summary>
public class TileRenderer : MonoBehaviour
{
    public MapMaterials  materials;
    public TMP_FontAsset labelFont;
    public MapZoom       mapZoom;

    // ── Zoom rebuild threshold ────────────────────────────────────────────────
    private const float ZoomRebuildThreshold = 0.15f;

    // ── Internal state ────────────────────────────────────────────────────────
    private float _tileWorldSize;

    // One entry per (matKey, featureClass) combination for lines
    private struct LineMaterialGroup
    {
        public string   featureClass;
        public float    minZoom;
        public int      extent;
        public MeshFilter   mf;
        public MeshRenderer mr;
        public List<List<(int x, int y)>> segments;
    }
    private List<LineMaterialGroup> _lineGroups = new();

    // Labels
    private struct ZoomLabel
    {
        public TextMeshPro tmp;
        public string      layerName;
        public float       minZoom;
    }
    private List<ZoomLabel> _labels = new();

    // Zoom tracking
    private float _lastBuiltZoom = -999f;

    // Update diagnostics (static = aggregated across all tile instances)
    private static float _updateLogTimer     = 0f;
    private static int   _updateRebuildCount = 0;
    private static float _updateRebuildMs    = 0f;

    // Fallback material cache (shared across all TileRenderer instances)
    private static Dictionary<string, Material> _defaultMaterialCache = new();

    // ═════════════════════════════════════════════════════════════════════════
    //  Public API — called on the main thread after background collection
    // ═════════════════════════════════════════════════════════════════════════

    public void UploadGeometry(TileGeometryCollector.TileGeometryData geo)
    {
        _offsetX   = geo.offsetX;
        _offsetZ   = geo.offsetZ;
        _worldSize = geo.worldSize;
        _lineGroups.Clear();
        _labels.Clear();
        _lastBuiltZoom = -999f;

        transform.localPosition = Vector3.zero;

        // ── Polygon meshes ────────────────────────────────────────────────────
        foreach (var pd in geo.polyMeshes)
        {
            if (pd.verts.Length == 0) continue;

            var go = new GameObject("poly_" + pd.matKey);
            go.transform.SetParent(transform, false);

            var mesh = new Mesh { name = pd.matKey };
            if (pd.verts.Length / 3 > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            // Convert flat float[] → Vector3[]
            int vertCount = pd.verts.Length / 3;
            var v3 = new Vector3[vertCount];
            for (int i = 0; i < vertCount; i++)
                v3[i] = new Vector3(pd.verts[i * 3], pd.verts[i * 3 + 1], pd.verts[i * 3 + 2]);

            mesh.vertices  = v3;
            mesh.triangles = pd.tris;

            var mat = ResolveMat(pd.layerName, pd.featureClass, "");
            go.AddComponent<MeshFilter>().sharedMesh       = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        // ── Line groups ───────────────────────────────────────────────────────
        foreach (var ld in geo.lineGroups)
        {
            var go = new GameObject("lines_" + ld.featureClass);
            go.transform.SetParent(transform, false);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = ResolveMat(ld.layerName, ld.featureClass, "");

            _lineGroups.Add(new LineMaterialGroup
            {
                featureClass = ld.featureClass,
                minZoom      = ld.minZoom,
                extent       = ld.extent,
                mf           = mf,
                mr           = mr,
                segments     = ld.segments,
            });
        }

        // ── Labels ────────────────────────────────────────────────────────────
        float initialZoom = (mapZoom != null) ? mapZoom.VisualZoom : 14f;

        foreach (var lbl in geo.labels)
        {
            var go = new GameObject("label:" + lbl.text);
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(lbl.wx, lbl.wy, lbl.wz);
            go.transform.rotation = Quaternion.Euler(90, 0, 0);

            var tmp       = go.AddComponent<TextMeshPro>();
            tmp.font      = labelFont;
            tmp.text      = lbl.text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color     = Color.white;

            float minZoom = GetLabelMinZoom(lbl.layerName);
            _labels.Add(new ZoomLabel { tmp = tmp, layerName = lbl.layerName, minZoom = minZoom });

            bool visible = initialZoom >= minZoom;
            go.SetActive(visible);
            if (visible)
                tmp.fontSize = GetZoomedFontSize(lbl.layerName, initialZoom);
        }

        // Build initial line ribbons
        BuildAllLineRibbons(initialZoom);
        _lastBuiltZoom = initialZoom;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Unity Update — only acts when zoom changes meaningfully
    // ═════════════════════════════════════════════════════════════════════════

    void Update()
    {
        if (mapZoom == null) return;
        float zoom = mapZoom.VisualZoom;

        _updateLogTimer -= Time.deltaTime;
        if (_updateLogTimer <= 0f)
        {
            Debug.Log($"[TileRenderer.Update x2s] rebuilds={_updateRebuildCount} tiles triggered, totalRibbonMs={_updateRebuildMs:F1}ms");
            _updateLogTimer     = 2f;
            _updateRebuildCount = 0;
            _updateRebuildMs    = 0f;
        }

        if (Mathf.Abs(zoom - _lastBuiltZoom) < ZoomRebuildThreshold) return;
        _lastBuiltZoom = zoom;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        for (int i = 0; i < _lineGroups.Count; i++)
        {
            var group   = _lineGroups[i];
            bool visible = zoom >= group.minZoom;
            group.mr.enabled = visible;
            if (visible)
                RebuildRibbonMesh(group.mf, group.segments, group.extent,
                                  GetZoomedLineWidth(group.featureClass, zoom));
        }

        foreach (var label in _labels)
        {
            bool visible = zoom >= label.minZoom;
            label.tmp.gameObject.SetActive(visible);
            if (visible)
                label.tmp.fontSize = GetZoomedFontSize(label.layerName, zoom);
        }

        _updateRebuildCount++;
        _updateRebuildMs += sw.ElapsedMilliseconds;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Ribbon mesh building
    // ═════════════════════════════════════════════════════════════════════════

    void BuildAllLineRibbons(float zoom)
    {
        for (int i = 0; i < _lineGroups.Count; i++)
        {
            var group    = _lineGroups[i];
            bool visible = zoom >= group.minZoom;
            group.mr.enabled = visible;
            if (visible)
                RebuildRibbonMesh(group.mf, group.segments, group.extent,
                                  GetZoomedLineWidth(group.featureClass, zoom));
        }
    }

    void RebuildRibbonMesh(MeshFilter mf,
                            List<List<(int x, int y)>> segments,
                            int extent, float lineWidth)
    {
        float half    = lineWidth * 0.5f;
        float offsetX = _offsetX;
        float offsetZ = _offsetZ;
        float worldSize = _worldSize;

        var verts = new List<Vector3>();
        var tris  = new List<int>();

        foreach (var ring in segments)
        {
            for (int i = 0; i < ring.Count - 1; i++)
            {
                float ax = offsetX + ((float)ring[i].x     / extent) * worldSize;
                float az = offsetZ + ((float)ring[i].y     / extent) * worldSize;
                float bx = offsetX + ((float)ring[i + 1].x / extent) * worldSize;
                float bz = offsetZ + ((float)ring[i + 1].y / extent) * worldSize;

                Vector3 a   = new Vector3(ax, 0f, az);
                Vector3 b   = new Vector3(bx, 0f, bz);
                Vector3 dir = b - a;
                float   len = dir.magnitude;
                if (len < 0.001f) continue;
                dir /= len;

                Vector3 perp = new Vector3(-dir.z, 0f, dir.x) * half;

                int bi = verts.Count;
                verts.Add(a - perp);
                verts.Add(a + perp);
                verts.Add(b + perp);
                verts.Add(b - perp);

                tris.Add(bi + 0); tris.Add(bi + 1); tris.Add(bi + 2);
                tris.Add(bi + 0); tris.Add(bi + 2); tris.Add(bi + 3);
            }
        }

        if (mf.sharedMesh == null)
            mf.sharedMesh = new Mesh { name = "ribbon" };

        var mesh = mf.sharedMesh;
        mesh.Clear();

        if (verts.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
    }

    // ── Stored offsets (set during UploadGeometry, needed for ribbon rebuild) ─
    private float _offsetX;
    private float _offsetZ;
    private float _worldSize;

    // ═════════════════════════════════════════════════════════════════════════
    //  Zoom helpers
    // ═════════════════════════════════════════════════════════════════════════

    float GetZoomedLineWidth(string featureClass, float zoom) =>
        GetBaseLineWidth(featureClass) * Mathf.Pow(2f, zoom - 14f);

    float GetZoomedFontSize(string layerName, float zoom)
    {
        float baseSize = layerName switch
        {
            "place"               => _worldSize * 0.04f,
            "water_name"          => _worldSize * 0.025f,
            "transportation_name" => _worldSize * 0.018f,
            "poi"                 => _worldSize * 0.015f,
            "housenumber"         => _worldSize * 0.01f,
            _                     => _worldSize * 0.02f,
        };
        return baseSize * Mathf.Pow(2f, zoom - 14f);
    }

    float GetBaseLineWidth(string featureClass) => featureClass switch
    {
        "motorway"  => _worldSize * 0.008f,
        "trunk"     => _worldSize * 0.007f,
        "primary"   => _worldSize * 0.006f,
        "secondary" => _worldSize * 0.005f,
        "tertiary"  => _worldSize * 0.004f,
        "minor"     => _worldSize * 0.003f,
        "service"   => _worldSize * 0.002f,
        "path"      => _worldSize * 0.001f,
        "cycleway"  => _worldSize * 0.0015f,
        "rail"      => _worldSize * 0.002f,
        "river"     => _worldSize * 0.005f,
        "canal"     => _worldSize * 0.004f,
        "stream"    => _worldSize * 0.002f,
        "ditch"     => _worldSize * 0.001f,
        "boundary"  => _worldSize * 0.001f,
        _           => _worldSize * 0.002f,
    };

    float GetLabelMinZoom(string layerName) => layerName switch
    {
        "place"               => 6f,
        "water_name"          => 10f,
        "transportation_name" => 13f,
        "poi"                 => 15f,
        "housenumber"         => 17f,
        _                     => 12f,
    };

    // ═════════════════════════════════════════════════════════════════════════
    //  Material helpers
    // ═════════════════════════════════════════════════════════════════════════

    Material ResolveMat(string layer, string featureClass, string subclass)
    {
        return (materials != null ? materials.Resolve(layer, featureClass, subclass) : null)
               ?? GetDefaultMaterial(layer, featureClass);
    }

    Material GetDefaultMaterial(string layer = "", string featureClass = "")
    {
        string key = layer + "/" + featureClass;
        if (_defaultMaterialCache.TryGetValue(key, out var existing)) return existing;

        Color color = layer switch
        {
            "transportation" => featureClass switch
            {
                "motorway"  => new Color(0.9f,  0.6f,  0.2f),
                "trunk"     => new Color(0.9f,  0.7f,  0.3f),
                "primary"   => new Color(0.95f, 0.85f, 0.4f),
                "secondary" => new Color(1f,    1f,    0.6f),
                "tertiary"  => new Color(0.9f,  0.9f,  0.9f),
                "rail"      => new Color(0.5f,  0.4f,  0.5f),
                "path"      => new Color(0.7f,  0.6f,  0.5f),
                "cycleway"  => new Color(0.4f,  0.7f,  0.4f),
                _           => new Color(0.8f,  0.8f,  0.8f),
            },
            "waterway"  => new Color(0.4f,  0.6f,  0.9f),
            "water"     => new Color(0.3f,  0.55f, 0.85f),
            "landcover" => featureClass switch
            {
                "grass"    => new Color(0.6f,  0.8f,  0.5f),
                "wood"     => new Color(0.3f,  0.6f,  0.3f),
                "forest"   => new Color(0.3f,  0.6f,  0.3f),
                "sand"     => new Color(0.9f,  0.85f, 0.65f),
                "wetland"  => new Color(0.5f,  0.7f,  0.6f),
                "farmland" => new Color(0.75f, 0.85f, 0.6f),
                _          => new Color(0.7f,  0.8f,  0.65f),
            },
            "landuse" => featureClass switch
            {
                "residential" => new Color(0.92f, 0.88f, 0.84f),
                "industrial"  => new Color(0.75f, 0.72f, 0.78f),
                "commercial"  => new Color(0.85f, 0.78f, 0.82f),
                "retail"      => new Color(0.9f,  0.75f, 0.75f),
                "cemetery"    => new Color(0.7f,  0.8f,  0.7f),
                "military"    => new Color(0.6f,  0.65f, 0.55f),
                _             => new Color(0.85f, 0.85f, 0.82f),
            },
            "park"     => new Color(0.5f,  0.75f, 0.45f),
            "building" => new Color(0.78f, 0.74f, 0.70f),
            "boundary" => new Color(0.7f,  0.4f,  0.4f),
            _          => new Color(0.85f, 0.85f, 0.85f),
        };

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
        _defaultMaterialCache[key] = mat;
        return mat;
    }
}