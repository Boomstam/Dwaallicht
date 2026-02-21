using UnityEngine;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// Renders a single MVT tile.
///
/// Key optimizations vs. the previous version:
///   1. Mesh combining  - All polygons sharing the same Material are batched into one
///      combined Mesh + MeshRenderer (one draw call per material per tile instead of
///      one per feature).
///   2. Line ribbons    - Roads/waterways are baked as flat triangle-strip meshes instead
///      of LineRenderers, and also batched per material. LineRenderer overhead eliminated.
///   3. Zoom throttling - Line ribbon meshes and label visibility are only updated when
///      the visual zoom level changes by more than ZoomRebuildThreshold.
///      No more work every frame when the camera is stationary.
/// </summary>
public class TileRenderer : MonoBehaviour
{
    public MapMaterials  materials;
    public TMP_FontAsset labelFont;
    public MapZoom       mapZoom;

    // ── Zoom rebuild threshold ────────────────────────────────────────────────
    // Ribbon widths / label visibility only recalculate when zoom drifts this much.
    private const float ZoomRebuildThreshold = 0.15f;

    // ── Internal state ────────────────────────────────────────────────────────
    private float _tileWorldSize;
    private float _tileOffsetX;
    private float _tileOffsetZ;
    private float _highestPolygonY;

    // Polygon mesh accumulators — one per Material, filled during Render() then flushed
    private Dictionary<Material, MeshAccumulator> _polyAccumulators = new();

    // One entry per (material, featureClass) combination for lines
    private struct LineMaterialGroup
    {
        public Material mat;
        public string   featureClass;
        public float    minZoom;
        public int      extent;         // tile extent (usually 4096)
        public MeshFilter   mf;
        public MeshRenderer mr;
        public List<List<(int x, int y)>> segments; // raw rings, kept for rebuild
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
    private static float _updateLogTimer    = 0f;
    private static int   _updateRebuildCount = 0;
    private static float _updateRebuildMs    = 0f;

    // Fallback material cache (shared across all TileRenderer instances)
    private static Dictionary<string, Material> _defaultMaterialCache = new();

    // ── Layer render order ────────────────────────────────────────────────────
    private static readonly string[] LayerOrder =
    {
        "landcover", "landuse", "water", "park", "waterway", "boundary",
        "building", "transportation", "transportation_name", "water_name",
        "place", "poi", "housenumber",
    };

    private static readonly string[] LandcoverOrder =
    {
        "grass", "farmland", "wetland", "sand", "wood", "forest"
    };

    // ═════════════════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════════════════

    public void Render(List<MVTLayer> layers, int z, int x, int y,
                       float worldSize, float offsetX, float offsetZ)
    {
        _tileWorldSize   = worldSize;
        _tileOffsetX     = offsetX;
        _tileOffsetZ     = offsetZ;
        _highestPolygonY = 0f;
        _polyAccumulators.Clear();
        _lineGroups.Clear();
        _labels.Clear();
        _lastBuiltZoom   = -999f;

        transform.localPosition = Vector3.zero;

        var layerMap = new Dictionary<string, MVTLayer>();
        foreach (var layer in layers)
            layerMap[layer.Name] = layer;

        int polygonIndex = 0;

        // ── DIAGNOSTICS ──
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int totalFeatures = 0;
        foreach (var l in layers) totalFeatures += l.Features.Count;
        // ────────────────

        foreach (var layerName in LayerOrder)
        {
            if (!layerMap.TryGetValue(layerName, out var layer)) continue;

            if (layerName == "landcover")
                CollectLandcoverOrdered(layer, ref polygonIndex);
            else
                CollectLayer(layer, ref polygonIndex);
        }

        long collectMs = sw.ElapsedMilliseconds; sw.Restart();

        // Turn accumulated polygon data into GameObjects / Meshes
        FlushPolygonMeshes();

        long polyMs = sw.ElapsedMilliseconds; sw.Restart();

        // Build initial line ribbon meshes
        float initialZoom = (mapZoom != null) ? mapZoom.VisualZoom : 14f;
        BuildAllLineRibbons(initialZoom);

        long lineMs = sw.ElapsedMilliseconds;

        // Count verts across all poly meshes (post-flush we lost the accumulators, count GO children)
        int polyDrawCalls = 0; int lineDrawCalls = 0;
        foreach (Transform child in transform)
        {
            if (child.name.StartsWith("poly_")) polyDrawCalls++;
            else if (child.name.StartsWith("lines_")) lineDrawCalls++;
        }

        int totalSegments = 0;
        foreach (var g in _lineGroups) totalSegments += g.segments.Count;

        Debug.Log($"[Tile {x},{y}] feats={totalFeatures} " +
                  $"| collect={collectMs}ms poly={polyMs}ms line={lineMs}ms " +
                  $"| drawCalls: poly={polyDrawCalls} line={lineDrawCalls} labels={_labels.Count} " +
                  $"| lineGroups={_lineGroups.Count} segments={totalSegments} " +
                  $"| polyVerts≈{polygonIndex * 10} (rough)");

        _lastBuiltZoom = initialZoom;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Unity Update — only acts when zoom changes meaningfully
    // ═════════════════════════════════════════════════════════════════════════

    void Update()
    {
        if (mapZoom == null) return;
        float zoom = mapZoom.VisualZoom;

        // Throttled aggregate log every 2s — shows whether Update is doing real work
        _updateLogTimer -= Time.deltaTime;
        if (_updateLogTimer <= 0f)
        {
            Debug.Log($"[TileRenderer.Update x2s] rebuilds={_updateRebuildCount} tiles triggered, totalRibbonMs={_updateRebuildMs:F1}ms");
            _updateLogTimer    = 2f;
            _updateRebuildCount = 0;
            _updateRebuildMs    = 0f;
        }

        if (Mathf.Abs(zoom - _lastBuiltZoom) < ZoomRebuildThreshold) return;
        _lastBuiltZoom = zoom;

        // Rebuild line ribbons with new widths + toggle visibility
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < _lineGroups.Count; i++)
        {
            var group    = _lineGroups[i];
            bool visible = zoom >= group.minZoom;
            group.mr.enabled = visible;
            if (visible)
                RebuildRibbonMesh(group.mf, group.segments, group.extent,
                                  GetZoomedLineWidth(group.featureClass, zoom));
        }

        // Toggle labels and scale font size
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
    //  Collection pass  (fills accumulators, no GameObjects created yet)
    // ═════════════════════════════════════════════════════════════════════════

    void CollectLandcoverOrdered(MVTLayer layer, ref int polygonIndex)
    {
        var byClass = new Dictionary<string, List<MVTFeature>>();
        foreach (var feature in layer.Features)
        {
            string fc = GetProp(feature, "class");
            if (!byClass.ContainsKey(fc)) byClass[fc] = new List<MVTFeature>();
            byClass[fc].Add(feature);
        }

        foreach (var fc in LandcoverOrder)
        {
            if (!byClass.TryGetValue(fc, out var features)) continue;
            foreach (var feature in features)
                AccumulatePolygon(feature, layer, ResolveMat("landcover", fc, ""),
                                  "landcover", fc, polygonIndex++);
        }

        foreach (var kv in byClass)
        {
            if (System.Array.IndexOf(LandcoverOrder, kv.Key) >= 0) continue;
            foreach (var feature in kv.Value)
                AccumulatePolygon(feature, layer, ResolveMat("landcover", kv.Key, ""),
                                  "landcover", kv.Key, polygonIndex++);
        }
    }

    void CollectLayer(MVTLayer layer, ref int polygonIndex)
    {
        foreach (var feature in layer.Features)
        {
            string fc   = GetProp(feature, "class");
            string sub  = GetProp(feature, "subclass");
            string name = GetProp(feature, "name");
            var mat     = ResolveMat(layer.Name, fc, sub);

            switch (layer.Name)
            {
                case "transportation":
                case "waterway":
                    AccumulateLine(feature, layer, mat, fc);
                    break;

                case "boundary":
                    AccumulateLine(feature, layer, mat, "boundary");
                    break;

                case "building":
                    AccumulatePolygon(feature, layer, mat, "building", fc, polygonIndex++);
                    break;

                case "water":
                case "landuse":
                case "park":
                    AccumulatePolygon(feature, layer, mat, layer.Name, fc, polygonIndex++);
                    break;

                case "transportation_name":
                case "water_name":
                case "place":
                case "poi":
                    if (!string.IsNullOrEmpty(name))
                        CreateLabel(feature, layer, name);
                    break;

                case "housenumber":
                    string hn = GetProp(feature, "housenumber");
                    if (!string.IsNullOrEmpty(hn))
                        CreateLabel(feature, layer, hn);
                    break;
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Polygon accumulation
    // ═════════════════════════════════════════════════════════════════════════

    private class MeshAccumulator
    {
        public readonly List<Vector3> verts = new();
        public readonly List<int>     tris  = new();

        public void Add(List<Vector3> fVerts, List<int> fTris)
        {
            int baseIdx = verts.Count;
            verts.AddRange(fVerts);
            foreach (int t in fTris)
                tris.Add(t + baseIdx);
        }
    }

    void AccumulatePolygon(MVTFeature feature, MVTLayer layer, Material mat,
                           string layerName, string featureClass, int index)
    {
        if (feature.Geometry.Count == 0) return;
        var outerRing = feature.Geometry[0];
        if (outerRing.Count < 3) return;

        float yOffset = GetLayerYOffset(layerName, featureClass)
                      + index * (_tileWorldSize * 0.000001f);
        if (yOffset > _highestPolygonY) _highestPolygonY = yOffset;

        var verts = new List<Vector3>(outerRing.Count);
        foreach (var pt in outerRing)
            verts.Add(ToWorld(pt.x, pt.y, layer.Extent, yOffset));

        if (verts.Count > 1 && verts[0] == verts[verts.Count - 1])
            verts.RemoveAt(verts.Count - 1);

        if (verts.Count < 3) return;

        var tris = Triangulate(verts);
        if (tris.Count == 0) return;

        if (!_polyAccumulators.TryGetValue(mat, out var acc))
        {
            acc = new MeshAccumulator();
            _polyAccumulators[mat] = acc;
        }
        acc.Add(verts, tris);
    }

    void FlushPolygonMeshes()
    {
        foreach (var kv in _polyAccumulators)
        {
            if (kv.Value.verts.Count == 0) continue;

            var go  = new GameObject("poly_" + kv.Key.name);
            go.transform.SetParent(transform, false);

            var mesh = new Mesh { name = kv.Key.name };
            if (kv.Value.verts.Count > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.SetVertices(kv.Value.verts);
            mesh.SetTriangles(kv.Value.tris, 0);
            mesh.RecalculateNormals();

            go.AddComponent<MeshFilter>().sharedMesh  = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = kv.Key;
        }
        _polyAccumulators.Clear();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Line accumulation
    // ═════════════════════════════════════════════════════════════════════════

    void AccumulateLine(MVTFeature feature, MVTLayer layer, Material mat, string featureClass)
    {
        if (feature.Type != MVTFeature.GeomType.LineString &&
            feature.Type != MVTFeature.GeomType.Unknown) return;

        // Find or create a group for (mat, featureClass)
        int groupIdx = -1;
        for (int i = 0; i < _lineGroups.Count; i++)
        {
            if (_lineGroups[i].mat == mat && _lineGroups[i].featureClass == featureClass)
            { groupIdx = i; break; }
        }

        if (groupIdx < 0)
        {
            var go = new GameObject("lines_" + featureClass);
            go.transform.SetParent(transform, false);

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            _lineGroups.Add(new LineMaterialGroup
            {
                mat          = mat,
                featureClass = featureClass,
                minZoom      = GetLineMinZoom(featureClass),
                extent       = layer.Extent,
                mf           = mf,
                mr           = mr,
                segments     = new List<List<(int x, int y)>>(),
            });
            groupIdx = _lineGroups.Count - 1;
        }

        // Copy segments into the group (struct requires write-back)
        var grp = _lineGroups[groupIdx];
        foreach (var ring in feature.Geometry)
            if (ring.Count >= 2)
                grp.segments.Add(ring);
        _lineGroups[groupIdx] = grp;
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Ribbon mesh building
    // ═════════════════════════════════════════════════════════════════════════

    void BuildAllLineRibbons(float zoom)
    {
        for (int i = 0; i < _lineGroups.Count; i++)
        {
            var group   = _lineGroups[i];
            bool visible = zoom >= group.minZoom;
            group.mr.enabled = visible;
            if (visible)
                RebuildRibbonMesh(group.mf, group.segments, group.extent,
                                  GetZoomedLineWidth(group.featureClass, zoom));
        }
    }

    /// <summary>
    /// Bakes all line segments into a single flat ribbon mesh (triangle strips).
    /// Segments share the mesh; width is uniform per group and determined by zoom.
    /// The mesh is reused across rebuilds — only its data is replaced.
    /// </summary>
    void RebuildRibbonMesh(MeshFilter mf,
                            List<List<(int x, int y)>> segments,
                            int extent, float lineWidth)
    {
        float half = lineWidth * 0.5f;

        var verts = new List<Vector3>();
        var tris  = new List<int>();

        foreach (var ring in segments)
        {
            for (int i = 0; i < ring.Count - 1; i++)
            {
                Vector3 a = ToWorld(ring[i].x,     ring[i].y,     extent);
                Vector3 b = ToWorld(ring[i + 1].x, ring[i + 1].y, extent);

                Vector3 dir = b - a;
                float   len = dir.magnitude;
                if (len < 0.001f) continue;
                dir /= len;

                // Perpendicular in the XZ plane (camera looks straight down)
                Vector3 perp = new Vector3(-dir.z, 0f, dir.x) * half;

                int bi = verts.Count;
                verts.Add(a - perp); // 0 left-start
                verts.Add(a + perp); // 1 right-start
                verts.Add(b + perp); // 2 right-end
                verts.Add(b - perp); // 3 left-end

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
        // No need for normals on flat ground-plane ribbons visible from above only,
        // but RecalculateNormals is cheap for unlit/simple materials.
        mesh.RecalculateNormals();
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Labels
    // ═════════════════════════════════════════════════════════════════════════

    void CreateLabel(MVTFeature feature, MVTLayer layer, string text)
    {
        if (feature.Geometry.Count == 0 || feature.Geometry[0].Count == 0) return;

        var ring = feature.Geometry[0];
        var pt   = ring[ring.Count / 2];
        Vector3 pos = ToWorld(pt.x, pt.y, layer.Extent,
                              _highestPolygonY + _tileWorldSize * 0.000001f);

        var go = new GameObject("label:" + text);
        go.transform.SetParent(transform, false);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(90, 0, 0);

        var tmp       = go.AddComponent<TextMeshPro>();
        tmp.font      = labelFont;
        tmp.text      = text;
        tmp.fontSize  = _tileWorldSize * 0.02f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = Color.white;

        float minZoom = GetLabelMinZoom(layer.Name);
        _labels.Add(new ZoomLabel { tmp = tmp, layerName = layer.Name, minZoom = minZoom });

        // Set initial visibility without waiting for next Update()
        float zoom = (mapZoom != null) ? mapZoom.VisualZoom : 14f;
        go.SetActive(zoom >= minZoom);
        if (zoom >= minZoom)
            tmp.fontSize = GetZoomedFontSize(layer.Name, zoom);
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  Ear-clip triangulator
    // ═════════════════════════════════════════════════════════════════════════

    List<int> Triangulate(List<Vector3> verts)
    {
        var tris    = new List<int>();
        var indices = new List<int>(verts.Count);
        for (int i = 0; i < verts.Count; i++) indices.Add(i);

        int safety = verts.Count * verts.Count;
        int idx    = 0;

        while (indices.Count > 3 && safety-- > 0)
        {
            int count = indices.Count;
            if (IsEar(verts, indices, idx))
            {
                tris.Add(indices[(idx + 0) % count]);
                tris.Add(indices[(idx + 1) % count]);
                tris.Add(indices[(idx + 2) % count]);
                indices.RemoveAt((idx + 1) % count);
            }
            else
            {
                idx++;
            }
            if (indices.Count > 0) idx %= indices.Count;
        }

        if (indices.Count == 3)
        { tris.Add(indices[0]); tris.Add(indices[1]); tris.Add(indices[2]); }

        return tris;
    }

    bool IsEar(List<Vector3> verts, List<int> indices, int idx)
    {
        int count = indices.Count;
        int a = indices[(idx + 0) % count];
        int b = indices[(idx + 1) % count];
        int c = indices[(idx + 2) % count];

        Vector3 va = verts[a], vb = verts[b], vc = verts[c];
        if (Vector3.Cross(vb - va, vc - va).y < 0) return false;

        for (int i = 0; i < count; i++)
        {
            int pi = indices[i];
            if (pi == a || pi == b || pi == c) continue;
            if (PointInTriangle(verts[pi], va, vb, vc)) return false;
        }
        return true;
    }

    bool PointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
        return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
    }

    float Sign(Vector3 p1, Vector3 p2, Vector3 p3) =>
        (p1.x - p3.x) * (p2.z - p3.z) - (p2.x - p3.x) * (p1.z - p3.z);

    // ═════════════════════════════════════════════════════════════════════════
    //  Zoom helpers
    // ═════════════════════════════════════════════════════════════════════════

    float GetZoomedLineWidth(string featureClass, float zoom) =>
        GetBaseLineWidth(featureClass) * Mathf.Pow(2f, zoom - 14f);

    float GetZoomedFontSize(string layerName, float zoom)
    {
        float baseSize = layerName switch
        {
            "place"               => _tileWorldSize * 0.04f,
            "water_name"          => _tileWorldSize * 0.025f,
            "transportation_name" => _tileWorldSize * 0.018f,
            "poi"                 => _tileWorldSize * 0.015f,
            "housenumber"         => _tileWorldSize * 0.01f,
            _                     => _tileWorldSize * 0.02f,
        };
        return baseSize * Mathf.Pow(2f, zoom - 14f);
    }

    float GetBaseLineWidth(string featureClass) => featureClass switch
    {
        "motorway"  => _tileWorldSize * 0.008f,
        "trunk"     => _tileWorldSize * 0.007f,
        "primary"   => _tileWorldSize * 0.006f,
        "secondary" => _tileWorldSize * 0.005f,
        "tertiary"  => _tileWorldSize * 0.004f,
        "minor"     => _tileWorldSize * 0.003f,
        "service"   => _tileWorldSize * 0.002f,
        "path"      => _tileWorldSize * 0.001f,
        "cycleway"  => _tileWorldSize * 0.0015f,
        "rail"      => _tileWorldSize * 0.002f,
        "river"     => _tileWorldSize * 0.005f,
        "canal"     => _tileWorldSize * 0.004f,
        "stream"    => _tileWorldSize * 0.002f,
        "ditch"     => _tileWorldSize * 0.001f,
        "boundary"  => _tileWorldSize * 0.001f,
        _           => _tileWorldSize * 0.002f,
    };

    float GetLineMinZoom(string featureClass) => featureClass switch
    {
        "motorway"  => 6f,
        "trunk"     => 8f,
        "primary"   => 10f,
        "secondary" => 11f,
        "tertiary"  => 12f,
        "minor"     => 13f,
        "service"   => 14f,
        "path"      => 14f,
        "cycleway"  => 14f,
        "track"     => 14f,
        "rail"      => 12f,
        "ferry"     => 10f,
        "boundary"  => 6f,
        "river"     => 10f,
        "canal"     => 11f,
        "stream"    => 13f,
        "ditch"     => 14f,
        _           => 12f,
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
    //  Coordinate / material helpers
    // ═════════════════════════════════════════════════════════════════════════

    Vector3 ToWorld(int tx, int ty, int extent, float yOffset = 0f)
    {
        float x = _tileOffsetX + ((float)tx / extent) * _tileWorldSize;
        float z = _tileOffsetZ + ((float)ty / extent) * _tileWorldSize;
        return new Vector3(x, yOffset, z);
    }

    float GetLayerYOffset(string layerName, string featureClass = "") => layerName switch
    {
        "landcover" => featureClass switch
        {
            "grass"    => 0f,
            "farmland" => 0.001f,
            "wetland"  => 0.002f,
            "sand"     => 0.003f,
            "wood"     => 0.004f,
            "forest"   => 0.004f,
            _          => 0f,
        },
        "landuse"  => 0.01f,
        "water"    => 0.02f,
        "park"     => 0.03f,
        "building" => 0.04f,
        _          => 0f,
    };

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

    string GetProp(MVTFeature feature, string key) =>
        feature.Properties.TryGetValue(key, out var val) ? val?.ToString() ?? "" : "";
}