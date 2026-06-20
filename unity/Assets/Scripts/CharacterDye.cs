using System;
using System.Collections;
using UnityEngine;

[Serializable]
public class ColorSwapSlot
{
    public bool  enabled   = false;
    public Color source    = Color.white;
    public Color target    = Color.red;
    [Range(0.01f, 0.5f)] public float tolerance = 0.10f;
}

/// <summary>
/// Per-renderer HSV dye + palette swap for billboard sprites using <c>Unwritten/BillboardLit</c>.
/// Drives all shader knobs via <see cref="MaterialPropertyBlock"/> — zero material instances, zero GC.
/// HSV is applied first; palette swaps act on the HSV-shifted result, so the eyedropper samples the
/// color the user actually sees on screen.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
[DisallowMultipleComponent]
public class CharacterDye : MonoBehaviour
{
    // ── HSV ──────────────────────────────────────────────────────────────────
    [Range(0f, 1f)] public float hueShift   = 0f;
    [Range(0f, 2f)] public float saturation = 1f;
    [Range(0f, 2f)] public float brightness = 1f;

    // ── Swatch recolor (cloth dye) ────────────────────────────────────────────
    // When useTargetHue is true, the shader IGNORES hueShift and recolors every non-protected pixel
    // to the picked swatch: it applies the swatch's hue + saturation and remaps the sprite's own
    // brightness so the outfit midtone (baseValue) lands on the swatch's value — so the base cloth
    // BECOMES the swatch, shadows stay darker and highlights lighter. Worked out in gamma space so
    // the result matches the authored sRGB swatch. This is what the Outfit picker drives; a single
    // hue rotation can't unify a multi-hue outfit. Default off → legacy hueShift behaviour.
    [Range(0f, 1f)]    public float targetHue    = 0f;
    [Range(0f, 1f)]    public float targetSat    = 1f;
    [Range(0f, 1f)]    public float targetVal    = 0.5f;
    [Range(0.01f, 1f)] public float baseValue    = 0.53f;   // outfit midtone anchor (NinjaGreen body)
                       public bool  useTargetHue = false;

    // ── Palette Swaps ─────────────────────────────────────────────────────────
    public const int SwapCount = 4;
    public ColorSwapSlot[] swaps = new ColorSwapSlot[SwapCount]
    {
        new ColorSwapSlot(), new ColorSwapSlot(),
        new ColorSwapSlot(), new ColorSwapSlot(),
    };

    // ── Whitelist protect ─────────────────────────────────────────────────────
    // Up to 4 art colors the HSV dye must NEVER recolor (skin, outline, etc.). The shader
    // matches the ORIGINAL (pre-dye) color against these in gamma space and keeps it as-is.
    // Independent of the swaps above (which the DevSettings eyedropper drives).
    public const int ProtectCount = 4;
    [Tooltip("Authored (sRGB) colors that the dye leaves untouched. Pushed raw — the shader compares in gamma.")]
    public Color[] protectColors  = new Color[ProtectCount];
    public bool[]  protectEnabled = new bool[ProtectCount];
    [Range(0.01f, 0.4f)] public float protectTolerance = 0.12f;

    // ── Cached shader property IDs ───────────────────────────────────────────
    static readonly int PropHue  = Shader.PropertyToID("_HueShift");
    static readonly int PropSat  = Shader.PropertyToID("_Saturation");
    static readonly int PropBri  = Shader.PropertyToID("_Brightness");
    static readonly int PropTgtHue = Shader.PropertyToID("_TargetHue");
    static readonly int PropTgtSat = Shader.PropertyToID("_TargetSat");
    static readonly int PropTgtVal = Shader.PropertyToID("_TargetVal");
    static readonly int PropBaseV  = Shader.PropertyToID("_BaseValue");
    static readonly int PropUseTgt = Shader.PropertyToID("_UseTargetHue");

    static readonly int PropSrc0 = Shader.PropertyToID("_SwapSrc0");
    static readonly int PropSrc1 = Shader.PropertyToID("_SwapSrc1");
    static readonly int PropSrc2 = Shader.PropertyToID("_SwapSrc2");
    static readonly int PropSrc3 = Shader.PropertyToID("_SwapSrc3");
    static readonly int PropDst0 = Shader.PropertyToID("_SwapDst0");
    static readonly int PropDst1 = Shader.PropertyToID("_SwapDst1");
    static readonly int PropDst2 = Shader.PropertyToID("_SwapDst2");
    static readonly int PropDst3 = Shader.PropertyToID("_SwapDst3");
    static readonly int PropSwapEnabled    = Shader.PropertyToID("_SwapEnabled");
    static readonly int PropSwapTolerances = Shader.PropertyToID("_SwapTolerances");

    static readonly int PropProt0 = Shader.PropertyToID("_ProtectColor0");
    static readonly int PropProt1 = Shader.PropertyToID("_ProtectColor1");
    static readonly int PropProt2 = Shader.PropertyToID("_ProtectColor2");
    static readonly int PropProt3 = Shader.PropertyToID("_ProtectColor3");
    static readonly int PropProtEnabled = Shader.PropertyToID("_ProtectEnabled");
    static readonly int PropProtTol     = Shader.PropertyToID("_ProtectTol");

    SpriteRenderer        sr;
    MaterialPropertyBlock mpb;

    // ── Eyedropper state ─────────────────────────────────────────────────────
    int  _eyeSlot   = -1;
    int  _eyeDelay  = 0;   // skip frames so the button click isn't mistaken for the pick
    bool _eyeActive = false;

    public bool EyedropperActive => _eyeActive;
    public int  EyedropperSlot   => _eyeSlot;

    // ── Unity lifecycle ───────────────────────────────────────────────────────
    void Awake()
    {
        sr  = GetComponent<SpriteRenderer>();
        mpb = new MaterialPropertyBlock();
        Apply();
    }

    void Update()
    {
        if (!_eyeActive) return;
        if (_eyeDelay > 0) { _eyeDelay--; return; }
        if (Input.GetMouseButtonDown(0))
        {
            StartCoroutine(SampleScreen(Input.mousePosition, _eyeSlot));
            _eyeActive = false;
        }
        else if (Input.GetKeyDown(KeyCode.Escape))
        {
            _eyeActive = false;
        }
    }

    // Samples one screen pixel at end-of-frame (after rendering) and writes it as the swap source.
    IEnumerator SampleScreen(Vector2 mousePos, int slot)
    {
        yield return new WaitForEndOfFrame();
        var tex = new Texture2D(1, 1, TextureFormat.RGB24, false);
        // ReadPixels origin is bottom-left; mouse origin is top-left — flip Y.
        tex.ReadPixels(new Rect(Mathf.Round(mousePos.x), Mathf.Round(Screen.height - mousePos.y), 1, 1), 0, 0);
        tex.Apply();
        swaps[slot].source = tex.GetPixel(0, 0);
        Destroy(tex);
        Apply();
    }

    // ── Eyedropper public API (called from DevSettings) ───────────────────────
    public void StartEyedropper(int slot)
    {
        _eyeSlot   = slot;
        _eyeDelay  = 2;
        _eyeActive = true;
    }
    public void CancelEyedropper() => _eyeActive = false;

    // ── Apply: push everything to the renderer's MaterialPropertyBlock ────────
    public void Apply()
    {
        if (sr == null || mpb == null) return;

        // ── Color-palette dye DISABLED for now ────────────────────────────────────────────────
        // Force identity so the character renders its TRUE art colors, ignoring any swatch left
        // serialized on this component (e.g. an old cyan "Outfit" pick on the persistent player).
        // Remove this block to re-enable the palette / Outfit recolor.
        hueShift = 0f; saturation = 1f; brightness = 1f;
        useTargetHue = false;
        if (swaps != null) for (int i = 0; i < swaps.Length; i++) if (swaps[i] != null) swaps[i].enabled = false;
        if (protectEnabled != null) for (int i = 0; i < protectEnabled.Length; i++) protectEnabled[i] = false;
        // ──────────────────────────────────────────────────────────────────────────────────────

        sr.GetPropertyBlock(mpb);

        mpb.SetFloat(PropHue, hueShift);
        mpb.SetFloat(PropSat, saturation);
        mpb.SetFloat(PropBri, brightness);
        mpb.SetFloat(PropTgtHue, targetHue);
        mpb.SetFloat(PropTgtSat, targetSat);
        mpb.SetFloat(PropTgtVal, targetVal);
        mpb.SetFloat(PropBaseV,  baseValue);
        mpb.SetFloat(PropUseTgt, useTargetHue ? 1f : 0f);

        mpb.SetVector(PropSrc0, (Vector4)swaps[0].source);
        mpb.SetVector(PropSrc1, (Vector4)swaps[1].source);
        mpb.SetVector(PropSrc2, (Vector4)swaps[2].source);
        mpb.SetVector(PropSrc3, (Vector4)swaps[3].source);
        mpb.SetVector(PropDst0, (Vector4)swaps[0].target);
        mpb.SetVector(PropDst1, (Vector4)swaps[1].target);
        mpb.SetVector(PropDst2, (Vector4)swaps[2].target);
        mpb.SetVector(PropDst3, (Vector4)swaps[3].target);

        mpb.SetVector(PropSwapEnabled, new Vector4(
            swaps[0].enabled ? 1f : 0f, swaps[1].enabled ? 1f : 0f,
            swaps[2].enabled ? 1f : 0f, swaps[3].enabled ? 1f : 0f));

        mpb.SetVector(PropSwapTolerances, new Vector4(
            swaps[0].tolerance, swaps[1].tolerance,
            swaps[2].tolerance, swaps[3].tolerance));

        // Whitelist protect (pushed raw / gamma — the shader compares in gamma space).
        Color Prot(int i) => (protectColors  != null && i < protectColors.Length)  ? protectColors[i]  : Color.clear;
        float En(int i)   => (protectEnabled != null && i < protectEnabled.Length && protectEnabled[i]) ? 1f : 0f;
        mpb.SetVector(PropProt0, (Vector4)Prot(0));
        mpb.SetVector(PropProt1, (Vector4)Prot(1));
        mpb.SetVector(PropProt2, (Vector4)Prot(2));
        mpb.SetVector(PropProt3, (Vector4)Prot(3));
        mpb.SetVector(PropProtEnabled, new Vector4(En(0), En(1), En(2), En(3)));
        mpb.SetVector(PropProtTol, new Vector4(
            protectTolerance, protectTolerance, protectTolerance, protectTolerance));

        sr.SetPropertyBlock(mpb);
    }

    /// <summary>Set the whitelist of colors the dye must never recolor (enables one slot per color)
    /// and re-apply. Pass the authored sRGB colors; the shader compares in gamma space.</summary>
    public void SetProtectedColors(Color[] colors, float tolerance)
    {
        if (protectColors  == null || protectColors.Length  != ProtectCount) protectColors  = new Color[ProtectCount];
        if (protectEnabled == null || protectEnabled.Length != ProtectCount) protectEnabled = new bool[ProtectCount];
        for (int i = 0; i < ProtectCount; i++)
        {
            bool on = colors != null && i < colors.Length;
            protectEnabled[i] = on;
            protectColors[i]  = on ? colors[i] : Color.clear;
        }
        protectTolerance = tolerance;
        Apply();
    }

    // ── HSV properties for DevSettings sliders ────────────────────────────────
    public float HueShift   { get => hueShift;   set { hueShift   = value; Apply(); } }
    public float Saturation { get => saturation; set { saturation = value; Apply(); } }
    public float Brightness { get => brightness; set { brightness = value; Apply(); } }

#if UNITY_EDITOR
    void OnValidate() => Apply();
#endif
}
