using System.Collections.Generic;
using EarcutNet;

/// <summary>
/// Pure-data geometry collection for a single MVT tile.
/// No Unity API calls — safe to run on any thread via Task.Run.
/// Results are handed to TileRenderer.UploadGeometry() on the main thread.
/// </summary>
public static class TileGeometryCollector
{
    // ── Output data structures ────────────────────────────────────────────────

    public class TileGeometryData
    {
        public int   tileX, tileY;
        public float offsetX, offsetZ, worldSize;

        // One entry per material key (layer/featureClass).
        // Verts are flat: [x0,y0,z0, x1,y1,z1, ...]
        public List<PolyMeshData>  polyMeshes  = new();
        public List<LineGroupData> lineGroups  = new();
        public List<LabelData>     labels      = new();
    }

    public class PolyMeshData
    {
        public string  matKey;          // e.g. "landcover/grass"
        public string  layerName;
        public string  featureClass;
        public float[] verts;           // flat XYZ
        public int[]   tris;
    }

    public class LineGroupData
    {
        public string  matKey;
        public string  layerName;
        public string  featureClass;
        public float   minZoom;
        public int     extent;
        public List<List<(int x, int y)>> segments = new();
    }

    public class LabelData
    {
        public float  wx, wy, wz;       // world position
        public string text;
        public string layerName;
    }

    // ── Layer ordering (mirrors TileRenderer) ─────────────────────────────────

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

    // ── Entry point ───────────────────────────────────────────────────────────

    public static TileGeometryData Collect(
        List<MVTLayer> layers,
        int tileX, int tileY,
        float tileWorldSize,
        float offsetX, float offsetZ)
    {
        var data = new TileGeometryData
        {
            tileX     = tileX,
            tileY     = tileY,
            offsetX   = offsetX,
            offsetZ   = offsetZ,
            worldSize = tileWorldSize,
        };

        // Working accumulators — keyed by matKey
        var polyAcc  = new Dictionary<string, PolyAccumulator>();
        var lineAcc  = new Dictionary<string, LineGroupData>();

        var layerMap = new Dictionary<string, MVTLayer>();
        foreach (var l in layers) layerMap[l.Name] = l;

        float highestPolyY = 0f;
        int   polygonIndex = 0;

        foreach (var layerName in LayerOrder)
        {
            if (!layerMap.TryGetValue(layerName, out var layer)) continue;

            if (layerName == "landcover")
                CollectLandcoverOrdered(layer, ref polygonIndex, ref highestPolyY,
                                        tileWorldSize, offsetX, offsetZ,
                                        polyAcc);
            else
                CollectLayer(layer, ref polygonIndex, ref highestPolyY,
                             tileWorldSize, offsetX, offsetZ,
                             polyAcc, lineAcc, data.labels);
        }

        // Flatten poly accumulators into output
        foreach (var kv in polyAcc)
        {
            if (kv.Value.vertCount == 0) continue;
            data.polyMeshes.Add(new PolyMeshData
            {
                matKey       = kv.Key,
                layerName    = kv.Value.layerName,
                featureClass = kv.Value.featureClass,
                verts        = kv.Value.verts.ToArray(),
                tris         = kv.Value.tris.ToArray(),
            });
        }

        // Flatten line accumulators into output
        foreach (var kv in lineAcc)
            data.lineGroups.Add(kv.Value);

        return data;
    }

    // ── Collection helpers ────────────────────────────────────────────────────

    static void CollectLandcoverOrdered(
        MVTLayer layer, ref int polygonIndex, ref float highestPolyY,
        float tileWorldSize, float offsetX, float offsetZ,
        Dictionary<string, PolyAccumulator> polyAcc)
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
            foreach (var f in features)
                AccumulatePolygon(f, layer, "landcover/" + fc, "landcover", fc,
                                  polygonIndex++, ref highestPolyY,
                                  tileWorldSize, offsetX, offsetZ, polyAcc);
        }

        foreach (var kv in byClass)
        {
            if (System.Array.IndexOf(LandcoverOrder, kv.Key) >= 0) continue;
            foreach (var f in kv.Value)
                AccumulatePolygon(f, layer, "landcover/" + kv.Key, "landcover", kv.Key,
                                  polygonIndex++, ref highestPolyY,
                                  tileWorldSize, offsetX, offsetZ, polyAcc);
        }
    }

    static void CollectLayer(
        MVTLayer layer, ref int polygonIndex, ref float highestPolyY,
        float tileWorldSize, float offsetX, float offsetZ,
        Dictionary<string, PolyAccumulator> polyAcc,
        Dictionary<string, LineGroupData>   lineAcc,
        List<LabelData> labels)
    {
        foreach (var feature in layer.Features)
        {
            string fc   = GetProp(feature, "class");
            string sub  = GetProp(feature, "subclass");
            string name = GetProp(feature, "name");
            string matKey = layer.Name + "/" + fc;

            switch (layer.Name)
            {
                case "transportation":
                case "waterway":
                    AccumulateLine(feature, layer, matKey, fc,
                                   lineAcc, tileWorldSize);
                    break;

                case "boundary":
                    AccumulateLine(feature, layer, layer.Name + "/boundary", "boundary",
                                   lineAcc, tileWorldSize);
                    break;

                case "building":
                case "water":
                case "landuse":
                case "park":
                    AccumulatePolygon(feature, layer, matKey, layer.Name, fc,
                                      polygonIndex++, ref highestPolyY,
                                      tileWorldSize, offsetX, offsetZ, polyAcc);
                    break;

                case "transportation_name":
                case "water_name":
                case "place":
                case "poi":
                    if (!string.IsNullOrEmpty(name))
                        CollectLabel(feature, layer, name,
                                     highestPolyY, tileWorldSize, offsetX, offsetZ, labels);
                    break;

                case "housenumber":
                    string hn = GetProp(feature, "housenumber");
                    if (!string.IsNullOrEmpty(hn))
                        CollectLabel(feature, layer, hn,
                                     highestPolyY, tileWorldSize, offsetX, offsetZ, labels);
                    break;
            }
        }
    }

    // ── Polygon accumulation ──────────────────────────────────────────────────

    class PolyAccumulator
    {
        public string      layerName;
        public string      featureClass;
        public int         vertCount;
        public List<float> verts = new();
        public List<int>   tris  = new();

        public void Add(List<float> fVerts, List<int> fTris)
        {
            int baseIdx = vertCount;
            verts.AddRange(fVerts);
            vertCount += fVerts.Count / 3;
            foreach (int t in fTris) tris.Add(t + baseIdx);
        }
    }

    static void AccumulatePolygon(
        MVTFeature feature, MVTLayer layer,
        string matKey, string layerName, string featureClass,
        int index, ref float highestPolyY,
        float tileWorldSize, float offsetX, float offsetZ,
        Dictionary<string, PolyAccumulator> polyAcc)
    {
        if (feature.Geometry.Count == 0) return;
        var outerRing = feature.Geometry[0];
        if (outerRing.Count < 3) return;

        float yOffset = GetLayerYOffset(layerName, featureClass)
                      + index * (tileWorldSize * 0.000001f);
        if (yOffset > highestPolyY) highestPolyY = yOffset;

        // Pack all rings into flat double[] for Earcut
        var earcutData  = new List<double>();
        var holeIndices = new List<int>();

        foreach (var pt in outerRing) { earcutData.Add(pt.x); earcutData.Add(pt.y); }

        for (int r = 1; r < feature.Geometry.Count; r++)
        {
            holeIndices.Add(earcutData.Count / 2);
            foreach (var pt in feature.Geometry[r]) { earcutData.Add(pt.x); earcutData.Add(pt.y); }
        }

        var triIndices = Earcut.Tessellate(earcutData, holeIndices);
        if (triIndices.Count == 0) return;

        // Convert to flat XYZ float[]
        int vertCount = earcutData.Count / 2;
        var verts = new List<float>(vertCount * 3);
        for (int i = 0; i < vertCount; i++)
        {
            float wx = offsetX + ((float)(earcutData[i * 2]     / layer.Extent)) * tileWorldSize;
            float wz = offsetZ + ((float)(earcutData[i * 2 + 1] / layer.Extent)) * tileWorldSize;
            verts.Add(wx);
            verts.Add(yOffset);
            verts.Add(wz);
        }

        if (!polyAcc.TryGetValue(matKey, out var acc))
        {
            acc = new PolyAccumulator { layerName = layerName, featureClass = featureClass };
            polyAcc[matKey] = acc;
        }
        acc.Add(verts, triIndices);
    }

    // ── Line accumulation ─────────────────────────────────────────────────────

    static void AccumulateLine(
        MVTFeature feature, MVTLayer layer,
        string matKey, string featureClass,
        Dictionary<string, LineGroupData> lineAcc,
        float tileWorldSize)
    {
        if (feature.Type != MVTFeature.GeomType.LineString &&
            feature.Type != MVTFeature.GeomType.Unknown) return;

        if (!lineAcc.TryGetValue(matKey, out var group))
        {
            group = new LineGroupData
            {
                matKey       = matKey,
                layerName    = layer.Name,
                featureClass = featureClass,
                minZoom      = GetLineMinZoom(featureClass),
                extent       = layer.Extent,
            };
            lineAcc[matKey] = group;
        }

        foreach (var ring in feature.Geometry)
            if (ring.Count >= 2)
                group.segments.Add(ring);
    }

    // ── Label collection ──────────────────────────────────────────────────────

    static void CollectLabel(
        MVTFeature feature, MVTLayer layer, string text,
        float highestPolyY, float tileWorldSize,
        float offsetX, float offsetZ,
        List<LabelData> labels)
    {
        if (feature.Geometry.Count == 0 || feature.Geometry[0].Count == 0) return;

        var ring = feature.Geometry[0];
        var pt   = ring[ring.Count / 2];

        float wx = offsetX + ((float)pt.x / layer.Extent) * tileWorldSize;
        float wz = offsetZ + ((float)pt.y / layer.Extent) * tileWorldSize;
        float wy = highestPolyY + tileWorldSize * 0.000001f;

        labels.Add(new LabelData
        {
            wx        = wx,
            wy        = wy,
            wz        = wz,
            text      = text,
            layerName = layer.Name,
        });
    }

    // ── Zoom / offset tables (must match TileRenderer) ────────────────────────

    static float GetLayerYOffset(string layerName, string featureClass) => layerName switch
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

    static float GetLineMinZoom(string featureClass) => featureClass switch
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

    static string GetProp(MVTFeature feature, string key) =>
        feature.Properties.TryGetValue(key, out var val) ? val?.ToString() ?? "" : "";
}