using System.Collections.Generic;
using UnityEngine;

/// <summary>Which side of a fight a unit is on. People are always on the players' side; a creature is on the creatures'
/// side unless something puts it elsewhere — a saved character that fights for its saver stands with the players, and
/// one that never fights stands with nobody.</summary>
public enum Side
{
    Creatures,
    Players,
    None,
}

/// <summary>
/// THE ONE ANSWER TO "ARE THESE TWO ENEMIES?" Every place that chooses who to strike — a creature picking its prey, a
/// skill gathering who its tick reaches, the server accepting a swing, the player's cursor — asks here, so a unit that
/// changes sides changes for all of them at once. Player-versus-player is a change to <see cref="Hostile"/> and nothing
/// else.
/// </summary>
public static class Sides
{
    /// <summary>The side <paramref name="unit"/> stands on. A creature carries its own; anything else is a person. No unit
    /// at all counts as a person, which is how a cast with no caster has always been treated.</summary>
    public static Side Of(ICombatUnit unit) => unit is Health creature ? creature.side : Side.Players;

    /// <summary>True when <paramref name="a"/> and <paramref name="b"/> fight each other: both stand on a side, and not
    /// the same one.</summary>
    public static bool Hostile(ICombatUnit a, ICombatUnit b) => Hostile(Of(a), Of(b));

    public static bool Hostile(Side a, Side b) => a != Side.None && b != Side.None && a != b;

    /// <summary>Is <paramref name="creature"/> an enemy of the people — what the player's own machine asks before it lets
    /// the cursor, a click or the auto-pilot pick it.</summary>
    public static bool HostileToPeople(Health creature) => creature != null && Hostile(Side.Players, creature.side);

    // The creatures standing on the players' side. Kept apart from the whole creature registry because every creature
    // looking for prey asks for them, and walking every creature in the world for that would cost the square of the
    // population. It is small: a saved character fights for a few seconds.
    static readonly List<Health> playerSide = new List<Health>(8);

    /// <summary>File <paramref name="creature"/> under the side it now stands on. Called by <see cref="Health"/> whenever
    /// its side changes, and when it is destroyed.</summary>
    public static void Filed(Health creature, Side side)
    {
        playerSide.Remove(creature);
        if (side == Side.Players) playerSide.Add(creature);
    }

    /// <summary>The living creature on the players' side nearest <paramref name="from"/> in <paramref name="map"/>, or null.</summary>
    public static Health NearestPlayerSideCreature(Vector3 from, string map, out float sqr)
    {
        Health best = null;
        sqr = float.MaxValue;
        for (int i = playerSide.Count - 1; i >= 0; i--)
        {
            var h = playerSide[i];
            if (h == null) { playerSide.RemoveAt(i); continue; }
            if (!h.isActiveAndEnabled || h.IsDead) continue;
            if (!string.IsNullOrEmpty(map) && h.Map != map) continue;
            Vector3 d = h.transform.position - from; d.y = 0f;
            if (d.sqrMagnitude < sqr) { sqr = d.sqrMagnitude; best = h; }
        }
        return best;
    }

    /// <summary>The living creature on the creatures' side nearest <paramref name="from"/> in <paramref name="map"/>, or
    /// null — what a creature fighting for the players hunts.</summary>
    public static Health NearestCreatureSideCreature(Vector3 from, string map)
    {
        Health best = null;
        float bestSqr = float.MaxValue;
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            var h = ai != null ? ai.Health : null;
            if (h == null || h.IsDead || h.side != Side.Creatures) continue;
            if (!string.IsNullOrEmpty(map) && h.Map != map) continue;
            Vector3 d = ai.transform.position - from; d.y = 0f;
            if (d.sqrMagnitude < bestSqr) { bestSqr = d.sqrMagnitude; best = h; }
        }
        return best;
    }
}
