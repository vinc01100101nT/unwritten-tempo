using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Drives the on-screen action bar (the <c>HUD_Hotbar</c> holder baked by <see cref="UIShellBuilder"/>) and
/// is the CENTRAL key router for active skills. The bar has nine slots (keys 1–9); each shows the skill the
/// player has BOUND to it (<see cref="HotbarBindings"/>) — symbol, hotkey glyph, MP cost, cooldown sweep,
/// countdown — or reads as an empty slot. The class system is gone: any UNLOCKED skill can be dragged or
/// clicked onto any slot in the Skills (K) menu; this panel decorates the slots, fires the bound skill on its
/// slot key or a click, and accepts drops (each cell is a <see cref="HotbarSlotTarget"/>).
///
/// Lives on the persistent GameUICanvas. Builds once (retries until the player + holder exist), refreshes the
/// cheap per-frame bits, and fully rebuilds when bindings change (<see cref="RebuildRequested"/>).
/// </summary>
public class HotbarPanel : MonoBehaviour
{
    // One decorated cell. Cached colour/cooldown state lets Refresh skip work (and string allocs) when
    // nothing visible changed — important since Refresh runs every frame.
    class Slot
    {
        public int index;               // 0..8 → key (index+1)
        public IHotbarSkill skill;      // the bound skill (null = empty slot)
        public Image cell, cooldown;
        public TMP_Text mp, label, cdNum;
        public GameObject hideOnCd;     // the center label to hide while counting down
        public int lastSecs = -1;
        public sbyte lastOnCd = -1, lastAff = -1;
        public bool isUlt;              // the dedicated ULT cell (the R seat) — same cooldown rules as keys 1–9
        public Color weaponColor = CellReady;   // the cell's own colour; affordability DIMS this, never replaces it
    }

    static readonly Color CellReady = Color.white;

    /// <summary>A cell you cannot afford: its OWN colour, darkened. Dimming rather than replacing is what keeps the
    /// weapon readable when the skill is greyed out.</summary>
    static Color Dim(Color c) => new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.5f, c.a);
    static readonly Color CellDim    = new Color(0.5f, 0.5f, 0.55f, 1f);
    static readonly Color CellEmpty  = new Color(0.32f, 0.32f, 0.37f, 0.6f);   // unbound slot
    static readonly Color KeyColor   = UiKit.TextOnDim;
    static readonly Color MpColor     = new Color(0.6f, 0.78f, 1f);
    static readonly Color MpNoColor   = new Color(1f, 0.5f, 0.45f);
    static readonly Color CdShade     = new Color(0f, 0f, 0f, 0.55f);
    static readonly Color UltKeyColor    = new Color(1f, 0.55f, 0.95f);   // ult slot's "R" glyph — distinct from 1–9

    // Pre-baked second strings so the countdown repaint never allocates (cooldowns are short).
    static readonly string[] Secs = BuildSecs();
    static string[] BuildSecs() { var a = new string[61]; for (int i = 0; i < a.Length; i++) a[i] = i.ToString(); return a; }

    /// <summary>Invoke to rebuild the bar (e.g. after a binding change). Set by the live HotbarPanel.</summary>
    public static System.Action RebuildRequested;

    Sprite whiteSprite;
    Player3DController player;
    Transform holder;
    readonly List<Slot> slots = new List<Slot>();
    Slot ult;                       // the dedicated ULT cell (last child), built + refreshed separately
    bool built;
    bool cellsEnsured;

    void Start()
    {
        RebuildRequested = Rebuild;
        // Ensure the buff bar exists even on a GameUICanvas built BEFORE it was added to the shell (so it appears
        // without a Build UI Shell rebuild). Idempotent — skips if the shell already added one.
        if (GetComponent<BuffBarPanel>() == null) gameObject.AddComponent<BuffBarPanel>();
        // The stamina gauge — added the same way so it appears without a shell rebuild. Idempotent.
        if (GetComponent<StaminaBar>() == null) gameObject.AddComponent<StaminaBar>();
        TryBuild();
    }

    void Update()
    {
        if (!built) { TryBuild(); return; }
        HandleHotkeys();
        Refresh();
    }

    // Central key router: pressing a slot's number key fires whatever skill is bound there. Number keys aren't
    // blocked by mouse-over-UI the way the old per-skill mouse path was, so hovering the bar doesn't swallow the key.
    void HandleHotkeys()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return;
        for (int i = 0; i < slots.Count; i++)
        {
            if (!SlotKeyDown(i)) continue;
            var s = slots[i];
            if (s.skill != null && s.skill.Activate()) UISounds.PlayClick();
        }
        // Dedicated ULT key (R) — fires the one equipped ultimate (charge-gated inside Activate).
        if (ult != null && ult.skill != null && Hotkeys.Down(KeyCode.R) && ult.skill.Activate()) UISounds.PlayClick();
    }

    /// <summary>True when active skills may fire — a bound skill just works; whether it is learned and its weapon is
    /// in hand is the cast's own test.</summary>
    static bool SkillsEnabled => Character.Instance != null;

    static bool SlotKeyDown(int i) => Hotkeys.DigitDown(i);

    /// <summary>Tear down the decorated cells and rebuild from the player's current bindings. Safe before
    /// the first build.</summary>
    public void Rebuild()
    {
        if (holder != null)
            for (int i = 0; i < holder.childCount; i++)
            {
                var cell = holder.GetChild(i);
                for (int c = cell.childCount - 1; c >= 0; c--) Destroy(cell.GetChild(c).gameObject);
                var btn = cell.GetComponent<Button>();       if (btn != null) Destroy(btn);
                var trig = cell.GetComponent<HoverRelay>();  if (trig != null) Destroy(trig);
            }
        slots.Clear();
        ult = null;      // the ult cell is torn down with the rest — rebuilt in TryBuild
        SkillHoverCard.Hide();   // the hovered cell may be torn down — don't leave a stale tooltip floating
        built = false;   // Update() will TryBuild() again next frame, reading the new bindings
    }

    void TryBuild()
    {
        if (player == null) player = Object.FindAnyObjectByType<Player3DController>();
        if (player == null) return;
        if (holder == null) holder = FindByName("HUD_Hotbar");
        if (holder == null) return;
        if (Character.Instance == null) return;   // need the bindings

        if (whiteSprite == null)
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

        EnsureCells();
        EnsurePassiveRow();

        slots.Clear();
        int n = Mathf.Min(HotbarBindings.SlotCount, holder.childCount);
        for (int i = 0; i < n; i++)
        {
            var cell = holder.GetChild(i).GetComponent<Image>();
            if (cell != null) slots.Add(BuildSlot(cell, i));
        }
        // The dedicated ULT cell is the extra child past the nine numbered slots (added by EnsureCells).
        ult = null;
        if (holder.childCount > HotbarBindings.SlotCount)
        {
            var ultCell = holder.GetChild(HotbarBindings.SlotCount).GetComponent<Image>();
            if (ultCell != null) ult = BuildUltSlot(ultCell);
        }
        RefreshPassiveRow();
        built = slots.Count > 0;
    }

    // The shell bakes 6 cells; the bar now needs 9. Clone the (pristine) first cell up to nine and widen the
    // holder — so already-baked scenes get the full bar with no editor re-bake. Runs once.
    void EnsureCells()
    {
        if (cellsEnsured || holder == null) return;
        cellsEnsured = true;
        int have = holder.childCount;
        if (have == 0) return;
        var template = holder.GetChild(0).gameObject;
        // Four numbered slots + one dedicated ult cell = SlotCount + 1 cells total.
        int wanted = HotbarBindings.SlotCount + 1;

        // AND ANY CELL PAST THAT GOES. A scene baked when the bar was nine long still has those children, and
        // leaving them switched on drew five empty squares nothing could ever fill or answer. Switched off
        // first, because a Destroy does not take effect until the end of the frame and they would draw once more.
        for (int i = have - 1; i >= wanted; i--)
        {
            var extra = holder.GetChild(i).gameObject;
            extra.SetActive(false);
            Destroy(extra);
        }

        // THE CELL AT THE END IS THE ULT, whether it was baked as a numbered one or cloned as one just now. A
        // scene baked with nine identical cells has no cell that calls itself the ult, so it is named here.
        if (have > HotbarBindings.SlotCount)
            holder.GetChild(HotbarBindings.SlotCount).gameObject.name = "UltSlot";
        for (int i = have; i < wanted; i++)
        {
            var clone = Instantiate(template, holder);
            clone.name = i < HotbarBindings.SlotCount ? "Hotslot" : "UltSlot";
            for (int c = clone.transform.childCount - 1; c >= 0; c--) Destroy(clone.transform.GetChild(c).gameObject);
            var b = clone.GetComponent<Button>();         if (b != null) Destroy(b);
            var t = clone.GetComponent<HoverRelay>();      if (t != null) Destroy(t);
            var s = clone.GetComponent<HotbarSlotTarget>();if (s != null) Destroy(s);
            var d = clone.GetComponent<HotbarSlotDragSource>(); if (d != null) Destroy(d);
        }
        // THE ULT IS THE BIG ONE. It is the seat a weapon only fills at its best, so it reads as a different
        // kind of thing at a glance rather than as a sixth number key.
        if (holder.childCount > HotbarBindings.SlotCount
            && holder.GetChild(HotbarBindings.SlotCount) is RectTransform ultRT)
            ultRT.sizeDelta = new Vector2(UltCellSize, UltCellSize);

        if (holder is RectTransform rt)
            rt.sizeDelta = new Vector2(HotbarBindings.SlotCount * 54 + UltCellSize + 6, UltCellSize);
    }

    const int UltCellSize = 64;
    const int PassiveCellSize = 30;

    // The read-only row of passives, above the bar. Built the same way the extra bar cells are — at runtime, from
    // the bar's own first cell — so a scene baked before this existed grows the row without being re-baked.
    RectTransform passiveRow;
    readonly List<Image> passiveCells = new List<Image>();
    readonly List<string> activePassives = new List<string>();

    void EnsurePassiveRow()
    {
        if (passiveRow != null || holder == null || holder.childCount == 0) return;

        var rowGO = new GameObject("HUD_HotbarPassives", typeof(RectTransform));
        passiveRow = (RectTransform)rowGO.transform;
        passiveRow.SetParent(holder.parent, false);

        // Anchored to the same place as the bar and lifted clear of it, so the two move together.
        var barRT = holder as RectTransform;
        if (barRT != null)
        {
            passiveRow.anchorMin = barRT.anchorMin;
            passiveRow.anchorMax = barRT.anchorMax;
            passiveRow.pivot = barRT.pivot;
            passiveRow.anchoredPosition = barRT.anchoredPosition + new Vector2(0f, barRT.sizeDelta.y * 0.5f + PassiveCellSize * 0.5f + 4f);
        }
        passiveRow.sizeDelta = new Vector2(0f, PassiveCellSize);

        var lay = rowGO.AddComponent<HorizontalLayoutGroup>();
        lay.spacing = 4;
        lay.childAlignment = TextAnchor.MiddleCenter;
        lay.childControlWidth = lay.childControlHeight = false;
        lay.childForceExpandWidth = lay.childForceExpandHeight = false;
    }

    // Cell i of the passive row, grown on demand from the bar's own first cell — only when the row gets longer, so
    // never on a steady frame.
    Image PassiveCell(int i)
    {
        while (passiveCells.Count <= i)
        {
            var clone = Instantiate(holder.GetChild(0).gameObject, passiveRow);
            clone.name = "PassiveSlot";
            for (int c = clone.transform.childCount - 1; c >= 0; c--) Destroy(clone.transform.GetChild(c).gameObject);
            // NOTHING ON A PASSIVE CELL IS TOUCHABLE. It is a readout of what is running, not a place to put
            // anything: a passive fires on its own trigger and has no key, so a click could only ever do nothing.
            var b = clone.GetComponent<Button>();               if (b != null) Destroy(b);
            var t = clone.GetComponent<HoverRelay>();            if (t != null) Destroy(t);
            var st = clone.GetComponent<HotbarSlotTarget>();     if (st != null) Destroy(st);
            var d = clone.GetComponent<HotbarSlotDragSource>();  if (d != null) Destroy(d);
            if (clone.transform is RectTransform crt) crt.sizeDelta = new Vector2(PassiveCellSize, PassiveCellSize);
            var img = clone.GetComponent<Image>();
            img.raycastTarget = false;
            passiveCells.Add(img);
        }
        return passiveCells[i];
    }

    // Fill the row from what is actually running: every learned passive of the weapon in hand. The row grows a cell
    // for each one, so no passive that fires is ever missing from it.
    void RefreshPassiveRow()
    {
        if (passiveRow == null || Character.Instance == null) return;

        var me = Character.Instance;
        me.PassiveSkills.CollectActive(activePassives);
        passiveRow.sizeDelta = new Vector2(activePassives.Count * (PassiveCellSize + 4), PassiveCellSize);

        for (int i = 0; i < activePassives.Count; i++)
        {
            var cell = PassiveCell(i);
            cell.gameObject.SetActive(true);
            for (int c = cell.transform.childCount - 1; c >= 0; c--) Destroy(cell.transform.GetChild(c).gameObject);

            var def = SkillCatalog.Get(activePassives[i]);
            if (def == null) { cell.color = CellEmpty; continue; }

            cell.color = SkillSymbol.ColorFor(def);
            var label = NewText("Label", cell.transform, def.Symbol, 11, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            Stretch(label.rectTransform, 1, 1);
            label.raycastTarget = false;
        }
        for (int i = activePassives.Count; i < passiveCells.Count; i++)
            if (passiveCells[i] != null) passiveCells[i].gameObject.SetActive(false);
    }

    Slot BuildSlot(Image cell, int index)
    {
        string id = Character.Instance.Hotbar.Get(index);
        var skill = PlayerSkills.ById(id);
        var s = new Slot { index = index, skill = skill, cell = cell };

        // Every cell is a drop target (drag a skill from the K menu onto it) and a button (click = fire bound).
        var target = cell.gameObject.GetComponent<HotbarSlotTarget>() ?? cell.gameObject.AddComponent<HotbarSlotTarget>();
        target.slotIndex = index;
        cell.raycastTarget = true;

        var btn = cell.gameObject.GetComponent<Button>() ?? cell.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = cell;
        var captured = s;
        btn.onClick.AddListener(() => { if (SkillsEnabled && captured.skill != null && captured.skill.Activate()) UISounds.PlayClick(); });

        // Hotkey glyph (top-left) — the SLOT's key, always shown (bound or empty).
        var key = NewText("Key", cell.transform, (index + 1).ToString(), 12, FontStyle.Bold, TextAnchor.UpperLeft, KeyColor);
        Stretch(key.rectTransform, 4, 2);

        if (skill == null)
        {
            cell.color = CellEmpty;   // dim, no symbol/mp → reads as an open slot
            var staleDrag = cell.gameObject.GetComponent<HotbarSlotDragSource>();   // an emptied cell isn't draggable
            if (staleDrag != null) Destroy(staleDrag);
            return s;
        }

        // Drag this filled slot onto another to rearrange the bar (a plain click still fires the skill).
        var drag = cell.gameObject.GetComponent<HotbarSlotDragSource>() ?? cell.gameObject.AddComponent<HotbarSlotDragSource>();
        drag.slotIndex = index;
        drag.tint = skill.Tint;

        cell.color = CellReady;

        // Radial cooldown shade.
        s.cooldown = NewImage("Cooldown", cell.transform, CdShade);
        s.cooldown.sprite = whiteSprite;
        s.cooldown.type = Image.Type.Filled;
        s.cooldown.fillMethod = Image.FillMethod.Radial360;
        s.cooldown.fillOrigin = (int)Image.Origin360.Top;
        s.cooldown.fillClockwise = true;
        s.cooldown.fillAmount = 0f;
        s.cooldown.raycastTarget = false;
        Stretch(s.cooldown.rectTransform, 3, 3);

        // The symbol IS the face of the slot — two letters on the weapon's colour, the way Au stands for gold.
        {
            s.weaponColor = skill.SymbolColor;
            cell.color = s.weaponColor;
            s.label = NewText("Label", cell.transform, skill.Symbol, 18, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            Stretch(s.label.rectTransform, 2, 2);
            s.hideOnCd = s.label.gameObject;
        }

        // MP cost (bottom-right). Always on top.
        s.mp = NewText("Mp", cell.transform, skill.ManaCost.ToString(), 10, FontStyle.Bold, TextAnchor.LowerRight, MpColor);
        Stretch(s.mp.rectTransform, 2, 3);

        // Countdown number (center), shown only while on cooldown.
        s.cdNum = NewText("CdNum", cell.transform, "", 18, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Stretch(s.cdNum.rectTransform, 2, 2);
        s.cdNum.gameObject.SetActive(false);

        // Skill-info hover card (tags, cost, description, effects).
        if (skill is SkillRunner sr && sr.def != null) SkillHoverCard.Attach(cell.transform, sr.def);

        return s;
    }

    // The dedicated ULT cell: display + fire only (its binding is separate from keys 1–9, so no drop-target/drag).
    // The tier picks the SLOT and nothing else — it shades and counts down exactly like keys 1–9, through the same
    // RefreshSlot body, because an ultimate is an ordinary skill sitting in a reserved seat.
    Slot BuildUltSlot(Image cell)
    {
        string id = Character.Instance.Hotbar.Ult;
        var skill = PlayerSkills.ById(id);
        var s = new Slot { index = -1, skill = skill, cell = cell, isUlt = true };

        var btn = cell.gameObject.GetComponent<Button>() ?? cell.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = cell;
        btn.onClick.RemoveAllListeners();
        var captured = s;
        btn.onClick.AddListener(() => { if (SkillsEnabled && captured.skill != null && captured.skill.Activate()) UISounds.PlayClick(); });
        cell.raycastTarget = true;

        // Drop target for the dedicated ULT slot — sentinel slotIndex −1 tells HotbarBindings.BindFromDrag this
        // is the ult cell (only an Ultimate-tier skill may land here). This is what lets you DRAG an ult onto R.
        var ultTarget = cell.gameObject.GetComponent<HotbarSlotTarget>() ?? cell.gameObject.AddComponent<HotbarSlotTarget>();
        ultTarget.slotIndex = -1;

        var key = NewText("Key", cell.transform, "R", 12, FontStyle.Bold, TextAnchor.UpperLeft, UltKeyColor);
        Stretch(key.rectTransform, 4, 2);

        if (skill == null)
        {
            cell.color = CellEmpty;
            var staleDrag = cell.gameObject.GetComponent<HotbarSlotDragSource>();   // an emptied ult cell isn't draggable
            if (staleDrag != null) Destroy(staleDrag);
            return s;
        }

        // Drag the equipped ult OFF the cell to remove it (sentinel slotIndex −1 → HotbarBindings.ClearUlt).
        var drag = cell.gameObject.GetComponent<HotbarSlotDragSource>() ?? cell.gameObject.AddComponent<HotbarSlotDragSource>();
        drag.slotIndex = -1;
        drag.tint = skill.Tint;

        cell.color = CellReady;
        // Radial cooldown shade — the same one keys 1–9 get, built the same way round.
        s.cooldown = NewImage("Cooldown", cell.transform, CdShade);
        s.cooldown.sprite = whiteSprite;
        s.cooldown.type = Image.Type.Filled;
        s.cooldown.fillMethod = Image.FillMethod.Radial360;
        s.cooldown.fillOrigin = (int)Image.Origin360.Top;
        s.cooldown.fillClockwise = true;
        s.cooldown.fillAmount = 0f;
        s.cooldown.raycastTarget = false;
        Stretch(s.cooldown.rectTransform, 3, 3);

        {
            s.weaponColor = skill.SymbolColor;
            cell.color = s.weaponColor;
            s.label = NewText("Label", cell.transform, skill.Symbol, 18, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            Stretch(s.label.rectTransform, 2, 2);
            s.hideOnCd = s.label.gameObject;
        }

        s.mp = NewText("Mp", cell.transform, skill.ManaCost.ToString(), 10, FontStyle.Bold, TextAnchor.LowerRight, MpColor);
        Stretch(s.mp.rectTransform, 2, 3);

        // Countdown number (center), shown only while on cooldown — same as every other cell.
        s.cdNum = NewText("CdNum", cell.transform, "", 18, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Stretch(s.cdNum.rectTransform, 2, 2);
        s.cdNum.gameObject.SetActive(false);

        if (skill is SkillRunner sr2 && sr2.def != null) SkillHoverCard.Attach(cell.transform, sr2.def);
        return s;
    }

    void Refresh()
    {
        bool ringOn = SkillsEnabled;   // hoist once: no ring → the whole bar reads as suppressed
        for (int i = 0; i < slots.Count; i++) RefreshSlot(slots[i], ringOn);
        RefreshSlot(ult, ringOn);      // the ult cell is a slot like any other — one body, so it can't drift
    }

    // ONE per-cell repaint for every hotbar cell, the ult included. Kept as a single body on purpose: the ult
    // differs only in which seat it occupies, so any cooldown/affordability rule written here applies everywhere.
    void RefreshSlot(Slot s, bool ringOn)
    {
        {
            if (s == null || s.skill == null || s.cooldown == null) return;   // empty slot — nothing live to refresh

            float frac = s.skill.CooldownFraction01;
            s.cooldown.fillAmount = frac;

            sbyte onCd = (sbyte)(frac > 0.0001f ? 1 : 0);
            if (onCd != s.lastOnCd)
            {
                if (s.hideOnCd != null) s.hideOnCd.SetActive(onCd == 0);
                s.cdNum.gameObject.SetActive(onCd == 1);
                s.lastOnCd = onCd;
                s.lastSecs = -1;
            }
            if (onCd == 1)
            {
                int secs = Mathf.CeilToInt(s.skill.CooldownRemaining);
                if (secs != s.lastSecs)
                {
                    s.cdNum.text = (secs >= 0 && secs < Secs.Length) ? Secs[secs] : secs.ToString();
                    s.lastSecs = secs;
                }
            }

            sbyte aff = (sbyte)(s.skill.Affordable && ringOn ? 1 : 0);
            if (aff != s.lastAff)
            {
                s.cell.color = aff == 1 ? s.weaponColor : Dim(s.weaponColor);
                if (s.mp != null) s.mp.color = aff == 1 ? MpColor : MpNoColor;
                s.lastAff = aff;
            }
        }
    }

    Transform FindByName(string n)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == n) return t;
        return null;
    }

    // ---- UI primitives (match StatPanel's style) ----
    TMP_Text NewText(string name, Transform parent, string str, int size, FontStyle style, TextAnchor anchor, Color color)
    {
        var t = UiKit.MakeText(name, parent, str, size, style, anchor, wrap: false);
        t.color = color; t.raycastTarget = false; t.richText = false;
        return t;
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static void Stretch(RectTransform rt, float padX, float padY)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }
}
