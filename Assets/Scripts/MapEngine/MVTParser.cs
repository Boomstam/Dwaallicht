using System.Collections.Generic;

public class MVTLayer
{
    public string Name;
    public int Extent = 4096;
    public List<MVTFeature> Features = new();
}

public class MVTFeature
{
    public GeomType Type;
    public Dictionary<string, object> Properties = new();
    public List<List<(int x, int y)>> Geometry = new();

    public enum GeomType { Unknown = 0, Point = 1, LineString = 2, Polygon = 3 }
}

public static class MVTParser
{
    public static List<MVTLayer> Parse(byte[] data)
    {
        var layers = new List<MVTLayer>();
        int pos = 0;

        while (pos < data.Length)
        {
            var (fieldNumber, wireType) = ReadTag(data, ref pos);
            if (fieldNumber == 3 && wireType == 2) // layer
            {
                int layerLen = (int)ReadVarint(data, ref pos);
                byte[] layerData = new byte[layerLen];
                System.Array.Copy(data, pos, layerData, 0, layerLen);
                pos += layerLen;
                layers.Add(ParseLayer(layerData));
            }
            else
            {
                SkipField(data, ref pos, wireType);
            }
        }

        return layers;
    }

    static MVTLayer ParseLayer(byte[] data)
    {
        var layer = new MVTLayer();
        var keys = new List<string>();
        var values = new List<object>();
        var rawFeatures = new List<byte[]>();
        int pos = 0;

        while (pos < data.Length)
        {
            var (fieldNumber, wireType) = ReadTag(data, ref pos);
            switch (fieldNumber)
            {
                case 1: // name
                    layer.Name = ReadString(data, ref pos);
                    break;
                case 2: // feature
                    int featLen = (int)ReadVarint(data, ref pos);
                    var featData = new byte[featLen];
                    System.Array.Copy(data, pos, featData, 0, featLen);
                    pos += featLen;
                    rawFeatures.Add(featData);
                    break;
                case 3: // key
                    keys.Add(ReadString(data, ref pos));
                    break;
                case 4: // value
                    int valLen = (int)ReadVarint(data, ref pos);
                    var valData = new byte[valLen];
                    System.Array.Copy(data, pos, valData, 0, valLen);
                    pos += valLen;
                    values.Add(ParseValue(valData));
                    break;
                case 5: // extent
                    layer.Extent = (int)ReadVarint(data, ref pos);
                    break;
                default:
                    SkipField(data, ref pos, wireType);
                    break;
            }
        }

        foreach (var fd in rawFeatures)
            layer.Features.Add(ParseFeature(fd, keys, values));

        return layer;
    }

    static MVTFeature ParseFeature(byte[] data, List<string> keys, List<object> values)
    {
        var feature = new MVTFeature();
        int pos = 0;

        while (pos < data.Length)
        {
            var (fieldNumber, wireType) = ReadTag(data, ref pos);
            switch (fieldNumber)
            {
                case 2: // tags (packed uint32)
                    int tagsLen = (int)ReadVarint(data, ref pos);
                    int end = pos + tagsLen;
                    while (pos < end)
                    {
                        int keyIdx = (int)ReadVarint(data, ref pos);
                        int valIdx = (int)ReadVarint(data, ref pos);
                        if (keyIdx < keys.Count && valIdx < values.Count)
                            feature.Properties[keys[keyIdx]] = values[valIdx];
                    }
                    break;
                case 3: // type
                    feature.Type = (MVTFeature.GeomType)ReadVarint(data, ref pos);
                    break;
                case 4: // geometry (packed uint32)
                    int geomLen = (int)ReadVarint(data, ref pos);
                    int geomEnd = pos + geomLen;
                    feature.Geometry = DecodeGeometry(data, ref pos, geomEnd, feature.Type);
                    break;
                default:
                    SkipField(data, ref pos, wireType);
                    break;
            }
        }

        return feature;
    }

    static List<List<(int x, int y)>> DecodeGeometry(byte[] data, ref int pos, int end, MVTFeature.GeomType type)
    {
        var rings = new List<List<(int, int)>>();
        List<(int, int)> current = null;
        int cx = 0, cy = 0;

        while (pos < end)
        {
            uint cmd = (uint)ReadVarint(data, ref pos);
            int cmdId = (int)(cmd & 0x7);
            int count = (int)(cmd >> 3);

            if (cmdId == 1 || cmdId == 2) // MoveTo or LineTo
            {
                if (cmdId == 1)
                {
                    current = new List<(int, int)>();
                    rings.Add(current);
                }
                for (int i = 0; i < count; i++)
                {
                    int dx = DecodeZigZag(ReadVarint(data, ref pos));
                    int dy = DecodeZigZag(ReadVarint(data, ref pos));
                    cx += dx; cy += dy;
                    current?.Add((cx, cy));
                }
            }
            else if (cmdId == 7) // ClosePath
            {
                if (current != null && current.Count > 0)
                    current.Add(current[0]); // close the ring
            }
        }

        return rings;
    }

    static object ParseValue(byte[] data)
    {
        int pos = 0;
        while (pos < data.Length)
        {
            var (fieldNumber, wireType) = ReadTag(data, ref pos);
            switch (fieldNumber)
            {
                case 1: return ReadString(data, ref pos);
                case 2: var f = ReadFloat(data, ref pos); return f;
                case 3: var d = ReadDouble(data, ref pos); return d;
                case 4: return (long)ReadVarint(data, ref pos);
                case 5: return (long)ReadVarint(data, ref pos);
                case 6: return DecodeZigZag(ReadVarint(data, ref pos));
                case 7: return ReadVarint(data, ref pos) != 0;
                default: SkipField(data, ref pos, wireType); break;
            }
        }
        return null;
    }

    // --- Protobuf primitives ---

    static (int field, int wire) ReadTag(byte[] data, ref int pos)
    {
        ulong tag = ReadVarint(data, ref pos);
        return ((int)(tag >> 3), (int)(tag & 0x7));
    }

    static ulong ReadVarint(byte[] data, ref int pos)
    {
        ulong result = 0; int shift = 0;
        while (true)
        {
            byte b = data[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return result;
    }

    static string ReadString(byte[] data, ref int pos)
    {
        int len = (int)ReadVarint(data, ref pos);
        string s = System.Text.Encoding.UTF8.GetString(data, pos, len);
        pos += len;
        return s;
    }

    static float ReadFloat(byte[] data, ref int pos)
    {
        float v = System.BitConverter.ToSingle(data, pos);
        pos += 4; return v;
    }

    static double ReadDouble(byte[] data, ref int pos)
    {
        double v = System.BitConverter.ToDouble(data, pos);
        pos += 8; return v;
    }

    static int DecodeZigZag(ulong n) => (int)((n >> 1) ^ (ulong)(-(long)(n & 1)));

    static void SkipField(byte[] data, ref int pos, int wireType)
    {
        switch (wireType)
        {
            case 0: ReadVarint(data, ref pos); break;
            case 1: pos += 8; break;
            case 2: pos += (int)ReadVarint(data, ref pos); break;
            case 5: pos += 4; break;
        }
    }
}