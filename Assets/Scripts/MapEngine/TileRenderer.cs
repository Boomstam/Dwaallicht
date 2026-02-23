using UnityEngine;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Renders a single MVT tile.
/// Geometry collection is done off the main thread by TileGeometryCollector.
/// This class handles Unity API upload and per-frame zoom-driven updates.
/// </summary>
public class TileRenderer : MonoBehaviour
{
    public MapMaterials  materials;
    public TMP_FontAsset labelFont;
    public MapZoom       mapZoom;

    [Header("Label Sizing")]
    [Tooltip("Global multiplier for all label sizes. Tune in inspector.")]
    public float labelScale = 35f;

    [Tooltip("Characters per line before wrapping.")]
    public int charsPerLine = 20;

    // ── Constants ─────────────────────────────────────────────────────────────
    private const float ZoomRebuildThreshold = 0.15f;
    private const float CharWidthFactor      = 0.55f;

    // ── Internal state ────────────────────────────────────────────────────────

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

    private struct ZoomLabel
    {
        public TextMeshPro tmp;
        public string      layerName;
        public string      featureClass;  // "town", "village", "hamlet", etc.
        public float       minZoom;
        public int         rank;
    }
    private List<ZoomLabel> _labels = new();

    // Set by MapController — drives label visibility for all tiles uniformly
    [HideInInspector] public bool showLabels = true;

    private float _lastBuiltZoom    = -999f;
    private bool  _needsZoomRefresh = false;

    private float _offsetX;
    private float _offsetZ;
    private float _worldSize;

    // Shared upload diagnostics
    private static int  s_labelsTotal   = 0;
    private static int  s_tilesUploaded = 0;
    private static int  s_tilesExpected = 0;

    private static Dictionary<string, Material> _defaultMaterialCache = new();

    // ═════════════════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════════════════

    public static void ResetDiagnostics(int expectedTiles)
    {
        s_labelsTotal   = 0;
        s_tilesUploaded = 0;
        s_tilesExpected = expectedTiles;
    }

    public void UploadGeometry(TileGeometryCollector.TileGeometryData geo)
    {
        _offsetX   = geo.offsetX;
        _offsetZ   = geo.offsetZ;
        _worldSize = geo.worldSize;
        _lineGroups.Clear();
        _labels.Clear();
        _lastBuiltZoom    = -999f;
        _needsZoomRefresh  = true;

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

            int vertCount = pd.verts.Length / 3;
            var v3 = new Vector3[vertCount];
            for (int i = 0; i < vertCount; i++)
                v3[i] = new Vector3(pd.verts[i * 3], pd.verts[i * 3 + 1], pd.verts[i * 3 + 2]);

            mesh.vertices  = v3;
            mesh.triangles = pd.tris;
            mesh.RecalculateNormals();   // Required for URP Lit — flat polys need up-facing normals
            mesh.RecalculateBounds();    // Required for correct frustum culling

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

        // ── Labels — create all, leave visibility to Update() ─────────────────
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
            tmp.color     = GetLabelColor(lbl.layerName);

            // Start inactive — Update() will set correct state once zoom is valid
            go.SetActive(false);

            _labels.Add(new ZoomLabel
            {
                tmp          = tmp,
                layerName    = lbl.layerName,
                featureClass = lbl.featureClass,
                minZoom      = GetLabelMinZoom(lbl.layerName, lbl.featureClass),
                rank         = lbl.rank,
            });
        }

        // ── Diagnostics ───────────────────────────────────────────────────────
        s_labelsTotal   += geo.labels.Count;
        s_tilesUploaded++;
        if (s_tilesUploaded == s_tilesExpected)
            Debug.Log($"[Labels] {s_tilesExpected} tiles uploaded, {s_labelsTotal} total labels created.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Unity Update
    // ═════════════════════════════════════════════════════════════════════════

    void Update()
    {
        if (mapZoom == null) return;
        float zoom = mapZoom.VisualZoom;

        // VisualZoom is 0 until the camera is positioned — wait for a valid value
        if (zoom < 1f) return;

        bool zoomChanged = Mathf.Abs(zoom - _lastBuiltZoom) >= ZoomRebuildThreshold;
        if (!zoomChanged && !_needsZoomRefresh) return;

        _lastBuiltZoom   = zoom;
        _needsZoomRefresh = false;

        // Lines
        for (int i = 0; i < _lineGroups.Count; i++)
        {
            var group    = _lineGroups[i];
            bool visible = zoom >= group.minZoom;
            group.mr.enabled = visible;
            if (visible)
                RebuildRibbonMesh(group.mf, group.segments, group.extent,
                                  GetZoomedLineWidth(group.featureClass, zoom));
        }

        // Labels
        int activeCount = 0;
        foreach (var label in _labels)
        {
            bool visible = showLabels && zoom >= label.minZoom;
            label.tmp.gameObject.SetActive(visible);
            if (visible)
            {
                ApplyFontSize(label.tmp, label.layerName, label.featureClass, zoom);
                activeCount++;
            }
        }


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
        float half      = lineWidth * 0.5f;
        float offsetX   = _offsetX;
        float offsetZ   = _offsetZ;
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

    // ═════════════════════════════════════════════════════════════════════════
    //  Label sizing
    // ═════════════════════════════════════════════════════════════════════════

    void ApplyFontSize(TextMeshPro tmp, string layerName, string featureClass, float zoom)
    {
        // Base size from feature class — towns biggest, hamlets smallest
        float classMult = layerName == "place" ? featureClass switch
        {
            "city"        => 2.0f,
            "town"        => 1.4f,
            "village"     => 1.0f,
            "suburb"      => 0.85f,
            "hamlet"      => 0.7f,
            "neighbourhood" => 0.6f,
            _             => 0.75f,
        } : layerName switch
        {
            "water_name"          => 1.0f,
            "transportation_name" => 0.6f,
            _                     => 0.75f,
        };

        float baseSize = _worldSize * 0.035f;
        // Keep labels a stable world-space size as camera moves.
        // Reference point is zoom 10 (overview camera height).
        float zoomCompensation = 1f / Mathf.Pow(2f, zoom - 10f);
        float fontSize         = baseSize * classMult * labelScale * zoomCompensation;

        tmp.fontSize = fontSize;
        float rectWidth = charsPerLine * fontSize * CharWidthFactor;
        tmp.rectTransform.sizeDelta = new Vector2(rectWidth, rectWidth * 2f);
    }

    /// <summary>
    /// Zoom thresholds based on feature class, tuned to this app's zoom range (10-18).
    /// Overview camera produces ~zoom 11.7, so towns must show at or below that.
    /// </summary>
    float GetLabelMinZoom(string layerName, string featureClass = "")
    {
        switch (layerName)
        {
            case "place":
                return featureClass switch
                {
                    "city"          => 10f,
                    "town"          => 10f,   // largest things in this region — always on
                    "village"       => 11f,
                    "suburb"        => 12f,
                    "hamlet"        => 13f,
                    "neighbourhood" => 14f,
                    _               => 13f,
                };

            case "water_name":
                return 11f;

            case "transportation_name":
                return 14f;

            default:
                return 10f;
        }
    }

    float GetZoomedLineWidth(string featureClass, float zoom) =>
        GetBaseLineWidth(featureClass) * Mathf.Pow(2f, zoom - 14f);

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

    Color GetLabelColor(string layerName) => layerName switch
    {
        "water_name"          => new Color(0.2f, 0.4f, 0.8f),
        "transportation_name" => new Color(0.25f, 0.25f, 0.25f),
        _                     => new Color(0.1f, 0.1f, 0.1f),
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

        // Use Unlit/Color so polygons always show their assigned color regardless of
        // lighting setup. For a top-down map this is simpler and more predictable than Lit.
        Shader unlitShader = Shader.Find("Unlit/Color");
        if (unlitShader == null) unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlitShader == null) unlitShader = Shader.Find("Universal Render Pipeline/Lit");

        var mat = new Material(unlitShader) { color = color };
        _defaultMaterialCache[key] = mat;
        return mat;
    }
}