using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// EVERY MAP IS CLOSED IN. Four invisible walls stand just outside the map's ground, so nothing that walks — a
/// player, a creature, a body thrown by a knockback — can step off the edge and fall out of the world.
///
/// <para>Built once, the moment a map arrives (<see cref="MapManager.Activate"/>), on every machine: the server moves
/// creatures with the same physics a player's machine moves its own legs with, so both need the same walls. They are
/// worked out from the ground the map actually has — every solid collider on the Ground layer — so a generated map
/// and a hand-made one are closed in the same way, and no scene file has to carry them.</para>
///
/// <para>The walls sit on the Ignore Raycast layer: they stop bodies, but a click on the world or a search for the
/// ground under a point passes straight through them. They are not ground — nothing ever stands on them.</para>
/// </summary>
public static class MapEdgeWalls
{
    public const string HolderName = "MapEdgeWalls";

    public static void Build(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer < 0) return;

        // THE GROUND YOU CAN SEE, not the ground you can stand on. A flat map's floor box reaches one unit past its
        // drawn ground on every side, so walls on the floor box would let a player stand out over nothing.
        bool hasSeen = false, hasSolid = false;
        Bounds seen = default, solid = default;
        foreach (var root in scene.GetRootGameObjects())
        {
            // Already closed in: a map adopted at boot can be activated twice.
            if (root.name == HolderName) return;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                if (r.gameObject.layer != groundLayer || !r.enabled) continue;
                if (!hasSeen) { seen = r.bounds; hasSeen = true; }
                else seen.Encapsulate(r.bounds);
            }
            foreach (var c in root.GetComponentsInChildren<Collider>(false))
            {
                if (c.gameObject.layer != groundLayer || c.isTrigger || !c.enabled) continue;
                if (!hasSolid) { solid = c.bounds; hasSolid = true; }
                else solid.Encapsulate(c.bounds);
            }
        }
        if (!hasSeen && !hasSolid)
        {
            GameLog.Warn(LogCat.Map, "map '" + scene.name + "' has no ground on the Ground layer, so its edges are left open.");
            return;
        }
        // Ground with nothing drawn on the Ground layer falls back to its solid shape.
        var ground = hasSeen ? seen : solid;

        var holder = new GameObject(HolderName)
        {
            // Made during play only; never baked into the scene if the editor saves it.
            hideFlags = HideFlags.DontSaveInEditor,
            layer = LayerMask.NameToLayer("Ignore Raycast"),
        };
        SceneManager.MoveGameObjectToScene(holder, scene);

        var walls = MapEdgeMath.Walls(ground.min, ground.max);
        for (int i = 0; i < walls.Length; i++)
        {
            var box = holder.AddComponent<BoxCollider>();
            box.center = walls[i].center;
            box.size = walls[i].size;
        }
    }
}

/// <summary>Where a map's four edge walls stand, from the box its ground fills. Numbers in, boxes out.</summary>
public static class MapEdgeMath
{
    /// <summary>How thick each wall is — thick enough that nothing moving in one frame can pass through it.</summary>
    public const float Thickness = 2f;

    /// <summary>How far each wall reaches below the lowest ground and above the highest — well past anything a body
    /// can jump to or be thrown to.</summary>
    public const float Reach = 50f;

    /// <summary>The four walls around the ground box <paramref name="min"/>..<paramref name="max"/>, in the order
    /// south, north, west, east. Each stands wholly OUTSIDE the ground with its inner face on the ground's edge, and
    /// the south and north walls run past the corners so the corners are shut too.</summary>
    public static Bounds[] Walls(Vector3 min, Vector3 max)
    {
        float lowY = min.y - Reach, highY = max.y + Reach;
        float midY = (lowY + highY) * 0.5f, sizeY = highY - lowY;
        float midX = (min.x + max.x) * 0.5f, midZ = (min.z + max.z) * 0.5f;
        float spanX = (max.x - min.x) + Thickness * 2f;
        float spanZ = max.z - min.z;
        float half = Thickness * 0.5f;
        return new[]
        {
            new Bounds(new Vector3(midX, midY, min.z - half), new Vector3(spanX, sizeY, Thickness)),
            new Bounds(new Vector3(midX, midY, max.z + half), new Vector3(spanX, sizeY, Thickness)),
            new Bounds(new Vector3(min.x - half, midY, midZ), new Vector3(Thickness, sizeY, spanZ)),
            new Bounds(new Vector3(max.x + half, midY, midZ), new Vector3(Thickness, sizeY, spanZ)),
        };
    }
}
