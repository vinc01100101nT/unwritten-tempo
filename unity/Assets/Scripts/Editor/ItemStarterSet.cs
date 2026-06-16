using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Generates a small set of <see cref="Item"/> assets in Assets/Items so the bag
/// has something to drag. Uses real art where it exists — weapons + consumables
/// from Art/NinjaAdventure/Items, and the UI equipment icons for armour slots
/// (the pack has no armour item art). Idempotent: skips items that already exist.
///
/// Menu: Tools ▸ unwritten ▸ Create Starter Items. Build UI Shell also calls this
/// so the bag is seeded automatically.
/// </summary>
public static class ItemStarterSet
{
    const string ART = "Assets/Art/NinjaAdventure";
    // In Resources so BagSeeder can load them at runtime (no baked prefab references).
    const string Dir = "Assets/Resources/Items";

    // (asset name, sprite path, equip slot, +HP, +ATK, +DEF, heal, flavor description)
    static readonly (string name, string sprite, EquipSlotType slot, int hp, int atk, int def, int heal, string desc)[] Defs =
    {
        ("Iron Sword",  ART + "/Items/Weapons/Sword/Sprite.png",  EquipSlotType.Weapon,  0, 3, 0, 0, "A dependable blade. Nothing fancy, always sharp."),
        ("Katana",      ART + "/Items/Weapons/Katana/Sprite.png", EquipSlotType.Weapon,  0, 5, 0, 0, "Folded steel with a wicked edge."),
        ("Sai",         ART + "/Items/Weapons/Sai/Sprite.png",    EquipSlotType.Weapon,  0, 2, 1, 0, "Quick to strike, quick to parry."),
        ("Iron Helm",   ART + "/UI/Skill Icon/Items & Weapon/Helmet.png", EquipSlotType.Head,    4, 0, 2, 0, "Dented but sturdy headgear."),
        ("Leather Vest",ART + "/UI/Skill Icon/Items & Weapon/Armor.png",  EquipSlotType.Body,    8, 0, 3, 0, "Boiled leather that turns a blow."),
        ("Boots",       ART + "/UI/Skill Icon/Items & Weapon/Boot.png",   EquipSlotType.Boots,   2, 0, 1, 0, "Worn travelling boots."),
        ("Wooden Guard",ART + "/UI/Skill Icon/Items & Weapon/Guard.png",  EquipSlotType.OffHand, 0, 0, 2, 0, "A simple off-hand buckler."),
        ("Jade Amulet", ART + "/UI/Skill Icon/Items & Weapon/Amulet.png", EquipSlotType.Trinket, 6, 1, 0, 0, "A warm green stone that steadies the heart."),
        ("Silver Ring", ART + "/UI/Skill Icon/Items & Weapon/Ring.png",   EquipSlotType.Trinket, 0, 2, 0, 0, "A plain band that sharpens your focus."),
        ("Life Potion", ART + "/Items/Potion/LifePot.png",       EquipSlotType.None, 0, 0, 0, 10, "Bittersweet brew that knits wounds."),
        ("Gold Coin",   ART + "/Items/Treasure/GoldCoin.png",    EquipSlotType.None, 0, 0, 0, 0, "Currency of the realm. (Shops come later.)"),
        ("Fire Scroll", ART + "/Items/Scroll/ScrollFire.png",    EquipSlotType.None, 0, 0, 0, 0, "Crackles with unspent flame. (Usable later.)"),
    };

    [MenuItem("Tools/unwritten/Create Starter Items")]
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
            item.slot = d.slot;
            item.icon = LoadSprite(d.sprite);
            item.bonusHP = d.hp;
            item.bonusAttack = d.atk;
            item.bonusDefense = d.def;
            item.healAmount = d.heal;
            item.description = d.desc;

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

    static Sprite LoadSprite(string path)
        => AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        var leaf = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
