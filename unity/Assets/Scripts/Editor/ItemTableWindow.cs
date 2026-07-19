using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A single spreadsheet-style view of every <see cref="Item"/> in the project — one item per row, icon preview plus the
/// balance-relevant fields editable inline (Undo-able, saved on demand). The item sibling of <see cref="MobTableWindow"/>.
///
/// <para>"Type" is the coarse bucket DERIVED from slot/category (<see cref="ItemTypes"/>) — editing it maps back onto
/// the real fields, so there's no duplicated value to hand-sync. "Kind" is context-sensitive: the weapon type for a
/// weapon, the equip slot for armor, blank otherwise. Open via <c>Tools ▸ unwritten ▸ Items ▸ Item Table</c>.</para>
/// </summary>
public class ItemTableWindow : EditorWindow
{
    const string MenuPath = "Tools/unwritten/Items/Item Table";
    const float RowHeight = 34f;

    [MenuItem(MenuPath)]
    static void Open()
    {
        var w = GetWindow<ItemTableWindow>("Item Table");
        w.minSize = new Vector2(40, 320);
        w.Reload();
    }

    readonly List<Item> _items = new List<Item>();
    Vector2 _scroll, _hscroll;
    string _search = "";
    string _sortField = "id";
    bool _sortAsc = true;

    void Reload()
    {
        _items.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:Item"))
        {
            var it = AssetDatabase.LoadAssetAtPath<Item>(AssetDatabase.GUIDToAssetPath(guid));
            if (it != null) _items.Add(it);
        }
        Resort();
        Repaint();
    }

    void Resort()
    {
        System.Comparison<Item> cmp = _sortField switch
        {
            "name"   => (a, b) => string.Compare(a.displayName, b.displayName, System.StringComparison.OrdinalIgnoreCase),
            "type"   => (a, b) => ItemTypes.Of(a).CompareTo(ItemTypes.Of(b)),
            "atk"    => (a, b) => a.bonusAttack.CompareTo(b.bonusAttack),
            "def"    => (a, b) => a.bonusDefense.CompareTo(b.bonusDefense),
            "elem"   => (a, b) => a.element.CompareTo(b.element),
            "rarity" => (a, b) => a.rarity.CompareTo(b.rarity),
            "zeny"   => (a, b) => a.zeny.CompareTo(b.zeny),
            _        => (a, b) => string.Compare(a.id, b.id, System.StringComparison.OrdinalIgnoreCase),
        };
        _items.Sort((a, b) => (_sortAsc ? 1 : -1) * cmp(a, b));
    }

    struct Col { public string label, sort; public float w; public Col(string l, string s, float wi) { label = l; sort = s; w = wi; } }
    static readonly Col[] Cols =
    {
        new Col("",       null,     38),   // icon
        new Col("id",     "id",     150),
        new Col("name",   "name",   120),
        new Col("type",   "type",   92),
        new Col("kind",   null,     104),
        new Col("elem",   "elem",   78),
        new Col("rarity", "rarity", 84),
        new Col("ATK",    "atk",    46),
        new Col("DEF",    "def",    46),
        new Col("MATK",   null,     50),
        new Col("MDEF",   null,     50),
        new Col("HP",     null,     46),
        new Col("wt",     null,     38),
        new Col("zeny",   "zeny",   52),
        new Col("heal",   null,     46),
        new Col("",       null,     28),   // select asset
    };
    static float TotalWidth => Cols.Sum(c => c.w) + 8;

    void OnGUI()
    {
        DrawToolbar();
        DrawHeader();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        _hscroll = GUILayout.BeginScrollView(_hscroll, GUILayout.ExpandWidth(true));
        foreach (var it in _items)
        {
            if (!string.IsNullOrEmpty(_search) && !MatchesSearch(it)) continue;
            DrawRow(it);
        }
        GUILayout.EndScrollView();
        EditorGUILayout.EndScrollView();
    }

    bool MatchesSearch(Item it)
    {
        string s = _search.ToLowerInvariant();
        return (it.id ?? "").ToLowerInvariant().Contains(s)
            || (it.displayName ?? "").ToLowerInvariant().Contains(s)
            || ItemTypes.Of(it).ToString().ToLowerInvariant().Contains(s)
            || it.weaponType.ToString().ToLowerInvariant().Contains(s)
            || it.element.ToString().ToLowerInvariant().Contains(s);
    }

    void DrawToolbar()
    {
        using (var flow = new EditorLayoutFlow.Flow(position.width, EditorStyles.toolbar))
        {
            flow.Item(70);
            GUILayout.Label($"{_items.Count} items", EditorStyles.miniLabel, GUILayout.Width(70));
            flow.Item(44 + 180);   // "Search" label + field kept together
            GUILayout.Label("Search", EditorStyles.miniLabel, GUILayout.Width(44));
            _search = GUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(180));
            flow.Item(70);
            if (GUILayout.Button("Save All", EditorStyles.toolbarButton, GUILayout.Width(70))) AssetDatabase.SaveAssets();
            flow.Item(60);
            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60))) Reload();
        }
    }

    void DrawHeader()
    {
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

    void DrawRow(Item it)
    {
        var rect = EditorGUILayout.GetControlRect(false, RowHeight, GUILayout.Width(TotalWidth));
        if (Mathf.FloorToInt(rect.y / RowHeight) % 2 == 0)
            EditorGUI.DrawRect(rect, new Color(1, 1, 1, 0.03f));

        float x = rect.x;
        int ci = 0;
        Rect Next() { var r = new Rect(x, rect.y + 3, Cols[ci].w - 3, RowHeight - 6); x += Cols[ci].w; ci++; return r; }

        var iconRect = Next();
        DrawIcon(new Rect(iconRect.x, rect.y + 2, 30, 30), it);

        var curType = ItemTypes.Of(it);

        EditorGUI.BeginChangeCheck();
        string id   = EditorGUI.TextField(VCenter(Next(), 18), it.id);
        string name = EditorGUI.TextField(VCenter(Next(), 18), it.displayName);
        var newType = (ItemType)EditorGUI.EnumPopup(VCenter(Next(), 18), curType);

        // Kind: weapon → weaponType; armor → equip slot; consumable/misc → blank. Captured for the apply block.
        var kindRect = VCenter(Next(), 18);
        WeaponType newWeaponKind = it.weaponType;
        EquipSlotType newSlotKind = it.slot;
        if (curType == ItemType.Weapon)      newWeaponKind = (WeaponType)EditorGUI.EnumPopup(kindRect, it.weaponType);
        else if (curType == ItemType.Armor)  newSlotKind   = (EquipSlotType)EditorGUI.EnumPopup(kindRect, it.slot);
        else EditorGUI.LabelField(kindRect, "—", EditorStyles.centeredGreyMiniLabel);

        var elem   = (Element)EditorGUI.EnumPopup(VCenter(Next(), 18), it.element);
        var rarity = (Rarity)EditorGUI.EnumPopup(VCenter(Next(), 18), it.rarity);
        int atk    = EditorGUI.IntField(VCenter(Next(), 18), it.bonusAttack);
        int def    = EditorGUI.IntField(VCenter(Next(), 18), it.bonusDefense);
        int matk   = EditorGUI.IntField(VCenter(Next(), 18), it.bonusMAtk);
        int mdef   = EditorGUI.IntField(VCenter(Next(), 18), it.bonusMDef);
        int hp     = EditorGUI.IntField(VCenter(Next(), 18), it.bonusHP);
        int wt     = EditorGUI.IntField(VCenter(Next(), 18), it.weight);
        int zeny   = EditorGUI.IntField(VCenter(Next(), 18), it.zeny);
        int heal   = EditorGUI.IntField(VCenter(Next(), 18), it.healAmount);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(it, "Edit Item");
            it.id = id; it.displayName = name; it.element = elem; it.rarity = rarity;
            it.bonusAttack = atk; it.bonusDefense = def; it.bonusMAtk = matk; it.bonusMDef = mdef;
            it.bonusHP = hp; it.weight = wt; it.zeny = zeny; it.healAmount = heal;

            // Type wins if it changed (remaps slot/category); otherwise honor the context-sensitive Kind edit.
            if (newType != curType) ItemTypes.Apply(it, newType);
            else if (curType == ItemType.Weapon) it.weaponType = newWeaponKind;
            else if (curType == ItemType.Armor)  it.slot = newSlotKind;

            EditorUtility.SetDirty(it);
        }

        if (GUI.Button(VCenter(Next(), 18), "◎"))
        {
            Selection.activeObject = it;
            EditorGUIUtility.PingObject(it);
        }
    }

    static Rect VCenter(Rect r, float h) => new Rect(r.x, r.y + (r.height - h) * 0.5f, r.width, h);

    void DrawIcon(Rect r, Item it)
    {
        var sp = it.icon;
        if (sp == null) { GUI.Label(r, "?", EditorStyles.centeredGreyMiniLabel); return; }
        var tex = sp.texture;
        if (tex == null) { GUI.Label(r, "?", EditorStyles.centeredGreyMiniLabel); return; }
        var rc = sp.rect;
        var uv = new Rect(rc.x / tex.width, rc.y / tex.height, rc.width / tex.width, rc.height / tex.height);
        GUI.DrawTextureWithTexCoords(r, tex, uv, true);
    }
}
