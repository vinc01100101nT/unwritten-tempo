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
    readonly Dictionary<MonsterDef, Texture2D> _sheets = new Dictionary<MonsterDef, Texture2D>();
    Vector2 _scroll, _hscroll;
    string _search = "";
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
            "tier"   => (a, b) => a.tier.CompareTo(b.tier),
            "hp"     => (a, b) => a.maxHealth.CompareTo(b.maxHealth),
            "atk"    => (a, b) => a.attack.CompareTo(b.attack),
            "weight" => (a, b) => a.weight.CompareTo(b.weight),
            "xp"     => (a, b) => a.xpReward.CompareTo(b.xpReward),
            "elem"   => (a, b) => a.element.CompareTo(b.element),
            _        => (a, b) => string.Compare(a.id, b.id, System.StringComparison.OrdinalIgnoreCase),
        };
        _defs.Sort((a, b) => (_sortAsc ? 1 : -1) * cmp(a, b));
    }

    Texture2D SheetFor(MonsterDef def)
    {
        if (_sheets.TryGetValue(def, out var t)) return t;
        t = string.IsNullOrEmpty(def.sheet) ? null : RigBuilder25D.MonsterSheet(def.sheet);
        _sheets[def] = t;
        return t;
    }

    // --- column layout (label, sortField or null, width) ---
    struct Col { public string label, sort; public float w; public Col(string l, string s, float wi) { label = l; sort = s; w = wi; } }
    static readonly Col[] Cols =
    {
        new Col("",      null,     42),   // preview
        new Col("id",    "id",     150),
        new Col("name",  "name",   120),
        new Col("biome", "biome",  90),
        new Col("tier",  "tier",   38),
        new Col("B",     null,     26),   // isBoss
        new Col("HP",    "hp",     58),
        new Col("DEF",   null,     46),
        new Col("MDEF",  null,     50),
        new Col("ATK",   "atk",    46),
        new Col("HIT",   null,     46),
        new Col("FLEE",  null,     46),
        new Col("elem",  "elem",   84),
        new Col("aElem", null,     84),
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
        }
        GUILayout.EndScrollView();
        EditorGUILayout.EndScrollView();
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

        // preview
        var pr = Next();
        DrawPreview(new Rect(pr.x, rect.y + 2, 34, 34), def);

        EditorGUI.BeginChangeCheck();
        string id    = EditorGUI.TextField(VCenter(Next(), 18), def.id);
        string name  = EditorGUI.TextField(VCenter(Next(), 18), def.displayName);
        // Biome.None = biome-agnostic (spawns in any biome); it's just another choice in the dropdown.
        var biome    = (Biome)EditorGUI.EnumPopup(VCenter(Next(), 18), def.biome);
        int tier     = EditorGUI.IntField(VCenter(Next(), 18), def.tier);
        bool boss    = EditorGUI.Toggle(VCenter(Next(), 18), def.isBoss);
        int hp       = EditorGUI.IntField(VCenter(Next(), 18), def.maxHealth);
        int dfn      = EditorGUI.IntField(VCenter(Next(), 18), def.defense);
        int mdfn     = EditorGUI.IntField(VCenter(Next(), 18), def.magicDefense);
        int atk      = EditorGUI.IntField(VCenter(Next(), 18), def.attack);
        int hit      = EditorGUI.IntField(VCenter(Next(), 18), def.hit);
        int flee     = EditorGUI.IntField(VCenter(Next(), 18), def.flee);
        var elem     = (Element)EditorGUI.EnumPopup(VCenter(Next(), 18), def.element);
        var aElem    = (Element)EditorGUI.EnumPopup(VCenter(Next(), 18), def.attackElement);
        int wt       = EditorGUI.IntField(VCenter(Next(), 18), def.weight);
        int xp       = EditorGUI.IntField(VCenter(Next(), 18), def.xpReward);
        int gMin     = EditorGUI.IntField(VCenter(Next(), 18), def.goldMin);
        int gMax     = EditorGUI.IntField(VCenter(Next(), 18), def.goldMax);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(def, "Edit Mob");
            def.id = id; def.displayName = name; def.biome = biome; def.tier = Mathf.Clamp(tier, 1, 4);
            def.isBoss = boss; def.maxHealth = hp; def.defense = dfn;
            def.magicDefense = mdfn; def.attack = atk; def.hit = hit; def.flee = flee;
            def.element = elem; def.attackElement = aElem; def.weight = wt; def.xpReward = xp;
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

    void DrawPreview(Rect r, MonsterDef def)
    {
        var tex = SheetFor(def);
        if (tex == null) { GUI.Label(r, "?", EditorStyles.centeredGreyMiniLabel); return; }

        // Front-facing frame = top-left cell of the sheet (column 0, top row). Unity tex coords are
        // bottom-left origin, so the top row sits at the top of the [0,1] V range.
        float c = Mathf.Min(Cell, Mathf.Min(tex.width, tex.height));
        var uv = new Rect(0f, 1f - c / tex.height, c / tex.width, c / tex.height);
        var prev = GUI.color;
        GUI.color = def.tint.a > 0f ? def.tint : Color.white;
        GUI.DrawTextureWithTexCoords(r, tex, uv, true);
        GUI.color = prev;
    }
}
