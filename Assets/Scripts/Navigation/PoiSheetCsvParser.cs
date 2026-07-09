using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Dwaallicht.Navigation
{
    public static class PoiSheetCsvParser
    {
        public static bool TryParse(string csv, Color pinColor, out List<PointOfInterest> pois, out string error)
        {
            pois = new List<PointOfInterest>();
            error = "";

            var records = ParseRecords(csv);
            if (records.Count == 0)
            {
                error = "CSV is empty.";
                return false;
            }

            var header = records[0];
            var nameIndex = FindColumn(header, "NAME", "TITLE", "NAAM");
            var latitudeIndex = FindColumn(header, "LATITUDE", "LAT", "BREEDTEGRAAD");
            var longitudeIndex = FindColumn(header, "LONGITUDE", "LON", "LNG", "LENGTEGRAAD");
            var publishIndex = FindColumn(header, "PUBLISH", "PUBLICEREN", "ACTIVE");
            var arIndex = FindColumn(header, "AR", "HASAR", "HAS_AR", "HAS AR", "AUGMENTEDREALITY", "AUGMENTED REALITY");
            var storylineIndex = FindColumn(header, "STORYLINE", "STORY", "CATEGORY", "CATEGORIE", "TYPE");

            if (nameIndex < 0 || latitudeIndex < 0 || longitudeIndex < 0)
            {
                error = "CSV must contain NAME, LATITUDE and LONGITUDE columns.";
                return false;
            }

            for (var i = 1; i < records.Count; i++)
            {
                var record = records[i];
                if (IsBlank(record))
                {
                    continue;
                }

                if (publishIndex >= 0 && !IsPublished(GetField(record, publishIndex)))
                {
                    continue;
                }

                var title = GetField(record, nameIndex).Trim();
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                if (!TryParseFloat(GetField(record, latitudeIndex), out var latitude)
                    || !TryParseFloat(GetField(record, longitudeIndex), out var longitude))
                {
                    continue;
                }

                if (LooksLikeSwappedBelgianCoordinates(latitude, longitude))
                {
                    var temp = latitude;
                    latitude = longitude;
                    longitude = temp;
                }

                pois.Add(new PointOfInterest
                {
                    id = BuildStableId(title, latitude, longitude),
                    title = title,
                    category = GetSheetCategory(GetField(record, storylineIndex)),
                    description = "",
                    latitude = latitude,
                    longitude = longitude,
                    color = GetSheetColor(GetField(record, storylineIndex), pinColor),
                    hasAr = arIndex >= 0 && IsPublished(GetField(record, arIndex)),
                    active = true
                });
            }

            return true;
        }

        private static string GetSheetCategory(string storyline)
        {
            var normalized = (storyline ?? "").Trim();
            if (string.Equals(normalized, "LIVE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "EVENT", StringComparison.OrdinalIgnoreCase))
            {
                return "Event";
            }

            return "Sheet";
        }

        private static Color GetSheetColor(string storyline, Color fallbackColor)
        {
            var normalized = (storyline ?? "").Trim();
            if (string.Equals(normalized, "LIVE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "EVENT", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(222f / 255f, 22f / 255f, 32f / 255f, 1f);
            }

            if (normalized == "1"
                || string.Equals(normalized, "YELLOW", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(255f / 255f, 203f / 255f, 34f / 255f, 1f);
            }

            if (normalized == "2"
                || string.Equals(normalized, "PURPLE", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(138f / 255f, 61f / 255f, 199f / 255f, 1f);
            }

            return fallbackColor;
        }

        private static List<List<string>> ParseRecords(string csv)
        {
            var records = new List<List<string>>();
            if (string.IsNullOrEmpty(csv))
            {
                return records;
            }

            var record = new List<string>();
            var field = "";
            var inQuotes = false;

            for (var i = 0; i < csv.Length; i++)
            {
                var c = csv[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field += '"';
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    record.Add(field);
                    field = "";
                }
                else if ((c == '\n' || c == '\r') && !inQuotes)
                {
                    if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                    {
                        i++;
                    }

                    record.Add(field);
                    field = "";
                    records.Add(record);
                    record = new List<string>();
                }
                else
                {
                    field += c;
                }
            }

            record.Add(field);
            if (!IsBlank(record))
            {
                records.Add(record);
            }

            return records;
        }

        private static int FindColumn(List<string> header, params string[] names)
        {
            for (var i = 0; i < header.Count; i++)
            {
                var column = NormalizeHeader(header[i]);
                for (var j = 0; j < names.Length; j++)
                {
                    if (column == NormalizeHeader(names[j]))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static string NormalizeHeader(string value)
        {
            return (value ?? "").Trim().Replace(" ", "").Replace("_", "").ToUpperInvariant();
        }

        private static bool IsBlank(List<string> record)
        {
            for (var i = 0; i < record.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(record[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetField(List<string> record, int index)
        {
            return index >= 0 && index < record.Count ? record[index] ?? "" : "";
        }

        private static bool TryParseFloat(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result)
                || float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
        }

        private static bool IsPublished(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim();
            return string.Equals(normalized, "YES", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "TRUE", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "Y", StringComparison.OrdinalIgnoreCase)
                || normalized == "1";
        }

        private static bool LooksLikeSwappedBelgianCoordinates(float latitude, float longitude)
        {
            return Mathf.Abs(latitude) < 15f
                && Mathf.Abs(longitude) > 35f
                && Mathf.Abs(longitude) <= 90f;
        }

        private static string BuildStableId(string title, float latitude, float longitude)
        {
            var value = $"{title.Trim().ToLowerInvariant()}|{latitude:0.000000}|{longitude:0.000000}";
            unchecked
            {
                uint hash = 2166136261;
                for (var i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= 16777619;
                }

                return "sheet-" + hash.ToString("x8");
            }
        }
    }
}
