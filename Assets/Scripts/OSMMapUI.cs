using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// OSMMapUI - In-game UI for controlling the OSM Map Manager.
/// 
/// Requires:
///   - OSMMapManager in the scene
///   - TextMeshPro (for labels)
///   - Unity UI (Canvas, Buttons, Sliders, InputFields)
/// 
/// Wire up the serialized fields in the Inspector.
/// </summary>
public class OSMMapUI : MonoBehaviour
{
    [Header("Map Manager Reference")]
    public OSMMapManager mapManager;

    [Header("Location Input")]
    public TMP_InputField latitudeInput;
    public TMP_InputField longitudeInput;
    public Button goToLocationButton;

    [Header("Zoom Controls")]
    public Slider zoomSlider;
    public TMP_Text zoomLabel;
    public Button zoomInButton;
    public Button zoomOutButton;

    [Header("Tile Style / Source")]
    public TMP_Dropdown tileSourceDropdown;
    public OSMTileSources tileSourcesAsset;  // Assign the ScriptableObject here

    [Header("Tile Material")]
    public Button applyMaterialButton;
    public Material[] availableMaterials;   // Drag in your custom materials
    public TMP_Dropdown materialDropdown;

    [Header("Preset Locations")]
    public Button[] presetButtons;
    [Tooltip("One entry per presetButton: name, lat, lon")]
    public LocationPreset[] presets;

    [Header("Status")]
    public TMP_Text statusLabel;
    public GameObject loadingIndicator;

    [System.Serializable]
    public struct LocationPreset
    {
        public string displayName;
        public double latitude;
        public double longitude;
        public int zoom;
    }

    // -------------------------------------------------------------------------

    private void Start()
    {
        if (mapManager == null)
        {
            mapManager = FindFirstObjectByType<OSMMapManager>();
            if (mapManager == null)
            {
                Debug.LogError("[OSMMapUI] No OSMMapManager found in scene!");
                return;
            }
        }

        // Initialise controls to match current map state
        if (latitudeInput)  latitudeInput.text  = mapManager.latitude.ToString("F6");
        if (longitudeInput) longitudeInput.text = mapManager.longitude.ToString("F6");

        if (zoomSlider)
        {
            zoomSlider.minValue = 1;
            zoomSlider.maxValue = 19;
            zoomSlider.wholeNumbers = true;
            zoomSlider.value = mapManager.zoomLevel;
            zoomSlider.onValueChanged.AddListener(OnZoomSliderChanged);
        }

        UpdateZoomLabel();

        // Wire buttons
        if (goToLocationButton) goToLocationButton.onClick.AddListener(OnGoToLocation);
        if (zoomInButton)       zoomInButton.onClick.AddListener(OnZoomIn);
        if (zoomOutButton)      zoomOutButton.onClick.AddListener(OnZoomOut);
        if (applyMaterialButton) applyMaterialButton.onClick.AddListener(OnApplyMaterial);

        // Tile source dropdown
        if (tileSourceDropdown && tileSourcesAsset != null)
        {
            tileSourceDropdown.ClearOptions();
            tileSourceDropdown.AddOptions(new System.Collections.Generic.List<string>(tileSourcesAsset.GetNames()));
            tileSourceDropdown.onValueChanged.AddListener(OnTileSourceChanged);
        }

        // Material dropdown
        if (materialDropdown && availableMaterials != null && availableMaterials.Length > 0)
        {
            var opts = new System.Collections.Generic.List<string>();
            foreach (var mat in availableMaterials) opts.Add(mat ? mat.name : "(null)");
            materialDropdown.ClearOptions();
            materialDropdown.AddOptions(opts);
        }

        // Preset location buttons
        if (presetButtons != null && presets != null)
        {
            for (int i = 0; i < presetButtons.Length && i < presets.Length; i++)
            {
                int index = i; // capture for lambda
                if (presetButtons[i])
                {
                    // Label the button
                    var label = presetButtons[i].GetComponentInChildren<TMP_Text>();
                    if (label) label.text = presets[i].displayName;

                    presetButtons[i].onClick.AddListener(() => OnPresetLocation(index));
                }
            }
        }

        // Map loaded event
        mapManager.onMapLoaded.AddListener(OnMapLoaded);

        SetStatus("Ready");
        if (loadingIndicator) loadingIndicator.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Button / Slider Handlers
    // -------------------------------------------------------------------------

    private void OnGoToLocation()
    {
        if (double.TryParse(latitudeInput?.text, out double lat) &&
            double.TryParse(longitudeInput?.text, out double lon))
        {
            SetStatus("Loading...");
            if (loadingIndicator) loadingIndicator.SetActive(true);
            mapManager.SetLocation(lat, lon);
        }
        else
        {
            SetStatus("Invalid coordinates. Use decimal degrees (e.g. 48.8566, 2.3522)");
        }
    }

    private void OnZoomSliderChanged(float value)
    {
        mapManager.SetZoom((int)value);
        UpdateZoomLabel();
        SetStatus("Loading...");
        if (loadingIndicator) loadingIndicator.SetActive(true);
    }

    private void OnZoomIn()
    {
        int newZoom = Mathf.Clamp(mapManager.zoomLevel + 1, 1, 19);
        if (zoomSlider) zoomSlider.value = newZoom;
        else
        {
            mapManager.SetZoom(newZoom);
            UpdateZoomLabel();
        }
    }

    private void OnZoomOut()
    {
        int newZoom = Mathf.Clamp(mapManager.zoomLevel - 1, 1, 19);
        if (zoomSlider) zoomSlider.value = newZoom;
        else
        {
            mapManager.SetZoom(newZoom);
            UpdateZoomLabel();
        }
    }

    private void OnTileSourceChanged(int index)
    {
        if (tileSourcesAsset == null || index >= tileSourcesAsset.sources.Count) return;
        var source = tileSourcesAsset.sources[index];
        mapManager.SetTileSource(source.urlTemplate.Replace("{apikey}", source.apiKey ?? ""));
        SetStatus($"Style: {source.name} — Loading...");
        if (loadingIndicator) loadingIndicator.SetActive(true);
    }

    private void OnApplyMaterial()
    {
        if (availableMaterials == null || materialDropdown == null) return;
        int idx = materialDropdown.value;
        if (idx < availableMaterials.Length && availableMaterials[idx] != null)
        {
            mapManager.ApplyMaterialToAllTiles(availableMaterials[idx]);
            SetStatus($"Material applied: {availableMaterials[idx].name}");
        }
    }

    private void OnPresetLocation(int index)
    {
        var preset = presets[index];
        if (latitudeInput)  latitudeInput.text  = preset.latitude.ToString("F6");
        if (longitudeInput) longitudeInput.text = preset.longitude.ToString("F6");
        if (zoomSlider) zoomSlider.value = preset.zoom;

        mapManager.zoomLevel = preset.zoom;
        mapManager.SetLocation(preset.latitude, preset.longitude);
        SetStatus($"Going to {preset.displayName}...");
        if (loadingIndicator) loadingIndicator.SetActive(true);
    }

    private void OnMapLoaded()
    {
        SetStatus("Map loaded.");
        if (loadingIndicator) loadingIndicator.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void UpdateZoomLabel()
    {
        if (zoomLabel)
            zoomLabel.text = $"Zoom: {mapManager.zoomLevel}";
    }

    private void SetStatus(string message)
    {
        if (statusLabel)
            statusLabel.text = message;
    }
}