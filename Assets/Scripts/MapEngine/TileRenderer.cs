using UnityEngine;
using System.Collections.Generic;
using TMPro;

public class TileRenderer : MonoBehaviour
{
    public MapMaterials materials;
    public TMP_FontAsset labelFont;

    private float tileWorldSize;
    private int tileX, tileY, tileZoom;
    private Dictionary<string, Material> _defaultMaterials = new();

    public void Render(List<MVTLayer> layers, int z, int x, int y, float worldSize)
    {
        tileZoom = z; tileX = x; tileY = y;
        tileWorldSize = worldSize;

        foreach (var layer in layers)
            RenderLayer(layer);
    }

    void RenderLayer(MVTLayer layer)
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
                    RenderPolygon(feature, layer, mat, "building");
                    break;
                case "water":
                case "landcover":
                case "landuse":
                case "park":
                    RenderPolygon(feature, layer, mat, layer.Name + "/" + featureClass);
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

    void RenderLine(MVTFeature feature, MVTLayer layer, Material mat, string label)
    {
        if (mat == null) return;

        foreach (var ring in feature.Geometry)
        {
            if (ring.Count < 2) continue;

            var go = new GameObject(label);
            go.transform.parent = transform;

            var lr = go.AddComponent<LineRenderer>();
            lr.material = mat;
            lr.positionCount = ring.Count;
            lr.useWorldSpace = false;

            float width = GetLineWidth(label);
            lr.startWidth = lr.endWidth = width;
            lr.numCapVertices = 4;

            for (int i = 0; i < ring.Count; i++)
                lr.SetPosition(i, TileCoordToLocal(ring[i].x, ring[i].y, layer.Extent));
        }
    }

    void RenderPolygon(MVTFeature feature, MVTLayer layer, Material mat, string label)
    {
        if (mat == null) return;
        if (feature.Geometry.Count == 0) return;

        var outerRing = feature.Geometry[0];
        if (outerRing.Count < 3) return;

        var go = new GameObject(label);
        go.transform.parent = transform;

        var mesh = new Mesh();
        var verts = new List<Vector3>();
        var tris = new List<int>();

        foreach (var pt in outerRing)
            verts.Add(TileCoordToLocal(pt.x, pt.y, layer.Extent));

        if (verts.Count > 1 && verts[0] == verts[verts.Count - 1])
            verts.RemoveAt(verts.Count - 1);

        tris.AddRange(Triangulate(verts));

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

        var pt = feature.Geometry[0][0];
        Vector3 pos = TileCoordToLocal(pt.x, pt.y, layer.Extent);

        var go = new GameObject("label:" + text);
        go.transform.parent = transform;
        go.transform.localPosition = pos + Vector3.up * 0.05f;
        go.transform.localRotation = Quaternion.Euler(90, 0, 0);

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = labelFont;
        tmp.text = text;
        tmp.fontSize = 1.5f;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.black;
    }

    Vector3 TileCoordToLocal(int tx, int ty, int extent)
    {
        float x = ((float)tx / extent) * tileWorldSize;
        float z = ((float)ty / extent) * tileWorldSize;
        return new Vector3(x, 0, z);
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