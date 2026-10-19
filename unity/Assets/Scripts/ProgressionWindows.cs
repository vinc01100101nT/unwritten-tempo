using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Builds and drives the Inventory and Character windows from the locked HUD spec, wired to the real
/// <see cref="Character"/> + <see cref="InventoryStore"/>.
/// Toggle keys: B = Inventory (what you wear, on your body, beside what you carry), C = Character, Esc = close all. Each window self-builds
/// at Start into a <see cref="UiKit.Window"/>; the shared <see cref="ItemTooltip"/> handles hover
/// (incl. the live equip→stat-delta preview). Lives on its own canvas (the builder sets it up).
/// </summary>
[RequireComponent(typeof(InventoryStore))]
public class ProgressionWindows : MonoBehaviour
{
    Transform canvas;
    GameObject invRoot, charRoot;

    // Inventory: what you carry (InventoryList, a grid of boxes), with what you wear (EquipmentPanel) in a see-through
    // frame on its left where the camera stands your own character. Every box hands hover, clicks and drags to ItemSlot and SlotDragHandler, the same code the rows used.
    readonly InventoryList bagList = new InventoryList();
    readonly EquipmentPanel equipment = new EquipmentPanel();
    readonly List<BagEntry> filtered = new List<BagEntry>();
    bool useFilter; ItemCategory tab; // useFilter=false => "All"
    TMP_Text invFooter;
    readonly List<Button> tabButtons = new List<Button>();
    readonly List<(bool filtered, ItemCategory cat)> tabFor = new List<(bool, ItemCategory)>();
    bool invWasOpen;

    // Character
    static ProgressionWindows active;   // singleton guard (avoid a doubled build if two canvases carry it)
    static readonly PrimaryStat[] StatOrder =
        { PrimaryStat.STR, PrimaryStat.AGI, PrimaryStat.VIT, PrimaryStat.INT, PrimaryStat.DEX, PrimaryStat.LUK };
    TMP_Text charIdentity, charLevel;
    // the derived numbers: the full-width lines, then the left and the right of the two-to-a-line block
    readonly TMP_Text[] charDerived = new TMP_Text[3];
    readonly TMP_Text[] statValue = new TMP_Text[6];
    readonly TMP_Text[] statCost = new TMP_Text[6];
    readonly Button[] statPlus = new Button[6];

    void Start()
    {
        if (active != null && active != this) { enabled = false; return; }
        active = this;
        canvas = GetComponentInParent<Canvas>() != null ? GetComponentInParent<Canvas>().transform : transform;
        ResetDockedWindowPositionsOnce();   // must run BEFORE the windows build (WindowFrame loads its saved pos on enable)
        ResetCharacterPositionOnce();
        ResetInventoryPositionOnce();
        EnsureTooltip();
        BuildInventory();
        BuildCharacter();

        if (Character.Instance != null) Character.Instance.OnStatsChanged += RefreshAll;
        if (InventoryStore.Instance != null) InventoryStore.Instance.OnChanged += RefreshAll;
        Wallet.OnChanged += OnGold;
        RefreshAll();
    }

    void OnDestroy()
    {
        if (active == this) active = null;
        if (Character.Instance != null) Character.Instance.OnStatsChanged -= RefreshAll;
        if (InventoryStore.Instance != null) InventoryStore.Instance.OnChanged -= RefreshAll;
        Wallet.OnChanged -= OnGold;
        equipment.Dispose();
    }

    // The bag window now has the equipment frame hanging off its left, so a position saved without it can leave the
    // frame off screen. Cleared once; drags after that persist normally.
    static void ResetInventoryPositionOnce()
    {
        if (PlayerPrefs.HasKey("pw_bag_grid_v1")) return;
        PlayerPrefs.DeleteKey("win_Inventory_x"); PlayerPrefs.DeleteKey("win_Inventory_y");
        PlayerPrefs.SetInt("pw_bag_grid_v1", 1);
    }

    // One-time move: the bag is now a single centred window holding what you carry AND what you wear, so a
    // position saved when it sat off to one side would drop it back there. Clear it once; drags after that
    // persist normally. The Equipment keys are deleted because that window no longer exists.
    // WindowFrame keys are "win_<title>_x/y".
    static void ResetDockedWindowPositionsOnce()
    {
        if (PlayerPrefs.HasKey("pw_dock_v2")) return;
        PlayerPrefs.DeleteKey("win_Inventory_x"); PlayerPrefs.DeleteKey("win_Inventory_y");
        PlayerPrefs.DeleteKey("win_Equipment_x"); PlayerPrefs.DeleteKey("win_Equipment_y");
        PlayerPrefs.SetInt("pw_dock_v2", 1);
    }

    // The Character window more than doubled in width when the ring moved into it, so a position saved for the old
    // narrow window can leave half of it off screen. Clear it once; drags after that persist normally.
    static void ResetCharacterPositionOnce()
    {
        if (PlayerPrefs.HasKey("pw_char_ring_v1")) return;
        PlayerPrefs.DeleteKey("win_Character_x"); PlayerPrefs.DeleteKey("win_Character_y");
        PlayerPrefs.SetInt("pw_char_ring_v1", 1);
    }

    void OnGold(long _) => RefreshInventoryFooter();

    void Update()
    {
        // Bare-key window hotkeys (no Alt — the Unity/OS Alt menu accelerators interfere with it):
        //   B = Bag (what you carry, with what you wear beside it) · C = Character, the Ring of Progression included
        //   (K = Skills, owned by SkillsWindow). V is the Sprint key (Player3DController).
        if (Hotkeys.Down(GameKeys.Inventory)) UiAnim.Toggle(invRoot);
        if (Hotkeys.Down(GameKeys.CharacterWindow)) UiAnim.Toggle(charRoot);
        if (Hotkeys.CancelDown() && AnyWindowOpen())
        {
            UiAnim.Close(invRoot); UiAnim.Close(charRoot);
            Hotkeys.MarkCancelHandled();   // this Escape is spent — it must not also pop the System window
        }

        // THE BODY IS UP ONLY WHILE THE BAG IS: however the window opened or closed (its key, its X, Escape, a menu).
        bool invOpen = UiAnim.IsOpen(invRoot);
        if (invOpen != invWasOpen)
        {
            invWasOpen = invOpen;
            if (invOpen) equipment.Opened(); else equipment.Closed();
        }
        equipment.Tick();
        KeepDerivedLive();
    }

    bool AnyWindowOpen() => UiAnim.IsOpen(invRoot) || UiAnim.IsOpen(charRoot);

    // Keep the Character sheet honest while a buff runs, expires, or ramps. Only while the window is actually OPEN, and
    // the text is rebuilt ONLY when a value really moved — the per-frame cost is a struct build + an int compare, no
    // string work and no allocation (PERFORMANCE.md: nothing per-frame may allocate).
    DerivedSnapshot lastLive;
    bool lastLiveValid;
    int lastSta = -1;   // stamina drains/fills outside the snapshot, so it's watched separately
    void KeepDerivedLive()
    {
        if (charRoot == null || !UiAnim.IsOpen(charRoot) || Character.Instance == null) return;
        var c = Character.Instance;
        var now = c.DerivedWithStates;
        // Compare the DISPLAYED (rounded) resource values, not the raw floats — otherwise a draining stamina bar would
        // rebuild the text every single frame instead of only when the number on screen actually changes.
        int sta = Mathf.RoundToInt(c.Stamina);
        if (lastLiveValid && sta == lastSta && DerivedSnapshot.Same(now, lastLive)) return;
        lastLive = now; lastLiveValid = true; lastSta = sta;
        RefreshCharacter();
    }

    void RefreshAll()
    {
        RefreshInventory();
        RefreshCharacter();
    }

    // ---- Inventory window -----------------------------------------------------

    // The bag window's parts, from the top-left of its content: the tabs, search, sort and grid, then the footer. The
    // grid's scroll lane reaches into the window's right margin.
    const float TabHeight = 20f, TabGap = 4f, FooterHeight = 18f;
    const float BagColumnX = 0f;
    static float BodyHeight => TabHeight + TabGap + InventoryList.Height;

    void BuildInventory()
    {
        // The window's frame leaves 32px of width and 50px of height around its content (UiKit.Window).
        var size = new Vector2(ItemGrid.Width + 32f, BodyHeight + 4f + FooterHeight + 50f);
        var content = UiKit.Window(canvas, "Inventory", size, new Vector2(0, 0), out invRoot);

        equipment.Build((RectTransform)invRoot.transform);

        // tabs, over the bag only
        var tabRow = UiKit.TabRow(content, TabHeight);
        tabRow.anchorMin = tabRow.anchorMax = new Vector2(0, 1);
        tabRow.pivot = new Vector2(0, 1);
        tabRow.sizeDelta = new Vector2(ItemGrid.Width, TabHeight);
        tabRow.anchoredPosition = new Vector2(BagColumnX, 0);
        AddTab(tabRow.transform, "All", false, default);
        AddTab(tabRow.transform, "Equip", true, ItemCategory.Equip);
        AddTab(tabRow.transform, "Use", true, ItemCategory.Use);
        AddTab(tabRow.transform, "Material", true, ItemCategory.Material);
        AddTab(tabRow.transform, "Etc", true, ItemCategory.Etc);

        // search, sort and the grid
        bagList.Build(content, new Vector2(BagColumnX, -(TabHeight + TabGap)));

        // footer
        invFooter = UiKit.Label("Footer", content, "", 12, FontStyle.Bold, TextAnchor.MiddleRight);
        invFooter.rectTransform.anchorMin = new Vector2(0, 0);
        invFooter.rectTransform.anchorMax = new Vector2(1, 0);
        invFooter.rectTransform.pivot = new Vector2(0.5f, 0);
        invFooter.rectTransform.sizeDelta = new Vector2(0, FooterHeight);
        UiKit.Paint(invFooter, UiKit.TextMain);
    }

    void AddTab(Transform parent, string label, bool filtered, ItemCategory cat)
    {
        var btn = UiKit.Btn(label, parent, () => { useFilter = filtered; tab = cat; RefreshInventory(); bagList.ToTop(); });
        btn.GetComponentInChildren<TMP_Text>().fontSize = 11;
        tabButtons.Add(btn);
        tabFor.Add((filtered, cat));
    }

    void RefreshInventory()
    {
        if (InventoryStore.Instance == null) return;
        filtered.Clear();
        // What is worn is on the body, in the equipment panel — not in the bag.
        foreach (var e in InventoryStore.Instance.Entries)
            if (!useFilter || (e.def != null && e.def.category == tab))
                filtered.Add(e);

        for (int i = 0; i < tabButtons.Count; i++)
            UiKit.MarkTab(tabButtons[i], tabFor[i].filtered == useFilter && (!useFilter || tabFor[i].cat == tab));
        bagList.Refresh(filtered, useFilter);
        equipment.Refresh();
        RefreshInventoryFooter();
    }

    void RefreshInventoryFooter()
    {
        if (invFooter == null) return;
        // Every figure here is the server's, copied (BagWire): what is carried, the most that can be, and how full the
        // tab on show is. "All" has no limit of its own, so it shows none.
        var who = Character.Instance;
        int w = who != null ? who.CarriedWeight : 0;
        int cap = who != null ? who.WeightCap : 0;
        string slots = useFilter && who != null
            ? $"<color={DimHex}>Slots</color> {who.SlotsUsed(tab)}/{BagRoom.SlotsPerCategory}        " : "";
        invFooter.text = $"{slots}<color={DimHex}>Weight</color> {w}/{cap}        {Wallet.Amount(Wallet.Gold)}";
    }
    // ---- Character window -----------------------------------------------------

    const float StatColumnW = 248f, ColumnGap = 14f, RingColumnW = 274f;
    const float StatRowHeight = 20f, DerivedTextSize = 11f;

    // The derived block's labels. The attack lines and CRIT each take a whole line: with a buff running they carry
    // bracketed extras that do not fit in half the column. Everything after them sits two to a line, so the
    // two-to-a-line texts open with as many empty lines as there are whole ones above them.
    const string DerivedFullLabels = "MELEE ATK\nRANGED ATK\nM.ATK\nCRIT";
    const string DerivedSkip = "\n\n\n\n";
    const string DerivedLeftLabels = DerivedSkip + "DEF\nHIT\nASPD\nMOVE\nGUARD\nLEECH\nREFLECT\nHP\nSTA";
    const string DerivedRightLabels = DerivedSkip + "M.DEF\nFLEE\nCAST\nRUN\nBARRIER\nMP LEECH\nRANGE\nMP";

    /// <summary>The Character window's right-hand column, where <see cref="SkillDraftPanel"/> draws the Ring of
    /// Progression. Null until the window is built.</summary>
    public static RectTransform RingColumn { get; private set; }

    /// <summary>The Character window itself (C), so the ring can tell when it is on screen.</summary>
    public static GameObject CharacterRoot { get; private set; }

    void BuildCharacter()
    {
        // Two columns: the stat sheet on the left, the Ring of Progression on the right (SkillDraftPanel builds
        // into RingColumn). The ring is drawn as a circle 274 wide, so the right column is sized for it; the window's
        // frame takes 32 of its width and 50 of its height.
        var content = UiKit.Window(canvas, "Character", new Vector2(StatColumnW + ColumnGap + RingColumnW + 32, 400),
                                   new Vector2(0, 0), out charRoot);
        CharacterRoot = charRoot;

        var col = UiKit.Img("Col", content, new Color(0, 0, 0, 0));
        col.raycastTarget = false;
        var colRt = col.rectTransform;
        colRt.anchorMin = new Vector2(0, 0); colRt.anchorMax = new Vector2(0, 1); colRt.pivot = new Vector2(0, 0.5f);
        colRt.sizeDelta = new Vector2(StatColumnW, 0); colRt.anchoredPosition = Vector2.zero;

        var split = UiKit.Img("ColumnDivider", content, UiKit.Rule);
        split.raycastTarget = false;
        var splitRt = split.rectTransform;
        splitRt.anchorMin = new Vector2(0, 0); splitRt.anchorMax = new Vector2(0, 1); splitRt.pivot = new Vector2(0.5f, 0.5f);
        splitRt.sizeDelta = new Vector2(2, 0); splitRt.anchoredPosition = new Vector2(StatColumnW + ColumnGap * 0.5f, 0);

        var ring = UiKit.Img("RingColumn", content, new Color(0, 0, 0, 0));
        var ringRt = ring.rectTransform;
        ringRt.anchorMin = new Vector2(0, 0); ringRt.anchorMax = new Vector2(1, 1);
        ringRt.offsetMin = new Vector2(StatColumnW + ColumnGap, 0); ringRt.offsetMax = Vector2.zero;
        RingColumn = ringRt;
        var vlg = col.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperLeft;

        // The identity plate: who and how many stat points on the left, level and XP on the right.
        var plate = UiKit.Framed(UiKit.Img("Identity", col.transform, UiKit.ButtonEdge), UiKit.ButtonEdge, UiKit.PanelRaise, UiKit.ButtonRadius);
        plate.raycastTarget = false;
        plate.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
        charIdentity = UiKit.MakeText("Who", plate.transform, "", 12, FontStyle.Bold, TextAnchor.MiddleLeft, null, false);
        charLevel = UiKit.MakeText("Level", plate.transform, "", 12, FontStyle.Normal, TextAnchor.MiddleRight, null, false);
        UiKit.Fill(charIdentity.rectTransform, new Vector2(6, 0), new Vector2(-6, 0));
        UiKit.Fill(charLevel.rectTransform, new Vector2(6, 0), new Vector2(-6, 0));
        charIdentity.raycastTarget = charLevel.raycastTarget = false;

        // Six allocatable stat rows: value · cost of the next point · + button.
        var stats = UiKit.Img("Stats", col.transform, new Color(0, 0, 0, 0));
        stats.raycastTarget = false;
        // Pinned: a row of stretching children would otherwise claim the room the derived numbers need.
        stats.gameObject.AddComponent<LayoutElement>().flexibleHeight = 0;
        var sv = stats.gameObject.AddComponent<VerticalLayoutGroup>();
        sv.spacing = 2;
        sv.childControlWidth = sv.childControlHeight = true;
        sv.childForceExpandWidth = true; sv.childForceExpandHeight = false;
        for (int i = 0; i < StatOrder.Length; i++) BuildStatRow(stats.transform, i);

        var div = UiKit.Img("Div", col.transform, UiKit.Rule);
        div.gameObject.AddComponent<LayoutElement>().preferredHeight = 2;

        // The derived numbers: grey labels written once, values beside them right-aligned and rewritten when one moves.
        var derived = UiKit.Img("Derived", col.transform, new Color(0, 0, 0, 0));
        derived.raycastTarget = false;
        derived.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        float half = ColumnGap * 0.5f;
        DerivedText(derived.transform, 0f, 1f, 0f, 0f, false, DerivedFullLabels);
        charDerived[0] = DerivedText(derived.transform, 0f, 1f, 0f, 0f, true, "");
        DerivedText(derived.transform, 0f, 0.5f, 0f, half, false, DerivedLeftLabels);
        charDerived[1] = DerivedText(derived.transform, 0f, 0.5f, 0f, half, true, "");
        DerivedText(derived.transform, 0.5f, 1f, half, 0f, false, DerivedRightLabels);
        charDerived[2] = DerivedText(derived.transform, 0.5f, 1f, half, 0f, true, "");
    }

    // One text of the derived block, spanning part of its width: labels grey from the left, values from the right.
    static TMP_Text DerivedText(Transform parent, float xMin, float xMax, float insetLeft, float insetRight, bool values, string s)
    {
        var t = UiKit.MakeText(values ? "Values" : "Labels", parent, s, DerivedTextSize, FontStyle.Normal,
                               values ? TextAnchor.UpperRight : TextAnchor.UpperLeft, null, false);
        t.raycastTarget = false;
        if (!values) UiKit.Paint(t, UiKit.TextDim);
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(xMin, 0); rt.anchorMax = new Vector2(xMax, 1);
        rt.offsetMin = new Vector2(insetLeft, 0); rt.offsetMax = new Vector2(-insetRight, 0);
        return t;
    }

    void BuildStatRow(Transform parent, int i)
    {
        var stat = StatOrder[i];
        var row = UiKit.Img("Row_" + stat, parent, UiKit.PanelRaise);
        var rowSize = row.gameObject.AddComponent<LayoutElement>();
        rowSize.preferredHeight = StatRowHeight; rowSize.flexibleHeight = 0;
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 4; h.padding = new RectOffset(5, 3, 2, 2);
        h.childControlWidth = h.childControlHeight = true;
        // Width is NOT shared out: each cell is the width it declares and the value takes what is left.
        h.childForceExpandHeight = true; h.childForceExpandWidth = false; h.childAlignment = TextAnchor.MiddleLeft;

        // Stat Family Icon v1: a procedural emblem in the left gutter (recognition by mark, not label).
        StatGlyph.Build(row.transform, stat, 14f);

        var name = UiKit.Label("Name", row.transform, stat.ToString(), 12, FontStyle.Bold, TextAnchor.MiddleLeft);
        name.gameObject.AddComponent<LayoutElement>().preferredWidth = 32;

        statValue[i] = UiKit.Label("Val", row.transform, "1", 12, FontStyle.Normal, TextAnchor.MiddleRight);
        statValue[i].gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        statCost[i] = UiKit.Label("Cost", row.transform, "", 11, FontStyle.Normal, TextAnchor.MiddleRight);
        statCost[i].gameObject.AddComponent<LayoutElement>().preferredWidth = 26;

        // ASKED FOR IN A WORLD, where a point is real power and the server decides; done here when there is no
        // world. Spent locally in a world it was simply undone by the server's next write, within fifteen
        // seconds, with nothing saying why.
        var btn = UiKit.Btn("+", row.transform, () =>
        {
            var me = GameNetwork.MyPlayer;
            if (me != null) { me.AskToSpendStatPoint(stat); return; }
            if (Character.Instance != null) Character.Instance.AddStat(stat);
        });
        btn.gameObject.AddComponent<LayoutElement>().preferredWidth = 20;
        btn.GetComponentInChildren<TMP_Text>().fontSize = 12;
        statPlus[i] = btn;
    }

    void RefreshCharacter()
    {
        if (charIdentity == null || Character.Instance == null) return;
        var c = Character.Instance;
        // Nothing is drawn from placeholder zeros: the window fills in the moment the server's first sheet arrives.
        if (!c.SheetReady) return;
        charIdentity.text = $"<color={AccentHex}><size=13>Adventurer</size></color>\n<color={TitleHex}>Stat Points: {c.StatPoints}</color>";
        charLevel.text = $"<b>Level {c.Level}</b>\nXP {c.XP:0.00}/{c.XPToNext}";

        for (int i = 0; i < StatOrder.Length; i++)
        {
            var s = StatOrder[i];
            // The points you bought, then what your gear adds in brackets — the server's own number, copied, never summed here.
            int gear = c.GearStat(s);
            statValue[i].text = gear == 0 ? c.GetStat(s).ToString()
                                          : c.GetStat(s) + " (" + (gear > 0 ? "+" : "") + gear + ")";
            int cost = c.CostToRaise(s);
            bool capped = cost < 0;
            bool can = c.CanRaise(s);
            statCost[i].text = capped ? "MAX" : "(" + cost + ")";
            UiKit.Paint(statCost[i], capped ? UiKit.TitleText
                              : can    ? UiKit.Good
                                       : UiKit.Bad);
            statPlus[i].interactable = can;
        }

        // The number shown is the character's OWN value; whatever buffs/debuffs add or take sits in parentheses after
        // it (green = better, red = worse). Diffing the two views keeps every line honest — no line can silently
        // include a buff while its neighbour doesn't.
        var b = c.BaseDerived;
        var d = c.DerivedWithStates;
        var pc = PlayerBody();
        charDerived[0].text =
            $"{b.meleeMin}-{b.meleeMax}{Delta(d.meleeMin - b.meleeMin, d.meleeMax - b.meleeMax)}\n" +
            $"{b.rangedMin}-{b.rangedMax}{Delta(d.rangedMin - b.rangedMin, d.rangedMax - b.rangedMax)}\n" +
            $"{b.matkMin}-{b.matkMax}{Delta(d.matkMin - b.matkMin, d.matkMax - b.matkMax)}\n" +
            $"{Pct(b.critChance)}{DeltaPct(d.critChance - b.critChance)}  ×{b.critMult:0.00}{Delta(d.critMult - b.critMult)}";
        charDerived[1].text = DerivedSkip +
            $"{Tenths(b.hardDef)}{DeltaTenths(d.hardDef - b.hardDef)} + {b.def}{Delta(d.def - b.def)} ({Tenths(Combat.MaxHardDefenseTenths)})\n" +
            $"{b.hit}{Delta(d.hit - b.hit)}\n" +
            $"{b.aspd}{Delta(d.aspd - b.aspd)} ({UnitStatMath.MaxAspd})\n" +
            $"{b.moveSpeed}{Delta(d.moveSpeed - b.moveSpeed)}\n" +
            $"{Tenths(d.guardHardDef)}\n" +
            $"{Pct(b.lifeStealPct)}{DeltaPct(d.lifeStealPct - b.lifeStealPct)}\n" +
            $"{Pct(b.reflectPct)}{DeltaPct(d.reflectPct - b.reflectPct)}\n" +
            $"{c.MaxHP}\n" +
            $"{Mathf.RoundToInt(c.Stamina)}/{Mathf.RoundToInt(c.maxStamina)}";
        charDerived[2].text = DerivedSkip +
            $"{Tenths(b.hardMdef)}{DeltaTenths(d.hardMdef - b.hardMdef)} + {b.mdef}{Delta(d.mdef - b.mdef)} ({Tenths(Combat.MaxHardDefenseTenths)})\n" +
            $"{b.flee}{Delta(d.flee - b.flee)}\n" +
            $"×{b.castTimeMult:0.00}{Delta(d.castTimeMult - b.castTimeMult)}\n" +
            $"{RunSpeed(pc)}\n" +
            $"{b.barrierHP}{Delta(d.barrierHP - b.barrierHP)}\n" +
            $"{Pct(b.manaLeechPct)}{DeltaPct(d.manaLeechPct - b.manaLeechPct)}\n" +
            $"{b.basicAttackRange:0.##}{Delta(d.basicAttackRange - b.basicAttackRange)}\n" +
            $"{c.MaxMP}";
    }

    static string Pct(float f01) => Mathf.RoundToInt(f01 * 100) + "%";
    // A server-sent figure in tenths of a percent, written as a percentage: 455 reads "45.5%".
    static string Tenths(int tenths) => (tenths / 10f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";
    static string DeltaTenths(int diff) => diff == 0 ? "" : $" <color={(diff > 0 ? BonusHex : MalusHex)}>({(diff > 0 ? "+" : "−")}{Tenths(Mathf.Abs(diff))})</color>";
    // Sprint/run gear lives on the player's BODY, not the character sheet — surfaced here because it's a speed the
    // player feels. Shown as the real run speed so it reads next to MOVE.
    static string RunSpeed(Player3DController pc) => pc == null ? "" : pc.CurrentRunSpeed.ToString("0.0");

    Player3DController playerBody;   // cached: resolved once, re-resolved only if the body was rebuilt
    Player3DController PlayerBody()
    {
        if (playerBody != null) return playerBody;
        var sc = StatusController.Player;
        return playerBody = sc != null ? sc.GetComponent<Player3DController>() : null;
    }

    // green = a buff is helping, red = a debuff is hurting; the rest are the theme's dim and accent text
    static string BonusHex => UiKit.Hex(UiKit.Good);
    static string MalusHex => UiKit.Hex(UiKit.Bad);
    static string DimHex => UiKit.Hex(UiKit.TextDim);
    static string AccentHex => UiKit.Hex(UiKit.Accent);
    static string TitleHex => UiKit.Hex(UiKit.TitleText);

    // " (+3)" / " (−2)", coloured; empty when nothing is modifying the stat (no parentheses noise on a clean sheet).
    static string Delta(int diff) => diff == 0 ? "" : $" <color={(diff > 0 ? BonusHex : MalusHex)}>({(diff > 0 ? "+" : "−")}{Mathf.Abs(diff)})</color>";

    // A ranged stat (ATK min-max) shows ONE bonus when both ends move together, and "min-max" when they differ.
    static string Delta(int lo, int hi)
    {
        if (lo == 0 && hi == 0) return "";
        if (lo == hi) return Delta(lo);
        bool up = hi > 0;
        string hex = up ? BonusHex : MalusHex;
        string sign = up ? "+" : "−";
        return $" <color={hex}>({sign}{Mathf.Abs(lo)}-{Mathf.Abs(hi)})</color>";
    }

    // Fractional stats (ASPD in swings a second, the CAST multiplier) read as a two-decimal delta.
    static string Delta(float diff)
    {
        if (Mathf.Abs(diff) < 0.005f) return "";
        return $" <color={(diff > 0 ? BonusHex : MalusHex)}>({(diff > 0 ? "+" : "−")}{Mathf.Abs(diff):0.00})</color>";
    }

    // Fraction stats (guard cut, leech, reflect, crit chance) read as a percentage-point delta.
    static string DeltaPct(float diff)
    {
        int pts = Mathf.RoundToInt(diff * 100);
        if (pts == 0) return "";
        return $" <color={(pts > 0 ? BonusHex : MalusHex)}>({(pts > 0 ? "+" : "−")}{Mathf.Abs(pts)}%)</color>";
    }

    // ---- helpers --------------------------------------------------------------

    // THE ITEM CARD'S MEASURES, copied off Ragnarok's own item window, in canvas units: the card, the picture panel at its
    // left, the top part (picture, title and details) above the card-slot strip, the title bar, the scrollbar, the strip.
    const float CardW = 344f, CardH = 180f, PicW = 104f, TopH = 146f, TitleH = 22f, BarW = 14f, SlotsH = 28f, Pad = 6f;

    void EnsureTooltip()
    {
        // Always build our own tooltip on a TOP-sorting nested canvas: an existing tooltip from the
        // old GameUICanvas sits at a lower sort order and would render BEHIND these windows. Building
        // ours (its Awake claims ItemTooltip.Instance) keeps hover visible above everything. A nested canvas that sorts
        // itself is never searched by its parent's pointer finder, so it carries its OWN — without it the card could be
        // neither dragged, scrolled nor closed.
        var layer = new GameObject("TooltipLayer", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster))
            { hideFlags = HideFlags.DontSaveInEditor };
        layer.transform.SetParent(canvas, false);
        UiKit.Fill((RectTransform)layer.transform, Vector2.zero, Vector2.zero);
        var lc = layer.GetComponent<Canvas>();
        lc.overrideSorting = true;
        lc.sortingOrder = 200;
        var host = layer.transform;

        // RAGNAROK'S ITEM WINDOW, one fixed size: the picture panel at the left; the name in a bar along the top (drag it
        // to move the card) with the × at the corner; the details under it with a scrollbar when they run long; the
        // card-slot strip along the bottom.
        var border = UiKit.Round(UiKit.Img("ItemTooltip", host, UiKit.WindowEdge), UiKit.WindowRadius);
        var rt = border.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.sizeDelta = new Vector2(CardW, CardH);
        var cg = border.gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false; cg.interactable = false;
        var fill = UiKit.Round(UiKit.Img("Fill", border.transform, UiKit.PanelFill), UiKit.WindowRadius - 1);
        UiKit.Fill(fill.rectTransform, new Vector2(1, 1), new Vector2(-1, -1));
        fill.raycastTarget = false;

        var picture = UiKit.Round(UiKit.Img("Picture", border.transform, UiKit.SlotFill), 3);
        picture.raycastTarget = false;
        Place(picture.rectTransform, Pad, Pad, PicW - Pad, TopH - Pad);
        var icon = ItemIcon.Make(picture.transform, 8f);
        var look = GradeGlow.MakeForSquare(picture.transform, icon.transform);
        // A skill has no picture: its badge — its colour, its letters — stands in the panel instead.
        var badge = UiKit.Round(UiKit.Img("Badge", picture.transform, Color.white), 6);
        badge.raycastTarget = false;
        var badgeRt = badge.rectTransform;
        badgeRt.anchorMin = badgeRt.anchorMax = badgeRt.pivot = new Vector2(0.5f, 0.5f);
        badgeRt.sizeDelta = new Vector2(64f, 64f);
        var badgeText = UiKit.Label("Letters", badge.transform, "", 26, FontStyle.Bold, TextAnchor.MiddleCenter);
        badgeText.raycastTarget = false;
        UiKit.Paint(badgeText, UiKit.TextOnDim);
        UiKit.Fill(badgeText.rectTransform, Vector2.zero, Vector2.zero);
        badge.gameObject.SetActive(false);

        // The thin rule between the picture and the words.
        var rule = UiKit.Img("Rule", border.transform, UiKit.WindowEdge);
        rule.raycastTarget = false;
        Place(rule.rectTransform, PicW + 2f, Pad, 1f, TopH - Pad);

        var bar = UiKit.Img("TitleBar", border.transform, new Color(0f, 0f, 0f, 0f));
        Place(bar.rectTransform, PicW + 6f, 4f, CardW - PicW - 6f - 28f, TitleH);
        bar.gameObject.AddComponent<WindowDragHandle>().window = rt;
        var title = UiKit.Label("Title", bar.transform, "Item", 15, FontStyle.Normal, TextAnchor.MiddleLeft);
        title.raycastTarget = false;
        title.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        title.overflowMode = TMPro.TextOverflowModes.Ellipsis;
        UiKit.Fill(title.rectTransform, Vector2.zero, Vector2.zero);

        var close = UiKit.BarButton(border.transform, "Close", "X", 2f, 24f, 24f);
        var closeRt = close.rectTransform;
        closeRt.anchorMin = closeRt.anchorMax = new Vector2(1, 1);
        closeRt.pivot = new Vector2(1, 1);
        closeRt.anchoredPosition = new Vector2(-2f, -2f);
        close.gameObject.AddComponent<ItemCardClose>();

        float detailsTop = 4f + TitleH + 4f, detailsHeight = TopH - detailsTop;
        float detailsWidth = CardW - (PicW + 8f) - BarW - 2f * Pad;
        var details = UiKit.Img("Details", border.transform, new Color(0f, 0f, 0f, 0f));
        Place(details.rectTransform, PicW + 8f, detailsTop, detailsWidth, detailsHeight);
        var content = UiKit.ScrollColumn(details.rectTransform);
        var contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 2;
        contentLayout.childControlWidth = contentLayout.childControlHeight = true;
        contentLayout.childForceExpandWidth = true; contentLayout.childForceExpandHeight = false;
        var body = UiKit.Label("Body", content, "", 13, FontStyle.Normal, TextAnchor.UpperLeft);
        body.raycastTarget = false;

        var scroll = details.GetComponent<ScrollRect>();
        scroll.verticalScrollbar = MakeCardScrollbar(border.transform, scroll, CardW - Pad - BarW, detailsTop, detailsHeight);
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

        // THE CARD SLOTS, as Ragnarok draws them: a strip in the slot colour with a diamond per slot the piece has.
        var slots = UiKit.Round(UiKit.Img("Slots", border.transform, UiKit.SlotFill), 3);
        slots.raycastTarget = false;
        Place(slots.rectTransform, Pad, TopH + 2f, CardW - 2f * Pad, SlotsH);
        var diamonds = new Image[ItemTooltip.MostCardSlots];
        for (int i = 0; i < diamonds.Length; i++)
        {
            var d = UiKit.Img("Slot" + i, slots.transform, UiKit.ButtonEdge);
            var drt = d.rectTransform;
            drt.anchorMin = drt.anchorMax = new Vector2(0f, 0.5f);
            drt.pivot = new Vector2(0.5f, 0.5f);
            drt.sizeDelta = new Vector2(13f, 13f);
            drt.anchoredPosition = new Vector2(20f + i * 30f, 0f);
            drt.localRotation = Quaternion.Euler(0f, 0f, 45f);
            diamonds[i] = d;
        }

        // THE HOVER STRIP: only the item's full name, white on a dark see-through strip by the cursor.
        var strip = UiKit.Img("ItemName", host, new Color(0f, 0f, 0f, 0.72f));
        strip.raycastTarget = false;
        var stripRt = strip.rectTransform;
        stripRt.anchorMin = stripRt.anchorMax = new Vector2(0, 1); stripRt.pivot = new Vector2(0, 1);
        var stripLayout = strip.gameObject.AddComponent<HorizontalLayoutGroup>();
        stripLayout.padding = new RectOffset(8, 8, 4, 4);
        stripLayout.childControlWidth = stripLayout.childControlHeight = true;
        stripLayout.childForceExpandWidth = stripLayout.childForceExpandHeight = false;
        // Hidden until something is hovered.
        var stripFade = strip.gameObject.AddComponent<CanvasGroup>();
        stripFade.alpha = 0f; stripFade.blocksRaycasts = false; stripFade.interactable = false;
        var stripFit = strip.gameObject.AddComponent<ContentSizeFitter>();
        stripFit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        stripFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var name = UiKit.Label("Name", strip.transform, "", 14, FontStyle.Bold, TextAnchor.MiddleLeft);
        name.raycastTarget = false;
        name.color = Color.white;
        name.textWrappingMode = TMPro.TextWrappingModes.NoWrap;

        var tip = border.gameObject.AddComponent<ItemTooltip>();
        tip.panel = rt; tip.titleText = title; tip.bodyText = body;
        tip.icon = icon;
        tip.look = look;
        tip.badge = badge; tip.badgeText = badgeText;
        tip.detailsScroll = scroll;
        tip.slotDiamonds = diamonds;
        tip.namePanel = stripRt; tip.nameText = name;
    }

    // Lay `r` at (x, y) from the top-left of its parent, w × h.
    static void Place(RectTransform r, float x, float y, float w, float h)
    {
        r.anchorMin = r.anchorMax = new Vector2(0, 1);
        r.pivot = new Vector2(0, 1);
        r.anchoredPosition = new Vector2(x, -y);
        r.sizeDelta = new Vector2(w, h);
    }

    // Ragnarok's scrollbar: an arrow at each end, a track between them and a coloured thumb that shows where you are.
    static Scrollbar MakeCardScrollbar(Transform parent, ScrollRect scroll, float x, float y, float h)
    {
        const float W = BarW;
        var root = new GameObject("Scrollbar", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(parent, false);
        Place(root, x, y, W, h);

        var up = Arrow(root, "Up", 0f, W, scroll, +1);
        var down = Arrow(root, "Down", h - W, W, scroll, -1);

        var track = UiKit.Round(UiKit.Img("Track", root, UiKit.ButtonEdge), 3);
        Place(track.rectTransform, 2f, W + 1f, W - 4f, h - 2f * W - 2f);
        var area = new GameObject("Sliding Area", typeof(RectTransform)).GetComponent<RectTransform>();
        area.SetParent(track.transform, false);
        UiKit.Fill(area, Vector2.zero, Vector2.zero);
        var handle = UiKit.Round(UiKit.Img("Handle", area, UiKit.Accent), 3);
        UiKit.Fill(handle.rectTransform, Vector2.zero, Vector2.zero);

        var bar = track.gameObject.AddComponent<Scrollbar>();
        bar.handleRect = handle.rectTransform;
        bar.targetGraphic = handle;
        bar.direction = Scrollbar.Direction.BottomToTop;
        up.transform.SetParent(root, false);
        down.transform.SetParent(root, false);
        return bar;
    }

    static Image Arrow(RectTransform root, string name, float y, float size, ScrollRect scroll, int direction)
    {
        var a = UiKit.Round(UiKit.Img(name, root, UiKit.TextDim), 2);
        Place(a.rectTransform, 0f, y, size, size);
        var glyph = UiKit.Label("Glyph", a.transform, direction > 0 ? "▲" : "▼", 9, FontStyle.Bold, TextAnchor.MiddleCenter);
        glyph.raycastTarget = false;
        glyph.color = Color.white;
        UiKit.Fill(glyph.rectTransform, Vector2.zero, Vector2.zero);
        var step = a.gameObject.AddComponent<ItemCardScrollStep>();
        step.scroll = scroll;
        step.direction = direction;
        return a;
    }

    // Green/red "+v LABEL" stat line; skipped when zero.
    static void Stat(System.Text.StringBuilder sb, string label, int v, string suffix = "")
    {
        if (v == 0) return;
        sb.Append(v > 0 ? $"\n<color={BonusHex}>+" : $"\n<color={MalusHex}>")
          .Append(v).Append(suffix).Append(' ').Append(label).Append("</color>");
    }

    static string Spaced(string s)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i])) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
