using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The <b>3D</b> palette for the town baker: which Kenney <i>prefab</i> represents each cell-kind
/// (ground / road / boundary) and each placeable object (<see cref="Prop3D"/>). It's the 3D analogue
/// of <see cref="TownTheme"/> (which is 2D tiles + sprite PropTemplates) — the layout algorithm
/// (<see cref="TownGenerator"/>) is shared; only the backend that turns a <see cref="TownLayout"/>
/// into scene objects differs. Create via Assets ▸ Create ▸ unwritten ▸ Town 3D Theme.
/// </summary>
[CreateAssetMenu(menuName = "unwritten/Town 3D Theme", fileName = "Town3DTheme")]
public class Town3DTheme : ScriptableObject
{
    [Header("Terrain prefabs")]
    [Tooltip("Tiled across every cell as the ground (e.g. a Kenney grass block).")]
    public GameObject groundPrefab;
    [Tooltip("Placed on road/path cells instead of ground. Null = roads just show ground.")]
    public GameObject roadPrefab;
    [Tooltip("Placed on the town's edge ring (e.g. a fence/wall piece). Used only when Scenic Border is OFF.")]
    public GameObject boundaryPrefab;

    [Tooltip("ON (default) = ring the flat town with tiered Kenney hills (built from the ground prefab) " +
             "that form the natural map boundary — no fence. OFF = a flat town with the boundary prefab " +
             "fence instead.")]
    public bool scenicBorder = true;

    [Tooltip("World size of one cell, in units. 0 = auto-measure from the ground prefab's footprint.")]
    [Min(0f)] public float cellSize = 0f;

    [Tooltip("Tile the ground prefab per-cell (chunky Kenney blocks). OFF (default) = one flat grass " +
             "plane. Kenney grass BLOCKS are stylized platform pieces — tiled as a lawn they read as " +
             "rows of pillows with leaking edges. Keep blocks for raised terrain (Phase 4), not the base.")]
    public bool tiledGround = false;

    [Header("Objects")]
    [Tooltip("Everything the town places — houses, trees, wells, props. Placement (Lot/OpenGround/" +
             "PlazaCenter/Roadside) drives WHERE, exactly like the 2D PropTemplate list.")]
    public List<Prop3D> props = new List<Prop3D>();
}

/// <summary>One placeable 3D object: a prefab + the footprint/placement metadata the shared
/// <see cref="TownGenerator"/> needs. Mirrors the fields of a 2D <see cref="PropTemplate"/> the
/// generator reads (size / placement / weight / anchor), plus a <see cref="kind"/> that sets how it
/// blocks movement.</summary>
[Serializable]
public class Prop3D
{
    public GameObject prefab;
    [Tooltip("Footprint in cells (width, depth).")]
    public Vector2Int size = new Vector2Int(1, 1);
    [Tooltip("Where it may go: Lot (building plot, path carved to road) / OpenGround (scattered) / " +
             "PlazaCenter (single centrepiece) / Roadside.")]
    public PropPlacement placement = PropPlacement.OpenGround;
    [Min(0f)] public float weight = 1f;
    [Tooltip("building / tree / decor — building blocks its whole footprint, tree blocks a small " +
             "trunk (walk under canopy), decor doesn't block.")]
    public string kind = "building";
    [Tooltip("Entrance cell (for Lot props, a path is carved from just outside it to the road). " +
             "Leave (0,0) to default to bottom-centre.")]
    public Vector2Int anchorCell;
}
