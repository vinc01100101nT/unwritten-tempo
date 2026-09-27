using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A single spreadsheet-style view of every <see cref="MonsterDef"/> in the project — one mob per row,
/// with a live front-facing thumbnail and the balance-relevant stats editable inline. Edits write straight
/// back to the asset (Undo-able, marked dirty, saved on demand), so this doubles as a balancing table.
///
/// Open via <c>Tools ▸ unwritten ▸ Catalogs ▸ Mob Table</c>. Click a column header to sort; click the asset
/// name's select button (or a row's ◎) to ping/select the underlying asset for the full Inspector.
/// </summary>
public class MobTableWindow : EditorWindow
{
    const string MenuPath = "Tools/unwritten/Catalogs/Mob Table";
    const int Cell = 16;                 // Ninja Adventure mob sheets are 16px square cells
    const float RowHeight = 38f;

    [MenuItem(MenuPath)]
    static void Open()
    {
        var w = GetWindow<MobTableWindow>("Mob Table");
        w.minSize = new Vector2(40, 320);
        w.Reload();
    }

    readonly List<MonsterDef> _defs = new List<MonsterDef>();
    readonly Dictionary<MonsterDef, (Texture2D tex, Rect uv)> _sheets = new Dictionary<MonsterDef, (Texture2D, Rect)>();
    Vector2 _scroll, _hscroll;
    string _search = "";
    readonly HashSet<MonsterDef> _expanded = new HashSet<MonsterDef>();
    // Toggling an expander CHANGES how many layout groups the window draws. IMGUI requires the Layout pass and the
    // Repaint pass of one frame to agree on that count, so the click is recorded here and applied after the loop —
    // flipping it mid-draw is the classic "GUILayout: Mismatched LayoutGroup" console spam.
    MonsterDef _pendingToggle;
    string _sortField = "id";
    bool _sortAsc = true;

    void Reload()
    {
        _defs.Clear();
        _sheets.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:MonsterDef"))
        {
            var def = AssetDatabase.LoadAssetAtPath<MonsterDef>(AssetDatabase.GUIDToAssetPath(guid));
            if (def != null) _defs.Add(def);
        }
        Resort();
        Repaint();
    }

    void Resort()
    {
        System.Comparison<MonsterDef> cmp = _sortField switch
        {
            "name"   => (a, b) => string.Compare(a.displayName, b.displayName, System.StringComparison.OrdinalIgnoreCase),
            "biome"  => (a, b) => a.biome.CompareTo(b.biome),
            "lvl"    => (a, b) => a.level.CompareTo(b.level),
            "gear"   => (a, b) => GearCount(a).CompareTo(GearCount(b)),
            // Sort by how far off-budget a mob is, so the mis-authored ones surface together.
            "pts"    => (a, b) => (UnitStatMath.PointsSpent(a.StatInputs) - UnitStatMath.PointsEarnedByLevel(a.level))
                        .CompareTo(UnitStatMath.PointsSpent(b.StatInputs) - UnitStatMath.PointsEarnedByLevel(b.level)),
            // Sort by the DERIVED value the column shows, not the superseded stored field.
            "hp"     => (a, b) => Derived(a, MonsterDefStats.Stat.HP).CompareTo(Derived(b, MonsterDefStats.Stat.HP)),
            "atk"    => (a, b) => Derived(a, MonsterDefStats.Stat.Atk).CompareTo(Derived(b, MonsterDefStats.Stat.Atk)),
            "weight" => (a, b) => a.weight.CompareTo(b.weight),
            "xp"     => (a, b) => a.xpReward.CompareTo(b.xpReward),
            "elem"   => (a, b) => a.element.CompareTo(b.element),
            "elemlv" => (a, b) => a.elementLevel.CompareTo(b.elementLevel),
            _        => (a, b) => string.Compare(a.id, b.id, System.StringComparison.OrdinalIgnoreCase),
        };
        _defs.Sort((a, b) => (_sortAsc ? 1 : -1) * cmp(a, b));
    }

    // The picture and the part of it that is the creature's front-facing frame, in texture coordinates. A grid
    // sheet's frame is its top-left 16px cell; strip art's (the bosses) is the first frame of the Idle picture.
    (Texture2D tex, Rect uv) SheetFor(MonsterDef def)
    {
        if (_sheets.TryGetValue(def, out var t)) return t;
        t = (null, default);
        if (!string.IsNullOrEmpty(def.sheet))
        {
            if (RigBuilder25D.TryStripArt(def.sheet, out var idle, out _, out _))
                t = (idle.sheet, new Rect(0f, 0f, 1f / idle.frames, 1f));
            else
            {
                var tex = RigBuilder25D.MonsterSheet(def.sheet);
                if (tex != null)
                {
                    // Unity tex coords are bottom-left origin, so the top row sits at the top of the [0,1] V range.
                    float c = Mathf.Min(Cell, Mathf.Min(tex.width, tex.height));
                    t = (tex, new Rect(0f, 1f - c / tex.height, c / tex.width, c / tex.height));
                }
            }
        }
        _sheets[def] = t;
        return t;
    }

    // --- column layout (label, sortField or null, width) ---
    struct Col { public string label, sort; public float w; public Col(string l, string s, float wi) { label = l; sort = s; w = wi; } }
    static readonly Col[] Cols =
    {
        new Col("",      null,     20),   // expand toggle
        new Col("",      null,     42),   // preview
        new Col("id",    "id",     150),
        new Col("name",  "name",   120),
        new Col("biome", "biome",  90),
        new Col("kind",  null,     64),
        new Col("size",  null,     100),
        // The AUTHORED stat block — a mob is a level and six primaries, exactly like the player.
        new Col("LVL",   "lvl",    38),
        new Col("STR",   null,     38),
        new Col("AGI",   null,     38),
        new Col("VIT",   null,     38),
        new Col("INT",   null,     38),
        new Col("DEX",   null,     38),
        new Col("LUK",   null,     38),
        // The level's point BUDGET vs what this spread costs — red when a mob carries stats its level can't afford.
        new Col("pts",   "pts",    74),
        // Worn gear + the ring's ranks, at a glance. Both are EDITED in the expander row (click ▸) — a spreadsheet
        // row can't hold 12 slots and 10 rank sliders without becoming unreadable.
        new Col("gear",  "gear",   120),
        // DERIVED readouts (read-only): what UnitStatMath actually produces from the block on the left, so a
        // designer edits the cause and immediately sees the effect. Never editable — these have no storage.
        new Col("=HP",   "hp",     52),
        new Col("=ATK",  "atk",    46),
        new Col("=DEF",  null,     44),
        new Col("=FLEE", null,     46),
        new Col("elem",  "elem",   84),
        new Col("Element Lv", "elemlv", 70),
        new Col("wt",    "weight", 38),
        new Col("XP",    "xp",     46),
        new Col("g.min", null,     46),
        new Col("g.max", null,     46),
        new Col("",      null,     28),   // select asset
    };
    static float TotalWidth => Cols.Sum(c => c.w) + 8;

    void OnGUI()
    {
        DrawToolbar();
        DrawHeader();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        _hscroll = GUILayout.BeginScrollView(_hscroll, GUILayout.ExpandWidth(true));
        foreach (var def in _defs)
        {
            if (!string.IsNullOrEmpty(_search) && !MatchesSearch(def)) continue;
            DrawRow(def);
            if (_expanded.Contains(def)) DrawGearAndRingEditor(def);
        }
        GUILayout.EndScrollView();
        EditorGUILayout.EndScrollView();

        // Apply a queued expander click now that the whole layout pass is done, then repaint with the new shape.
        if (_pendingToggle != null)
        {
            if (!_expanded.Remove(_pendingToggle)) _expanded.Add(_pendingToggle);
            _pendingToggle = null;
            Repaint();
        }
    }

    bool MatchesSearch(MonsterDef d)
    {
        string s = _search.ToLowerInvariant();
        return (d.id ?? "").ToLowerInvariant().Contains(s)
            || (d.displayName ?? "").ToLowerInvariant().Contains(s)
            || (d.sheet ?? "").ToLowerInvariant().Contains(s)
            || d.biome.ToString().ToLowerInvariant().Contains(s);
    }

    void DrawToolbar()
    {
        using (var flow = new EditorLayoutFlow.Flow(position.width, EditorStyles.toolbar))
        {
            flow.Item(70);
            GUILayout.Label($"{_defs.Count} mobs", EditorStyles.miniLabel, GUILayout.Width(70));
            flow.Item(44 + 180);   // "Search" label + field kept together
            GUILayout.Label("Search", EditorStyles.miniLabel, GUILayout.Width(44));
            _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(180));
            flow.Item(70);
            if (GUILayout.Button("Save All", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                AssetDatabase.SaveAssets();
                MonsterCatalog.Reload();
                WikiExport.RunAll();
            }
            flow.Item(60);
            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60))) Reload();
        }
    }

    void DrawHeader()
    {
        // Lay the header out with the SAME manual per-column rects the rows use (see DrawRow's Next()),
        // so titles sit exactly over their values. Auto-layout (GUILayout) added padding/spacing that
        // drifted the headers off their columns. Offset by -_hscroll.x to track the horizontal scroll.
        var style = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleLeft };
        var bar = EditorGUILayout.GetControlRect(false, 18, GUILayout.Width(TotalWidth));
        float x = bar.x - _hscroll.x;
        foreach (var c in Cols)
        {
            var r = new Rect(x, bar.y, c.w - 3, bar.height);
            x += c.w;
            string label = c.label;
            if (c.sort != null && c.sort == _sortField) label += _sortAsc ? " ▲" : " ▼";
            if (c.sort != null)
            {
                if (GUI.Button(r, label, EditorStyles.toolbarButton))
                {
                    if (_sortField == c.sort) _sortAsc = !_sortAsc; else { _sortField = c.sort; _sortAsc = true; }
                    Resort();
                }
            }
            else GUI.Label(r, label, style);
        }
    }

    void DrawRow(MonsterDef def)
    {
        var rect = EditorGUILayout.GetControlRect(false, RowHeight, GUILayout.Width(TotalWidth));
        if (Mathf.FloorToInt(rect.y / RowHeight) % 2 == 0)
            EditorGUI.DrawRect(rect, new Color(1, 1, 1, 0.03f));

        float x = rect.x;
        int ci = 0;
        Rect Next() { var r = new Rect(x, rect.y + 3, Cols[ci].w - 3, RowHeight - 6); x += Cols[ci].w; ci++; return r; }

        // expander — opens the gear + ring-ranks editor beneath this row
        bool open = _expanded.Contains(def);
        if (GUI.Button(VCenter(Next(), 18), open ? "▾" : "▸", EditorStyles.label)) _pendingToggle = def;

        // preview
        var pr = Next();
        DrawPreview(new Rect(pr.x, rect.y + 2, 34, 34), def);

        EditorGUI.BeginChangeCheck();
        string id    = EditorGUI.TextField(VCenter(Next(), 18), def.id);
        string name  = EditorGUI.TextField(VCenter(Next(), 18), def.displayName);
        // Biome.None = biome-agnostic (spawns in any biome); it's just another choice in the dropdown.
        var biome    = (Biome)EditorGUI.EnumPopup(VCenter(Next(), 18), def.biome);
        var kind     = (CreatureKind)EditorGUI.EnumPopup(VCenter(Next(), 18), def.kind);
        // Size is one number, times a character's height; its group (Small / Medium / Large) is worked out from it and
        // shown beside it, never typed. A shadow is always 1 whatever its entry says (MobSizes.Of), so it is locked.
        var sizeCell = Next();
        EditorGUI.BeginDisabledGroup(def.kind == CreatureKind.Shadow);
        float size   = EditorGUI.FloatField(VCenter(new Rect(sizeCell.x, sizeCell.y, 44f, sizeCell.height), 18), def.size);
        EditorGUI.EndDisabledGroup();
        GUI.Label(VCenter(new Rect(sizeCell.x + 48f, sizeCell.y, sizeCell.width - 48f, sizeCell.height), 18),
                  MobSizes.GroupOf(MobSizes.Of(def.kind, def.size)).ToString(), EditorStyles.miniLabel);
        int lvl      = EditorGUI.IntField(VCenter(Next(), 18), def.level);
        int str      = EditorGUI.IntField(VCenter(Next(), 18), def.str);
        int agi      = EditorGUI.IntField(VCenter(Next(), 18), def.agi);
        int vit      = EditorGUI.IntField(VCenter(Next(), 18), def.vit);
        int itl      = EditorGUI.IntField(VCenter(Next(), 18), def.intel);
        int dex      = EditorGUI.IntField(VCenter(Next(), 18), def.dex);
        int luk      = EditorGUI.IntField(VCenter(Next(), 18), def.luk);
        DrawPoints(Next(), def);
        DrawGearSummary(Next(), def);
        // Derived readouts — computed live from the block just edited, never stored and never editable.
        DrawDerived(Next(), Derived(def, MonsterDefStats.Stat.HP));
        DrawDerived(Next(), Derived(def, MonsterDefStats.Stat.Atk));
        DrawDerived(Next(), Derived(def, MonsterDefStats.Stat.Def));
        DrawDerived(Next(), Derived(def, MonsterDefStats.Stat.Flee));
        var elem     = (Element)EditorGUI.EnumPopup(VCenter(Next(), 18), def.element);
        int elemLv   = EditorGUI.IntField(VCenter(Next(), 18), def.elementLevel);
        int wt       = EditorGUI.IntField(VCenter(Next(), 18), def.weight);
        int xp       = EditorGUI.IntField(VCenter(Next(), 18), def.xpReward);
        int gMin     = EditorGUI.IntField(VCenter(Next(), 18), def.goldMin);
        int gMax     = EditorGUI.IntField(VCenter(Next(), 18), def.goldMax);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(def, "Edit Mob");
            def.id = id; def.displayName = name; def.biome = biome;
            def.kind = kind;
            def.size = MobSizes.Clamp(size);
            def.level = Mathf.Max(1, lvl);
            // Budget guard: an entry the level can't afford SNAPS to the highest value that fits, rather than being
            // stored as an over-budget spread. Each stat is checked against what's left once the other five are paid
            // for, in field order — so typing 99 into STR spends the remainder and leaves the rest untouched.
            var typed = new[] { str, agi, vit, itl, dex, luk };
            var prior = new[] { def.str, def.agi, def.vit, def.intel, def.dex, def.luk };
            FitSpreadToBudget(def.level, typed, prior);
            def.str = typed[0]; def.agi = typed[1]; def.vit = typed[2];
            def.intel = typed[3]; def.dex = typed[4]; def.luk = typed[5];
            def.element = elem; def.elementLevel = Mathf.Clamp(elemLv, 1, Elements.MaxLevel); def.weight = wt; def.xpReward = xp;
            def.goldMin = gMin; def.goldMax = gMax;
            EditorUtility.SetDirty(def);
        }

        if (GUI.Button(VCenter(Next(), 18), "◎"))
        {
            Selection.activeObject = def;
            EditorGUIUtility.PingObject(def);
        }
    }

    static Rect VCenter(Rect r, float h) => new Rect(r.x, r.y + (r.height - h) * 0.5f, r.width, h);

    static int Clamp99(int v) => Mathf.Clamp(v, 1, UnitStatMath.MaxStatValue);

    /// <summary>Force a typed spread inside the level's point budget, IN PLACE. Only a stat the user actually EDITED
    /// is capped — it absorbs whatever points the others leave over, so typing 99 into STR at level 9 becomes 27
    /// rather than dragging the untouched stats down with it. Stats left alone are never modified, which is why this
    /// needs <paramref name="prior"/>: the committed values are what the edited stat is measured against.
    ///
    /// <para>Result always satisfies <c>PointsSpent ≤ PointsEarnedByLevel</c>, so the table cannot write a mob
    /// stronger than its level allows. (Lowering the LEVEL doesn't retro-clamp a spread — nothing was typed into a
    /// stat, so the pts column just turns red and the choice of what to give up stays the designer's.)</para></summary>
    static void FitSpreadToBudget(int level, int[] s, int[] prior)
    {
        int budget = UnitStatMath.PointsEarnedByLevel(level);
        for (int i = 0; i < s.Length; i++)
        {
            s[i] = Clamp99(s[i]);
            if (s[i] == prior[i]) continue;   // untouched this edit — leave it exactly as authored
            // What the rest cost: already-resolved values to the left, still-committed values to the right.
            int others = 0;
            for (int j = 0; j < s.Length; j++)
                if (j != i) others += UnitStatMath.PointsToRaise(j < i ? s[j] : prior[j]);
            s[i] = Mathf.Min(s[i], UnitStatMath.MaxAffordableStat(budget - others));
        }
    }

    // The derived columns read the one calculation /mobinfo reads too, so the table and the game can never disagree.
    static int Derived(MonsterDef def, MonsterDefStats.Stat which) => MonsterDefStats.Derived(def, which);

    static int GearCount(MonsterDef d)
    {
        if (d.startingGear == null) return 0;
        int n = 0;
        for (int i = 0; i < d.startingGear.Length; i++) if (d.startingGear[i] != null) n++;
        return n;
    }

    static GUIStyle gearStyle;
    // At-a-glance: how many pieces worn, and the ring's total ranks. Editing happens in the expander.
    static void DrawGearSummary(Rect r, MonsterDef def)
    {
        int worn = GearCount(def);
        int ranks = 0;
        if (def.ringRankValues != null) for (int i = 0; i < def.ringRankValues.Length; i++) ranks += Mathf.Max(0, def.ringRankValues[i]);

        gearStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleLeft };
        gearStyle.normal.textColor = worn > 0 ? new Color(0.75f, 0.7f, 1f) : new Color(0.45f, 0.45f, 0.45f);
        string label = worn == 0 && ranks == 0 ? "—" : ranks > 0 ? $"{worn} worn · ring +{ranks}" : $"{worn} worn";
        GUI.Label(VCenter(r, 18), label, gearStyle);
    }

    // Every slot a unit can fill. Cosmetic layers are omitted — they carry no stats, so authoring them here would be
    // a knob that changes nothing.
    static readonly EquipSlotType[] GearSlots =
    {
        EquipSlotType.Weapon, EquipSlotType.Shield, EquipSlotType.Head, EquipSlotType.Armor,
        EquipSlotType.Garment, EquipSlotType.Boots,
    };

    /// <summary>The per-mob detail editor: what it WEARS (one object field per slot) and, when a ring is worn, that
    /// ring's invested ranks. Kept out of the row itself so the table stays scannable; everything here writes straight
    /// back to the asset, Undo-able, exactly like the inline columns.</summary>
    void DrawGearAndRingEditor(MonsterDef def)
    {
        var box = EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(TotalWidth - 20));
        GUILayout.Space(2);

        EditorGUILayout.LabelField($"   {def.displayName} — equipment", EditorStyles.miniBoldLabel);

        EditorGUI.BeginChangeCheck();

        // --- worn gear, one field per slot ---
        var worn = new Dictionary<EquipSlotType, Item>();
        if (def.startingGear != null)
            foreach (var it in def.startingGear)
                if (it != null && it.slot != EquipSlotType.None) worn[it.slot] = it;

        int perRow = 3;
        for (int i = 0; i < GearSlots.Length; i++)
        {
            if (i % perRow == 0) EditorGUILayout.BeginHorizontal();
            var slot = GearSlots[i];
            worn.TryGetValue(slot, out var cur);
            EditorGUILayout.LabelField(slot.ToString(), EditorStyles.miniLabel, GUILayout.Width(74));
            var picked = (Item)EditorGUILayout.ObjectField(cur, typeof(Item), false, GUILayout.Width(150));
            // Only accept an item that actually belongs in this slot — otherwise the "worn set" would lie about
            // where a piece sits.
            if (picked != null && picked.slot != slot) picked = cur;
            if (picked != null) worn[slot] = picked; else worn.Remove(slot);
            GUILayout.Space(8);
            if (i % perRow == perRow - 1 || i == GearSlots.Length - 1) EditorGUILayout.EndHorizontal();
        }

        // --- what state every piece comes to life at: one set for the whole creature ---
        GUILayout.Space(4);
        EditorGUILayout.LabelField("   gear state  (every piece this creature wears comes to life at these)",
                                   EditorStyles.miniBoldLabel);
        var quality = def.gearQuality;
        int folds = def.gearFolds;
        int inkTier = def.gearInkTier;
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Grade", EditorStyles.miniLabel, GUILayout.Width(74));
        quality = (Quality)EditorGUILayout.EnumPopup(quality, GUILayout.Width(80));
        GUILayout.Space(8);
        EditorGUILayout.LabelField("Folds", EditorStyles.miniLabel, GUILayout.Width(44));
        int cap = CraftingMath.FoldCap(quality);
        folds = Mathf.Clamp(EditorGUILayout.IntField(folds, GUILayout.Width(50)), 0, cap);
        EditorGUILayout.LabelField("of " + cap, EditorStyles.miniLabel, GUILayout.Width(44));
        GUILayout.Space(8);
        EditorGUILayout.LabelField("Ink", EditorStyles.miniLabel, GUILayout.Width(30));
        inkTier = EditorGUILayout.IntPopup(Mathf.Clamp(inkTier, 1, InkTierValues.Length),
                                           InkTierNames, InkTierValues, GUILayout.Width(80));
        int written = CraftingMath.InkSlots(quality);
        EditorGUILayout.LabelField(written == 0 ? "writes nothing" : written == 1 ? "writes 1 stat"
                                                                   : "writes " + written + " stats",
                                   EditorStyles.miniLabel, GUILayout.Width(110));
        EditorGUILayout.EndHorizontal();

        // --- ring ranks: part of the creature, so they always count ---
        var stats = new List<StatId>();
        var vals  = new List<int>();
        {
            GUILayout.Space(4);
            EditorGUILayout.LabelField("   ring ranks  (same soft-cap curve as the player's ring)", EditorStyles.miniBoldLabel);
            for (int i = 0; i < RingStatOrder.Length; i++)
            {
                var st = RingStatOrder[i];
                int cur = RanksIn(def, st);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(st.ToString(), EditorStyles.miniLabel, GUILayout.Width(90));
                int next = Mathf.Max(0, EditorGUILayout.IntField(cur, GUILayout.Width(50)));
                // Show what that rank count is actually WORTH, so a designer tunes the effect, not the raw number.
                EditorGUILayout.LabelField(next > 0 ? $"+{SkillRing.Percent(st, next) * 100f:0.#}%" : "—",
                                           EditorStyles.miniLabel, GUILayout.Width(60));
                EditorGUILayout.EndHorizontal();
                if (next > 0) { stats.Add(st); vals.Add(next); }
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(def, "Edit Mob Equipment");
            var gear = new List<Item>();
            foreach (var kv in worn) if (kv.Value != null) gear.Add(kv.Value);
            def.startingGear = gear.ToArray();
            def.gearQuality = quality;
            def.gearFolds = Mathf.Clamp(folds, 0, CraftingMath.FoldCap(quality));
            def.gearInkTier = inkTier;
            def.ringRankStats  = stats.ToArray();
            def.ringRankValues = vals.ToArray();
            EditorUtility.SetDirty(def);
        }

        GUILayout.Space(2);
        EditorGUILayout.EndVertical();
    }

    // The five Inks, by the names the game calls them, for the gear-state dropdown.
    static readonly string[] InkTierNames = { "Watery", "Thin", "Faint", "Clear", "Sharp" };
    static readonly int[] InkTierValues = { 1, 2, 3, 4, 5 };

    // Ring stats in the canonical LOOP order (SkillRing.Ring), never enum order.
    static readonly StatId[] RingStatOrder =
    {
        StatId.Def, StatId.HP, StatId.Atk, StatId.Accuracy, StatId.CastSpeed,
        StatId.MAtk, StatId.MP, StatId.Evasion, StatId.Critical, StatId.AttackSpeed,
    };

    static int RanksIn(MonsterDef def, StatId stat)
    {
        if (def.ringRankStats == null || def.ringRankValues == null) return 0;
        for (int i = 0; i < def.ringRankStats.Length && i < def.ringRankValues.Length; i++)
            if (def.ringRankStats[i] == stat) return def.ringRankValues[i];
        return 0;
    }

    static GUIStyle pointsStyle;
    // "spent/budget" against the SAME economy the player spends from: a mob at level L may carry exactly the stats
    // a player who reached L could have bought. Red = over-budget (this mob is stronger than its level allows);
    // amber = a meaningful chunk unspent (weaker than its level implies). Green = on budget.
    static void DrawPoints(Rect r, MonsterDef def)
    {
        var u = def.StatInputs;
        int spent = UnitStatMath.PointsSpent(u);
        int budget = UnitStatMath.PointsEarnedByLevel(def.level);
        pointsStyle ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        // A few points can always be unspendable (the next point costs more than what's left), so only flag a real gap.
        pointsStyle.normal.textColor =
            spent > budget            ? new Color(1f, 0.45f, 0.45f) :
            spent < budget - 10       ? new Color(0.95f, 0.8f, 0.4f) :
                                        new Color(0.62f, 0.72f, 0.62f);
        GUI.Label(VCenter(r, 18), $"{spent}/{budget}", pointsStyle);
    }

    static GUIStyle derivedStyle;
    // Greyed + right-aligned so a derived number reads as a readout, not a field you can type into.
    static void DrawDerived(Rect r, int value)
    {
        derivedStyle ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(0.62f, 0.72f, 0.62f) },
        };
        GUI.Label(VCenter(r, 18), value.ToString(), derivedStyle);
    }

    void DrawPreview(Rect r, MonsterDef def)
    {
        var (tex, uv) = SheetFor(def);
        if (tex == null) { GUI.Label(r, "?", EditorStyles.centeredGreyMiniLabel); return; }

        // A strip frame is drawn at its own shape (a 96×48 Samurai frame is twice as wide as tall), fitted in the cell.
        float aspect = (uv.width * tex.width) / (uv.height * tex.height);
        if (aspect > 1f) { float h = r.width / aspect; r = new Rect(r.x, r.y + (r.height - h) * 0.5f, r.width, h); }
        else if (aspect < 1f) { float w = r.height * aspect; r = new Rect(r.x + (r.width - w) * 0.5f, r.y, w, r.height); }
        var prev = GUI.color;
        GUI.color = def.tint.a > 0f ? def.tint : Color.white;
        GUI.DrawTextureWithTexCoords(r, tex, uv, true);
        GUI.color = prev;
    }
}
