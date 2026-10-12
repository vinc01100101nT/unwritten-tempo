using UnityEngine;

/// <summary>
/// THE SPOT A PLAYER PICKED ON THE MAP WINDOW'S ZONE VIEW, to be led to — on the map they stand in or on any other.
/// The ground arrows (<see cref="QuestArrows"/>) lead there instead of to the followed quest while it is set: straight
/// to it on its own map, or, from any other map, to the door into the next map on the fewest-doors way there
/// (<see cref="WorldRoutes.NextMap"/>), worked out again in each map on arrival. Reaching the spot, or a right click on the zone view, clears it, and the
/// arrows go back to the quest.
///
/// <para>Only this player's machine holds it. It decides nothing and changes no number — it is where the player
/// wants to walk, which is the player's own business (movement's exception, decided by the user 2026-09-14).</para>
/// </summary>
public static class MapGuide
{
    public static bool Active { get; private set; }

    /// <summary>The map the spot is on, by scene name.</summary>
    public static string Map { get; private set; }

    /// <summary>The spot, in that map's own coordinates: x east, y north (the ground's X and Z).</summary>
    public static Vector2 Spot { get; private set; }

    /// <summary>Goes up every time the spot is set or cleared, so a drawing of it redraws only then.</summary>
    public static int Version { get; private set; }

    public static void Set(string map, Vector2 spot)
    {
        if (string.IsNullOrEmpty(map)) return;
        Active = true;
        Map = map;
        Spot = spot;
        Version++;
    }

    public static void Clear()
    {
        if (!Active) return;
        Active = false;
        Map = null;
        Version++;
    }
}
