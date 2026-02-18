using UnityEngine;

[RequireComponent(typeof(OSMMapManager))]
public class OSMHandDrawnController : MonoBehaviour
{
    [Header("Shader Asset")]
    public Shader handDrawnShader;

    [Header("Paper & Ink")]
    public Color paperColor    = new Color(0.96f, 0.93f, 0.85f, 1f);
    public Color inkColor      = new Color(0.12f, 0.08f, 0.04f, 1f);
    [Range(0f, 5f)]   public float edgeStrength   = 1.8f;
    [Range(0f, 1f)]   public float edgeThreshold  = 0.15f;

    [Header("Color Style")]
    [Range(0f, 2f)]   public float saturation  = 0.45f;
    [Range(0.5f, 2f)] public float brightness  = 1.05f;
    [Range(0f, 1f)]   public float warmTint    = 0.18f;
    [Range(0f, 1f)]   public float paperBlend  = 0.12f;

    [Header("Pencil Texture")]
    [Range(10f, 500f)] public float pencilScale    = 180f;
    [Range(0f, 1f)]    public float pencilStrength = 0.28f;
    [Range(0f, 1f)]    public float hatchStrength  = 0.12f;
    [Range(10f, 200f)] public float hatchScale     = 60f;

    [Header("Wobbly Lines")]
    [Range(0f, 0.02f)] public float wobbleStrength = 0.004f;
    [Range(1f, 50f)]   public float wobbleScale    = 12f;

    [Header("Presets")]
    public HandDrawnPreset activePreset = HandDrawnPreset.Pencil;

    public enum HandDrawnPreset { Pencil, InkWash, Watercolor, Blueprint, Sepia }

    private OSMMapManager _map;
    private Material _sharedMaterial;

    private static readonly int PropPaperColor    = Shader.PropertyToID("_PaperColor");
    private static readonly int PropInkColor      = Shader.PropertyToID("_InkColor");
    private static readonly int PropEdgeStrength  = Shader.PropertyToID("_EdgeStrength");
    private static readonly int PropEdgeThreshold = Shader.PropertyToID("_EdgeThreshold");
    private static readonly int PropSaturation    = Shader.PropertyToID("_Saturation");
    private static readonly int PropBrightness    = Shader.PropertyToID("_Brightness");
    private static readonly int PropWarmTint      = Shader.PropertyToID("_WarmTint");
    private static readonly int PropPaperBlend    = Shader.PropertyToID("_PaperBlend");
    private static readonly int PropPencilScale   = Shader.PropertyToID("_PencilScale");
    private static readonly int PropPencilStr     = Shader.PropertyToID("_PencilStrength");
    private static readonly int PropHatchStr      = Shader.PropertyToID("_HatchStrength");
    private static readonly int PropHatchScale    = Shader.PropertyToID("_HatchScale");
    private static readonly int PropWobbleStr     = Shader.PropertyToID("_WobbleStrength");
    private static readonly int PropWobbleScale   = Shader.PropertyToID("_WobbleScale");

    private void Awake()
    {
        _map = GetComponent<OSMMapManager>();

        if (handDrawnShader == null)
            handDrawnShader = Shader.Find("OSM/HandDrawn");

        if (handDrawnShader == null)
        {
            Debug.LogError("[OSMHandDrawnController] Could not find 'OSM/HandDrawn' shader.");
            return;
        }

        _sharedMaterial = new Material(handDrawnShader) { name = "OSM_HandDrawn_Material" };
        _map.tileMaterial = _sharedMaterial;
        _map.ApplyMaterialToAllTiles(_sharedMaterial);

        ApplyPreset(activePreset);
        _map.onMapLoaded.AddListener(PushAllProperties);
    }

    private void OnValidate()
    {
        if (_sharedMaterial != null) PushAllProperties();
    }
    
    private void Update()
    {
        PushAllProperties();

        // Also push to every live tile's individual material instance
        foreach (var tile in _map.GetActiveTileMaterials())
        {
            CopyPropertiesToMaterial(tile);
        }
    }

    public void PushAllProperties()
    {
        if (_sharedMaterial == null) return;
        _sharedMaterial.SetColor(PropPaperColor,    paperColor);
        _sharedMaterial.SetColor(PropInkColor,      inkColor);
        _sharedMaterial.SetFloat(PropEdgeStrength,  edgeStrength);
        _sharedMaterial.SetFloat(PropEdgeThreshold, edgeThreshold);
        _sharedMaterial.SetFloat(PropSaturation,    saturation);
        _sharedMaterial.SetFloat(PropBrightness,    brightness);
        _sharedMaterial.SetFloat(PropWarmTint,      warmTint);
        _sharedMaterial.SetFloat(PropPaperBlend,    paperBlend);
        _sharedMaterial.SetFloat(PropPencilScale,   pencilScale);
        _sharedMaterial.SetFloat(PropPencilStr,     pencilStrength);
        _sharedMaterial.SetFloat(PropHatchStr,      hatchStrength);
        _sharedMaterial.SetFloat(PropHatchScale,    hatchScale);
        _sharedMaterial.SetFloat(PropWobbleStr,     wobbleStrength);
        _sharedMaterial.SetFloat(PropWobbleScale,   wobbleScale);
    }

    public void ApplyPreset(HandDrawnPreset preset)
    {
        activePreset = preset;
        switch (preset)
        {
            case HandDrawnPreset.Pencil:
                paperColor = new Color(0.96f, 0.93f, 0.85f); inkColor = new Color(0.12f, 0.08f, 0.04f);
                saturation = 0.35f; brightness = 1.05f; warmTint = 0.15f; paperBlend = 0.10f;
                edgeStrength = 2.0f; edgeThreshold = 0.12f;
                pencilScale = 200f; pencilStrength = 0.35f; hatchStrength = 0.18f; hatchScale = 70f;
                wobbleStrength = 0.003f; wobbleScale = 10f;
                break;
            case HandDrawnPreset.InkWash:
                paperColor = new Color(0.98f, 0.97f, 0.92f); inkColor = new Color(0.05f, 0.05f, 0.10f);
                saturation = 0.20f; brightness = 1.1f; warmTint = 0.05f; paperBlend = 0.08f;
                edgeStrength = 3.5f; edgeThreshold = 0.10f;
                pencilScale = 150f; pencilStrength = 0.15f; hatchStrength = 0.05f; hatchScale = 50f;
                wobbleStrength = 0.006f; wobbleScale = 14f;
                break;
            case HandDrawnPreset.Watercolor:
                paperColor = new Color(0.95f, 0.95f, 0.98f); inkColor = new Color(0.20f, 0.15f, 0.35f);
                saturation = 0.75f; brightness = 1.15f; warmTint = 0.05f; paperBlend = 0.20f;
                edgeStrength = 1.0f; edgeThreshold = 0.20f;
                pencilScale = 120f; pencilStrength = 0.10f; hatchStrength = 0.03f; hatchScale = 40f;
                wobbleStrength = 0.008f; wobbleScale = 8f;
                break;
            case HandDrawnPreset.Blueprint:
                paperColor = new Color(0.12f, 0.22f, 0.55f); inkColor = new Color(0.85f, 0.92f, 1.00f);
                saturation = 0.0f; brightness = 1.0f; warmTint = 0.0f; paperBlend = 0.30f;
                edgeStrength = 2.5f; edgeThreshold = 0.08f;
                pencilScale = 300f; pencilStrength = 0.20f; hatchStrength = 0.25f; hatchScale = 90f;
                wobbleStrength = 0.002f; wobbleScale = 20f;
                break;
            case HandDrawnPreset.Sepia:
                paperColor = new Color(0.88f, 0.78f, 0.60f); inkColor = new Color(0.28f, 0.15f, 0.05f);
                saturation = 0.10f; brightness = 0.95f; warmTint = 0.60f; paperBlend = 0.25f;
                edgeStrength = 1.5f; edgeThreshold = 0.18f;
                pencilScale = 160f; pencilStrength = 0.30f; hatchStrength = 0.20f; hatchScale = 55f;
                wobbleStrength = 0.005f; wobbleScale = 11f;
                break;
        }
        PushAllProperties();
    }

    private void CopyPropertiesToMaterial(Material mat)
    {
        if (mat == null) return;
        mat.SetColor(PropPaperColor,    paperColor);
        mat.SetColor(PropInkColor,      inkColor);
        mat.SetFloat(PropEdgeStrength,  edgeStrength);
        mat.SetFloat(PropEdgeThreshold, edgeThreshold);
        mat.SetFloat(PropSaturation,    saturation);
        mat.SetFloat(PropBrightness,    brightness);
        mat.SetFloat(PropWarmTint,      warmTint);
        mat.SetFloat(PropPaperBlend,    paperBlend);
        mat.SetFloat(PropPencilScale,   pencilScale);
        mat.SetFloat(PropPencilStr,     pencilStrength);
        mat.SetFloat(PropHatchStr,      hatchStrength);
        mat.SetFloat(PropHatchScale,    hatchScale);
        mat.SetFloat(PropWobbleStr,     wobbleStrength);
        mat.SetFloat(PropWobbleScale,   wobbleScale);
    }

    public void SetPresetPencil()     => ApplyPreset(HandDrawnPreset.Pencil);
    public void SetPresetInkWash()    => ApplyPreset(HandDrawnPreset.InkWash);
    public void SetPresetWatercolor() => ApplyPreset(HandDrawnPreset.Watercolor);
    public void SetPresetBlueprint()  => ApplyPreset(HandDrawnPreset.Blueprint);
    public void SetPresetSepia()      => ApplyPreset(HandDrawnPreset.Sepia);

    private void OnDestroy()
    {
        if (_sharedMaterial != null) Destroy(_sharedMaterial);
    }
}