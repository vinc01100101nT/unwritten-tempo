using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// What every <see cref="Biome"/> looks like — the ONE place the map generator reads a biome's ground tiles, colour
/// wash, road and props from. A new biome is one row here (plus its name appended to <see cref="Biome"/>); nothing
/// else in code changes. The design behind each row is docs/3-world/biomes.md.
/// </summary>
public class BiomeTable : ScriptableObject
{
    public const string AssetPath = "Assets/3_Data/Recipes/BiomeTable.asset";

    public List<BiomeRow> rows = new List<BiomeRow>();

    public static BiomeTable Load() => AssetDatabase.LoadAssetAtPath<BiomeTable>(AssetPath);

    /// <summary>The row for <paramref name="biome"/>, or null when the table has none.</summary>
    public BiomeRow For(Biome biome)
    {
        foreach (var r in rows)
            if (r != null && r.biome == biome) return r;
        return null;
    }
}

[System.Serializable]
public class BiomeRow
{
    public Biome biome;

    [Tooltip("The main ground, laid everywhere that is not a patch or a road. A tile from the ground tile folder.")]
    public Texture2D baseTile;
    [Tooltip("The ground sprinkled in organic patches over the main ground. A tile from the ground tile folder.")]
    public Texture2D patchTile;
    [Tooltip("The road tile for this biome. Empty = the map recipe's own road choice. A road the World Creator sets on a map still wins over this.")]
    public Texture2D roadTile;
    [Tooltip("Colour multiplied over the whole ground. White = the tiles exactly as painted.")]
    public Color tint = Color.white;
    [Tooltip("How much of the ground is patch tile: 0 = almost none, 1 = mostly patch. Below 0 = use the map recipe's own amount.")]
    public float patchAmount = -1f;

    [Tooltip("Which props a recipe's prop category places in this biome. A category not listed uses the full pack list; a category listed with no prefabs places nothing.")]
    public List<BiomeProps> props = new List<BiomeProps>();

    /// <summary>This biome's prefabs for <paramref name="category"/>; false when the biome leaves that category to the pack list.</summary>
    public bool TryGetProps(PropCategory category, out List<GameObject> prefabs)
    {
        foreach (var p in props)
            if (p != null && p.category == category) { prefabs = p.prefabs; return true; }
        prefabs = null;
        return false;
    }
}

[System.Serializable]
public class BiomeProps
{
    public PropCategory category;
    public List<GameObject> prefabs = new List<GameObject>();
}
