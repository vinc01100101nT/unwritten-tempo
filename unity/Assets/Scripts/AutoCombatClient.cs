using System;

/// <summary>
/// AUTO-COMBAT ON THE PLAYER'S MACHINE — what the server last said, and the three things this machine may ask.
///
/// <para>Whether it is on is the server's answer (see <c>NetPlayer</c>'s auto-combat half); this holds the last one and
/// never turns it on by itself. It may turn it OFF on the spot: the player taking the reins back (a click, a key, WASD)
/// stops the hunt at once and the server is told, because the legs are this machine's and a hunt that kept walking for a
/// round trip after the player grabbed them would fight the player.</para>
/// </summary>
public static class AutoCombatClient
{
    /// <summary>Is auto-combat on — the server's last word, or off the instant this machine stopped it.</summary>
    public static bool On { get; private set; }

    /// <summary>The settings as the server last sent them. Never null.</summary>
    public static AutoCombatSettings Settings { get; private set; } = new AutoCombatSettings();

    /// <summary>Moves on every word from the server and every local stop, so a window redraws only when it must.</summary>
    public static int Version { get; private set; }

    /// <summary>How many times a Fly Wing has set the player down elsewhere while hunting — the hunt moves its Radius
    /// spot when this changes.</summary>
    public static int Landings { get; private set; }

    /// <summary>SERVER'S WORD: on or off, the settings, and a sentence to say when there is one.</summary>
    public static void FromServer(bool on, AutoCombatSettings settings, string why)
    {
        On = on;
        Settings = settings ?? new AutoCombatSettings();
        refusedUntil.Clear();
        // THE CHARACTER CARRIES THE SLOTS TOO, so the skills only auto-combat presses get runners on this machine —
        // the same rebuild a change to the bar asks for.
        owedToCharacter = true;
        GiveToCharacterIfOwed();
        if (!string.IsNullOrEmpty(why)) OverheadNotice.Warn(why);
        Version++;
    }

    // The word can arrive before this machine has built its character (the bag has the same race) — held until it has.
    static bool owedToCharacter;

    /// <summary>Lay the last settings onto the character, once there is one to lay them on. Asked every frame by the
    /// hunt; a bool compare when nothing is owed.</summary>
    public static void GiveToCharacterIfOwed()
    {
        if (!owedToCharacter) return;
        var me = Character.Instance;
        if (me == null) return;
        owedToCharacter = false;
        bool slotsMoved = !SameSlots(me.AutoCombat.slots, Settings.slots);
        me.AutoCombat = Settings.Clone();
        if (slotsMoved) me.RefreshUsableSkills();
    }

    // SKILLS THE SERVER REFUSED from an auto-skill slot, each skipped for a while — pressed again a quarter of a second
    // later it would only be refused again. For a while rather than for good, because a refusal can be passing (out of
    // reach, a cooldown still crossing the wire). At most four entries.
    static readonly System.Collections.Generic.Dictionary<string, float> refusedUntil =
        new System.Collections.Generic.Dictionary<string, float>(4);
    const float SecondsToSkipARefusedSkill = 10f;

    /// <summary>SERVER'S WORD: it refused a press of <paramref name="skillId"/>. Auto-combat leaves it alone a while.</summary>
    public static void ServerRefused(string skillId)
    {
        var slots = Settings.slots;
        if (string.IsNullOrEmpty(skillId) || slots == null || System.Array.IndexOf(slots, skillId) < 0) return;
        refusedUntil[skillId] = UnityEngine.Time.time + SecondsToSkipARefusedSkill;
    }

    /// <summary>Is this skill being left alone after a refusal?</summary>
    public static bool Refused(string skillId)
        => refusedUntil.TryGetValue(skillId, out float until) && UnityEngine.Time.time < until;

    /// <summary>SERVER'S WORD: a Fly Wing set them down elsewhere, and the hunt goes on from there.</summary>
    public static void LandedElsewhere() => Landings++;

    /// <summary>Ask the server to switch it on. Nothing changes here until it answers.</summary>
    public static void AskOn() => GameNetwork.MyPlayer?.AskAutoCombat(true);

    /// <summary>Stop it now, here, and tell the server. Nothing happens when it is already off.</summary>
    public static void StopHere()
    {
        if (!On) return;
        On = false;
        Version++;
        GameNetwork.MyPlayer?.AskAutoCombat(false);
    }

    /// <summary>T: on if it is off (asked), off if it is on (at once).</summary>
    public static void Toggle()
    {
        if (On) StopHere();
        else AskOn();
    }

    /// <summary>Ask the server to keep these settings. It answers with what it actually kept.</summary>
    public static void AskToKeep(AutoCombatSettings settings) => GameNetwork.MyPlayer?.AskAutoCombatSettings(settings);

    static bool SameSlots(string[] a, string[] b)
    {
        if (a == null || b == null) return a == b;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
        return true;
    }
}
