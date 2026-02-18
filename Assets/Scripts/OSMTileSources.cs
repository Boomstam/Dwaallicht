using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// OSMTileSources - A library of popular OSM-compatible tile server URL templates.
/// Use with OSMMapManager.SetTileSource() or assign in the Inspector via OSMMapUI.
/// 
/// NOTE: Always check each provider's Terms of Service before use in production.
/// OpenStreetMap tile servers have usage policies - for heavy use, host your own tiles
/// or use a commercial provider.
/// </summary>
[CreateAssetMenu(fileName = "OSMTileSources", menuName = "OSM/Tile Sources")]
public class OSMTileSources : ScriptableObject
{
    [System.Serializable]
    public class TileSource
    {
        public string name;
        [TextArea]
        public string description;
        public string urlTemplate;
        [Tooltip("Some providers require an API key. Insert it here and use {apikey} in the URL template.")]
        public string apiKey;
    }

    public List<TileSource> sources = new List<TileSource>()
    {
        new TileSource {
            name = "OpenStreetMap Standard",
            description = "The default OSM tile style. Free, requires attribution.",
            urlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png"
        },
        new TileSource {
            name = "OpenTopoMap",
            description = "Topographic map based on OSM data.",
            urlTemplate = "https://tile.opentopomap.org/{z}/{x}/{y}.png"
        },
        new TileSource {
            name = "Stamen Watercolor",
            description = "Artistic watercolor style map (via Stadia Maps).",
            urlTemplate = "https://tiles.stadiamaps.com/tiles/stamen_watercolor/{z}/{x}/{y}.jpg"
        },
        new TileSource {
            name = "Stamen Toner",
            description = "High-contrast black and white map.",
            urlTemplate = "https://tiles.stadiamaps.com/tiles/stamen_toner/{z}/{x}/{y}.png"
        },
        new TileSource {
            name = "Stamen Terrain",
            description = "Terrain map with hill shading.",
            urlTemplate = "https://tiles.stadiamaps.com/tiles/stamen_terrain/{z}/{x}/{y}.png"
        },
        new TileSource {
            name = "CartoDB Positron",
            description = "Clean, light-grey map great as a background layer.",
            urlTemplate = "https://cartodb-basemaps-a.global.ssl.fastly.net/light_all/{z}/{x}/{y}.png"
        },
        new TileSource {
            name = "CartoDB Dark Matter",
            description = "Dark-themed map ideal for data visualization.",
            urlTemplate = "https://cartodb-basemaps-a.global.ssl.fastly.net/dark_all/{z}/{x}/{y}.png"
        },
        new TileSource {
            name = "Esri World Imagery",
            description = "Satellite imagery from Esri (free, check ToS).",
            urlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"
        },
        new TileSource {
            name = "Esri World Street Map",
            description = "Street map from Esri.",
            urlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Street_Map/MapServer/tile/{z}/{y}/{x}"
        }
    };

    /// <summary>Get a tile source by name. Returns null if not found.</summary>
    public TileSource GetByName(string sourceName)
    {
        return sources.Find(s => s.name == sourceName);
    }

    /// <summary>Build the full URL for a tile, substituting {apikey} if needed.</summary>
    public string BuildUrl(TileSource source, int x, int y, int z)
    {
        return source.urlTemplate
            .Replace("{z}", z.ToString())
            .Replace("{x}", x.ToString())
            .Replace("{y}", y.ToString())
            .Replace("{apikey}", source.apiKey ?? "");
    }

    /// <summary>Return all source names as an array (useful for UI dropdowns).</summary>
    public string[] GetNames()
    {
        string[] names = new string[sources.Count];
        for (int i = 0; i < sources.Count; i++)
            names[i] = sources[i].name;
        return names;
    }
}