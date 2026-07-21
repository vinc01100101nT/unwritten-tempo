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

    // ---- Time-of-day town chatter ----
    // Four mood pools keyed to the shared day/night clock (TimeOfDay), so what townsfolk say tracks the hour:
    // a "morning!" at sunrise, "getting late" at dusk, "you're out late" after dark. PickTownLine() below is the
    // single choke that reads the clock and picks the right pool — callers just ask for a line.

    static readonly string[] TownMorning =
    {
        "Morning, traveler!",
        "Up with the sun, eh?",
        "Fresh start to the day.",
        "Bread's still warm from the oven.",
        "Cock's crowed already.",
        "Early one, aren't you?",
    };

    static readonly string[] TownDay =
    {
        "Lovely day for it.",
        "Fresh bread at the market!",
        "Mind how you go.",
        "Sun's high — good hunting weather.",
        "Welcome to our town.",
        "Busy day today.",
        "Any news from the road?",
        "Off adventuring, are we?",
    };

    static readonly string[] TownEvening =
    {
        "Getting late, traveler.",
        "Best head in before dark.",
        "Sun's going down.",
        "Supper's on soon.",
        "Long shadows tonight.",
        "Storm's coming, I can feel it.",
    };

    static readonly string[] TownNight =
    {
        "You're out late.",
        "Careful in the dark.",
        "Can't sleep either?",
        "Gates are shut till dawn.",
        "Quiet, this hour.",
        "Watch for things in the night.",
    };

    /// <summary>Pick a town bark that fits the current time of day. Reads the shared <see cref="TimeOfDay"/> clock
    /// (0 = sunrise, 0.25 = noon, 0.5 = sunset, 0.75 = midnight) and draws from the matching mood pool; falls back to
    /// the daytime pool when there's no clock in the scene. One random index — no allocation.</summary>
    public static string PickTownLine()
    {
        string[] pool = TownDay;
        var tod = TimeOfDay.Instance;
        if (tod != null)
        {
            float t = tod.time01;
            if (t < 0.125f) pool = TownMorning;        // sunrise
            else if (t < 0.45f) pool = TownDay;        // daytime → late afternoon
            else if (t < 0.625f) pool = TownEvening;   // sunset → dusk
            else pool = TownNight;                     // night → pre-dawn
        }
        return pool[Random.Range(0, pool.Length)];
    }

    // ---- Multi-line town conversation (F-interact dialog box) ----
    // Each entry is a short 2-line exchange, so the dialog box's NEXT → CLOSE flow has something to page through.
    // Still time-of-day driven, like the one-liners above.
    static readonly string[][] ConvoMorning =
    {
        new[] { "Morning, traveler! Up with the sun, I see.", "The market opens at first light — fresh bread, if you're quick about it." },
        new[] { "Bright and early, eh?", "Nothing beats the town before the crowds roll in. Enjoy the quiet." },
    };
    static readonly string[][] ConvoDay =
    {
        new[] { "Lovely day for it, isn't it?", "Mind how you go past the gate — folk say there's monsters on the road." },
        new[] { "Looking for work, or just passing through?", "Either way, the tavern's the place for news. Tell them I sent you." },
    };
    static readonly string[][] ConvoEvening =
    {
        new[] { "Getting late, traveler.", "Best find a bed before the gates shut for the night." },
        new[] { "Sun's near down.", "Storm's coming too, I can feel it in my knees. Don't linger." },
    };
    static readonly string[][] ConvoNight =
    {
        new[] { "You're out late, friend.", "Careful in the dark — not everything out here is friendly." },
        new[] { "Can't sleep either?", "Stay near the lamps. The night has ears in this town." },
    };

    /// <summary>A short multi-line town conversation that fits the current time of day (for the F-interact dialog box).
    /// Same clock buckets as <see cref="PickTownLine"/>; returns a fresh copy-free reference to a canned exchange.</summary>
    public static string[] PickTownConversation()
    {
        string[][] set = ConvoDay;
        var tod = TimeOfDay.Instance;
        if (tod != null)
        {
            float t = tod.time01;
            if (t < 0.125f) set = ConvoMorning;
            else if (t < 0.45f) set = ConvoDay;
            else if (t < 0.625f) set = ConvoEvening;
            else set = ConvoNight;
        }
        return set[Random.Range(0, set.Length)];
    }
}
