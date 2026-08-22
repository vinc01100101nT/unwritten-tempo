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
/// player has BOUND to it (<see cref="HotbarBindings"/>) — icon/label, hotkey glyph, MP cost, cooldown sweep,
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
        public Image cell, icon, cooldown;
        public TMP_Text mp, label, cdNum;
        public GameObject hideOnCd;     // the center label to hide while counting down (null when an icon shows)
        public int lastSecs = -1;
        public sbyte lastOnCd = -1, lastAff = -1;
        public bool isUlt;              // the dedicated ULT cell (the R seat) — same cooldown rules as keys 1–9
    }

    static readonly Color CellReady = Color.white;
    static readonly Color CellDim    = new Color(0.5f, 0.5f, 0.55f, 1f);
    static readonly Color CellEmpty  = new Color(0.32f, 0.32f, 0.37f, 0.6f);   // unbound slot
    static readonly Color KeyColor   = new Color(1f, 0.85f, 0.45f);
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

    // ONE shared modifier tooltip on the canvas ROOT (not parented inside a cell). The old per-cell tooltip
    // lived under its cell, so a neighbouring cell — drawn later — could occlude it, and a parent could clip
    // it: that's the "sometimes the helper doesn't show" bug. A single top-sibling panel, repositioned over
    // the hovered cell, is always drawn above everything and never clipped.
    GameObject tipRoot;
    TMP_Text tipText;
    RectTransform tipRT;
    Image tipIcon;
    static readonly Vector3[] cornerBuf = new Vector3[4];   // reused by GetWorldCorners (no per-hover alloc)

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

    // Central key router: pressing a slot's number key fires whatever skill is bound there. Gated only by the
    // dev panel (DevSettings.Blocking) — number keys aren't blocked by mouse-over-UI the way the old per-skill
    // mouse path was, so hovering the bar doesn't swallow the key.
    void HandleHotkeys()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return;
        if (DevSettings.Blocking) return;
        for (int i = 0; i < slots.Count; i++)
        {
            if (!SlotKeyDown(i)) continue;
            var s = slots[i];
            if (s.skill != null && s.skill.Activate()) UISounds.PlayClick();
        }
        // Dedicated ULT key (R) — fires the one equipped ultimate (charge-gated inside Activate).
        if (ult != null && ult.skill != null && Hotkeys.Down(KeyCode.R) && ult.skill.Activate()) UISounds.PlayClick();
    }

    /// <summary>True when active skills may fire. Skills come from the equipped weapon now, so they're no longer
    /// gated by the ring — a bound skill just works (the bar is empty anyway without a weapon/Grimoire binding).</summary>
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
                var trig = cell.GetComponent<EventTrigger>(); if (trig != null) Destroy(trig);
            }
        slots.Clear();
        ult = null;      // the ult cell is torn down with the rest — rebuilt in TryBuild
        HideTip();       // the hovered cell may be torn down — don't leave a stale tooltip floating
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
        EnsureSharedTooltip();

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
        // Nine numbered slots + one dedicated ult cell = SlotCount + 1 cells total.
        int wanted = HotbarBindings.SlotCount + 1;
        for (int i = have; i < wanted; i++)
        {
            var clone = Instantiate(template, holder);
            clone.name = i < HotbarBindings.SlotCount ? "Hotslot" : "UltSlot";
            for (int c = clone.transform.childCount - 1; c >= 0; c--) Destroy(clone.transform.GetChild(c).gameObject);
            var b = clone.GetComponent<Button>();         if (b != null) Destroy(b);
            var t = clone.GetComponent<EventTrigger>();    if (t != null) Destroy(t);
            var s = clone.GetComponent<HotbarSlotTarget>();if (s != null) Destroy(s);
            var d = clone.GetComponent<HotbarSlotDragSource>(); if (d != null) Destroy(d);
        }
        if (holder is RectTransform rt) rt.sizeDelta = new Vector2(wanted * 54, 54);
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
            cell.color = CellEmpty;   // dim, no icon/label/mp → reads as an open slot
            var staleDrag = cell.gameObject.GetComponent<HotbarSlotDragSource>();   // an emptied cell isn't draggable
            if (staleDrag != null) Destroy(staleDrag);
            return s;
        }

        // Drag this filled slot onto another to rearrange the bar (a plain click still fires the skill).
        var drag = cell.gameObject.GetComponent<HotbarSlotDragSource>() ?? cell.gameObject.AddComponent<HotbarSlotDragSource>();
        drag.slotIndex = index;
        drag.iconSprite = skill.Icon;
        drag.tint = skill.Tint;

        cell.color = CellReady;

        // Icon (below the sweep so it greys under it). Falls back to a centered label if unset.
        if (skill.Icon != null)
        {
            s.icon = NewImage("Icon", cell.transform, CellReady);
            s.icon.sprite = skill.Icon;
            s.icon.preserveAspect = true;
            s.icon.raycastTarget = false;
            Stretch(s.icon.rectTransform, 5, 5);
        }

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

        // Center label — only when there's no icon (above the sweep so it reads when ready).
        if (skill.Icon == null)
        {
            s.label = NewText("Label", cell.transform, skill.ShortLabel, 11, FontStyle.Bold, TextAnchor.MiddleCenter, skill.Tint);
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

        // Skill-info hover card (icon, tags, cost, description, effects).
        if (skill is SkillRunner sr && sr.def != null) AttachSkillTooltip(cell.transform, sr.def);

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
        drag.iconSprite = skill.Icon;
        drag.tint = skill.Tint;

        cell.color = CellReady;
        if (skill.Icon != null)
        {
            s.icon = NewImage("Icon", cell.transform, CellReady);
            s.icon.sprite = skill.Icon; s.icon.preserveAspect = true; s.icon.raycastTarget = false;
            Stretch(s.icon.rectTransform, 5, 5);
        }

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

        if (skill.Icon == null)
        {
            s.label = NewText("Label", cell.transform, skill.ShortLabel, 11, FontStyle.Bold, TextAnchor.MiddleCenter, skill.Tint);
            Stretch(s.label.rectTransform, 2, 2);
            s.hideOnCd = s.label.gameObject;
        }

        s.mp = NewText("Mp", cell.transform, skill.ManaCost.ToString(), 10, FontStyle.Bold, TextAnchor.LowerRight, MpColor);
        Stretch(s.mp.rectTransform, 2, 3);

        // Countdown number (center), shown only while on cooldown — same as every other cell.
        s.cdNum = NewText("CdNum", cell.transform, "", 18, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Stretch(s.cdNum.rectTransform, 2, 2);
        s.cdNum.gameObject.SetActive(false);

        if (skill is SkillRunner sr2 && sr2.def != null) AttachSkillTooltip(cell.transform, sr2.def);
        return s;
    }

    // Build the ONE shared skill-info card on the canvas root (once). Fixed width, auto height via ContentSizeFitter;
    // the skill icon is pinned top-right (out of the vertical layout). Hidden until a hover positions + shows it.
    void EnsureSharedTooltip()
    {
        if (tipRoot != null) return;
        var img = NewImage("SkillInfoCard", transform, new Color(0.06f, 0.06f, 0.08f, 0.97f));
        tipRoot = img.gameObject;
        tipRT = img.rectTransform;
        tipRT.anchorMin = tipRT.anchorMax = new Vector2(0.5f, 0.5f);
        tipRT.pivot = new Vector2(0.5f, 0f);   // grows upward from the point above the cell
        tipRT.sizeDelta = new Vector2(288f, 120f);   // width fixed; height is driven by the fitter below
        img.raycastTarget = false;

        var vlg = tipRoot.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 8, 8);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        var fit = tipRoot.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        tipText = NewText("Text", tipRoot.transform, "", 12, FontStyle.Normal, TextAnchor.UpperLeft, Color.white);
        tipText.richText = true;
        tipText.textWrappingMode = TMPro.TextWrappingModes.Normal;

        tipIcon = NewImage("Icon", tipRoot.transform, Color.white);
        tipIcon.preserveAspect = true; tipIcon.raycastTarget = false;
        var le = tipIcon.gameObject.AddComponent<LayoutElement>(); le.ignoreLayout = true;   // pinned, not in the flow
        var irt = tipIcon.rectTransform;
        irt.anchorMin = irt.anchorMax = new Vector2(1f, 1f); irt.pivot = new Vector2(1f, 1f);
        irt.anchoredPosition = new Vector2(-8f, -8f); irt.sizeDelta = new Vector2(34f, 34f);

        tipRoot.SetActive(false);
    }

    // Bind a cell's hover to the shared card: populate from the skill's def + position over THIS cell on enter, hide on exit.
    void AttachSkillTooltip(Transform cell, SkillDef def)
    {
        if (def == null) return;
        var captured = def;
        var cellRT = (RectTransform)cell;
        var trig = cell.gameObject.GetComponent<EventTrigger>() ?? cell.gameObject.AddComponent<EventTrigger>();
        AddTrigger(trig, EventTriggerType.PointerEnter, () => ShowSkillTip(cellRT, captured));
        AddTrigger(trig, EventTriggerType.PointerExit,  HideTip);
    }

    void ShowSkillTip(RectTransform cellRT, SkillDef def)
    {
        if (def == null) return;
        EnsureSharedTooltip();
        tipText.text = SkillTooltip.Build(def);
        if (tipIcon != null) { tipIcon.sprite = def.icon; tipIcon.enabled = def.icon != null; }
        tipRoot.transform.SetAsLastSibling();   // above every cell — the fix for "sometimes it doesn't show"
        tipRoot.SetActive(true);

        var canvas = GetComponentInParent<Canvas>();
        Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
        cellRT.GetWorldCorners(cornerBuf);                       // [1]=top-left, [2]=top-right
        Vector3 topCenter = (cornerBuf[1] + cornerBuf[2]) * 0.5f;
        Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, topCenter);
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, screen, cam, out var local))
            tipRT.anchoredPosition = local + new Vector2(0f, 8f);
    }

    void HideTip() { if (tipRoot != null) tipRoot.SetActive(false); }

    static void AddTrigger(EventTrigger trig, EventTriggerType type, System.Action fn)
    {
        var e = new EventTrigger.Entry { eventID = type };
        e.callback.AddListener(_ => fn());
        trig.triggers.Add(e);
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
                s.cell.color = aff == 1 ? CellReady : CellDim;
                if (s.icon != null) s.icon.color = aff == 1 ? CellReady : CellDim;
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
