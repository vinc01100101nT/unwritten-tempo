using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The <b>Combo Composer</b> (N) — the runtime loadout screen where the player builds combo chains (sibling of the
/// Grimoire/Skills menu, not an editor window). Each chain is 4 ordered skill slots + one hotkey; a skill may live in
/// exactly ONE chain. Two-click flow: click an empty slot to select it, then click a skill from the palette to fill it.
/// Filling a slot COMMITS that skill — it leaves the solo hotbar (RO-style loadout choice). All edits write straight
/// to <see cref="Character.Combos"/> (the SSOT) and persist via <see cref="SaveManager"/>.
///
/// <para>Self-builds a <see cref="UiKit.Window"/> on the persistent GameUICanvas (added by <c>UIShellBuilder</c>);
/// rebuilds its whole body on any change — this is an edit-time surface, not a per-frame hot path.</para>
/// </summary>
public class ComboComposerPanel : MonoBehaviour
{
    const KeyCode ToggleKey = KeyCode.U;   // free: N=world map, M=minimap, P/T/F3/F4 taken

    // Combo-safe trigger keys (avoid WASD, 1–9, the window toggles, and F3/F4 = PerfHud). Six keys → ComboBook.MaxChains.
    static readonly KeyCode[] SafeKeys =
    {
        KeyCode.F1, KeyCode.F2, KeyCode.F5, KeyCode.F6, KeyCode.F7, KeyCode.F8,
    };

    static readonly Color RowBg     = new Color(0.16f, 0.16f, 0.20f, 0.95f);
    static readonly Color SlotEmpty = new Color(0.10f, 0.10f, 0.13f, 0.95f);
    static readonly Color SlotSel   = new Color(0.20f, 0.35f, 0.28f, 1f);
    static readonly Color SlotFull  = new Color(0.18f, 0.20f, 0.26f, 1f);
    static readonly Color SubColor  = new Color(0.75f, 0.8f, 0.95f, 0.85f);
    static readonly Color RampColor = new Color(1f, 0.75f, 0.35f);

    GameObject root;
    RectTransform scrollContent;
    bool built;

    ComboChain selChain;     // slot currently awaiting a skill (null = none)
    int selSlot = -1;

    void Update()
    {
        if (!built) { TryBuild(); return; }
        if (root == null) return;
        if (Hotkeys.Down(ToggleKey))
        {
            bool open = UiAnim.Toggle(root);
            if (open) { ClearSelection(); Rebuild(); UISounds.PlayOpen(); } else UISounds.PlayClose();
        }
        else if (UiAnim.IsOpen(root) && Hotkeys.CancelDown())
        {
            UiAnim.Close(root); UISounds.PlayClose();
        }
    }

    void TryBuild()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        var content = UiKit.Window(canvas.transform, "Combo Composer", new Vector2(440, 520), Vector2.zero, out root);
        scrollContent = EnsureScroll(content);

        var vlg = scrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8;
        vlg.padding = new RectOffset(2, 2, 2, 2);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        built = true;   // hidden until first open
    }

    RectTransform EnsureScroll(RectTransform viewport)
    {
        if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.GetComponent<Image>();
        if (vpImg != null) vpImg.raycastTarget = true;

        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 22f;

        var sc = new GameObject("ScrollContent", typeof(RectTransform));
        var scrt = (RectTransform)sc.transform;
        scrt.SetParent(viewport, false);
        scrt.anchorMin = new Vector2(0, 1); scrt.anchorMax = new Vector2(1, 1); scrt.pivot = new Vector2(0.5f, 1);
        scrt.offsetMin = scrt.offsetMax = Vector2.zero;
        sc.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport;
        scroll.content = scrt;
        return scrt;
    }

    // ---- rebuild -------------------------------------------------------------

    void Rebuild()
    {
        if (scrollContent == null) return;
        for (int i = scrollContent.childCount - 1; i >= 0; i--) Destroy(scrollContent.GetChild(i).gameObject);

        var ch = Character.Instance;
        if (ch == null)
        {
            var note = NewText(scrollContent, "Start the game to compose combos.", 14, FontStyle.Italic, TextAnchor.MiddleCenter);
            note.color = SubColor; SetHeight(note.gameObject, 60);
            return;
        }

        var caption = NewText(scrollContent, "Pick a slot, then a skill. A skill in a chain leaves your hotbar.\nLand the 4th hit un-interrupted for big damage.",
                              12, FontStyle.Italic, TextAnchor.MiddleCenter);
        caption.color = SubColor; SetHeight(caption.gameObject, 34);

        var combos = ch.Combos;
        for (int c = 0; c < combos.Chains.Count; c++) BuildChainBlock(combos.Chains[c], c);

        if (combos.ChainCount < ComboBook.MaxChains)
        {
            var add = UiKit.Btn("＋ Add Chain", scrollContent, () => { combos.AddChain(); Persist(); Rebuild(); });
            SetHeight(add.gameObject, 30);
        }

        // Available-skills palette (everything not committed to a chain).
        var palHead = NewText(scrollContent, "Available skills", 13, FontStyle.Bold, TextAnchor.MiddleLeft);
        palHead.color = UiKit.Gold; SetHeight(palHead.gameObject, 22);
        BuildPalette(combos);
    }

    void BuildChainBlock(ComboChain chain, int index)
    {
        var block = NewImage(scrollContent, RowBg);
        SetHeight(block.gameObject, 96);

        // Header: title · hotkey · shared CD · remove.
        var title = NewText(block.transform, $"Chain {index + 1}", 13, FontStyle.Bold, TextAnchor.UpperLeft, UiKit.TextMain);
        Place(title.rectTransform, 8, -4, 90, 20, new Vector2(0, 1));

        string keyLabel = chain.hotkey == KeyCode.None ? "Key: —" : $"Key: {KeyName(chain.hotkey)}";
        var keyBtn = UiKit.Btn(keyLabel, block.transform, () => { CycleHotkey(chain); });
        Place((RectTransform)keyBtn.transform, 104, -4, 92, 22, new Vector2(0, 1));

        float cd = chain.SharedCooldown();
        var cdT = NewText(block.transform, cd > 0 ? $"CD {cd:0.#}s" : "CD —", 12, FontStyle.Normal, TextAnchor.UpperRight, SubColor);
        Place(cdT.rectTransform, -46, -6, 80, 18, new Vector2(1, 1));

        var rm = UiKit.Btn("✕", block.transform, () => { Character.Instance.Combos.RemoveChain(chain); ClearSelection(); Persist(); Rebuild(); });
        Place((RectTransform)rm.transform, -6, -4, 26, 22, new Vector2(1, 1));

        // 4 slot cells.
        for (int s = 0; s < ComboChain.Slots; s++) BuildSlotCell(block.transform, chain, s);

        // Ramp preview under the slots.
        var ramp = NewText(block.transform, RampString(chain), 11, FontStyle.Bold, TextAnchor.LowerLeft, RampColor);
        Place(ramp.rectTransform, 10, 4, 300, 16, new Vector2(0, 0));
    }

    void BuildSlotCell(Transform parent, ComboChain chain, int slot)
    {
        bool selected = chain == selChain && slot == selSlot;
        bool filled = chain.HasSlot(slot);
        var cell = NewImage(parent, selected ? SlotSel : (filled ? SlotFull : SlotEmpty));
        Place(cell.rectTransform, 10 + slot * 50, -30, 46, 40, new Vector2(0, 1));
        cell.raycastTarget = true;

        var def = filled ? chain.Def(slot) : null;
        if (def != null && def.icon != null)
        {
            var icon = NewImage(cell.transform, Color.white);
            icon.sprite = def.icon; icon.preserveAspect = true; icon.raycastTarget = false;
            UiKit.Fill(icon.rectTransform, new Vector2(4, 4), new Vector2(-4, -4));
        }
        else if (def != null)
        {
            var lbl = NewText(cell.transform, def.shortLabel, 10, FontStyle.Bold, TextAnchor.MiddleCenter, def.tint == default ? Color.white : def.tint);
            UiKit.Fill(lbl.rectTransform, Vector2.zero, Vector2.zero); lbl.raycastTarget = false;
        }
        else
        {
            var plus = NewText(cell.transform, (slot + 1).ToString(), 14, FontStyle.Bold, TextAnchor.MiddleCenter, SubColor);
            UiKit.Fill(plus.rectTransform, Vector2.zero, Vector2.zero); plus.raycastTarget = false;
        }

        var btn = cell.gameObject.AddComponent<Button>();
        btn.targetGraphic = cell;
        var capChain = chain; int capSlot = slot;
        btn.onClick.AddListener(() =>
        {
            if (capChain.HasSlot(capSlot)) ClearSlotAt(capChain, capSlot);   // click a filled slot → free it
            else SelectSlot(capChain, capSlot);                              // click an empty slot → arm it
        });
    }

    void BuildPalette(ComboBook combos)
    {
        var pool = SkillCatalog.OrderedIds();
        int shown = 0;
        for (int i = 0; i < pool.Length; i++)
        {
            var def = SkillCatalog.Get(pool[i]);
            if (def == null || combos.IsCommitted(def.id)) continue;   // committed skills aren't available
            BuildPaletteChip(def);
            shown++;
        }
        if (shown == 0)
        {
            var none = NewText(scrollContent, "No free skills — every composed skill is in a chain.", 12, FontStyle.Italic, TextAnchor.MiddleCenter);
            none.color = SubColor; SetHeight(none.gameObject, 40);
        }
    }

    void BuildPaletteChip(SkillDef def)
    {
        var chip = NewImage(scrollContent, SlotFull);
        SetHeight(chip.gameObject, 40);
        chip.raycastTarget = true;

        if (def.icon != null)
        {
            var icon = NewImage(chip.transform, Color.white);
            icon.sprite = def.icon; icon.preserveAspect = true; icon.raycastTarget = false;
            var irt = icon.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f); irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(6f, 0f); irt.sizeDelta = new Vector2(32f, 32f);
        }

        var name = NewText(chip.transform, string.IsNullOrEmpty(def.displayName) ? def.id : def.displayName,
                           13, FontStyle.Bold, TextAnchor.MiddleLeft, def.tint == default ? Color.white : def.tint);
        name.raycastTarget = false;
        var nrt = name.rectTransform;
        nrt.anchorMin = new Vector2(0f, 0f); nrt.anchorMax = new Vector2(1f, 1f);
        nrt.offsetMin = new Vector2(def.icon != null ? 46f : 10f, 0f); nrt.offsetMax = new Vector2(-96f, 0f);

        var sub = NewText(chip.transform, $"MP {def.manaCost} · CD {def.cooldown:0.#}s", 10, FontStyle.Normal, TextAnchor.MiddleRight, SubColor);
        sub.raycastTarget = false;
        var srt = sub.rectTransform;
        srt.anchorMin = new Vector2(1f, 0f); srt.anchorMax = new Vector2(1f, 1f); srt.pivot = new Vector2(1f, 0.5f);
        srt.sizeDelta = new Vector2(90f, 0f); srt.anchoredPosition = new Vector2(-8f, 0f);

        var btn = chip.gameObject.AddComponent<Button>();
        btn.targetGraphic = chip;
        var id = def.id;
        btn.onClick.AddListener(() => AssignToSelection(id));
    }

    // ---- actions -------------------------------------------------------------

    void SelectSlot(ComboChain chain, int slot) { selChain = chain; selSlot = slot; UISounds.PlayClick(); Rebuild(); }
    void ClearSelection() { selChain = null; selSlot = -1; }

    void AssignToSelection(string id)
    {
        var combos = Character.Instance != null ? Character.Instance.Combos : null;
        if (combos == null) return;

        // Target the armed slot; else the first empty slot of any chain; else make a new chain if allowed.
        ComboChain chain = selChain; int slot = selSlot;
        if (chain == null || slot < 0 || slot >= ComboChain.Slots || chain.HasSlot(slot))
            (chain, slot) = FirstEmptySlot(combos);
        if (chain == null) { BattleNotice.Warn("No free combo slot"); return; }

        if (!combos.AssignSlot(chain, slot, id)) { BattleNotice.Warn("Already in a chain"); return; }

        // Committing pulls the skill off the solo hotbar (a chain skill isn't castable solo).
        if (Character.Instance.Hotbar.Unbind(id)) HotbarPanel.RebuildRequested?.Invoke();
        ClearSelection();
        Persist();
        UISounds.PlayClick();
        Rebuild();
    }

    void ClearSlotAt(ComboChain chain, int slot)
    {
        Character.Instance.Combos.ClearSlot(chain, slot);
        ClearSelection();
        Persist();
        UISounds.PlayClick();
        Rebuild();
    }

    void CycleHotkey(ComboChain chain)
    {
        var combos = Character.Instance.Combos;
        int start = 0;
        for (int i = 0; i < SafeKeys.Length; i++) if (SafeKeys[i] == chain.hotkey) { start = i + 1; break; }

        // Pick the next safe key not used by ANOTHER chain (so two chains can't share a trigger).
        for (int step = 0; step < SafeKeys.Length; step++)
        {
            var key = SafeKeys[(start + step) % SafeKeys.Length];
            if (!KeyUsedElsewhere(combos, chain, key)) { combos.SetHotkey(chain, key); break; }
        }
        Persist();
        Rebuild();
    }

    static bool KeyUsedElsewhere(ComboBook combos, ComboChain self, KeyCode key)
    {
        for (int c = 0; c < combos.Chains.Count; c++)
        {
            var ch = combos.Chains[c];
            if (ch != null && ch != self && ch.hotkey == key) return true;
        }
        return false;
    }

    static (ComboChain, int) FirstEmptySlot(ComboBook combos)
    {
        for (int c = 0; c < combos.Chains.Count; c++)
        {
            var ch = combos.Chains[c];
            if (ch == null) continue;
            for (int s = 0; s < ComboChain.Slots; s++) if (!ch.HasSlot(s)) return (ch, s);
        }
        return (combos.AddChain(), 0);   // null if at MaxChains
    }

    void Persist()
    {
        if (SaveManager.Instance != null) SaveManager.Instance.SaveNow();
        ComboBarPanel.RebuildRequested?.Invoke();
    }

    static string RampString(ComboChain chain)
    {
        var sb = new System.Text.StringBuilder(40);
        for (int i = 0; i < ComboChain.Slots; i++)
        {
            if (i > 0) sb.Append("<alpha=#FF>  ·  ");
            float m = ComboBook.SlotMultiplier(i);
            bool filled = chain.HasSlot(i);
            // Set alpha explicitly each entry — TMP alpha tags persist, so a dim slot would otherwise bleed into the next.
            sb.Append(filled ? "<alpha=#FF><b>" : "<alpha=#66>").Append(m.ToString("0.#")).Append("×");
            if (filled) sb.Append("</b>");
        }
        return sb.ToString();
    }

    static string KeyName(KeyCode k) => k.ToString();

    // ---- tiny helpers --------------------------------------------------------

    TMP_Text NewText(Transform parent, string s, int size, FontStyle style, TextAnchor anchor, Color color)
    {
        var t = UiKit.MakeText("Text", parent, s, size, style, anchor);
        t.color = color; t.richText = true;
        return t;
    }
    TMP_Text NewText(Transform parent, string s, int size, FontStyle style, TextAnchor anchor)
        => NewText(parent, s, size, style, anchor, UiKit.TextMain);

    Image NewImage(Transform parent, Color color) => UiKit.Img("Image", parent, color);

    // Place a child by top/left-ish anchored corner: (x,y) from the given anchor, with a size. Simplifies the
    // absolute layout inside a fixed-height chain block.
    static void Place(RectTransform rt, float x, float y, float w, float h, Vector2 anchor)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);
    }

    static void SetHeight(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = h;
    }
}
