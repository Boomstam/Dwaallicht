using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Dwaallicht.Navigation
{
    [AddComponentMenu("Dwaallicht/Navigation/POI Manager")]
    public sealed class PoiManager : MonoBehaviour
    {
        [SerializeField] private bool loadSavedPois = true;
        [SerializeField] private List<PointOfInterest> pois = new List<PointOfInterest>();

        public IReadOnlyList<PointOfInterest> Pois => pois;
        public PointOfInterest SelectedPoi { get; private set; }
        public event Action PoisChanged;
        public event Action<PointOfInterest> SelectionChanged;

        private string SavePath => Path.Combine(Application.persistentDataPath, "dwaallicht-pois.json");

        private void Awake()
        {
            if (loadSavedPois)
            {
                Load();
            }

            if (pois.Count == 0)
            {
                SeedDefaults();
            }

            foreach (var poi in pois)
            {
                poi.EnsureId();
            }
        }

        public PointOfInterest AddPoi(string title, Vector2 latLon, string category, string description)
        {
            var poi = new PointOfInterest
            {
                title = string.IsNullOrWhiteSpace(title) ? "Nieuw punt" : title.Trim(),
                category = string.IsNullOrWhiteSpace(category) ? "Algemeen" : category.Trim(),
                description = description ?? "",
                latitude = latLon.x,
                longitude = latLon.y,
                color = CategoryColor(category)
            };

            poi.EnsureId();
            pois.Add(poi);
            Save();
            PoisChanged?.Invoke();
            SelectPoi(poi);
            return poi;
        }

        public void SelectPoi(PointOfInterest poi)
        {
            SelectedPoi = poi != null && poi.active ? poi : null;
            SelectionChanged?.Invoke(SelectedPoi);
        }

        public void SelectPoi(string id)
        {
            SelectPoi(pois.Find(p => p.id == id));
        }

        public void RemovePoi(PointOfInterest poi)
        {
            if (poi == null)
            {
                return;
            }

            pois.Remove(poi);
            if (SelectedPoi == poi)
            {
                SelectedPoi = null;
                SelectionChanged?.Invoke(null);
            }

            Save();
            PoisChanged?.Invoke();
        }

        public void Save()
        {
            try
            {
                var wrapper = new PoiListWrapper { pois = pois };
                File.WriteAllText(SavePath, JsonUtility.ToJson(wrapper, true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PoiManager] Could not save POIs: {ex.Message}");
            }
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(SavePath))
                {
                    return;
                }

                var wrapper = JsonUtility.FromJson<PoiListWrapper>(File.ReadAllText(SavePath));
                if (wrapper?.pois != null)
                {
                    pois = wrapper.pois;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PoiManager] Could not load POIs: {ex.Message}");
            }
        }

        private void SeedDefaults()
        {
            pois.Add(new PointOfInterest
            {
                title = "Startpunt",
                category = "Route",
                description = "Testpunt in het midden van de kaart.",
                latitude = 51.18623f,
                longitude = 4.22974f,
                color = new Color(0.15f, 0.63f, 0.23f, 1f)
            });
            pois.Add(new PointOfInterest
            {
                title = "Verhaalpunt",
                category = "Verhaal",
                description = "Voorbeeld-POI om selectie en navigatie te testen.",
                latitude = 51.18845f,
                longitude = 4.2255f,
                color = new Color(0.86f, 0.08f, 0.13f, 1f)
            });
        }

        private static Color CategoryColor(string category)
        {
            if (string.Equals(category, "Route", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(0.15f, 0.63f, 0.23f, 1f);
            }

            if (string.Equals(category, "Verhaal", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(0.86f, 0.08f, 0.13f, 1f);
            }

            if (string.Equals(category, "Event", StringComparison.OrdinalIgnoreCase))
            {
                return new Color(1f, 0.72f, 0.08f, 1f);
            }

            return new Color(0.12f, 0.55f, 0.95f, 1f);
        }

        [Serializable]
        private sealed class PoiListWrapper
        {
            public List<PointOfInterest> pois;
        }
    }
}
