using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Tilemaps;

/// <summary>
/// The art palette + content pool for a town theme (grassland village, desert oasis, …).
/// A <see cref="MapRecipe"/> points at one of these; the generator pulls tiles, house
/// templates, and monster prefabs from it. Create via Assets ▸ Create ▸ unwritten ▸ Town Theme.
/// </summary>
[CreateAssetMenu(menuName = "unwritten/Town Theme", fileName = "TownTheme")]
public class TownTheme : ScriptableObject
{
    [Header("Ground (→ 'Ground' tilemap)")]
    [Tooltip("Grass/dirt base tiles — one picked per cell for variety.")]
    public List<TileBase> groundTiles = new List<TileBase>();

    [Header("Roads (→ 'Roads' tilemap, walkable, sorts above ground)")]
    public List<TileBase> roadTiles = new List<TileBase>();

    [Header("Auto-tiling floor terrains (RuleTiles — built by Tools ▸ unwritten ▸ Build Floor RuleTiles)")]
    // Typed as TileBase (not RuleTile) so this runtime script needs no 2D-Extras assembly reference;
    // RuleTile inherits TileBase, so the Inspector still accepts the generated RuleTile assets.
    [Tooltip("Optional. Drag a Floor RuleTile (e.g. Dirt) here and roads/paths use it instead of the flat " +
             "roadTiles above — 1-wide spokes become a path that auto-edges into the surrounding ground.")]
    public TileBase pathTile;
    [Tooltip("Optional. Floor RuleTiles scattered as organic patches on the Ground layer (Sand / Snow / " +
             "Ice / …). Each patch auto-edges against the base ground. Empty = no patches.")]
    public List<TileBase> patchTerrains = new List<TileBase>();

    [Header("Props (multi-cell objects placed as whole units)")]
    [Tooltip("Every discrete object the town places — houses, trees, wells, statues, stalls — " +
             "captured via Tools ▸ unwritten ▸ Capture Prop Template. Each carries its own " +
             "placement (Lot / OpenGround / PlazaCenter / Roadside), so this ONE list drives all " +
             "object placement. Trees are props too — capture them and tag placement OpenGround.")]
    [FormerlySerializedAs("houses")]   // carries the old houses list into props (same script GUID)
    public List<PropTemplate> props = new List<PropTemplate>();

    [Header("Boundary")]
    [Tooltip("Edge-ring tiles for the town border, used by every MapRecipe.boundary style: " +
             "Treeline lays them on the walk-under Obstacles layer; Fence/Wall/Water make them " +
             "solid. (Trees, bushes and other objects live in Props above as PropTemplates.)")]
    public List<TileBase> boundaryTiles = new List<TileBase>();

    [Header("Decoration (→ 'Decor' tilemap, no collision)")]
    [Tooltip("Flowers, pebbles, path detail scattered along roads / in yards.")]
    public List<TileBase> decorTiles = new List<TileBase>();

    [Header("Monsters (optional perimeter spawners)")]
    public List<GameObject> monsterPrefabs = new List<GameObject>();
}
