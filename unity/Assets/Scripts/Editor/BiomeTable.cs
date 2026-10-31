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
    [Tooltip("The road tile for this biome. Empty = the map recipe's own road choice.")]
    public Texture2D roadTile;
    [Tooltip("Colour multiplied over the whole ground. White = the tiles exactly as painted.")]
    public Color tint = Color.white;
    [Tooltip("How much of the ground is patch tile: 0 = almost none, 1 = mostly patch. Below 0 = use the map recipe's own amount.")]
    public float patchAmount = -1f;
    [Tooltip("How high the ground rises from its lowest to its highest point, in world units (a character is 0.5 tall). " +
             "0 = flat ground. Never steeper than the steepest slope below: where it would be, the higher ground's edge is " +
             "cut back into a ramp of that slope when the map is built.")]
    [Min(0f)] public float bumpHeight = 0f;
    [Tooltip("How wide one hill is, in world units. Wider = gentler.")]
    [Min(1f)] public float bumpWidth = 10f;
    [Tooltip("How many flat levels the ground settles into, joined by walkable slopes. 0 = smooth rolling mounds instead.")]
    [Min(0)] public int bumpLevels = 0;
    [Tooltip("The steepest slope the ground may have, in degrees. Players and creatures walk slopes up to 45 degrees; " +
             "anything steeper is a wall to both.")]
    [Range(1f, 40f)] public float bumpSteepestDegrees = 5.4f;
    [Tooltip("Whether this biome's fields line their roads with rock cliffs. How tall they stand, how wide the pass is and " +
             "how many meeting points they have come from the map's recipe, which can also turn them on or off for itself.")]
    public bool cliffs = false;
    [Tooltip("The colour of this biome's cliff faces, drawn lighter toward the top and darker toward the foot. Fully " +
             "transparent (alpha 0) = not chosen: the face is drawn with the rock tile instead.")]
    public Color cliffFaceColor = new Color(0f, 0f, 0f, 0f);

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

    /// <summary>How many of its own <paramref name="category"/> props this biome places, as a multiple of the map
    /// recipe's count; 1 when the biome leaves that category to the pack list.</summary>
    public float CountMultiplier(PropCategory category)
    {
        foreach (var p in props)
            if (p != null && p.category == category) return p.countMultiplier;
        return 1f;
    }
}

[System.Serializable]
public class BiomeProps
{
    public PropCategory category;
    public List<GameObject> prefabs = new List<GameObject>();
    [Tooltip("How many of these props the biome places, as a multiple of the map recipe's own count for this category: 1 = as the recipe says, 0.5 = half, 3 = three times.")]
    [Min(0f)] public float countMultiplier = 1f;
}
