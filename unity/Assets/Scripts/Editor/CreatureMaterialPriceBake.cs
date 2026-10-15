using System.Text;
using UnityEditor;

/// <summary>
/// WRITES EVERY CREATURE MATERIAL'S PRICE INTO ITS ITEM FILE — the saved number the shop pays and the tooltip shows
/// (<see cref="Item.zeny"/>), worked out by the one rule
/// (<see cref="CreatureMaterials.SellValue(int, Rarity, Quality, int)"/>) from its creature's level, the material's
/// tier and the grade and folds of the creature's gear.
///
/// <para>Run it after changing any creature's level, grade or folds, or the price rule:
/// <c>CreatureMaterialPriceBake.BakeAll(true)</c> through the live editor. <c>BakeAll(false)</c> only reports what it
/// would write. It has no menu item and nothing calls it by itself: a saved file changes only because somebody ran
/// it. <c>tools/check-creature-materials.py</c> names every price that no longer agrees with the rule.</para>
/// </summary>
public static class CreatureMaterialPriceBake
{
    public static string BakeAll(bool write)
    {
        var said = new StringBuilder();
        int prices = 0;
        foreach (var d in MonsterCatalog.All)
        {
            if (d == null) continue;
            prices += Price(d, d.material, write, said);
        }
        if (write) AssetDatabase.SaveAssets();
        said.Append(write ? "wrote " : "would write ").Append(prices).Append(" price(s)");
        return said.ToString();
    }

    static int Price(MonsterDef d, Item material, bool write, StringBuilder said)
    {
        if (!CreatureMaterials.Is(material)) return 0;
        // THE RULE NAMES WHAT A SHOP PAYS; the item keeps the value that pays exactly that once the Tax is taken.
        int sells = CreatureMaterials.SellValue(d.level, CreatureMaterials.TierOfStep(material.materialStep), d.gearQuality, d.gearFolds);
        int price = ShopMath.ValueForSellPrice(sells);
        if (material.value == price) return 0;
        said.Append(material.displayName).Append(": ").Append(material.value).Append(" -> ").Append(price).Append('\n');
        if (write) { material.value = price; EditorUtility.SetDirty(material); }
        return 1;
    }
}
