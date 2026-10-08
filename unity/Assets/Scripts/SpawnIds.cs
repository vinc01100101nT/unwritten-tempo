using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One unit's SPAWN_ID: the map it was numbered in, its number there, and the "Snake #3" a log line prints.
/// Built once, when the number is handed out, so a log line reads a field instead of building the text per blow.
/// </summary>
public sealed class SpawnTag
{
    public readonly string map;
    public readonly int number;
    public readonly string label;

    public SpawnTag(string map, int number, string name)
    {
        this.map = map;
        this.number = number;
        label = string.Concat(name, " #", number.ToString());
    }
}

/// <summary>
/// SPAWN_IDS — the number that tells two of the same creature apart in the server's log. Every creature, townsperson
/// and player that appears in a map is given the next number FOR THAT MAP. Server only: a player's machine hands out
/// nothing, and its log names units without a number.
///
/// <para><b>Why a number and not the mob id.</b> Every snake shares one mob id, so "Snake hit Alpha" cannot say which
/// of ten snakes it was. The number is not the mob id and not the player id — it exists only to tell units apart
/// inside one map's log lines.</para>
///
/// <para><b>Counted per map, for the whole life of the server.</b> A map nobody is in is let go and brought back
/// later; starting its count again at 1 would make "field_15 Snake #3" mean two different snakes in the same log.
/// So the count only ever goes up until the server stops.</para>
///
/// <para><b>The number belongs to the BODY.</b> A creature that dies and comes back is the same body, so it keeps
/// its number and the log says it came back. A brand-new body gets the next number. A player keeps theirs through a
/// death in the same map, and gets a new one on entering another map — their full player id, written on the line
/// where they enter, is what joins the two.</para>
/// </summary>
public static class SpawnIds
{
    // THE LAST NUMBER HANDED OUT IN EACH MAP. One entry per map name this server has ever held, which is bounded by
    // the number of maps in the game, so nothing ever needs removing from it.
    static readonly Dictionary<string, int> lastByMap = new Dictionary<string, int>();

    static SpawnTag Next(string map, string name)
    {
        if (string.IsNullOrEmpty(map)) map = "nowhere";
        lastByMap.TryGetValue(map, out int last);
        last++;
        lastByMap[map] = last;
        return new SpawnTag(map, last, name);
    }

    /// <summary>A creature has just been announced to the world. A body that already carries a number is a body
    /// coming back — <see cref="MobCameBack"/> has already said so — and keeps the number it has.</summary>
    public static void MobAppeared(Health mob, string mobId)
    {
        if (mob == null || !GameServer.IsServer || mob.Spawn != null) return;
        mob.Spawn = Next(mob.Map, PlainName(mob.name));
        if (GameLog.On(LogCat.Spawn))
            GameLog.Info(LogCat.Spawn, string.Concat(mob.Spawn.map, " ", mob.Spawn.label, " (", mobId, ") appeared"));
    }

    /// <summary>A creature that died is alive again, in the same body and so with the same number.</summary>
    public static void MobCameBack(Health mob)
    {
        if (mob == null || !GameServer.IsServer || mob.Spawn == null) return;
        if (GameLog.On(LogCat.Spawn))
            GameLog.Info(LogCat.Spawn, string.Concat(mob.Spawn.map, " ", mob.Spawn.label, " came back"));
    }

    /// <summary>A townsperson has just been announced. Townsfolk take no blows and never come back, so the number is
    /// only written on this line and kept nowhere.</summary>
    public static void NpcAppeared(GameObject npc, string modelName)
    {
        if (npc == null || !GameServer.IsServer) return;
        var tag = Next(npc.scene.name, PlainName(npc.name));
        if (GameLog.On(LogCat.Spawn))
            GameLog.Info(LogCat.Spawn, string.Concat(tag.map, " ", tag.label, " (", modelName, ") appeared"));
    }

    /// <summary>A player is now standing in <paramref name="map"/>. Called only when their map CHANGES, so a death
    /// and a revive in the same map keep the number they have.</summary>
    public static void PlayerEnteredMap(Character who, string map)
    {
        if (who == null || !GameServer.IsServer || who.IsWatchedCopy || string.IsNullOrEmpty(map)) return;
        who.Spawn = Next(map, GameNetwork.NameOf(who));
        PlaySession.EnteredMap(who, map);
        if (GameLog.On(LogCat.Spawn))
            GameLog.Info(LogCat.Spawn, string.Concat(who.Spawn.map, " ", who.Spawn.label, " (",
                                                     GameNetwork.CharacterIdOf(who), ") entered"));
    }

    /// <summary>A player who died is standing again, in the same map and so with the same number.</summary>
    public static void PlayerGotUp(Character who)
    {
        if (who == null || !GameServer.IsServer || who.IsWatchedCopy || !GameLog.On(LogCat.Spawn)) return;
        GameLog.Info(LogCat.Spawn, string.Concat(MapOf(who), " ", Label(who), " got up", CombatAuthority.HealthLeft(who)));
    }

    /// <summary>"Snake #3" — what a log line calls this unit. A unit with no number yet is named plainly: a player's
    /// machine hands out none, and a creature that reached a fight without being announced is given its number here,
    /// on the server, so no line on the server ever names a unit that has none.
    /// <b>Only ever called behind <see cref="GameLog.On"/></b> — a unit without a number builds a string.</summary>
    public static string Label(ICombatUnit unit)
    {
        if (PlayerUnits.Gone(unit)) return "somebody";
        var tag = unit.Spawn;
        if (tag != null) return tag.label;

        if (unit is Health mob)
        {
            if (!GameServer.IsServer) return PlainName(mob.name);
            MobAppeared(mob, "no mob id");
            return mob.Spawn.label;
        }
        if (unit is Character ch) return GameNetwork.NameOf(ch);
        var body = unit.Body;
        return body != null ? PlainName(body.name) : "somebody";
    }

    /// <summary>The map a log line puts in front of this unit — the one it was numbered in, or where it stands.</summary>
    public static string MapOf(ICombatUnit unit)
    {
        if (PlayerUnits.Gone(unit)) return "nowhere";
        var tag = unit.Spawn;
        if (tag != null) return tag.map;
        string map = unit.Map;
        return string.IsNullOrEmpty(map) ? "nowhere" : map;
    }

    // Unity names every copy "<prefab>(Clone)", which would print on every line and says nothing.
    static string PlainName(string objectName)
    {
        const string Clone = "(Clone)";
        return objectName != null && objectName.EndsWith(Clone, System.StringComparison.Ordinal)
            ? objectName.Substring(0, objectName.Length - Clone.Length).TrimEnd()
            : objectName;
    }
}
