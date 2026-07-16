using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates a small set of <see cref="Item"/> assets in Assets/3_Data/Resources/Items so the bag has
/// something to drag — this is the DATA table (id/slot/stats/rarity). Icons are supplied SEPARATELY
/// by <see cref="ItemIconImporter"/> from GPT-authored PNGs in Assets/1_Authored/Art/Items (no vendor art baked
/// in here). Idempotent: re-running refreshes stats on existing assets and leaves their icons alone.
///
/// Menu: Tools ▸ unwritten ▸ Create Starter Items. Build UI Shell also calls this
/// so the bag is seeded automatically.
/// </summary>
public static class ItemStarterSet
{
    // In Resources so BagSeeder can load them at runtime (no baked prefab references).
    const string Dir = "Assets/3_Data/Resources/Items";

    // (asset name, id, slot, +HP, +ATK, +DEF, heal, rarity, cardSlots, weight, zeny, category, description)
    static readonly (string name, string id, EquipSlotType slot, WeaponType weapon, int hp, int atk, int def, int heal,
                     Rarity rarity, int cards, int weight, int zeny, ItemCategory cat, string desc)[] Defs =
    {
        ("Iron Sword",  "item.iron-sword",   EquipSlotType.Weapon, WeaponType.Sword,  0, 3, 0, 0, Rarity.Common,    2,  90, 5000,  ItemCategory.Equip, "A dependable blade. Nothing fancy, always sharp."),
        ("Katana",      "item.katana",       EquipSlotType.Weapon, WeaponType.Sword,  0, 5, 0, 0, Rarity.Rare,      3, 110, 22000, ItemCategory.Equip, "Folded steel with a wicked edge."),
        ("Sai",         "item.sai",          EquipSlotType.Weapon, WeaponType.Dagger, 0, 2, 1, 0, Rarity.Uncommon,  1,  70, 3200,  ItemCategory.Equip, "Quick to strike, quick to parry."),
        ("Iron Helm",   "item.iron-helm",    EquipSlotType.Head,    WeaponType.None,  4, 0, 2, 0, Rarity.Common,   1,  40,  600,  ItemCategory.Equip, "Dented but sturdy headgear."),
        ("Leather Vest","item.leather-vest", EquipSlotType.Armor,   WeaponType.None,  8, 0, 3, 0, Rarity.Uncommon, 1, 120, 2400,  ItemCategory.Equip, "Boiled leather that turns a blow."),
        ("Boots",       "item.boots",        EquipSlotType.Boots,   WeaponType.None,  2, 0, 1, 0, Rarity.Common,   0,  60,  900,  ItemCategory.Equip, "Worn travelling boots."),
        ("Wooden Guard","item.wooden-guard", EquipSlotType.Shield,  WeaponType.None,  0, 0, 2, 0, Rarity.Common,   0, 130, 1100,  ItemCategory.Equip, "A simple off-hand buckler."),
        ("Jade Amulet", "item.jade-amulet",  EquipSlotType.Accessory1, WeaponType.None, 6, 1, 0, 0, Rarity.Epic, 1,  10, 9000,  ItemCategory.Equip, "A warm green stone that steadies the heart."),
        ("Silver Ring", "item.silver-ring",  EquipSlotType.Accessory2, WeaponType.None, 0, 2, 0, 0, Rarity.Rare, 1,  10, 4200,  ItemCategory.Equip, "A plain band that sharpens your focus."),
        ("Dawnbreaker", "item.dawnbreaker",  EquipSlotType.Weapon, WeaponType.TwoHandSword, 0, 9, 0, 0, Rarity.Legendary, 3, 130, 0, ItemCategory.Equip, "Named drop from the Sunken Throne. Screenshot-worthy."),
        ("Ring of Beginnings", "item.ring-of-beginnings", EquipSlotType.Ring, WeaponType.None, 0, 0, 0, 0, Rarity.Rare, 0, 5, 0, ItemCategory.Equip, "Your life's-work vessel. It carries the ring you walk — its passives and skills only live while it's worn."),
        ("Life Potion", "item.life-potion",  EquipSlotType.None, WeaponType.None, 0, 0, 0, 10, Rarity.Common,   0,   7,   50,  ItemCategory.Use,      "Bittersweet brew that knits wounds."),
        ("Iron Ore",    "item.iron-ore",     EquipSlotType.None, WeaponType.None, 0, 0, 0, 0,  Rarity.Common,   0,  15,   80,  ItemCategory.Material, "Refining material."),
        ("Fire Scroll", "item.fire-scroll",  EquipSlotType.None, WeaponType.None, 0, 0, 0, 0,  Rarity.Uncommon, 0,  10,    0,  ItemCategory.Etc,      "Crackles with unspent flame. (Usable later.)"),
    };

    [MenuItem("Tools/unwritten/Scene Setup/Create Starter Items", priority = 130)]
    static void CreateMenu()
    {
        int n = EnsureStarterItems().Count;
        Debug.Log($"[unwritten] Starter items ready in {Dir} ({n} total). Run Build UI Shell to seed the bag.");
    }

    /// <summary>Creates or updates the starter items and returns the full set (sorted).
    /// Re-applies stats to existing assets so older items (made before Phase F's stat
    /// fields existed) pick up their +ATK/+HP/heal values when you re-run this.</summary>
    public static List<Item> EnsureStarterItems()
    {
        EnsureFolder(Dir);
        foreach (var d in Defs)
        {
            string path = $"{Dir}/{d.name}.asset";
            var item = AssetDatabase.LoadAssetAtPath<Item>(path);
            bool isNew = item == null;
            if (isNew) item = ScriptableObject.CreateInstance<Item>();

            item.displayName = d.name;
            item.id = d.id;
            if (isNew) item.icon = null;   // icons come from ItemIconImporter (GPT art); don't clobber a hand-set one
            item.slot = d.slot;
            item.weaponType = d.weapon;
            item.bonusHP = d.hp;
            item.bonusAttack = d.atk;
            item.bonusDefense = d.def;
            item.healAmount = d.heal;
            item.rarity = d.rarity;
            item.cardSlots = d.cards;
            item.weight = d.weight;
            item.zeny = d.zeny;
            item.category = d.cat;
            item.description = d.desc;

            // Equipment-specific combat audio (overrides the player defaults while equipped).
            if (d.slot == EquipSlotType.Weapon)
            {
                item.attackSfx = SfxAssets.Set(0.8f, SfxAssets.SwordDir, "Sword Attack 1", "Sword Attack 2", "Sword Attack 3");
                item.hitSfx    = SfxAssets.Set(0.9f, SfxAssets.SwordDir, "Sword Impact Hit 1", "Sword Impact Hit 2", "Sword Impact Hit 3");
            }
            else if (d.slot == EquipSlotType.Shield)
            {
                item.guardSfx = SfxAssets.Set(0.9f, SfxAssets.SwordDir, "Sword Blocked 1", "Sword Blocked 2", "Sword Blocked 3");
                item.blockChance = 0.25f;   // a buckler blocks ~1 in 4 hits
            }

            if (isNew) AssetDatabase.CreateAsset(item, path);
            else EditorUtility.SetDirty(item);   // persist the refreshed stats
        }
        AssetDatabase.SaveAssets();

        return AssetDatabase.FindAssets("t:Item", new[] { Dir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Item>)
            .Where(x => x != null)
            .OrderBy(x => x.name)
            .ToList();
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        var leaf = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
