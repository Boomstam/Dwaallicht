using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "MapMaterials", menuName = "Map/MapMaterials")]
public class MapMaterials : ScriptableObject
{
    [Header("Transportation")]
    public Material motorway;
    public Material trunk;
    public Material primary;
    public Material secondary;
    public Material tertiary;
    public Material minor;
    public Material service;
    public Material path;
    public Material track;
    public Material cycleway;
    public Material rail;
    public Material ferry;

    [Header("Waterway")]
    public Material river;
    public Material canal;
    public Material stream;
    public Material ditch;

    [Header("Water Bodies")]
    public Material water;

    [Header("Land")]
    public Material grass;
    public Material forest;
    public Material sand;
    public Material wetland;
    public Material farmland;
    public Material rock;

    [Header("Landuse")]
    public Material residential;
    public Material industrial;
    public Material commercial;
    public Material retail;
    public Material park;
    public Material cemetery;
    public Material military;

    [Header("Buildings")]
    public Material building;

    [Header("Boundaries")]
    public Material boundary;

    // Resolve a material by layer name and feature class/subclass
    public Material Resolve(string layer, string featureClass, string subclass = "")
    {
        switch (layer)
        {
            case "transportation":
                switch (featureClass)
                {
                    case "motorway": return motorway;
                    case "trunk":    return trunk;
                    case "primary":  return primary;
                    case "secondary":return secondary;
                    case "tertiary": return tertiary;
                    case "minor":    return minor;
                    case "service":  return service;
                    case "path":     return subclass == "cycleway" ? cycleway : path;
                    case "track":    return track;
                    case "rail":     return rail;
                    case "ferry":    return ferry;
                    default:         return minor;
                }
            case "waterway":
                switch (featureClass)
                {
                    case "river": return river;
                    case "canal": return canal;
                    case "stream":return stream;
                    case "ditch": return ditch;
                    default:      return stream;
                }
            case "water":
            case "water_name":
                return water;
            case "landcover":
                switch (featureClass)
                {
                    case "grass":   return grass;
                    case "wood":
                    case "forest":  return forest;
                    case "sand":    return sand;
                    case "wetland": return wetland;
                    case "farmland":return farmland;
                    case "rock":    return rock;
                    default:        return grass;
                }
            case "landuse":
                switch (featureClass)
                {
                    case "residential": return residential;
                    case "industrial":  return industrial;
                    case "commercial":  return commercial;
                    case "retail":      return retail;
                    case "cemetery":    return cemetery;
                    case "military":    return military;
                    default:            return residential;
                }
            case "park":      return park;
            case "building":  return building;
            case "boundary":  return boundary;
            default:          return null;
        }
    }
}