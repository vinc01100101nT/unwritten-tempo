using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// HOW TO GET FROM ONE MAP TO ANOTHER, through the doors between them (<see cref="WorldAtlas.links"/>). The server
/// points a quest step on another map at the next door to take, and that door is decided there, never on a player's
/// machine. A player's machine asks it only for the way to a spot the player picked on the map window
/// (<see cref="MapGuide"/>) — where they want to walk, which decides nothing.
///
/// <para>The search itself is a plain function over a table of neighbours (<see cref="NextFrom"/>), so it can be
/// checked with numbers alone. The table is built once from the atlas.</para>
/// </summary>
public static class WorldRoutes
{
    static Dictionary<string, List<string>> neighbours;
    static WorldAtlas atlasLoaded;
    static bool atlasLooked;

    /// <summary>The baked world layout, loaded once. Null when none has been baked.</summary>
    public static WorldAtlas Atlas
    {
        get
        {
            if (!atlasLooked) { atlasLooked = true; atlasLoaded = Resources.Load<WorldAtlas>("WorldAtlas"); }
            return atlasLoaded;
        }
    }

    /// <summary>Is somebody standing at <paramref name="x"/>, <paramref name="z"/> inside <paramref name="from"/> (the
    /// map's own coordinates, as a save writes them) within <paramref name="reach"/> metres of a door that leads to
    /// <paramref name="to"/>? The doors are the world layout's, so a map name with no door to it is always no.</summary>
    public static bool DoorWithin(string from, string to, float x, float z, float reach)
    {
        var atlas = Atlas;
        if (atlas == null || atlas.links == null || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return false;
        foreach (var l in atlas.links)
        {
            if (l == null) continue;
            if (l.a == from && l.b == to && KeeperMath.WithinReach(x - l.ax, z - l.az, reach)) return true;
            if (l.b == from && l.a == to && KeeperMath.WithinReach(x - l.bx, z - l.bz, reach)) return true;
        }
        return false;
    }

    static Dictionary<string, List<string>> Neighbours
    {
        get
        {
            if (neighbours != null) return neighbours;
            neighbours = new Dictionary<string, List<string>>();
            var atlas = Atlas;
            if (atlas != null && atlas.links != null)
                foreach (var l in atlas.links)
                {
                    if (l == null || string.IsNullOrEmpty(l.a) || string.IsNullOrEmpty(l.b)) continue;
                    Add(neighbours, l.a, l.b);
                    Add(neighbours, l.b, l.a);
                }
            return neighbours;
        }
    }

    static void Add(Dictionary<string, List<string>> into, string from, string to)
    {
        if (!into.TryGetValue(from, out var list)) into[from] = list = new List<string>();
        if (!list.Contains(to)) list.Add(to);
    }

    /// <summary>The next map to walk into on the shortest way from <paramref name="from"/> to <paramref name="to"/>,
    /// and how many doors the whole way takes. Null and -1 when there is no way; <paramref name="to"/> itself and 0
    /// when they are the same map.</summary>
    public static string NextMap(string from, string to, out int doors)
    {
        // The doors never move while the game runs, so each answer is worked out once and kept: this is asked on
        // every change to a player's bag, and a search there would be garbage on every kill.
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) { doors = -1; return null; }
        var key = (from, to);
        if (answers.TryGetValue(key, out var known)) { doors = known.doors; return known.next; }
        var next = NextFrom(Neighbours, from, to, out doors);
        answers[key] = (next, doors);
        return next;
    }

    static readonly Dictionary<(string, string), (string next, int doors)> answers =
        new Dictionary<(string, string), (string next, int doors)>();

    /// <summary>The shortest-way search over any table of neighbours: breadth first, so the first time the goal is
    /// reached it is reached by the fewest doors.</summary>
    public static string NextFrom(Dictionary<string, List<string>> table, string from, string to, out int doors)
    {
        doors = -1;
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return null;
        if (from == to) { doors = 0; return to; }
        var firstStep = new Dictionary<string, string>();
        var depth = new Dictionary<string, int> { [from] = 0 };
        var queue = new Queue<string>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            if (!table.TryGetValue(at, out var next)) continue;
            foreach (var n in next)
            {
                if (depth.ContainsKey(n)) continue;
                depth[n] = depth[at] + 1;
                firstStep[n] = at == from ? n : firstStep[at];
                if (n == to) { doors = depth[n]; return firstStep[n]; }
                queue.Enqueue(n);
            }
        }
        return null;
    }
}
