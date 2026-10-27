using System.Collections.Generic;
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

        // A SEAMLESS WORLD SAYS WHERE ITS EDGE IS. Its ground comes and goes in chunks, so the ground loaded when it
        // arrives is not the world; the world's own box is.
        var world = SeamlessWorld.InScene(scene);
        if (world != null)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == HolderName) return;
            // Its squares need not fill a box (a world grows area by area), so the walls follow the outline of the squares
            // it has: every square edge with no square beyond it.
            var box = world.WorldBounds(-MapEdgeMath.Reach, MapEdgeMath.Reach);
            // The grid's squares are the chunks of its one square size; any other chunk (a town piece) must lie inside
            // them, or the world is not a grid of squares (an older world cut into uneven chunks) and keeps the box.
            float size = 0f;
            foreach (var c in world.chunks) { float wd = c.max.x - c.min.x; if (Mathf.Abs(wd - (c.max.y - c.min.y)) < 0.01f) size = Mathf.Max(size, wd); }
            var squares = new List<Rect>();
            var others = new List<Rect>();
            foreach (var c in world.chunks)
            {
                var r = Rect.MinMaxRect(c.min.x, c.min.y, c.max.x, c.max.y);
                if (size > 0f && Mathf.Abs(r.width - size) < 0.01f && Mathf.Abs(r.height - size) < 0.01f) squares.Add(r); else others.Add(r);
            }
            bool grid = squares.Count > 0;
            foreach (var o in others)
            {
                bool covered = true;
                foreach (var corner in new[] { o.min, o.max, new Vector2(o.xMin, o.yMax), new Vector2(o.xMax, o.yMin) })
                {
                    bool inAny = false;
                    foreach (var sq in squares) if (corner.x >= sq.xMin - 0.01f && corner.x <= sq.xMax + 0.01f && corner.y >= sq.yMin - 0.01f && corner.y <= sq.yMax + 0.01f) { inAny = true; break; }
                    if (!inAny) { covered = false; break; }
                }
                if (!covered) { grid = false; break; }
            }
            if (!grid) { BuildWalls(scene, box); return; }
            // The squares are in the map's own coordinates; the walls stand where this machine has set the world down.
            var walls = MapEdgeMath.OutlineWalls(squares, box.min.y, box.max.y);
            var shift = world.WorldBounds(0f, 0f).min - new Vector3(world.worldMin.x, 0f, world.worldMin.y);
            for (int i = 0; i < walls.Length; i++) walls[i].center += new Vector3(shift.x, 0f, shift.z);
            BuildWalls(scene, walls);
            return;
        }

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
        BuildWalls(scene, hasSeen ? seen : solid);
    }

    static void BuildWalls(Scene scene, Bounds ground) => BuildWalls(scene, MapEdgeMath.Walls(ground.min, ground.max));

    static void BuildWalls(Scene scene, Bounds[] walls)
    {
        var holder = new GameObject(HolderName)
        {
            // Made during play only; never baked into the scene if the editor saves it.
            hideFlags = HideFlags.DontSaveInEditor,
            layer = LayerMask.NameToLayer("Ignore Raycast"),
        };
        SceneManager.MoveGameObjectToScene(holder, scene);

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
    /// <summary>The walls round a set of equal squares that need not fill a box: one wall along every square edge with
    /// no square beyond it, wholly outside the square, its inner face on the edge, running a wall's thickness past each
    /// end so corners are shut. Edges in one straight line are joined into one wall. Heights <paramref name="lowY"/>..
    /// <paramref name="highY"/>.</summary>
    public static Bounds[] OutlineWalls(List<Rect> squares, float lowY, float highY)
    {
        var at = new HashSet<(int, int)>();
        float size = squares[0].width;
        (int, int) Key(Vector2 min) => (Mathf.RoundToInt(min.x / size), Mathf.RoundToInt(min.y / size));
        foreach (var r in squares) at.Add(Key(r.min));
        float midY = (lowY + highY) * 0.5f, sizeY = highY - lowY, half = Thickness * 0.5f;
        var walls = new List<Bounds>();
        // For each of the four sides: the runs of exposed edges along each line, joined.
        foreach (var (dx, dz) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
        {
            var open = new List<(int line, int pos)>();
            foreach (var (i, j) in at)
                if (!at.Contains((i + dx, j + dz))) open.Add(dz != 0 ? (j, i) : (i, j));
            open.Sort();
            for (int k = 0; k < open.Count;)
            {
                int line = open[k].line, from = open[k].pos, to = from;
                while (k + 1 < open.Count && open[k + 1].line == line && open[k + 1].pos == to + 1) { k++; to++; }
                k++;
                float a = from * size - Thickness, b = (to + 1) * size + Thickness, mid = (a + b) * 0.5f, span = b - a;
                if (dz != 0)
                {
                    float edge = dz < 0 ? line * size - half : (line + 1) * size + half;
                    walls.Add(new Bounds(new Vector3(mid, midY, edge), new Vector3(span, sizeY, Thickness)));
                }
                else
                {
                    float edge = dx < 0 ? line * size - half : (line + 1) * size + half;
                    walls.Add(new Bounds(new Vector3(edge, midY, mid), new Vector3(Thickness, sizeY, span)));
                }
            }
        }
        return walls.ToArray();
    }

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
