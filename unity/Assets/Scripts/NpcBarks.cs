using UnityEngine;

/// <summary>
/// Canned ambient "bark" lines for <see cref="TownNPC"/> — short, world-flavored chatter shown as a floating
/// speech bubble (via <see cref="DamagePopup.SpawnBubble"/>) when the player wanders past. This is the LLM-FREE
/// liveliness layer: pure motion + reactivity, no Claude API, no per-line cost. Pools live here as plain
/// string arrays so a map can pass a themed set to <see cref="TownNPC"/> (town vs field vs guards) without any
/// per-NPC data.
///
/// Keep lines SHORT (a glance, not a paragraph) — the bubble lingers only a couple seconds. Adding lines costs
/// nothing at runtime (the pool is static and shared; one random index per bark).
/// </summary>
public static class NpcBarks
{
    /// <summary>Everyday townsfolk small-talk. The default pool.</summary>
    public static readonly string[] Town =
    {
        "Morning, traveler!",
        "Lovely day for it.",
        "Mind how you go.",
        "Fresh bread at the market!",
        "Heard there's monsters past the gate...",
        "Welcome to our town.",
        "Spare a moment?",
        "Watch yourself out there.",
        "Safe travels.",
        "Quiet day, thank the stars.",
        "You look like trouble.",
        "Any news from the road?",
        "The well's run a bit low.",
        "My back's killing me.",
        "Off adventuring, are we?",
        "Storm's coming, I can feel it.",
    };

    /// <summary>Wary lines for folk out in the wilds / near danger.</summary>
    public static readonly string[] Field =
    {
        "Careful — things bite out here.",
        "Don't stray too far.",
        "Saw something move in the grass...",
        "Best be home by dark.",
        "You're brave, I'll give you that.",
        "Keep your blade close.",
    };
}
