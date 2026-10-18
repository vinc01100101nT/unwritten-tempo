using System.Collections.Generic;

/// <summary>
/// WHAT THE GAME SAYS ABOUT ONE CREATURE — its level, size, element, combat numbers, stats, experience and drops, as
/// it spawns. Written by the server and only shown by a player's machine (CLAUDE.md, RULE NOCLIENTMATH).
///
/// <para>One wording, two readers: the <c>/mobinfo</c> chat command prints these lines, and the map window's card
/// shows them when the pointer rests on a creature's name — so the two can never describe a creature differently.</para>
/// </summary>
public static class MobInfo
{
    // ---- the server's side: the lines, built once per creature ------------------------------------------------
    // Everything in them is the creature's saved data and fixed tables, so a creature's lines never change while
    // the catalog stands. Building them walks its gear, its ring and the item catalog, which is why it happens once.
    static readonly Dictionary<string, string[]> linesById = new Dictionary<string, string[]>(64);
    static readonly Dictionary<string, string> textById = new Dictionary<string, string>(64);

    /// <summary>SERVER: the lines <c>/mobinfo</c> prints for <paramref name="d"/>, in order.</summary>
    public static string[] Lines(MonsterDef d)
    {
        if (d == null || string.IsNullOrEmpty(d.id)) return System.Array.Empty<string>();
        if (linesById.TryGetValue(d.id, out var known)) return known;

        float size = MobSizes.Of(d.kind, d.size);
        var lines = new[]
        {
            $"<color={UiKit.Hex(UiKit.Accent)}>{d.displayName}</color> ({d.id}) — {d.kind}, Lv {d.level}",
            $"Size {size:0.##} ({MobSizes.GroupOf(size)}) · Element {d.element} {d.elementLevel} · Biome {d.biome}",
            $"HP {MonsterDefStats.Derived(d, MonsterDefStats.Stat.HP)} · ATK {MonsterDefStats.Derived(d, MonsterDefStats.Stat.Atk)}"
                + $" · DEF {MonsterDefStats.DefenseLabel(d)} · FLEE {MonsterDefStats.Derived(d, MonsterDefStats.Stat.Flee)}",
            $"STR {d.str} · AGI {d.agi} · VIT {d.vit} · INT {d.intel} · DEX {d.dex} · LUK {d.luk}",
            $"XP {CreatureKinds.KillExperience(d)}",
            // WHAT IT DROPS, read from the same tables the death roll reads, in the one wording the wiki prints too.
            "Drops: " + CreatureDrops.Line(d),
        };
        linesById[d.id] = lines;
        return lines;
    }

    /// <summary>SERVER: the same lines as one text, a line break between each — what the map window's card is sent.</summary>
    public static string Text(MonsterDef d)
    {
        if (d == null || string.IsNullOrEmpty(d.id)) return "";
        if (textById.TryGetValue(d.id, out var known)) return known;
        string text = string.Join("\n", Lines(d));
        textById[d.id] = text;
        return text;
    }

    /// <summary>Forget every creature's lines — called when the creature catalog is reloaded.</summary>
    public static void ForgetLines()
    {
        linesById.Clear();
        textById.Clear();
    }

    // ---- the player's machine: what the server sent, kept per connection ------------------------------------
    static readonly Dictionary<string, string> sentById = new Dictionary<string, string>(64);
    static readonly Dictionary<string, float> askedAt = new Dictionary<string, float>(64);

    // An ask with no answer after this long is forgotten, so resting on the name again asks again.
    const float AskAgainAfter = 5f;

    /// <summary>PLAYER'S MACHINE: the text the server sent for creature <paramref name="id"/>; false before it arrives.</summary>
    public static bool TryGetSent(string id, out string text) => sentById.TryGetValue(id ?? "", out text);

    /// <summary>PLAYER'S MACHINE: ask the server for creature <paramref name="id"/>'s text, unless it is already here
    /// or was asked for moments ago.</summary>
    public static void Ask(string id)
    {
        if (string.IsNullOrEmpty(id) || sentById.ContainsKey(id)) return;
        float now = UnityEngine.Time.unscaledTime;
        if (askedAt.TryGetValue(id, out float at) && now - at < AskAgainAfter) return;
        if (GameNetwork.AskMobInfo(id)) askedAt[id] = now;
    }

    /// <summary>PLAYER'S MACHINE: keep what the server sent.</summary>
    public static void TakeSent(string id, string text)
    {
        if (string.IsNullOrEmpty(id)) return;
        sentById[id] = text ?? "";
        askedAt.Remove(id);
    }

    /// <summary>PLAYER'S MACHINE: drop everything a server sent — on every new connection, because the server now
    /// answering may hold different numbers from the one that answered before.</summary>
    public static void ForgetSent()
    {
        sentById.Clear();
        askedAt.Clear();
    }
}
