using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the on-screen action bar (the <c>HUD_Hotbar</c> holder baked by <see cref="UIShellBuilder"/>) and is the
/// CENTRAL key router for it. Three rows of nine slots (<see cref="HotbarBindings"/>): the first row answers F1–F9,
/// the second 1–9, the third whatever keys the player gave it in Settings (<see cref="GameKeys.HotbarKey"/>). Each slot
/// shows what was put there — a skill (symbol, MP cost, cooldown sweep and countdown, the shared cooldown included for
/// ultimates), a usable item (its name and how many are carried), one exact piece of gear (its name), or nothing.
/// Anything can be dragged onto any slot from the Skills (K) window or the bag; a slot is pressed by its key or a click.
///
/// <para>Pressing a skill casts it; an item is used through the bag's own door (<see cref="ItemUse"/>); a piece of gear
/// is equipped through the bag's own equip. The server decides all three. A passive never
/// goes on the bar: it is never pressed, so the Skills window will not drag one and the server refuses one.</para>
///
/// Lives on the persistent GameUICanvas. Builds once (retries until the player + holder exist), refreshes the cheap
/// per-frame bits, and fully rebuilds when the bar or the bag changes (<see cref="RebuildRequested"/>).
/// </summary>
public class HotbarPanel : MonoBehaviour
{
    // What a slot holds, so a press knows which door to use.
    enum Kind { Empty, Skill, Item, Gear }

    // One decorated cell. Cached colour/cooldown state lets Refresh skip work (and string allocs) when nothing visible
    // changed — important since Refresh runs every frame.
    class Slot
    {
        public int index;
        public Kind kind;
        public IHotbarSkill skill;      // Kind.Skill
        public string itemId;           // Kind.Item
        public bool itemNoneLeft;       // Kind.Item with none carried: the slot is kept, drawn red
        public string copyId;           // Kind.Gear
        public Image cell, cooldown;
        public TMP_Text mp, label, cdNum;
        public GameObject hideOnCd;     // the center label to hide while counting down
        public int lastSecs = -1;
        public sbyte lastOnCd = -1, lastAff = -1;
        public Color weaponColor = CellReady;   // the cell's own colour; affordability DIMS this, never replaces it
    }

    static readonly Color CellReady = Color.white;

    /// <summary>A cell you cannot afford: its OWN colour, darkened. Dimming rather than replacing is what keeps the
    /// weapon readable when the skill is greyed out.</summary>
    static Color Dim(Color c) => new Color(c.r * 0.45f, c.g * 0.45f, c.b * 0.5f, c.a);
    static readonly Color CellEmpty  = UiKit.SlotEmpty;   // unbound slot
    static readonly Color KeyColor   = UiKit.TextOnDim;
    static readonly Color MpColor     = new Color(0.6f, 0.78f, 1f);
    static readonly Color MpNoColor   = new Color(1f, 0.5f, 0.45f);
    static readonly Color CdShade     = new Color(0f, 0f, 0f, 0.55f);

    // An item's or a piece's name on a tile: as large as fits, never below a size that can still be read, and cut with
    // "…" once even that does not fit the square.
    const float NameMinSize = 9f, NameMaxSize = 12f;

    // Pre-baked second strings so the countdown repaint never allocates (the shared cooldown runs to 40).
    static readonly string[] Secs = BuildSecs();
    static string[] BuildSecs() { var a = new string[61]; for (int i = 0; i < a.Length; i++) a[i] = i.ToString(); return a; }

    /// <summary>Invoke to rebuild the bar (e.g. after a binding change). Set by the live HotbarPanel.</summary>
    public static System.Action RebuildRequested;

    Sprite whiteSprite;
    Player3DController player;
    Transform holder;
    readonly List<Slot> slots = new List<Slot>();
    bool built;
    bool cellsEnsured;

    void Start()
    {
        RebuildRequested = Rebuild;
        // Ensure the buff bar exists even on a GameUICanvas built BEFORE it was added to the shell (so it appears
        // without a Build UI Shell rebuild). Idempotent — skips if the shell already added one.
        if (GetComponent<BuffBarPanel>() == null) gameObject.AddComponent<BuffBarPanel>();
        // The shop window — added the same way, so a HUD built before shops existed still has one. Idempotent.
        if (GetComponent<ShopPanel>() == null) gameObject.AddComponent<ShopPanel>();
        // The Keeper's window — added the same way. Idempotent.
        if (GetComponent<KeeperPanel>() == null) gameObject.AddComponent<KeeperPanel>();
        // The Elite Summoner's window — added the same way. Idempotent.
        if (GetComponent<SummonerPanel>() == null) gameObject.AddComponent<SummonerPanel>();
        // The stamina gauge — added the same way so it appears without a shell rebuild. Idempotent.
        if (GetComponent<StaminaBar>() == null) gameObject.AddComponent<StaminaBar>();
        // The kill combo and the per-hour rates on the left edge — added the same way. Idempotent.
        if (GetComponent<KillComboHud>() == null) gameObject.AddComponent<KillComboHud>();
        // The Auto-Combat window (opened from the System window) — added the same way. Idempotent.
        if (GetComponent<AutoCombatWindow>() == null) gameObject.AddComponent<AutoCombatWindow>();
        // The Emblem window (opened from the System window) — added the same way. Idempotent.
        if (GetComponent<EmblemWindow>() == null) gameObject.AddComponent<EmblemWindow>();
        TryBuild();
    }

    void Update()
    {
        if (!built) { TryBuild(); return; }
        HandleHotkeys();
        Refresh();
    }

    // Central key router: each slot's key presses that slot. Keys aren't blocked by mouse-over-UI, so hovering the bar
    // doesn't swallow them.
    void HandleHotkeys()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return;
        for (int i = 0; i < slots.Count; i++)
        {
            var key = GameKeys.HotbarKey(slots[i].index);
            if (key != KeyCode.None && Hotkeys.Down(key)) Press(slots[i]);
        }
    }

    // ONE press, from a key or a click, so the two can never do different things.
    static void Press(Slot s)
    {
        var me = Character.Instance;
        if (s == null || me == null) return;
        switch (s.kind)
        {
            case Kind.Skill:
                if (s.skill != null && s.skill.Activate()) UISounds.PlayClick();
                return;
            case Kind.Item:
            {
                // Through the bag's own door, by where the stack sits in the bag — the server reads it there.
                var stack = me.CarriedStack(s.itemId);
                if (stack != null && ItemUse.Use(stack.def, me.Bag.IndexOf(stack))) UISounds.PlayClick();
                return;
            }
            case Kind.Gear:
            {
                if (!me.FindCopy(s.copyId, out _, out bool worn)) return;
                if (worn) { OverheadNotice.Warn("Already equipped"); return; }
                var net = GameNetwork.MyPlayer;
                if (net != null) { net.AskToEquipCopy(s.copyId); UISounds.PlayClick(); }
                return;
            }
        }
    }

    /// <summary>Tear down the decorated cells and rebuild from the player's current bar. Safe before the first build.</summary>
    public void Rebuild()
    {
        if (holder != null)
            for (int i = 0; i < holder.childCount; i++)
            {
                var cell = holder.GetChild(i);
                for (int c = cell.childCount - 1; c >= 0; c--) Destroy(cell.GetChild(c).gameObject);
                // THE BUTTON AND THE HOVER STAY, and are re-pointed when the cell is built again. Destroying them here
                // and building in the same frame would hand the new cell the component already on its way out — a
                // cell that can no longer be clicked until the next rebuild.
                HoverRelay.Set(cell.gameObject, null, null);
                var box = cell.GetComponent<ItemSlot>();
                if (box != null) box.SetItem(null);
                var skillCard = cell.GetComponent<SkillRightClick>();
                if (skillCard != null) skillCard.def = null;
            }
        slots.Clear();
        SkillHoverCard.Hide();   // the hovered cell may be torn down — don't leave a stale tooltip floating
        ItemTooltip.HideTip();
        built = false;   // Update() will TryBuild() again next frame, reading the new bar
    }

    void TryBuild()
    {
        if (player == null) player = Object.FindAnyObjectByType<Player3DController>();
        if (player == null) return;
        if (holder == null) holder = FindByName("HUD_Hotbar");
        if (holder == null) return;
        if (Character.Instance == null) return;   // need the bar

        if (whiteSprite == null)
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

        EnsureCells();

        slots.Clear();
        int n = Mathf.Min(HotbarBindings.SlotCount, holder.childCount);
        for (int i = 0; i < n; i++)
        {
            var cell = holder.GetChild(i).GetComponent<Image>();
            if (cell != null) slots.Add(BuildSlot(cell, i));
        }
        built = slots.Count > 0;
    }

    const float CellSize = 48f, CellGap = 6f;

    // WHERE THE BAR SITS, bottom-centre, and how tall it is — owned here, so everything stacked above it (the stamina
    // gauge) is placed from the same numbers rather than from a height typed for a one-row bar.
    const float BarBottom = 42f;
    static float BarHeight => HotbarBindings.Rows * (CellSize + CellGap) - CellGap;

    /// <summary>The height above the screen's bottom edge where the bar ends — the first free line
    /// for anything that stacks above it.</summary>
    public static float TopOfStack => BarBottom + BarHeight + 6f;

    // The shell bakes the cells with NO layout on them; the bar is three rows of nine. Lay the holder out as that grid
    // and clone the (pristine) first cell up to 27, at runtime — so an older bake with fewer cells still gets the full bar.
    // Runs once.
    void EnsureCells()
    {
        if (cellsEnsured || holder == null) return;
        cellsEnsured = true;
        int have = holder.childCount;
        if (have == 0) return;
        var template = holder.GetChild(0).gameObject;
        int wanted = HotbarBindings.SlotCount;

        for (int i = have - 1; i >= wanted; i--)
        {
            var extra = holder.GetChild(i).gameObject;
            extra.SetActive(false);
            Destroy(extra);
        }
        for (int i = have; i < wanted; i++)
        {
            var clone = Instantiate(template, holder);
            clone.name = "Hotslot";
            for (int c = clone.transform.childCount - 1; c >= 0; c--) Destroy(clone.transform.GetChild(c).gameObject);
            var b = clone.GetComponent<Button>();         if (b != null) Destroy(b);
            var t = clone.GetComponent<HoverRelay>();      if (t != null) Destroy(t);
            var s = clone.GetComponent<HotbarSlotTarget>();if (s != null) Destroy(s);
            var d = clone.GetComponent<HotbarSlotDragSource>(); if (d != null) Destroy(d);
        }
        // EVERY CELL THE SAME SIZE: a scene baked when the ult had a bigger seat of its own still carries that size.
        for (int i = 0; i < holder.childCount && i < wanted; i++)
            if (holder.GetChild(i) is RectTransform crt) crt.sizeDelta = new Vector2(CellSize, CellSize);

        // A GRID OF NINE ACROSS, first row on top, added at runtime only: a layout saved into the HUD rearranges the
        // cells in the editor too and marks the scene modified (tools/check-scene-stays-clean.py). An older bake's
        // row layout is removed first.
        var row = holder.GetComponent<HorizontalLayoutGroup>();
        if (row != null) DestroyImmediate(row);
        var grid = holder.GetComponent<GridLayoutGroup>() ?? holder.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(CellSize, CellSize);
        grid.spacing = new Vector2(CellGap, CellGap);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = HotbarBindings.PerRow;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.MiddleCenter;

        if (holder is RectTransform rt)
        {
            rt.sizeDelta = new Vector2(HotbarBindings.PerRow * (CellSize + CellGap) - CellGap, BarHeight);
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, BarBottom);
        }
    }

    Slot BuildSlot(Image cell, int index)
    {
        var me = Character.Instance;
        string entry = me.Hotbar.Get(index);
        var s = new Slot { index = index, cell = cell };

        // Every cell is a drop target (drag a skill, an item or a piece onto it) and a button (click = press it).
        var target = cell.gameObject.GetComponent<HotbarSlotTarget>() ?? cell.gameObject.AddComponent<HotbarSlotTarget>();
        target.slotIndex = index;
        cell.raycastTarget = true;

        var btn = cell.gameObject.GetComponent<Button>() ?? cell.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;
        btn.targetGraphic = cell;
        btn.onClick.RemoveAllListeners();
        var captured = s;
        btn.onClick.AddListener(() => Press(captured));

        // The slot's key (top-left), always shown — the third row's only once the player has given it one.
        var key = NewText("Key", cell.transform, GameKeys.Label(GameKeys.HotbarKey(index)), 11, FontStyle.Bold,
                          TextAnchor.UpperLeft, KeyColor);
        Stretch(key.rectTransform, 3, 2);

        string skillId = HotbarEntry.SkillOf(entry);
        if (skillId.Length > 0) BuildSkill(s, skillId);
        else if (HotbarEntry.IsItem(entry, out string itemId)) BuildItem(s, itemId);
        else if (HotbarEntry.IsGear(entry, out string copyId)) BuildGear(s, copyId);

        if (s.kind == Kind.Empty)
        {
            cell.color = CellEmpty;   // dim, nothing drawable on it → reads as an open slot
            // SOMETHING THAT CANNOT BE DRAWN YET still sits in the slot, so it can still be dragged off; only a truly
            // empty cell has nothing to drag.
            var staleDrag = cell.gameObject.GetComponent<HotbarSlotDragSource>();
            if (string.IsNullOrEmpty(entry)) { if (staleDrag != null) Destroy(staleDrag); }
            else
            {
                var keep = staleDrag != null ? staleDrag : cell.gameObject.AddComponent<HotbarSlotDragSource>();
                keep.slotIndex = index;
            }
            return s;
        }

        // Drag this filled slot onto another to rearrange the bar, or off the bar to empty it (a click still presses).
        var drag = cell.gameObject.GetComponent<HotbarSlotDragSource>() ?? cell.gameObject.AddComponent<HotbarSlotDragSource>();
        drag.slotIndex = index;
        // An item or a piece sits on a white tile, where the white key letter would vanish; it takes the name's colour.
        if (s.kind == Kind.Item || s.kind == Kind.Gear) key.color = s.itemNoneLeft ? UiKit.TextOnDim : UiKit.TextMain;
        key.transform.SetAsLastSibling();
        return s;
    }

    void BuildSkill(Slot s, string skillId)
    {
        var skill = PlayerSkills.ById(skillId);
        if (skill == null) return;
        s.kind = Kind.Skill;
        s.skill = skill;
        var cell = s.cell;

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
        s.weaponColor = skill.SymbolColor;
        cell.color = s.weaponColor;
        s.label = NewText("Label", cell.transform, skill.Symbol, 18, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Stretch(s.label.rectTransform, 2, 2);
        s.hideOnCd = s.label.gameObject;

        // MP cost (bottom-right) for a skill that costs mana.
        if (skill.ManaCost > 0)
        {
            s.mp = NewText("Mp", cell.transform, skill.ManaCost.ToString(), 10, FontStyle.Bold, TextAnchor.LowerRight, MpColor);
            Stretch(s.mp.rectTransform, 2, 3);
        }

        // Countdown number (center), shown only while on cooldown.
        s.cdNum = NewText("CdNum", cell.transform, "", 18, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Stretch(s.cdNum.rectTransform, 2, 2);
        s.cdNum.gameObject.SetActive(false);

        // Skill-info hover card (tags, cost, description, effects, levels).
        if (skill is SkillRunner sr && sr.def != null) SkillHoverCard.Attach(cell.transform, sr.def);
    }

    void BuildItem(Slot s, string itemId)
    {
        var stack = Character.Instance.CarriedStack(itemId);
        // NONE LEFT: the item keeps its slot, drawn red with a 0, until the next one is picked up.
        var def = stack != null ? stack.def : ItemCatalog.Resolve(itemId);
        if (def == null || !def.IsUsable) return;
        s.kind = Kind.Item;
        s.itemId = itemId;
        s.itemNoneLeft = stack == null;
        s.cell.color = s.itemNoneLeft ? UiKit.Bad : ItemIcon.Back(def, null);
        // Its picture, as the bag draws it; only a thing with no picture writes its name.
        if (!ItemIcon.Show(ItemIcon.Make(s.cell.transform, 3f), def))
        {
            var name = NameOnTile(s.cell.transform, def.displayName);
            if (s.itemNoneLeft) name.color = UiKit.TextOnDim;
        }
        // How many are carried (bottom-right), exactly as the bag the server sent says.
        var count = NewText("Count", s.cell.transform, s.itemNoneLeft ? "0" : stack.count.ToString(), 11, FontStyle.Bold,
                            TextAnchor.LowerRight, UiKit.TextOnDim);
        UiKit.StyleItemCount(count);
        Stretch(count.rectTransform, 3, 2);
        // Hover: its name. Right-click: its card — the same box behaviour as the bag's.
        var box = ItemBoxOn(s.cell.gameObject);
        box.SetItem(def);
        box.shownCount = s.itemNoneLeft ? 0 : stack.count;
        box.previewOnHover = false;
    }

    void BuildGear(Slot s, string copyId)
    {
        var me = Character.Instance;
        if (!me.FindCopy(copyId, out int bagIndex, out bool worn)) return;
        EquipmentInstance inst = null;
        if (!worn) inst = me.Bag[bagIndex].instance;
        else
            foreach (var kv in me.Gear.Worn)
                if (kv.Value != null && kv.Value.copyId == copyId) { inst = kv.Value; break; }
        if (inst == null || inst.def == null) return;
        s.kind = Kind.Gear;
        s.copyId = copyId;
        s.cell.color = ItemIcon.Back(inst.def, inst);
        // Its picture, as the bag draws it; with none, the copy's own name ("+15 Petal Storm [C]"), not the bare design's.
        if (!ItemIcon.Show(ItemIcon.Make(s.cell.transform, 3f), inst.def))
            NameOnTile(s.cell.transform, ItemTooltip.CopyTitle(inst));
        GradeGlow.Show(GradeGlow.MakeForSquare(s.cell.transform), inst);
        // The same tooltip its row shows: in the bag, the equip preview; worn, the plain card.
        // Hover: its name. Right-click: its card — in the bag, with the "if equipped" lines; worn, the plain card.
        var box = ItemBoxOn(s.cell.gameObject);
        box.SetInstance(inst);
        box.previewOnHover = !worn;
        box.previewSlot = inst.def.slot;
    }

    // The item box behaviour on a tile, kept on the tile across rebuilds as its button is.
    static ItemSlot ItemBoxOn(GameObject cell)
    {
        var box = cell.GetComponent<ItemSlot>();
        if (box == null) box = cell.AddComponent<ItemSlot>();
        return box;
    }

    // A name on a light tile: largest size that fits between NameMinSize and NameMaxSize, then cut with "…".
    TMP_Text NameOnTile(Transform cell, string name)
    {
        var t = UiKit.MakeText("Name", cell, name, NameMaxSize, FontStyle.Bold, TextAnchor.MiddleCenter, wrap: true);
        t.color = UiKit.TextMain;
        t.raycastTarget = false;
        t.richText = false;
        t.enableAutoSizing = true;
        t.fontSizeMin = NameMinSize;
        t.fontSizeMax = NameMaxSize;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.rectTransform.anchorMin = Vector2.zero;
        t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = new Vector2(3, 12);
        t.rectTransform.offsetMax = new Vector2(-3, -12);
        return t;
    }

    void Refresh()
    {
        bool ringOn = SkillsEnabled;   // hoist once: no character → the whole bar reads as suppressed
        for (int i = 0; i < slots.Count; i++) RefreshSlot(slots[i], ringOn);
    }

    /// <summary>True when skills may fire — a bound skill just works; whether it is learned and its weapon is in hand
    /// is the cast's own test.</summary>
    static bool SkillsEnabled => Character.Instance != null;

    // ONE per-cell repaint for every skill cell. An ultimate's countdown already shows the shared cooldown when that
    // is the longer of the two (SkillRunner.CooldownRemaining), so no cell needs a rule of its own.
    void RefreshSlot(Slot s, bool ringOn)
    {
        if (s == null || s.kind != Kind.Skill || s.skill == null || s.cooldown == null) return;

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
