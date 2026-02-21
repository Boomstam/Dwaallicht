using UnityEngine;
using System.Collections.Generic;
using TMPro;

public class TileRenderer : MonoBehaviour
{
    public MapMaterials materials;
    public TMP_FontAsset labelFont;

    private float tileWorldSize;
    private float tileOffsetX;
    private float tileOffsetZ;
    private Dictionary<string, Material> _defaultMaterials = new();
    private float _highestPolygonY = 0f;

    private static readonly string[] LayerOrder = new[]
    {
        "landcover",
        "landuse",
        "water",
        "park",
        "waterway",
        "boundary",
        "building",
        "transportation",
        "transportation_name",
        "water_name",
        "place",
        "poi",
        "housenumber",
    };

    private static readonly string[] LandcoverOrder = new[]
    {
        "grass", "farmland", "wetland", "sand", "wood", "forest"
    };

    public void Render(List<MVTLayer> layers, int z, int x, int y, float worldSize, float offsetX, float offsetZ)
    {
        tileWorldSize = worldSize;
        tileOffsetX = offsetX;
        tileOffsetZ = offsetZ;
        _highestPolygonY = 0f;

        // Always keep the tile GO at origin - world offset is baked into geometry
        transform.localPosition = Vector3.zero;

        var layerMap = new Dictionary<string, MVTLayer>();
        foreach (var layer in layers)
            layerMap[layer.Name] = layer;

        int polygonIndex = 0;

        foreach (var layerName in LayerOrder)
        {
            if (!layerMap.TryGetValue(layerName, out var layer)) continue;

            if (layerName == "landcover")
                RenderLandcoverOrdered(layer, ref polygonIndex);
            else
                RenderLayer(layer, ref polygonIndex);
        }
    }

    void RenderLandcoverOrdered(MVTLayer layer, ref int polygonIndex)
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
            {
                Material mat = materials != null
                    ? materials.Resolve("landcover", fc, "") ?? GetDefaultMaterial("landcover", fc)
                    : GetDefaultMaterial("landcover", fc);
                RenderPolygon(feature, layer, mat, "landcover/" + fc, fc, polygonIndex++);
            }
        }

        foreach (var kv in byClass)
        {
            if (System.Array.IndexOf(LandcoverOrder, kv.Key) >= 0) continue;
            foreach (var feature in kv.Value)
            {
                Material mat = materials != null
                    ? materials.Resolve("landcover", kv.Key, "") ?? GetDefaultMaterial("landcover", kv.Key)
                    : GetDefaultMaterial("landcover", kv.Key);
                RenderPolygon(feature, layer, mat, "landcover/" + kv.Key, kv.Key, polygonIndex++);
            }
        }
    }

    void RenderLayer(MVTLayer layer, ref int polygonIndex)
    {
        foreach (var feature in layer.Features)
        {
            string featureClass = GetProp(feature, "class");
            string subclass = GetProp(feature, "subclass");
            string name = GetProp(feature, "name");

            Material mat = materials != null
                ? materials.Resolve(layer.Name, featureClass, subclass) ?? GetDefaultMaterial(layer.Name, featureClass)
                : GetDefaultMaterial(layer.Name, featureClass);

            switch (layer.Name)
            {
                case "transportation":
                    RenderLine(feature, layer, mat, layer.Name + "/" + featureClass);
                    break;
                case "waterway":
                    RenderLine(feature, layer, mat, layer.Name + "/" + featureClass);
                    break;
                case "building":
                    RenderPolygon(feature, layer, mat, "building", featureClass, polygonIndex++);
                    break;
                case "water":
                case "landuse":
                case "park":
                    RenderPolygon(feature, layer, mat, layer.Name + "/" + featureClass, featureClass, polygonIndex++);
                    break;
                case "boundary":
                    RenderLine(feature, layer, mat, "boundary");
                    break;
                case "transportation_name":
                case "water_name":
                case "place":
                case "poi":
                    if (!string.IsNullOrEmpty(name))
                        RenderLabel(feature, layer, name);
                    break;
                case "housenumber":
                    if (!string.IsNullOrEmpty(GetProp(feature, "housenumber")))
                        RenderLabel(feature, layer, GetProp(feature, "housenumber"));
                    break;
            }
        }
    }

    // Convert tile coordinate to world position with baked offset
    Vector3 ToWorld(int tx, int ty, int extent, float yOffset = 0f)
    {
        float x = tileOffsetX + ((float)tx / extent) * tileWorldSize;
        float z = tileOffsetZ + ((float)ty / extent) * tileWorldSize;
        return new Vector3(x, yOffset, z);
    }

    void RenderLine(MVTFeature feature, MVTLayer layer, Material mat, string label)
    {
        if (mat == null) return;

        foreach (var ring in feature.Geometry)
        {
            if (ring.Count < 2) continue;

            var go = new GameObject(label);
            go.transform.parent = transform;
            go.transform.localPosition = Vector3.zero;

            var lr = go.AddComponent<LineRenderer>();
            lr.material = mat;
            lr.positionCount = ring.Count;
            lr.useWorldSpace = true;

            float width = GetLineWidth(label);
            lr.startWidth = lr.endWidth = width;
            lr.numCapVertices = 4;

            for (int i = 0; i < ring.Count; i++)
                lr.SetPosition(i, ToWorld(ring[i].x, ring[i].y, layer.Extent));
        }
    }

    void RenderPolygon(MVTFeature feature, MVTLayer layer, Material mat, string label, string featureClass, int index)
    {
        if (mat == null) return;
        if (feature.Geometry.Count == 0) return;

        var outerRing = feature.Geometry[0];
        if (outerRing.Count < 3) return;

        var go = new GameObject(label);
        go.transform.parent = transform;
        go.transform.localPosition = Vector3.zero;

        float yOffset = GetLayerYOffset(layer.Name, featureClass) + index * (tileWorldSize * 0.000001f);
        if (yOffset > _highestPolygonY) _highestPolygonY = yOffset;

        var verts = new List<Vector3>();
        foreach (var pt in outerRing)
            verts.Add(ToWorld(pt.x, pt.y, layer.Extent, yOffset));

        if (verts.Count > 1 && verts[0] == verts[verts.Count - 1])
            verts.RemoveAt(verts.Count - 1);

        var tris = Triangulate(verts);

        var mesh = new Mesh();
        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.RecalculateNormals();

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        mf.mesh = mesh;
        mr.material = mat;
    }

    void RenderLabel(MVTFeature feature, MVTLayer layer, string text)
    {
        if (labelFont == null) return;
        if (feature.Geometry.Count == 0 || feature.Geometry[0].Count == 0) return;

        var ring = feature.Geometry[0];
        var pt = ring[ring.Count / 2];
        Vector3 worldPos = ToWorld(pt.x, pt.y, layer.Extent, _highestPolygonY + tileWorldSize * 0.000001f);

        var go = new GameObject("label:" + text);
        go.transform.parent = transform;
        go.transform.position = worldPos;
        go.transform.rotation = Quaternion.Euler(90, 0, 0);

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = labelFont;
        tmp.text = text;
        tmp.fontSize = tileWorldSize * 0.02f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
    }

    Material GetDefaultMaterial(string layer = "", string featureClass = "")
    {
        string key = layer + "/" + featureClass;
        if (_defaultMaterials.TryGetValue(key, out var existing)) return existing;

        Color color = layer switch
        {
            "transportation" => featureClass switch
            {
                "motorway"   => new Color(0.9f, 0.6f, 0.2f),
                "trunk"      => new Color(0.9f, 0.7f, 0.3f),
                "primary"    => new Color(0.95f, 0.85f, 0.4f),
                "secondary"  => new Color(1f, 1f, 0.6f),
                "tertiary"   => new Color(0.9f, 0.9f, 0.9f),
                "rail"       => new Color(0.5f, 0.4f, 0.5f),
                "path"       => new Color(0.7f, 0.6f, 0.5f),
                "cycleway"   => new Color(0.4f, 0.7f, 0.4f),
                _            => new Color(0.8f, 0.8f, 0.8f),
            },
            "waterway"  => new Color(0.4f, 0.6f, 0.9f),
            "water"     => new Color(0.3f, 0.55f, 0.85f),
            "landcover" => featureClass switch
            {
                "grass"    => new Color(0.6f, 0.8f, 0.5f),
                "wood"     => new Color(0.3f, 0.6f, 0.3f),
                "forest"   => new Color(0.3f, 0.6f, 0.3f),
                "sand"     => new Color(0.9f, 0.85f, 0.65f),
                "wetland"  => new Color(0.5f, 0.7f, 0.6f),
                "farmland" => new Color(0.75f, 0.85f, 0.6f),
                _          => new Color(0.7f, 0.8f, 0.65f),
            },
            "landuse" => featureClass switch
            {
                "residential" => new Color(0.92f, 0.88f, 0.84f),
                "industrial"  => new Color(0.75f, 0.72f, 0.78f),
                "commercial"  => new Color(0.85f, 0.78f, 0.82f),
                "retail"      => new Color(0.9f, 0.75f, 0.75f),
                "cemetery"    => new Color(0.7f, 0.8f, 0.7f),
                "military"    => new Color(0.6f, 0.65f, 0.55f),
                _             => new Color(0.85f, 0.85f, 0.82f),
            },
            "park"     => new Color(0.5f, 0.75f, 0.45f),
            "building" => new Color(0.78f, 0.74f, 0.70f),
            "boundary" => new Color(0.7f, 0.4f, 0.4f),
            _          => new Color(0.85f, 0.85f, 0.85f),
        };

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = color;
        _defaultMaterials[key] = mat;
        return mat;
    }

    float GetLayerYOffset(string layerName, string featureClass = "")
    {
        return layerName switch
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
            "landuse"   => 0.01f,
            "water"     => 0.02f,
            "park"      => 0.03f,
            "building"  => 0.04f,
            _           => 0f,
        };
    }

    float GetLineWidth(string label)
    {
        if (label.Contains("motorway"))  return 2f;
        if (label.Contains("trunk"))     return 1.8f;
        if (label.Contains("primary"))   return 1.5f;
        if (label.Contains("secondary")) return 1.2f;
        if (label.Contains("tertiary"))  return 1f;
        if (label.Contains("rail"))      return 0.8f;
        return 0.5f;
    }

    List<int> Triangulate(List<Vector3> verts)
    {
        var tris = new List<int>();
        var indices = new List<int>();
        for (int i = 0; i < verts.Count; i++) indices.Add(i);

        int safety = verts.Count * verts.Count;
        int idx = 0;

        while (indices.Count > 3 && safety-- > 0)
        {
            int count = indices.Count;
            int a = indices[(idx + 0) % count];
            int b = indices[(idx + 1) % count];
            int c = indices[(idx + 2) % count];

            if (IsEar(verts, indices, idx))
            {
                tris.Add(a); tris.Add(b); tris.Add(c);
                indices.RemoveAt((idx + 1) % count);
            }
            else
            {
                idx++;
            }
            idx %= indices.Count;
        }

        if (indices.Count == 3)
        {
            tris.Add(indices[0]);
            tris.Add(indices[1]);
            tris.Add(indices[2]);
        }

        return tris;
    }

    bool IsEar(List<Vector3> verts, List<int> indices, int idx)
    {
        int count = indices.Count;
        int a = indices[(idx + 0) % count];
        int b = indices[(idx + 1) % count];
        int c = indices[(idx + 2) % count];

        Vector3 va = verts[a], vb = verts[b], vc = verts[c];

        Vector3 cross = Vector3.Cross(vb - va, vc - va);
        if (cross.y < 0) return false;

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
        bool hasNeg = d1 < 0 || d2 < 0 || d3 < 0;
        bool hasPos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNeg && hasPos);
    }

    float Sign(Vector3 p1, Vector3 p2, Vector3 p3) =>
        (p1.x - p3.x) * (p2.z - p3.z) - (p2.x - p3.x) * (p1.z - p3.z);

    string GetProp(MVTFeature feature, string key) =>
        feature.Properties.TryGetValue(key, out var val) ? val?.ToString() ?? "" : "";
}