using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds and drives the Outfit panel (hotkey <c>O</c>):
/// <list type="bullet">
///   <item>Left – animated front-facing character preview rendered live with the chosen
///         cloth color via the <c>Unwritten/ClothPreviewUI</c> shader.</item>
///   <item>Right – 20-swatch color palette (5 columns × 4 rows).</item>
/// </list>
///
/// Color is applied to the in-game player via <see cref="CharacterDye"/>:
/// <list type="number">
///   <item>ABSOLUTE-hue recolor: every non-protected cloth pixel has its hue SET to the chosen
///         swatch hue (keeping its own saturation/value so shading survives). The shader does
///         this in GAMMA space, so the result hue equals the authored sRGB swatch exactly.
///         A single hue ROTATION can't do this — the outfit is several hues (green body, yellow
///         sash, teal shade), so rotating fans them across the wheel instead of unifying them.</item>
///   <item>A shader-side WHITELIST keeps the four protected colors at their ORIGINAL value
///         (matched on the pre-dye color, in gamma space) so they are never re-tinted —
///         regardless of the chosen hue. This is robust under the project's linear color
///         space, where a "shift-then-swap-back" scheme silently fails.</item>
/// </list>
///
/// Protected colors (NEVER re-tinted):
/// <list type="bullet">
///   <item>#EF914F – skin/tan</item>
///   <item>#D14B34 – mouth/red accent</item>
///   <item>#F2EAF1 – near-white highlight</item>
///   <item>#141B1B – near-black outline</item>
/// </list>
///
/// Added to the GameUICanvas GO by <see cref="UIShellBuilder"/>. Finds its panel
/// container ("CustomizePanel/Content") at runtime and populates it on the first
/// frame that both the container and <see cref="PersistentPlayer3D.Instance"/> are
/// available — the same lazy-build pattern used by <see cref="StatPanel"/>.
///
/// Persistence: the chosen hue-shift is saved to <see cref="PlayerPrefs"/> and
/// restored automatically every session.
/// </summary>
public class CharacterCustomizePanel : MonoBehaviour
{
    // ── Protected colors ────────────────────────────────────────────────────────
    // These map to #EF914F, #D14B34, #F2EAF1, #141B1B and are swapped back to their
    // originals after every hue shift so they can never be re-coloured.
    static readonly Color[] Protected = {
        new Color(0.9373f, 0.5686f, 0.3098f, 1f),   // #EF914F skin / tan
        new Color(0.8196f, 0.2941f, 0.2039f, 1f),   // #D14B34 mouth / red accent
        new Color(0.9490f, 0.9176f, 0.9451f, 1f),   // #F2EAF1 near-white highlight
        new Color(0.0784f, 0.1059f, 0.1059f, 1f),   // #141B1B near-black outline
    };
    const float ProtectTol = 0.12f;

    // ── Palette (5 cols × 4 rows) ───────────────────────────────────────────────
    static readonly Color[] Palette;
    static CharacterCustomizePanel()
    {
        string[] hex = {
            "8B1A1A", "C0392B", "E74C3C", "E67E22", "F39C12",
            "F1C40F", "2ECC71", "27AE60", "145A32", "16A085",
            "00BCD4", "3498DB", "2980B9", "1A237E", "6A1B9A",
            "9C27B0", "E91E63", "C2185B", "795548", "607D8B",
        };
        Palette = new Color[hex.Length];
        for (int i = 0; i < hex.Length; i++)
            ColorUtility.TryParseHtmlString("#" + hex[i], out Palette[i]);
    }

    const int PalCols    = 5;
    const int SwatchSize = 36;

    // ── PlayerPrefs keys ────────────────────────────────────────────────────────
    // Only the swatch INDEX is saved; the absolute hue is recomputed from the palette on load,
    // so there's no stored hue to migrate when the palette changes.
    const string PrefIdx = "unwritten.clothPaletteIdx";

    // ── Runtime state ───────────────────────────────────────────────────────────
    bool  built;
    GameObject panelRoot;   // the CustomizePanel window (toggled by PanelToggle); animate only when open
    Image previewImage;
    Material previewMat;

    Sprite[] walkFrames;
    int   frameIdx;
    float frameTimer;
    const float FPS = 6f;

    // Selected swatch decomposed into HSV — the dye applies the swatch's hue + saturation and
    // anchors its value as the new outfit midtone (ClothMidValue).
    float  currentTargetHue;
    float  currentTargetSat;
    float  currentTargetVal;
    bool   hasSelection;            // false → leave the outfit at its original art colors
    int    selectedIdx = -1;
    Button selectedSwatch;

    // Midtone (value) of the UNMODIFIED cloth's dominant color (#56864C ≈ 0.53). The remap makes
    // this brightness land exactly on the swatch's value, so the base cloth becomes the picked color
    // while darker pixels stay darker and lighter pixels stay lighter.
    const float ClothMidValue = 0.53f;

    // ── Lifecycle ───────────────────────────────────────────────────────────────
    void Start()  => TryBuild();

    void Update()
    {
        if (!built)
        {
            TryBuild();
            return;
        }
        // Keep polling for walk frames / player colour until available.
        if (walkFrames == null || walkFrames.Length == 0)
            TryGrabPlayerState();

        // This component lives on the always-active canvas, so Update runs even while the Outfit
        // window is closed. Only animate the preview when the panel is actually visible.
        if (panelRoot != null && !panelRoot.activeInHierarchy) return;

        TickFrame();
    }

    void OnDestroy()
    {
        if (previewMat != null) Destroy(previewMat);
    }

    // ── Build ───────────────────────────────────────────────────────────────────
    void TryBuild()
    {
        // Both the panel container and the player must be ready before building.
        if (PersistentPlayer3D.Instance == null) return;
        var content = FindContent();
        if (content == null) return;
        panelRoot = content.parent.gameObject;   // the "CustomizePanel" window PanelToggle shows/hides

        selectedIdx  = PlayerPrefs.GetInt(PrefIdx, -1);
        hasSelection = selectedIdx >= 0 && selectedIdx < Palette.Length;
        if (hasSelection) SwatchHSV(selectedIdx);

        // ── Preview shader ───────────────────────────────────────────────────
        var shader = Shader.Find("Unwritten/ClothPreviewUI");
        if (shader != null)
        {
            previewMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // ── Content: HorizontalLayoutGroup with two columns ──────────────────
        var hlg = content.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing            = 16;
        hlg.padding            = new RectOffset(8, 8, 8, 8);
        hlg.childAlignment     = TextAnchor.MiddleLeft;
        hlg.childControlWidth  = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = true;

        // ── Left column: character preview ───────────────────────────────────
        var leftGO  = MakeContainer("LeftCol", content);
        var leftLe  = leftGO.AddComponent<LayoutElement>();
        leftLe.preferredWidth   = 260;
        leftLe.flexibleWidth    = 0;
        leftLe.flexibleHeight   = 1;
        var leftVlg = leftGO.AddComponent<VerticalLayoutGroup>();
        leftVlg.spacing              = 8;
        leftVlg.childAlignment       = TextAnchor.MiddleCenter;
        leftVlg.childControlWidth    = leftVlg.childControlHeight = true;
        leftVlg.childForceExpandWidth  = true;
        leftVlg.childForceExpandHeight = false;

        // Sub-label.
        var lbl = MakeText("CharLabel", leftGO.transform, "Character", 14, FontStyle.Bold, font);
        var lblLe = lbl.gameObject.AddComponent<LayoutElement>();
        lblLe.preferredHeight = 20;
        lblLe.flexibleWidth   = 1;

        // Preview image.
        var prevImg = MakeImage("Preview", leftGO.transform, Color.white);
        var prvLe   = prevImg.gameObject.AddComponent<LayoutElement>();
        prvLe.preferredWidth  = 224;
        prvLe.preferredHeight = 224;
        prvLe.flexibleWidth   = 0;
        prevImg.preserveAspect = true;
        if (previewMat != null) prevImg.material = previewMat;
        previewImage = prevImg;

        // ── Thin divider ─────────────────────────────────────────────────────
        var divGO = MakeContainer("Divider", content);
        var divLe = divGO.AddComponent<LayoutElement>();
        divLe.preferredWidth = 1; divLe.flexibleWidth = 0;
        var divImg = divGO.AddComponent<Image>();
        divImg.color = new Color(1, 1, 1, 0.18f);

        // ── Right column: colour palette ─────────────────────────────────────
        var rightGO  = MakeContainer("RightCol", content);
        var rightLe  = rightGO.AddComponent<LayoutElement>();
        rightLe.flexibleWidth   = 1;
        rightLe.flexibleHeight  = 1;
        var rightVlg = rightGO.AddComponent<VerticalLayoutGroup>();
        rightVlg.spacing               = 10;
        rightVlg.padding               = new RectOffset(8, 8, 4, 4);
        rightVlg.childAlignment        = TextAnchor.UpperCenter;
        rightVlg.childControlWidth     = rightVlg.childControlHeight = true;
        rightVlg.childForceExpandWidth   = true;
        rightVlg.childForceExpandHeight  = false;

        // Sub-label.
        var clbl = MakeText("ColLabel", rightGO.transform, "Cloth Color", 14, FontStyle.Bold, font);
        var clblLe = clbl.gameObject.AddComponent<LayoutElement>();
        clblLe.preferredHeight = 20;
        clblLe.flexibleWidth   = 1;

        // Swatch grid.
        int rows   = Mathf.CeilToInt((float)Palette.Length / PalCols);
        int gridH  = rows * SwatchSize + (rows - 1) * 4;

        var gridGO = MakeContainer("SwatchGrid", rightGO.transform);
        var gridLe = gridGO.AddComponent<LayoutElement>();
        gridLe.preferredHeight = gridH;
        gridLe.flexibleWidth   = 1;

        var glg = gridGO.AddComponent<GridLayoutGroup>();
        glg.cellSize        = new Vector2(SwatchSize, SwatchSize);
        glg.spacing         = new Vector2(4, 4);
        glg.padding         = new RectOffset(0, 0, 0, 0);
        glg.startCorner     = GridLayoutGroup.Corner.UpperLeft;
        glg.startAxis       = GridLayoutGroup.Axis.Horizontal;
        glg.childAlignment  = TextAnchor.UpperCenter;
        glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = PalCols;

        for (int i = 0; i < Palette.Length; i++)
            BuildSwatch(gridGO.transform, i, Palette[i], i == selectedIdx);

        // ── First apply ──────────────────────────────────────────────────────
        built = true;
        TryGrabPlayerState();
    }

    void BuildSwatch(Transform parent, int idx, Color color, bool selected)
    {
        // Background image.
        var go  = new GameObject("Swatch", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = true;

        // Border: white when selected, dim otherwise.
        var outline = go.AddComponent<Outline>();
        SetOutline(outline, selected);

        // Button.
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var bc = btn.colors;
        bc.normalColor      = Color.white;
        bc.highlightedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        bc.pressedColor     = new Color(0.72f, 0.72f, 0.72f, 1f);
        bc.selectedColor    = Color.white;
        btn.colors = bc;

        int capturedIdx = idx;
        btn.onClick.AddListener(() => OnSwatchClicked(capturedIdx, btn));

        if (selected) selectedSwatch = btn;
    }

    // ── Input ───────────────────────────────────────────────────────────────────
    void OnSwatchClicked(int idx, Button btn)
    {
        // Update selection highlight.
        if (selectedSwatch != null)
        {
            var ol = selectedSwatch.GetComponent<Outline>();
            if (ol != null) SetOutline(ol, false);
        }
        selectedSwatch = btn;
        var newOl = btn.GetComponent<Outline>();
        if (newOl != null) SetOutline(newOl, true);

        // Recolor to this swatch: cloth becomes the picked color (no base-hue guess, no rotation).
        SwatchHSV(idx);
        selectedIdx  = idx;
        hasSelection = true;

        PlayerPrefs.SetInt(PrefIdx, selectedIdx);
        PlayerPrefs.Save();

        UISounds.PlayClick();
        PushColor();
    }

    // ── Color application ────────────────────────────────────────────────────────
    /// <summary>Push the current hue shift to both the preview material and the in-game player.</summary>
    void PushColor()
    {
        PushToPreview();
        PushToPlayer();
    }

    void PushToPreview()
    {
        if (previewMat == null) return;

        previewMat.SetFloat("_TargetHue",    currentTargetHue);
        previewMat.SetFloat("_TargetSat",    currentTargetSat);
        previewMat.SetFloat("_TargetVal",    currentTargetVal);
        previewMat.SetFloat("_BaseValue",    ClothMidValue);
        previewMat.SetFloat("_UseTargetHue", hasSelection ? 1f : 0f);
        previewMat.SetFloat("_Saturation",   1f);
        previewMat.SetFloat("_Brightness",   1f);

        // Whitelist: enable all 4 protect slots so these art colors are never re-tinted.
        previewMat.SetVector("_ProtectEnabled", new Vector4(1f, 1f, 1f, 1f));
        previewMat.SetVector("_ProtectTol",
            new Vector4(ProtectTol, ProtectTol, ProtectTol, ProtectTol));
        for (int i = 0; i < Protected.Length; i++)
            previewMat.SetVector($"_ProtectColor{i}", (Vector4)Protected[i]);
    }

    void PushToPlayer()
    {
        if (PersistentPlayer3D.Instance == null) return;
        var dye = PersistentPlayer3D.Instance.GetComponent<CharacterDye>();
        if (dye == null) return;

        dye.targetHue    = currentTargetHue;
        dye.targetSat    = currentTargetSat;
        dye.targetVal    = currentTargetVal;
        dye.baseValue    = ClothMidValue;
        dye.useTargetHue = hasSelection;
        // The shader keeps these ORIGINAL colors no matter the chosen hue (skin/outline/etc.).
        dye.SetProtectedColors(Protected, ProtectTol);   // calls Apply()
    }

    void TryGrabPlayerState()
    {
        walkFrames = GetWalkFrames();
        if (walkFrames != null && walkFrames.Length > 0 && previewImage != null)
            previewImage.sprite = walkFrames[frameIdx % walkFrames.Length];
        PushColor();
    }

    // ── Animation ────────────────────────────────────────────────────────────────
    void TickFrame()
    {
        if (walkFrames == null || walkFrames.Length == 0 || previewImage == null) return;
        frameTimer += Time.deltaTime;
        float ft = 1f / FPS;
        while (frameTimer >= ft)
        {
            frameTimer -= ft;
            frameIdx = (frameIdx + 1) % walkFrames.Length;
        }
        previewImage.sprite = walkFrames[frameIdx];
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────
    Sprite[] GetWalkFrames()
    {
        if (PersistentPlayer3D.Instance == null) return null;
        var ctrl = PersistentPlayer3D.Instance.GetComponent<Player3DController>();
        if (ctrl == null) return null;

        // Animated sheet (richer): row 0 = down direction; cols 4-7 = walk frames.
        if (ctrl.animSheet != null && ctrl.animCell > 0)
            return SpriteSheetCache.Grid(ctrl.animSheet, ctrl.animCell, 0, 4, 4, ctrl.animPpu);

        // Legacy sprite array fallback.
        return (ctrl.down != null && ctrl.down.Length > 0) ? ctrl.down : null;
    }

    // Decompose a palette swatch into HSV (its authored sRGB value) — the exact H/S/V the gamma-space
    // shader lands on, so the recolored cloth matches the swatch.
    void SwatchHSV(int idx)
    {
        Color.RGBToHSV(Palette[idx], out currentTargetHue, out currentTargetSat, out currentTargetVal);
    }

    static void SetOutline(Outline ol, bool selected)
    {
        ol.effectColor    = selected ? Color.white : new Color(0, 0, 0, 0.5f);
        ol.effectDistance = selected ? new Vector2(2, -2) : new Vector2(1, -1);
    }

    RectTransform FindContent()
    {
        foreach (var t in GetComponentsInChildren<Transform>(includeInactive: true))
            if (t.name == "CustomizePanel")
                return t.Find("Content") as RectTransform;
        return null;
    }

    // ── UI factory helpers ───────────────────────────────────────────────────────
    static GameObject MakeContainer(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static GameObject MakeContainer(string name, RectTransform parent)
        => MakeContainer(name, (Transform)parent);

    static Image MakeImage(string name, Transform parent, Color color)
    {
        var go  = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color         = color;
        img.raycastTarget = false;
        return img;
    }

    static Text MakeText(string name, Transform parent, string content, int size, FontStyle style, Font font)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font            = font;
        t.fontSize        = size;
        t.fontStyle       = style;
        t.alignment       = TextAnchor.MiddleCenter;
        t.color           = Color.white;
        t.text            = content;
        t.raycastTarget   = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow   = VerticalWrapMode.Overflow;
        var sh = go.AddComponent<Shadow>();
        sh.effectColor    = new Color(0, 0, 0, 0.6f);
        sh.effectDistance = new Vector2(1, -1);
        return t;
    }
}
