using UnityEngine;

public class MapTest : MonoBehaviour
{
    async void Start()
    {
        var reader = new PMTilesReader();
        await reader.Initialize();

        byte[] tileData = await reader.GetTile(14, 8388, 5480);

        if (tileData == null)
        {
            Debug.LogError("Tile still null.");
            return;
        }

        Debug.Log($"Got tile, size: {tileData.Length} bytes");

        var layers = MVTParser.Parse(tileData);
        Debug.Log($"Parsed {layers.Count} layers:");
        foreach (var layer in layers)
        {
            Debug.Log($"  Layer '{layer.Name}': {layer.Features.Count} features");
            if (layer.Features.Count > 0)
            {
                string propStr = "";
                foreach (var kv in layer.Features[0].Properties)
                    propStr += $"{kv.Key}={kv.Value} ";
                Debug.Log($"    First feature props: {propStr}");
            }
        }
    }
}