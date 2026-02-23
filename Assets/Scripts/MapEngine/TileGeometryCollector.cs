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

        public List<PolyMeshData>  polyMeshes = new();
        public List<LineGroupData> lineGroups = new();
        public List<LabelData>     labels     = new();
    }

    public class PolyMeshData
    {
        public string  matKey;
        public string  layerName;
        public string  featureClass;
        public float[] verts;           // flat XYZ
        public int[]   tris;
    }

    public class LineGroupData
    {
        public string matKey;
        public string layerName;
        public string featureClass;
        public float  minZoom;
        public int    extent;
        public List<List<(int x, int y)>> segments = new();
    }

    public class LabelData
    {
        public float  wx, wy, wz;
        public string text;
        public string layerName;
        public string featureClass;     // e.g. "town", "village", "hamlet" for place layer
        public int    rank;
    }

    // ── Layer ordering ────────────────────────────────────────────────────────

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
        float offsetX, float offsetZ,
        float mapWidthX)
    {
        // Mirror X so that east (higher tileX) maps to lower worldX,
        // matching screen-right = world -X with north-up camera.
        // offsetX passed in is (tileX-MIN_X)*size; mirrored = mapWidthX - offsetX - tileWorldSize
        float mirroredOffX = mapWidthX - offsetX - tileWorldSize;

        var data = new TileGeometryData
        {
            tileX     = tileX,
            tileY     = tileY,
            offsetX   = mirroredOffX,   // TileRenderer reads this for line rendering
            offsetZ   = offsetZ,
            worldSize = tileWorldSize,
        };

        var polyAcc = new Dictionary<string, PolyAccumulator>();
        var lineAcc = new Dictionary<string, LineGroupData>();

        var layerMap = new Dictionary<string, MVTLayer>();
        foreach (var l in layers) layerMap[l.Name] = l;

        // Per-tile dedup: only suppress exact duplicate names within a single tile.
        // Cross-tile duplicates are intentional — the label visibility system will
        // handle showing only the closest/most-relevant one via zoom gating.
        var seenNames = new HashSet<string>();

        float highestPolyY = 0f;
        int   polygonIndex = 0;

        foreach (var layerName in LayerOrder)
        {
            if (!layerMap.TryGetValue(layerName, out var layer)) continue;

            if (layerName == "landcover")
                CollectLandcoverOrdered(layer, ref polygonIndex, ref highestPolyY,
                                        tileWorldSize, mirroredOffX, offsetZ, polyAcc);
            else
                CollectLayer(layer, ref polygonIndex, ref highestPolyY,
                             tileWorldSize, mirroredOffX, offsetZ,
                             polyAcc, lineAcc, data.labels, seenNames);
        }

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
        List<LabelData> labels,
        HashSet<string> seenNames)
    {
        foreach (var feature in layer.Features)
        {
            string fc     = GetProp(feature, "class");
            string name   = GetProp(feature, "name");
            string matKey = layer.Name + "/" + fc;

            switch (layer.Name)
            {
                case "transportation":
                case "waterway":
                    AccumulateLine(feature, layer, matKey, fc, lineAcc);
                    break;

                case "boundary":
                    AccumulateLine(feature, layer, layer.Name + "/boundary", "boundary", lineAcc);
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
                    // Only place if geometry is long + wide enough, and name not yet seen in this tile.
                    if (!string.IsNullOrEmpty(name)
                        && seenNames.Add(name)
                        && RoadFitsLabel(feature, name))
                    {
                        CollectLabel(feature, layer, name, fc, rank: 10,
                                     highestPolyY, tileWorldSize, offsetX, offsetZ, labels);
                    }
                    break;

                case "water_name":
                    if (!string.IsNullOrEmpty(name) && seenNames.Add(name))
                    {
                        int rank = int.TryParse(GetProp(feature, "rank"), out int wr) ? wr : 5;
                        CollectLabel(feature, layer, name, fc, rank,
                                     highestPolyY, tileWorldSize, offsetX, offsetZ, labels);
                    }
                    break;

                case "place":
                    if (!string.IsNullOrEmpty(name) && seenNames.Add(name))
                    {
                        int rank = int.TryParse(GetProp(feature, "rank"), out int pr) ? pr : 10;
                        CollectLabel(feature, layer, name, fc, rank,
                                     highestPolyY, tileWorldSize, offsetX, offsetZ, labels);
                    }
                    break;

                // poi and housenumber — skip entirely
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
                      + index * 0.0005f;
        if (yOffset > highestPolyY) highestPolyY = yOffset;

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

        // Winding: MVT Y-down flips CW, X-mirror flips back CCW — no manual reversal needed.

        int vertCount = earcutData.Count / 2;
        var verts = new List<float>(vertCount * 3);
        for (int i = 0; i < vertCount; i++)
        {
            // X is mirrored: offsetX is already flipped, and tile-local X runs right-to-left
            float wx = offsetX + (1.0f - (float)(earcutData[i * 2]     / layer.Extent)) * tileWorldSize;
            float wz = offsetZ + (float)(earcutData[i * 2 + 1] / layer.Extent) * tileWorldSize;
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
        Dictionary<string, LineGroupData> lineAcc)
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

    // ── Road geometry fitness check ───────────────────────────────────────────

    // In tile coords (extent = 4096)
    private const float CharLengthInTileCoords = 180f;
    private const float MinSegmentLength       = 250f;

    static bool RoadFitsLabel(MVTFeature feature, string text)
    {
        float required = text.Length * CharLengthInTileCoords;

        float bestRingLength = 0f;
        float bestMinSegment = 0f;

        foreach (var ring in feature.Geometry)
        {
            if (ring.Count < 2) continue;

            float totalLen = 0f;
            float minSeg   = float.MaxValue;

            for (int i = 0; i < ring.Count - 1; i++)
            {
                float dx  = ring[i + 1].x - ring[i].x;
                float dy  = ring[i + 1].y - ring[i].y;
                float len = (float)System.Math.Sqrt(dx * dx + dy * dy);
                totalLen += len;
                if (len < minSeg) minSeg = len;
            }

            if (totalLen > bestRingLength)
            {
                bestRingLength = totalLen;
                bestMinSegment = minSeg;
            }
        }

        return bestRingLength >= required && bestMinSegment >= MinSegmentLength;
    }

    // ── Label collection ──────────────────────────────────────────────────────

    static void CollectLabel(
        MVTFeature feature, MVTLayer layer, string text, string featureClass, int rank,
        float highestPolyY, float tileWorldSize,
        float offsetX, float offsetZ,
        List<LabelData> labels)
    {
        if (feature.Geometry.Count == 0 || feature.Geometry[0].Count == 0) return;

        var ring = feature.Geometry[0];
        var pt   = ring[ring.Count / 2];

        float wx = offsetX + (1.0f - (float)pt.x / layer.Extent) * tileWorldSize;
        float wz = offsetZ + ((float)pt.y / layer.Extent) * tileWorldSize;
        float wy = highestPolyY + tileWorldSize * 0.000001f;

        labels.Add(new LabelData
        {
            wx           = wx,
            wy           = wy,
            wz           = wz,
            text         = text,
            layerName    = layer.Name,
            featureClass = featureClass,
            rank         = rank,
        });
    }

    // ── Zoom / offset tables ──────────────────────────────────────────────────

    // Y-offsets keep layers visually separated without noticeable floating.
    // At tileWorldSize=1000 and a top-down orthographic-ish camera, 0.5 world
    // units per layer is imperceptible (< 0.03 degree tilt at height 1000).
    //
    // Per-polygon index step is now 0.0005 (was 0.001×tileWorldSize=1.0 — too big).
    // That gives 100 polygons of headroom before the next layer base is reached.
    //
    // Layer stack (bottom → top):
    //   0.00  landcover  (grass < farmland < wetland < sand < wood < forest)
    //   0.50  landuse    (residential < industrial < commercial < retail < cemetery < military)
    //   1.00  water      (lakes, ocean)
    //   2.00  park       (nature_reserve < national_park < regular park)
    //   2.50  building
    static float GetLayerYOffset(string layerName, string featureClass) => layerName switch
    {
        "landcover" => featureClass switch
        {
            "grass"    => 0.00f,
            "farmland" => 0.05f,
            "wetland"  => 0.10f,
            "sand"     => 0.15f,
            "wood"     => 0.20f,
            "forest"   => 0.25f,
            _          => 0.00f,
        },
        "landuse" => featureClass switch
        {
            "residential" => 0.50f,
            "industrial"  => 0.55f,
            "commercial"  => 0.60f,
            "retail"      => 0.65f,
            "cemetery"    => 0.70f,
            "military"    => 0.75f,
            _             => 0.50f,
        },
        "water"    => 1.00f,
        "park"     => featureClass switch
        {
            "nature_reserve" => 2.00f,
            "national_park"  => 2.05f,
            "protected_area" => 2.05f,
            _                => 2.10f,
        },
        "building" => 2.50f,
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