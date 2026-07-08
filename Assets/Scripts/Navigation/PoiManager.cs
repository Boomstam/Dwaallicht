using System;
using System.Collections.Generic;
using System.IO;
using Dwaallicht.Cloud;
using UnityEngine;

namespace Dwaallicht.Navigation
{
    [AddComponentMenu("Dwaallicht/Navigation/POI Manager")]
    public sealed class PoiManager : MonoBehaviour
    {
        [SerializeField] private bool loadSavedPois = false;
        [SerializeField] private bool loadSheetPois = true;
        [SerializeField] private string sheetFolderName = "DriveSync";
        [SerializeField] private string sheetFileName = "Points of Interest.csv";
        [SerializeField] private bool reloadAfterDriveSync = true;
        [SerializeField] private Color sheetPoiColor = new Color(0.86f, 0.08f, 0.13f, 1f);
        [SerializeField] private List<PointOfInterest> pois = new List<PointOfInterest>();

        public IReadOnlyList<PointOfInterest> Pois => pois;
        public PointOfInterest SelectedPoi { get; private set; }
        public event Action PoisChanged;
        public event Action<PointOfInterest> SelectionChanged;

        private GoogleDriveFolderSync driveSync;
        private string SavePath => Path.Combine(Application.persistentDataPath, "dwaallicht-pois.json");

        private void Awake()
        {
            if (loadSavedPois)
            {
                Load();
            }

            var loadedSheetPois = loadSheetPois && TryLoadSheetPois();
            if (!loadedSheetPois && pois.Count == 0)
            {
                SeedDefaults();
            }

            foreach (var poi in pois)
            {
                poi.EnsureId();
            }
        }

        private void OnEnable()
        {
            SubscribeToDriveSync();
        }

        private void Start()
        {
            SubscribeToDriveSync();
        }

        private void OnDisable()
        {
            if (driveSync != null)
            {
                driveSync.SyncFinished -= HandleDriveSyncFinished;
                driveSync = null;
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

        private void SubscribeToDriveSync()
        {
            if (!reloadAfterDriveSync || driveSync != null)
            {
                return;
            }

            driveSync = FindFirstObjectByType<GoogleDriveFolderSync>();
            if (driveSync != null)
            {
                driveSync.SyncFinished += HandleDriveSyncFinished;
            }
        }

        private void HandleDriveSyncFinished(bool success)
        {
            if (!success || !loadSheetPois || !TryLoadSheetPois())
            {
                return;
            }

            PoisChanged?.Invoke();
            if (SelectedPoi == null || !pois.Contains(SelectedPoi))
            {
                SelectPoi(pois.Count > 0 ? pois[0] : null);
            }
        }

        private bool TryLoadSheetPois()
        {
            foreach (var path in GetSheetCandidatePaths())
            {
                if (string.IsNullOrWhiteSpace(path) || path.Contains("://") || !File.Exists(path))
                {
                    continue;
                }

                try
                {
                    if (!PoiSheetCsvParser.TryParse(File.ReadAllText(path), sheetPoiColor, out var parsedPois, out var error))
                    {
                        Debug.LogWarning($"[PoiManager] Could not parse sheet POIs from {path}: {error}");
                        continue;
                    }

                    pois = parsedPois;
                    foreach (var poi in pois)
                    {
                        poi.EnsureId();
                    }

                    Debug.Log($"[PoiManager] Loaded {pois.Count} sheet POI(s) from {path}.");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PoiManager] Could not load sheet POIs from {path}: {ex.Message}");
                }
            }

            return false;
        }

        private IEnumerable<string> GetSheetCandidatePaths()
        {
            if (driveSync != null)
            {
                yield return Path.Combine(driveSync.LocalRootPath, sheetFileName);
            }

            yield return Path.Combine(Application.persistentDataPath, sheetFolderName, sheetFileName);
            yield return Path.Combine(Application.streamingAssetsPath, sheetFolderName, sheetFileName);
        }

        private void SeedDefaults()
        {
            pois.Add(new PointOfInterest
            {
                title = "Steenbakkerijmuseum 't Geleeg",
                category = "Route",
                description = "Kalibratiepunt voor de kaartonderlaag.",
                latitude = 51.094750f,
                longitude = 4.347785f,
                color = new Color(0.15f, 0.63f, 0.23f, 1f)
            });
            pois.Add(new PointOfInterest
            {
                title = "De Banaan",
                category = "Verhaal",
                description = "Kalibratiepunt voor de kaartonderlaag.",
                latitude = 51.107604f,
                longitude = 4.369738f,
                color = new Color(0.86f, 0.08f, 0.13f, 1f)
            });
            pois.Add(new PointOfInterest
            {
                title = "Rond punt Colonel Silvertopstraat",
                category = "Event",
                description = "Kalibratiepunt voor de kaartonderlaag.",
                latitude = 51.086803f,
                longitude = 4.360948f,
                color = new Color(1f, 0.72f, 0.08f, 1f)
            });
            pois.Add(new PointOfInterest
            {
                title = "Spoorwegbrug",
                category = "Verificatie",
                description = "Verificatiepunt voor de 3-punts kaartkalibratie.",
                latitude = 51.087620f,
                longitude = 4.355594f,
                color = new Color(0.54f, 0.24f, 0.78f, 1f)
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
