using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The spreadsheet view of the item catalog — one row per <see cref="Item"/>, the balance-relevant fields editable
/// inline (Undo-able, saved on demand). The item sibling of <see cref="MobTableWindow"/>.
///
/// <para><b>ONE table, opened once per bucket.</b> Weapons, Armor, Rings, Consumables, Materials, Quest Items and Misc
/// are the same window filtered to one <see cref="ItemType"/>, each showing only the columns that bucket actually has —
/// a weapon shows ATK and reach, a consumable shows the skill it runs, and neither wastes a column on the other's
/// blanks. Six near-identical windows would rot the moment one got a fix the others didn't, so there is exactly one
/// body here and a menu entry per bucket.</para>
///
/// <para>Every item lands in exactly one bucket (<see cref="ItemTypes.Of"/>) and Misc catches whatever the named
/// buckets don't, so no item can be invisible in all of them. "Type" is DERIVED from slot/category — editing it maps
/// back onto the real fields, so there is no duplicated value to hand-sync; changing it moves the row to another
/// table. Open via <c>Tools ▸ unwritten ▸ Catalogs ▸ Items ▸ …</c>.</para>
/// </summary>
public class ItemTableWindow : EditorWindow
{
    const float RowHeight = 34f;

    // ---- The buckets -----------------------------------------------------------------------------------------
    // Adding a table later is this list plus a menu entry — never another copy of the window.

    enum ColId { Icon, Id, Name, Type, Kind, Elem, Rarity, Atk, Def, MAtk, MDef, HP, MP, Reach, Sockets, Weight, Zeny, UseSkill, Select }

    struct Col
    {
        public ColId id; public string label, sort; public float w;
        public Col(ColId i, string l, string s, float wi) { id = i; label = l; sort = s; w = wi; }
    }

    static readonly Col[] All =
    {
        new Col(ColId.Icon,    "",        null,     38),
        new Col(ColId.Id,      "id",      "id",     150),
        new Col(ColId.Name,    "name",    "name",   120),
        new Col(ColId.Type,    "type",    "type",   92),
        new Col(ColId.Kind,    "kind",    null,     104),
        new Col(ColId.Elem,    "elem",    "elem",   78),
        new Col(ColId.Rarity,  "rarity",  "rarity", 84),
        new Col(ColId.Atk,     "ATK",     "atk",    46),
        new Col(ColId.Def,     "DEF",     "def",    46),
        new Col(ColId.MAtk,    "MATK",    null,     50),
        new Col(ColId.MDef,    "MDEF",    null,     50),
        new Col(ColId.HP,      "HP",      null,     46),
        new Col(ColId.MP,      "MP",      null,     46),
        new Col(ColId.Reach,   "reach",   null,     52),
        new Col(ColId.Sockets, "◇",       null,     32),
        new Col(ColId.Weight,  "wt",      null,     38),
        new Col(ColId.Zeny,    "zeny",    "zeny",   52),
        new Col(ColId.UseSkill,"runs skill", null,  200),
        new Col(ColId.Select,  "",        null,     28),
    };

    // Each bucket's columns, in draw order. Icon/id/name/select are on every one; the middle is what differs.
    static ColId[] ColumnsFor(ItemType t)
    {
        switch (t)
        {
            case ItemType.Weapon:
                return new[] { ColId.Icon, ColId.Id, ColId.Name, ColId.Kind, ColId.Elem, ColId.Rarity,
                               ColId.Atk, ColId.MAtk, ColId.Reach, ColId.Sockets, ColId.Weight, ColId.Zeny, ColId.Select };
            case ItemType.Armor:
                return new[] { ColId.Icon, ColId.Id, ColId.Name, ColId.Kind, ColId.Elem, ColId.Rarity,
                               ColId.Def, ColId.MDef, ColId.HP, ColId.MP, ColId.Sockets, ColId.Weight, ColId.Zeny, ColId.Select };
            case ItemType.Ring:
                return new[] { ColId.Icon, ColId.Id, ColId.Name, ColId.Rarity,
                               ColId.HP, ColId.MP, ColId.Sockets, ColId.Weight, ColId.Zeny, ColId.Select };
            case ItemType.Consumable:
                return new[] { ColId.Icon, ColId.Id, ColId.Name, ColId.Rarity, ColId.UseSkill,
                               ColId.Weight, ColId.Zeny, ColId.Select };
            case ItemType.Material:
                return new[] { ColId.Icon, ColId.Id, ColId.Name, ColId.Rarity, ColId.Weight, ColId.Zeny, ColId.Select };
            case ItemType.Quest:
                return new[] { ColId.Icon, ColId.Id, ColId.Name, ColId.Weight, ColId.Select };
            default:   // Misc — the catch-all, so it shows the coarse Type to explain why a row landed here
                return new[] { ColId.Icon, ColId.Id, ColId.Name, ColId.Type, ColId.Rarity, ColId.Weight, ColId.Zeny, ColId.Select };
        }
    }

    // ---- Menu ------------------------------------------------------------------------------------------------

    const string Root = "Tools/unwritten/Catalogs/Items/";
    [MenuItem(Root + "Weapons")]     static void OpenWeapons()     => Open(ItemType.Weapon);
    [MenuItem(Root + "Armor")]       static void OpenArmor()       => Open(ItemType.Armor);
    [MenuItem(Root + "Rings")]       static void OpenRings()       => Open(ItemType.Ring);
    [MenuItem(Root + "Consumables")] static void OpenConsumables() => Open(ItemType.Consumable);
    [MenuItem(Root + "Materials")]   static void OpenMaterials()   => Open(ItemType.Material);
    [MenuItem(Root + "Quest Items")] static void OpenQuest()       => Open(ItemType.Quest);
    [MenuItem(Root + "Misc")]        static void OpenMisc()        => Open(ItemType.Misc);

    static void Open(ItemType bucket)
    {
        // One window PER bucket, so two tables can sit side by side. Keyed by title, which is how GetWindow finds it.
        var w = CreateOrFocus(bucket);
        w._bucket = bucket;
        w.titleContent = new GUIContent(Title(bucket));
        // Width floor stays tiny on purpose: the window must be able to shrink to a sliver beside another one, and a
        // real minimum would hide every column past the greedy one.
        w.minSize = new Vector2(40, 320);
        w.Reload();
    }

    static ItemTableWindow CreateOrFocus(ItemType bucket)
    {
        foreach (var open in Resources.FindObjectsOfTypeAll<ItemTableWindow>())
            if (open._bucket == bucket) { open.Focus(); return open; }
        return CreateWindow<ItemTableWindow>();
    }

    static string Title(ItemType t) => t == ItemType.Quest ? "Quest Items" : t.ToString() + (t == ItemType.Misc ? "" : "s");

    // ---- State -----------------------------------------------------------------------------------------------

    [SerializeField] ItemType _bucket = ItemType.Weapon;   // survives a domain reload, so the window reopens as itself
    readonly List<Item> _items = new List<Item>();
    Vector2 _scroll, _hscroll;
    string _search = "";
    string _sortField = "id";
    bool _sortAsc = true;

    Col[] _cols;
    Col[] Cols
    {
        get
        {
            if (_cols == null || _cols.Length == 0)
            {
                var ids = ColumnsFor(_bucket);
                _cols = ids.Select(id => All.First(c => c.id == id)).ToArray();
            }
            return _cols;
        }
    }
    float TotalWidth => Cols.Sum(c => c.w) + 8;

    void Reload()
    {
        _cols = null;                       // bucket may have changed → rebuild the column set
        _items.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:Item"))
        {
            var it = AssetDatabase.LoadAssetAtPath<Item>(AssetDatabase.GUIDToAssetPath(guid));
            if (it != null && ItemTypes.Of(it) == _bucket) _items.Add(it);
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

    void OnGUI()
    {
        DrawToolbar();
        DrawHeader();

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        _hscroll = GUILayout.BeginScrollView(_hscroll, GUILayout.ExpandWidth(true));
        bool any = false;
        foreach (var it in _items)
        {
            if (!string.IsNullOrEmpty(_search) && !MatchesSearch(it)) continue;
            DrawRow(it);
            any = true;
        }
        if (!any)
            EditorGUILayout.LabelField(_items.Count == 0
                ? $"No {Title(_bucket).ToLowerInvariant()} authored yet."
                : "Nothing matches that search.", EditorStyles.centeredGreyMiniLabel);
        GUILayout.EndScrollView();
        EditorGUILayout.EndScrollView();
    }

    bool MatchesSearch(Item it)
    {
        string s = _search.ToLowerInvariant();
        return (it.id ?? "").ToLowerInvariant().Contains(s)
            || (it.displayName ?? "").ToLowerInvariant().Contains(s)
            || (it.useSkillId ?? "").ToLowerInvariant().Contains(s)
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
        Rect Field() => VCenter(Next(), 18);

        var curType = ItemTypes.Of(it);
        var newType = curType;
        var newWeaponKind = it.weaponType;
        var newSlotKind = it.slot;
        bool retypedOrMoved = false;

        EditorGUI.BeginChangeCheck();
        foreach (var c in Cols)
        {
            switch (c.id)
            {
                case ColId.Icon:    DrawIcon(new Rect(Next().x, rect.y + 2, 30, 30), it); break;
                case ColId.Id:      it.id = EditorGUI.TextField(Field(), it.id); break;
                case ColId.Name:    it.displayName = EditorGUI.TextField(Field(), it.displayName); break;
                case ColId.Type:    newType = (ItemType)EditorGUI.EnumPopup(Field(), curType); break;

                // Kind is context-sensitive: a weapon's kind is its weapon type, a piece of armor's is which slot it
                // fills. Neither question means anything for the bag buckets, which is why they don't show the column.
                case ColId.Kind:
                    if (curType == ItemType.Weapon)     newWeaponKind = (WeaponType)EditorGUI.EnumPopup(Field(), it.weaponType);
                    else if (curType == ItemType.Armor) newSlotKind   = (EquipSlotType)EditorGUI.EnumPopup(Field(), it.slot);
                    else EditorGUI.LabelField(Field(), "—", EditorStyles.centeredGreyMiniLabel);
                    break;

                case ColId.Elem:    it.element = (Element)EditorGUI.EnumPopup(Field(), it.element); break;
                case ColId.Rarity:  it.rarity = (Rarity)EditorGUI.EnumPopup(Field(), it.rarity); break;
                case ColId.Atk:     it.bonusAttack = EditorGUI.IntField(Field(), it.bonusAttack); break;
                case ColId.Def:     it.bonusDefense = EditorGUI.IntField(Field(), it.bonusDefense); break;
                case ColId.MAtk:    it.bonusMAtk = EditorGUI.IntField(Field(), it.bonusMAtk); break;
                case ColId.MDef:    it.bonusMDef = EditorGUI.IntField(Field(), it.bonusMDef); break;
                case ColId.HP:      it.bonusHP = EditorGUI.IntField(Field(), it.bonusHP); break;
                case ColId.MP:      it.bonusMP = EditorGUI.IntField(Field(), it.bonusMP); break;
                case ColId.Reach:   it.attackRange = EditorGUI.FloatField(Field(), it.attackRange); break;
                case ColId.Sockets: it.cardSlots = EditorGUI.IntField(Field(), it.cardSlots); break;
                case ColId.Weight:  it.weight = EditorGUI.IntField(Field(), it.weight); break;
                case ColId.Zeny:    it.zeny = EditorGUI.IntField(Field(), it.zeny); break;
                case ColId.UseSkill: DrawUseSkill(Field(), it); break;

                case ColId.Select:
                    if (GUI.Button(Field(), "◎")) { Selection.activeObject = it; EditorGUIUtility.PingObject(it); }
                    break;
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(it, "Edit Item");
            // Type wins if it changed (it remaps slot/category, which moves the row to another table); otherwise
            // honour the context-sensitive Kind edit.
            if (newType != curType) { ItemTypes.Apply(it, newType); retypedOrMoved = true; }
            else if (curType == ItemType.Weapon) it.weaponType = newWeaponKind;
            else if (curType == ItemType.Armor && newSlotKind != it.slot) { it.slot = newSlotKind; retypedOrMoved = true; }

            EditorUtility.SetDirty(it);
            // A row that no longer belongs in this bucket must leave it, or the table would show an item it claims not
            // to hold. Deferred to the next frame — reloading mid-draw changes the row count between layout passes.
            if (retypedOrMoved) EditorApplication.delayCall += Reload;
        }
    }

    // The skill a consumable runs. Typed as a plain id (that is how every other skill reference is stored), but
    // validated live against the catalog so a typo shows up here rather than as a silent no-op at the bag.
    void DrawUseSkill(Rect r, Item it)
    {
        var field = new Rect(r.x, r.y, r.width - 18, r.height);
        var mark = new Rect(r.xMax - 16, r.y, 16, r.height);
        it.useSkillId = EditorGUI.TextField(field, it.useSkillId);

        if (string.IsNullOrEmpty(it.useSkillId)) { EditorGUI.LabelField(mark, "—", EditorStyles.centeredGreyMiniLabel); return; }
        var def = SkillCatalog.Get(it.useSkillId);
        if (def == null)              EditorGUI.LabelField(mark, new GUIContent("✗", "No skill with that id."), EditorStyles.centeredGreyMiniLabel);
        else if (def.ItemUseEntry == null) EditorGUI.LabelField(mark, new GUIContent("!", "That skill has no On Item Used trigger, so using this item would do nothing."), EditorStyles.centeredGreyMiniLabel);
        else                          EditorGUI.LabelField(mark, new GUIContent("✓", def.displayName), EditorStyles.centeredGreyMiniLabel);
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
