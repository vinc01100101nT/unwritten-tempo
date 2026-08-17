using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// In-game wardrobe (hotkey <c>O</c>): recolor the live-3D character's clothing PER PART, at runtime.
/// Left column lists the character's dyeable parts — auto-detected from its atlas the first time the panel
/// opens (skin and eyes are filtered out, so they're never offered). Right column is a color palette.
/// Click a part, then click a color: the real in-world character recolors instantly via
/// <see cref="CharacterDye3D"/>, and the choice is saved with the character.
///
/// Replaces the retired 2D ninja customize panel; it populates the same "CustomizePanel/Content" container
/// that <c>UIShellBuilder</c> drops onto the GameUICanvas.
/// </summary>
public class CharacterWardrobePanel : MonoBehaviour
{
    // 20-color palette (5 × 4) — the colors offered for any part.
    static readonly Color[] Palette;
    static CharacterWardrobePanel()
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
    const int SwatchSize = 34;

    bool built;
    GameObject panelRoot;
    CharacterDye3D dye;
    CharacterDye3D builtForDye;   // which character the current part swatches belong to (rebuild on change)

    Transform partsGrid;
    readonly List<Color> parts        = new List<Color>();
    readonly List<Image> partSwatches = new List<Image>();
    int selectedPart = -1;

    // Free color picker (HSV): saturation/value square + hue bar, plus the hex readout in the Clothing column.
    TMPro.TMP_Text hexLabel;
    Image          hexSwatch;
    RawImage       svImage;
    Texture2D      svTex, hueTex;
    RectTransform  svHandle, hueHandle;
    float          curH, curS, curV;

    void Start() => TryBuild();

    void Update()
    {
        if (!built) { TryBuild(); return; }
        if (panelRoot == null) return;

        if (Hotkeys.Down(KeyCode.O))
        {
            bool open = UiAnim.Toggle(panelRoot);
            if (open) { EnsureParts(); UISounds.PlayOpen(); }
            else UISounds.PlayClose();
        }
        else if (UiAnim.IsOpen(panelRoot) && Hotkeys.CancelDown())
        {
            UiAnim.Close(panelRoot);
            UISounds.PlayClose();
            Hotkeys.MarkCancelHandled();
        }

        // While open, keep the parts column in sync with the current character (rebuilds after a model swap,
        // and fills in if the character wasn't ready the frame the panel opened). Cheap: early-outs unchanged.
        if (UiAnim.IsOpen(panelRoot))
            EnsureParts();
    }

    // ── Build ─────────────────────────────────────────────────────────────────────
    void TryBuild()
    {
        var content = FindContent();
        if (content == null) return;
        panelRoot = content.parent.gameObject;
        UiKit.ClickShield(panelRoot.transform as RectTransform);

        var hlg = content.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing               = 16;
        hlg.padding               = new RectOffset(10, 10, 10, 10);
        hlg.childAlignment        = TextAnchor.UpperLeft;
        hlg.childControlWidth     = hlg.childControlHeight = true;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = true;

        // Left: clothing parts.
        var leftGO  = MakeContainer("PartsCol", content);
        var leftLe  = leftGO.AddComponent<LayoutElement>();
        leftLe.preferredWidth = 210; leftLe.flexibleWidth = 0; leftLe.flexibleHeight = 1;
        var leftVlg = leftGO.AddComponent<VerticalLayoutGroup>();
        leftVlg.spacing = 8; leftVlg.childAlignment = TextAnchor.UpperCenter;
        leftVlg.childControlWidth = leftVlg.childControlHeight = true;
        leftVlg.childForceExpandWidth = true; leftVlg.childForceExpandHeight = false;

        var pl = MakeText("PartsLabel", leftGO.transform, "Clothing", 14, FontStyle.Bold);
        pl.gameObject.AddComponent<LayoutElement>().preferredHeight = 20;

        var gridGO = MakeContainer("PartsGrid", leftGO.transform);
        gridGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        var glg = gridGO.AddComponent<GridLayoutGroup>();
        glg.cellSize = new Vector2(SwatchSize, SwatchSize);
        glg.spacing  = new Vector2(6, 6);
        glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        glg.constraintCount = 4;
        partsGrid = gridGO.transform;

        // Hex readout of the selected part's current color (swatch + code), in the Clothing column.
        var hexRow = MakeContainer("HexRow", leftGO.transform);
        hexRow.AddComponent<LayoutElement>().preferredHeight = 26;
        var hrl = hexRow.AddComponent<HorizontalLayoutGroup>();
        hrl.spacing = 6; hrl.childAlignment = TextAnchor.MiddleCenter;
        hrl.childControlWidth = hrl.childControlHeight = true;
        hrl.childForceExpandWidth = false; hrl.childForceExpandHeight = true;

        var swGO = new GameObject("HexSwatch", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        swGO.transform.SetParent(hexRow.transform, false);
        var swLe = swGO.AddComponent<LayoutElement>();
        swLe.preferredWidth = 22; swLe.flexibleWidth = 0;
        hexSwatch = swGO.GetComponent<Image>();
        hexSwatch.color = Color.white; hexSwatch.raycastTarget = false;
        var swOl = swGO.AddComponent<Outline>();
        swOl.effectColor = new Color(0, 0, 0, 0.6f); swOl.effectDistance = new Vector2(1, -1);

        hexLabel = MakeText("HexLabel", hexRow.transform, "#FFFFFF", 13, FontStyle.Bold);
        hexLabel.alignment = TMPro.TextAlignmentOptions.Left;
        hexLabel.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        var resetBtn = MakeButton("ResetPart", leftGO.transform, "Reset part", () =>
        {
            if (selectedPart < 0) return;
            Dye()?.ClearPart(parts[selectedPart]);
            RefreshPartSwatches();
            UISounds.PlayClick();
        });
        resetBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 26;

        // Divider.
        var divGO = MakeContainer("Divider", content);
        divGO.AddComponent<LayoutElement>().preferredWidth = 1;
        divGO.AddComponent<Image>().color = new Color(1, 1, 1, 0.18f);

        // Right: color palette.
        var rightGO  = MakeContainer("ColorCol", content);
        var rightLe  = rightGO.AddComponent<LayoutElement>();
        rightLe.flexibleWidth = 1; rightLe.flexibleHeight = 1;
        var rightVlg = rightGO.AddComponent<VerticalLayoutGroup>();
        rightVlg.spacing = 8; rightVlg.childAlignment = TextAnchor.UpperCenter;
        rightVlg.childControlWidth = rightVlg.childControlHeight = true;
        rightVlg.childForceExpandWidth = true; rightVlg.childForceExpandHeight = false;

        var cl = MakeText("ColorLabel", rightGO.transform, "Color", 14, FontStyle.Bold);
        cl.gameObject.AddComponent<LayoutElement>().preferredHeight = 20;

        var palGO = MakeContainer("PaletteGrid", rightGO.transform);
        palGO.AddComponent<LayoutElement>().flexibleWidth = 1;
        var pglg = palGO.AddComponent<GridLayoutGroup>();
        pglg.cellSize = new Vector2(SwatchSize, SwatchSize);
        pglg.spacing  = new Vector2(6, 6);
        pglg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        pglg.constraintCount = PalCols;
        for (int i = 0; i < Palette.Length; i++)
        {
            Color c = Palette[i];
            MakeButton("Pal", palGO.transform, "", () => { PickColor(c, true); UISounds.PlayClick(); }).image.color = c;
        }

        BuildPicker(rightGO.transform);

        built = true;
    }

    // ── Parts ───────────────────────────────────────────────────────────────────
    void EnsureParts()
    {
        if (partsGrid == null) return;
        var d = Dye();
        if (d == null) return;
        if (d == builtForDye && parts.Count > 0) return;   // already built for this character

        // First build, or the character was swapped → rebuild the parts column for the current body.
        builtForDye = d;
        foreach (Transform c in partsGrid) Destroy(c.gameObject);
        parts.Clear();
        partSwatches.Clear();
        selectedPart = -1;

        var detected = d.DetectableParts();
        if (detected.Count > 0)
            d.RetainParts(detected);   // drop stale dyes; never on an empty scan (mesh not ready ≠ wipe the save)
        for (int i = 0; i < detected.Count; i++)
        {
            parts.Add(detected[i]);
            int idx = i;
            var sw = MakeButton("Part", partsGrid, "", () => SelectPart(idx));
            partSwatches.Add(sw.image);
        }
        RefreshPartSwatches();
        if (parts.Count > 0) SelectPart(0);
    }

    void SelectPart(int i)
    {
        selectedPart = i;
        for (int k = 0; k < partSwatches.Count; k++)
        {
            var ol = partSwatches[k].GetComponent<Outline>() ?? partSwatches[k].gameObject.AddComponent<Outline>();
            bool sel = k == i;
            ol.effectColor    = sel ? Color.white : new Color(0, 0, 0, 0.5f);
            ol.effectDistance = sel ? new Vector2(2, -2) : new Vector2(1, -1);
        }
        // Point the picker + hex readout at this part's current color.
        Color cur = (i >= 0 && i < parts.Count) ? (Dye()?.TargetFor(parts[i]) ?? parts[i]) : Color.white;
        SyncPickerTo(cur);
        UpdateHex(cur);
        UISounds.PlayClick();
    }

    // Recolor the selected part. When <paramref name="syncPicker"/> the picker's HSV state and handles are
    // re-derived from the chosen color (used by palette clicks + part selection); the picker's OWN drags pass
    // false so their live HSV isn't overwritten by the RGB round-trip.
    void PickColor(Color c, bool syncPicker)
    {
        if (selectedPart < 0 || selectedPart >= parts.Count) return;
        Dye()?.SetPartColor(parts[selectedPart], c);
        RefreshPartSwatches();
        if (syncPicker) SyncPickerTo(c);
        UpdateHex(c);
    }

    void UpdateHex(Color c)
    {
        if (hexLabel  != null) hexLabel.text  = "#" + ColorUtility.ToHtmlStringRGB(c);
        if (hexSwatch != null) hexSwatch.color = c;
    }

    // Each part swatch shows its CURRENT color (dyed target, or its own color if untouched).
    void RefreshPartSwatches()
    {
        var d = Dye();
        for (int i = 0; i < partSwatches.Count && i < parts.Count; i++)
            partSwatches[i].color = d != null ? d.TargetFor(parts[i]) : parts[i];
    }

    CharacterDye3D Dye()
    {
        if (dye != null) return dye;
        // The live-3D player body is a Character3DVisual that is NOT parented under the player root (the game
        // positions it separately), so we locate it directly. The player's visual is the one whose `player`
        // is wired (NPC/mob visuals leave it null). Add the dye there if this character was built before it existed.
        Character3DVisual visual = null;
        foreach (var v in FindObjectsByType<Character3DVisual>())
            if (v != null && v.player != null) { visual = v; break; }
        if (visual == null) return null;
        dye = visual.GetComponent<CharacterDye3D>() ?? visual.gameObject.AddComponent<CharacterDye3D>();
        return dye;
    }

    RectTransform FindContent()
    {
        foreach (var t in GetComponentsInChildren<Transform>(includeInactive: true))
            if (t.name == "CustomizePanel")
                return t.Find("Content") as RectTransform;
        return null;
    }

    // ── Free color picker (HSV) ───────────────────────────────────────────────────
    void BuildPicker(Transform col)
    {
        svTex  = new Texture2D(64, 64, TextureFormat.RGBA32, false)
                 { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        hueTex = MakeHueTex(128);
        RebuildSV(0f);

        var pl = MakeText("PickerLabel", col, "Custom", 14, FontStyle.Bold);
        pl.gameObject.AddComponent<LayoutElement>().preferredHeight = 20;

        // Saturation (x) × Value (y) square.
        svImage = MakeRaw("SVSquare", col, svTex);
        svImage.gameObject.AddComponent<LayoutElement>().preferredHeight = 110;
        var svDrag = svImage.gameObject.AddComponent<UiDragArea>();
        svDrag.onPick = n => { curS = n.x; curV = n.y; OnPicker(false); };
        svHandle = MakeHandle(svImage.transform, new Vector2(11, 11), false);

        // Hue bar.
        var hueImg = MakeRaw("HueBar", col, hueTex);
        hueImg.gameObject.AddComponent<LayoutElement>().preferredHeight = 16;
        var hueDrag = hueImg.gameObject.AddComponent<UiDragArea>();
        hueDrag.onPick = n => { curH = n.x; RebuildSV(curH); OnPicker(false); };
        hueHandle = MakeHandle(hueImg.transform, new Vector2(3, 0), true);

        PositionHandles();
    }

    // A picker drag changed H/S/V → apply the resulting color and move the handles.
    void OnPicker(bool syncFromColor)
    {
        Color c = Color.HSVToRGB(curH, curS, curV);
        PickColor(c, syncFromColor);
        PositionHandles();
    }

    // Point the picker at an externally-chosen color (part select / palette click).
    void SyncPickerTo(Color c)
    {
        if (svImage == null) return;
        Color.RGBToHSV(c, out curH, out curS, out curV);
        RebuildSV(curH);
        PositionHandles();
    }

    void PositionHandles()
    {
        if (svHandle != null)
        {
            svHandle.anchorMin = svHandle.anchorMax = new Vector2(curS, curV);
            svHandle.anchoredPosition = Vector2.zero;
        }
        if (hueHandle != null)
        {
            hueHandle.anchorMin = new Vector2(curH, 0f);
            hueHandle.anchorMax = new Vector2(curH, 1f);
            hueHandle.anchoredPosition = Vector2.zero;
            hueHandle.sizeDelta = new Vector2(3, 0);
        }
    }

    void RebuildSV(float h)
    {
        if (svTex == null) return;
        int w = svTex.width, hgt = svTex.height;
        for (int y = 0; y < hgt; y++)
        {
            float v = (float)y / (hgt - 1);
            for (int x = 0; x < w; x++)
                svTex.SetPixel(x, y, Color.HSVToRGB(h, (float)x / (w - 1), v));
        }
        svTex.Apply(false);
    }

    static Texture2D MakeHueTex(int n)
    {
        var t = new Texture2D(n, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int x = 0; x < n; x++) t.SetPixel(x, 0, Color.HSVToRGB((float)x / (n - 1), 1f, 1f));
        t.Apply(false);
        return t;
    }

    void OnDestroy()
    {
        if (svTex  != null) Destroy(svTex);
        if (hueTex != null) Destroy(hueTex);
    }

    static RawImage MakeRaw(string name, Transform parent, Texture tex)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var ri = go.GetComponent<RawImage>();
        ri.texture = tex;
        return ri;
    }

    static RectTransform MakeHandle(Transform parent, Vector2 size, bool line)
    {
        var go = new GameObject("Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = Color.white; img.raycastTarget = false;
        var ol = go.AddComponent<Outline>();
        ol.effectColor = new Color(0, 0, 0, 0.85f); ol.effectDistance = new Vector2(1, -1);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        if (!line) rt.pivot = new Vector2(0.5f, 0.5f);
        return rt;
    }

    // ── UI factory ────────────────────────────────────────────────────────────────
    static GameObject MakeContainer(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static GameObject MakeContainer(string name, RectTransform parent) => MakeContainer(name, (Transform)parent);

    static TMPro.TMP_Text MakeText(string name, Transform parent, string content, int size, FontStyle style)
    {
        var t = UiKit.MakeText(name, parent, content, size, style, TextAnchor.MiddleCenter, wrap: false);
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }

    static Button MakeButton(string name, Transform parent, string label, UnityEngine.Events.UnityAction onClick)
    {
        var go  = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = Color.white;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);
        if (!string.IsNullOrEmpty(label))
        {
            var t = UiKit.MakeText(name + "Lbl", go.transform, label, 12, FontStyle.Normal, TextAnchor.MiddleCenter, wrap: false);
            t.color = Color.white; t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        return btn;
    }
}

/// <summary>Reports pointer press/drag as a normalized (0..1) position within this RectTransform — the input
/// surface for the wardrobe's saturation/value square and hue bar. Event-driven (no per-frame work).</summary>
public class UiDragArea : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    public System.Action<Vector2> onPick;
    RectTransform rt;

    void Awake() => rt = transform as RectTransform;

    public void OnPointerDown(PointerEventData e) => Report(e);
    public void OnDrag(PointerEventData e)        => Report(e);

    void Report(PointerEventData e)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, e.position, e.pressEventCamera, out var lp))
            return;
        var r = rt.rect;
        onPick?.Invoke(new Vector2(
            Mathf.Clamp01((lp.x - r.xMin) / r.width),
            Mathf.Clamp01((lp.y - r.yMin) / r.height)));
    }
}
