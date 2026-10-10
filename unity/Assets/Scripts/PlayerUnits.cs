using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Everybody in this world who counts as a PLAYER, as fighters — the one place anything asks "who is here?".
///
/// <para><b>Why it exists.</b> "The player" used to be a single global handle, which is a fact about a screen: there
/// is one person at this keyboard. A server holds as many people as have signed in, so every rule written against
/// that handle either picked the wrong person or found nobody — a creature that never chased, a kill that paid
/// nobody. This is the same question asked in a form that has more than one answer.</para>
///
/// <para>On a player's own machine this holds exactly one entry — themselves — so nothing about how the game
/// behaves at one keyboard changes. On a server it holds one per connection.</para>
///
/// <para><b>Cost.</b> A plain list, walked only when something asks; the walk is over the number of PEOPLE, not the
/// number of creatures, and no memory is allocated to answer. Nothing here runs per frame by itself.</para>
/// </summary>
public static class PlayerUnits
{
    static readonly List<ICombatUnit> present = new List<ICombatUnit>();

    // THE SAME PEOPLE, FILED BY THE MAP THEY ARE STANDING IN. A creature looking for somebody to chase asked the
    // whole world twice a second and threw away everybody who was not even in its map — creatures times players,
    // most of it wasted. This answers the same question by looking only where the creature actually is.
    //
    // It cannot silently lose anybody. A person is filed when they arrive and re-filed the moment they are told
    // they are somewhere new, and anyone whose map is not known yet sits in the unfiled list below, which EVERY
    // search reads as well as its own map's. So the worst a stale entry can do is be considered twice, never
    // vanish — which matters, because a person a creature cannot see is a bug nobody would ever notice.
    static readonly Dictionary<string, List<ICombatUnit>> byMap = new Dictionary<string, List<ICombatUnit>>();
    static readonly List<ICombatUnit> unfiled = new List<ICombatUnit>();

    static List<ICombatUnit> BucketFor(string map)
    {
        if (!byMap.TryGetValue(map, out var list)) byMap[map] = list = new List<ICombatUnit>();
        return list;
    }

    // Take a person out of wherever they are filed, without disturbing anybody else.
    static void Unfile(ICombatUnit unit)
    {
        unfiled.Remove(unit);
        foreach (var list in byMap.Values) if (list.Remove(unit)) return;
    }

    /// <summary>File this person under the map they are in now — called when they arrive and every time they are
    /// told they are somewhere new. Safe to call with the same map twice.</summary>
    public static void Refile(ICombatUnit unit)
    {
        if (unit == null || !present.Contains(unit)) return;
        Unfile(unit);
        string map = unit.Map;
        if (string.IsNullOrEmpty(map)) unfiled.Add(unit);
        else BucketFor(map).Add(unit);
    }

    /// <summary>Everyone filed under <paramref name="map"/>, plus everyone whose map is not known yet, added to
    /// <paramref name="into"/> (which is not cleared). An empty map adds everybody. The caller still asks each one's
    /// map, the same way <see cref="NearestTo"/> does, so a stale entry is skipped rather than reached across maps.</summary>
    public static void CollectIn(string map, List<ICombatUnit> into)
    {
        if (into == null) return;
        if (string.IsNullOrEmpty(map)) { into.AddRange(present); return; }
        if (byMap.TryGetValue(map, out var here)) into.AddRange(here);
        into.AddRange(unfiled);
    }

    /// <summary>Everyone currently in the world. Never null; empty before anybody has arrived.</summary>
    public static IReadOnlyList<ICombatUnit> Present => present;

    /// <summary>Somebody has arrived. Safe to call twice — a duplicate is ignored rather than doubled.</summary>
    public static void Enter(ICombatUnit unit)
    {
        if (unit == null || present.Contains(unit)) return;
        present.Add(unit);
        Refile(unit);
    }

    /// <summary>Somebody has gone. Safe to call for a unit that was never in.</summary>
    public static void Leave(ICombatUnit unit)
    {
        if (unit == null) return;
        present.Remove(unit);
        Unfile(unit);
    }

    /// <summary>TRUE when this unit is not really there any more — including the case that looks fine and is not:
    /// a unit whose object Unity has already destroyed.
    ///
    /// <para><b>Why this cannot just be <c>== null</c>.</b> Everything in the game holds a unit as an
    /// <see cref="ICombatUnit"/>, and comparing an INTERFACE reference to null is a plain C# comparison — it knows
    /// nothing about Unity's rule that a destroyed object reports itself as null. So a player who disconnected
    /// leaves a reference that passes a null check and then throws the moment anything is read off it. The cast
    /// below is what puts Unity's own answer back in play, and it is the reason this lives in one place instead of
    /// being remembered at each of the sites that ask.</para></summary>
    public static bool Gone(ICombatUnit unit)
    {
        if (unit == null) return true;                        // no reference at all

        var obj = unit as UnityEngine.Object;
        // ReferenceEquals, NOT ==: at this point == is Unity's own overload, which answers "destroyed" as null too,
        // so it cannot tell a failed cast apart from a destroyed object. This asks the plain C# question first.
        if (ReferenceEquals(obj, null)) return false;         // not a Unity object — nothing can have destroyed it
        return obj == null;                                   // and now Unity's answer: has it been destroyed?
    }

    /// <summary>Is ANYBODY standing within <paramref name="radius"/> of <paramref name="point"/>, in
    /// <paramref name="map"/>?
    ///
    /// <para>For the places that must keep clear of people rather than find one — a creature choosing where to
    /// appear, most of all. Asked of everyone in that map, because "not on top of the player" is a rule about ALL of
    /// them: a spot that is clear of one person and underneath another is not clear.</para>
    ///
    /// <para><paramref name="map"/> is the scene name of the place being asked about. Every map in a world shares
    /// one set of coordinates, so a spot judged across all of them is judged against people who are not there — see
    /// <see cref="ICombatUnit.Map"/>. An empty map asks about everybody, which is what a machine holding exactly one
    /// map wants.</para></summary>
    /// <summary>Is anybody at all standing in this map? Asked by the server when it is deciding whether a map
    /// still needs to exist. The unfiled list counts: somebody whose map has not been reported yet is somewhere,
    /// and letting a map go out from under them is worse than holding an empty one a few seconds longer.</summary>
    public static bool AnyoneIn(string map)
    {
        if (unfiled.Count > 0) return true;
        return !string.IsNullOrEmpty(map) && byMap.TryGetValue(map, out var here) && here.Count > 0;
    }

    public static bool AnyoneWithin(Vector3 point, float radius, string map)
    {
        float rSqr = radius * radius;
        for (int i = 0; i < present.Count; i++)
        {
            var unit = present[i];
            if (Gone(unit)) continue;
            if (!string.IsNullOrEmpty(map) && unit.Map != map) continue;

            var body = unit.Body;
            if (body == null) continue;

            Vector3 d = body.position - point;
            d.y = 0f;
            if (d.sqrMagnitude < rSqr) return true;
        }
        return false;
    }

    /// <summary>The closest LIVING player to <paramref name="from"/> IN <paramref name="map"/> that still has a body
    /// in the world, or null when there is nobody to find. Distance is measured flat (ignoring height) for the same
    /// reason the creatures' leash and aggro radii are: a person standing on a ledge above a monster is not further
    /// away in any sense a player would recognise.
    ///
    /// <para><b>The map is not optional bookkeeping, it is the question.</b> A server holds every map at once and
    /// they all sit in the same coordinates, so without it a creature in the field picks the nearest person by
    /// numbers alone and starts hunting somebody standing in a town — walking at them, snagging on its own map's
    /// scenery, and stopping just out of reach forever. Measured: creatures held ~0.97 units from a person they
    /// could bite at 0.59, climbing scenery nobody could see, and never landed a blow. An empty map means "anybody",
    /// which is right for a machine holding one map and wrong for the machine holding the world.</para></summary>
    public static ICombatUnit NearestTo(Vector3 from, string map)
    {
        ICombatUnit best = null;
        float bestSqr = float.MaxValue;

        // An empty map still means "anybody", which is what one machine holding one map asks for.
        if (string.IsNullOrEmpty(map)) { Closest(present, from, map, ref best, ref bestSqr); return best; }

        if (byMap.TryGetValue(map, out var here)) Closest(here, from, map, ref best, ref bestSqr);
        Closest(unfiled, from, map, ref best, ref bestSqr);
        return best;
    }

    static void Closest(List<ICombatUnit> people, Vector3 from, string map,
                        ref ICombatUnit best, ref float bestSqr)
    {
        for (int i = 0; i < people.Count; i++)
        {
            var unit = people[i];
            if (Gone(unit) || unit.IsDead) continue;
            // ASKED AGAIN even though the filing already answered it: a person filed under the wrong map must be
            // skipped rather than chased across the world, and this is what makes a stale entry harmless.
            if (!string.IsNullOrEmpty(map) && unit.Map != map) continue;

            var body = unit.Body;
            if (body == null) continue;   // signed in, but their body has not been built yet

            Vector3 d = body.position - from;
            d.y = 0f;
            // As a creature weighs it: somebody at peace counts as further away (WeaponDrawMath.NoticedSqr).
            var host = unit.StatusHost;
            float sqr = WeaponDrawMath.NoticedSqr(d.sqrMagnitude, host != null && host.PeaceBonusesOn);
            if (sqr >= bestSqr) continue;

            bestSqr = sqr;
            best = unit;
        }
    }
}
