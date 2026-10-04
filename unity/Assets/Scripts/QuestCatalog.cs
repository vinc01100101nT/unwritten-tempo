using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EVERY QUEST IN THE GAME, loaded once from <c>Resources/Quests</c>, by id. Both the server (which judges steps) and a
/// player's machine (which only shows the titles and step lines the server's ids point at) read the same assets.
///
/// <para><b>A quest that cannot be finished is suspended, never offered.</b> One naming an item the game no longer has,
/// a townsperson nobody lists, or two steps with one id could never be completed — and with no way to skip it, a
/// player holding it would be stuck for good. It is left out here, said once in the log, and its records are kept
/// untouched until the data is fixed. The same faults stop the data checker (<c>tools/check-quest-data.py</c>).</para>
/// </summary>
public static class QuestCatalog
{
    public const string ResourceFolder = "Quests";

    static Dictionary<string, QuestDef> byId;
    static List<QuestDef> all;

    /// <summary>Every playable quest, in a fixed order (by id), so every machine lists them alike.</summary>
    public static IReadOnlyList<QuestDef> All { get { Load(); return all; } }

    /// <summary>The playable quest with this id, or null — unknown, or suspended for a fault in its data.</summary>
    public static QuestDef Get(string id)
    {
        Load();
        return !string.IsNullOrEmpty(id) && byId.TryGetValue(id, out var q) ? q : null;
    }

    static void Load()
    {
        if (byId != null) return;
        byId = new Dictionary<string, QuestDef>();
        all = new List<QuestDef>();
        var seen = new HashSet<string>();
        var loaded = Resources.LoadAll<QuestDef>(ResourceFolder);
        System.Array.Sort(loaded, (a, b) => string.CompareOrdinal(a != null ? a.id : "", b != null ? b.id : ""));
        foreach (var q in loaded)
        {
            if (q == null) continue;
            if (string.IsNullOrEmpty(q.id)) { Suspend(q.name, "it has no id"); continue; }
            if (!seen.Add(q.id)) { Suspend(q.id, "two quests share this id"); byId.Remove(q.id); continue; }
            string fault = FaultIn(q);
            if (fault != null) { Suspend(q.id, fault); continue; }
            byId[q.id] = q;
        }
        foreach (var q in loaded) if (q != null && byId.TryGetValue(q.id, out var ok) && ok == q) all.Add(q);
    }

    static bool WorldSpawnsPlace(string creatureId, string map)
    {
        var spawns = WorldSpawns.Instance;
        if (spawns == null) return false;
        foreach (var row in spawns.rows)
            if (row != null && row.creature == creatureId && row.population > 0
                && (string.IsNullOrEmpty(map) || row.map == map)) return true;
        return false;
    }

    static void Suspend(string id, string why) =>
        GameLog.Warn(LogCat.Server, "quest '" + id + "' is suspended — " + why + ". Nobody is given it, and nobody holding it is shown it, until its data is fixed.");

    /// <summary>Why <paramref name="q"/> could never be finished, or null when every step can be.</summary>
    public static string FaultIn(QuestDef q)
    {
        if (q.steps == null || q.steps.Length == 0) return "it has no steps";
        var stepIds = new HashSet<string>();
        foreach (var s in q.steps)
        {
            if (s == null || string.IsNullOrEmpty(s.id)) return "a step has no id";
            if (!stepIds.Add(s.id)) return "two steps share the id '" + s.id + "'";
            var o = s.objective;
            if (o == null) return "step '" + s.id + "' has no objective";
            if (o.kind == QuestObjectiveKind.TalkTo)
            {
                if (WorldTownsfolkIds.Find(o.townspersonId) == null)
                    return "step '" + s.id + "' names a townsperson nobody lists: '" + o.townspersonId + "'";
            }
            else if (o.kind == QuestObjectiveKind.Kill)
            {
                if (MonsterCatalog.Get(o.creatureId) == null)
                    return "step '" + s.id + "' names a creature the game does not have: '" + o.creatureId + "'";
                // A CREATURE NOBODY PLACES can never be defeated for credit — a summon, a shadow at its own place, one
                // only drawn into a mix — and a quest cannot be abandoned, so it would be held open for good.
                if (!WorldSpawnsPlace(o.creatureId, o.map))
                    return "step '" + s.id + "' is to defeat '" + o.creatureId + "', which the world spawn list places "
                           + (string.IsNullOrEmpty(o.map) ? "nowhere" : "nowhere in '" + o.map + "'");
            }
            else if (ItemCatalog.Resolve(o.itemId) == null)
                return "step '" + s.id + "' names an item the game does not have: '" + o.itemId + "'";
        }
        if (q.givenAutomatically && !string.IsNullOrEmpty(q.offeredBy)) return "it is both given automatically and offered";
        if (!string.IsNullOrEmpty(q.offeredBy))
        {
            var giver = WorldTownsfolkIds.Find(q.offeredBy);
            if (giver == null) return "it is offered by a townsperson nobody lists: '" + q.offeredBy + "'";
            // A station worker or a shopkeeper opens a window when F is pressed, never a conversation, so nobody could
            // ever be offered it.
            if (giver.station != CraftStation.None || !string.IsNullOrEmpty(giver.shop))
                return "it is offered by '" + q.offeredBy + "', who opens a window instead of a conversation";
        }
        // A REWARDED QUEST JUDGES ONLY WHAT WAS DONE. A step judged on what is held can be finished by one item lent from
        // character to character — and the quest paid to every one of them.
        if (q.reward != null && q.reward.Any)
            foreach (var s in q.steps)
            {
                var k = s.objective.kind;
                if (k == QuestObjectiveKind.Have || k == QuestObjectiveKind.FoldedTo || k == QuestObjectiveKind.Wear)
                    return "it pays a reward but step '" + s.id + "' is judged on holding something, which one lent item "
                           + "could finish for everybody";
            }
        // AN OFFER WITH NOTHING TO SAY would open a conversation with no lines.
        if (!string.IsNullOrEmpty(q.offeredBy) && string.IsNullOrWhiteSpace(q.offerText))
            return "it is offered but its offer text is empty";
        if (q.reward?.items != null)
            foreach (var r in q.reward.items)
                if (r == null || ItemCatalog.Resolve(r.itemId) == null)
                    return "its reward names an item the game does not have: '" + r?.itemId + "'";
        return null;
    }
}

/// <summary>A townsperson's row in <see cref="WorldTownsfolk"/>, found by the id a quest names.</summary>
public static class WorldTownsfolkIds
{
    public static WorldTownsfolk.Row Find(string id)
    {
        var list = WorldTownsfolk.Instance;
        if (list == null || string.IsNullOrEmpty(id)) return null;
        for (int i = 0; i < list.rows.Count; i++)
            if (list.rows[i] != null && list.rows[i].id == id) return list.rows[i];
        return null;
    }

    /// <summary>The nearest somebody who works <paramref name="station"/>, counted in doors from <paramref name="map"/>;
    /// null when nobody anywhere does.</summary>
    public static WorldTownsfolk.Row WorkingAt(CraftStation station, string map)
    {
        var list = WorldTownsfolk.Instance;
        if (list == null || station == CraftStation.None) return null;
        WorldTownsfolk.Row best = null;
        int bestDoors = int.MaxValue;
        for (int i = 0; i < list.rows.Count; i++)
        {
            var r = list.rows[i];
            if (r == null || r.station != station) continue;
            WorldRoutes.NextMap(map, r.map, out int doors);
            if (doors < 0) continue;
            if (doors < bestDoors) { best = r; bestDoors = doors; }
            if (doors == 0) break;
        }
        return best;
    }
}
