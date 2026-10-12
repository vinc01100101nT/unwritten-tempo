using UnityEngine;

/// <summary>
/// A player active that can show up on the HUD hotbar. The composed-skill interpreter
/// (<see cref="SkillRunner"/>, one per authored <see cref="SkillDef"/>) implements this so the
/// <see cref="HotbarPanel"/> can render a slot for it — key, MP cost,
/// a cooldown sweep — and fire it on click, WITHOUT the hotbar knowing the concrete type.
///
/// A skill reaches a slot by being put there — dragged from the Skills window — and the panel finds the runner that
/// casts it through this interface.
/// </summary>
public interface IHotbarSkill
{
    /// <summary>Left-to-right position on the bar (lower = further left).</summary>
    int SlotOrder { get; }
    /// <summary>Short label shown in the slot (fits a 48px cell), e.g. "Bash".</summary>
    string ShortLabel { get; }
    /// <summary>The hotkey glyph shown in the corner, e.g. "1" / "Q".</summary>
    string KeyLabel { get; }
    /// <summary>The two letters the slot wears, falling back to the skill's whole name when none was authored.</summary>
    string Symbol { get; }

    /// <summary>The colour of the weapon this skill belongs to — the slot's background.</summary>
    Color SymbolColor { get; }
    /// <summary>Mana spent per use (shown in the corner; greys the slot when unaffordable).</summary>
    int ManaCost { get; }
    /// <summary>True when the player currently has enough MP to use it.</summary>
    bool Affordable { get; }
    /// <summary>Seconds left on cooldown (0 when ready) — drives the slot's countdown number.</summary>
    float CooldownRemaining { get; }
    /// <summary>Cooldown as a 0..1 fraction (1 = just used, 0 = ready) — drives the radial sweep.</summary>
    float CooldownFraction01 { get; }
    /// <summary>Accent colour for the slot label, so the actives read as distinct.</summary>
    Color Tint { get; }
    /// <summary>Fire the skill now (same target/MP/cooldown rules as the keypress). Returns true if it fired.</summary>
    bool Activate();
}

/// <summary>
/// IS A SKILL READY TO PRESS — the one test, read by anything that presses on the player's behalf (auto-combat's
/// slots). It compares only what the server sent (the cooldown's end, the mana pool) and decides nothing: the press
/// still goes to the server, which still says yes or no.
/// </summary>
public static class HotbarReady
{
    public static bool Now(IHotbarSkill skill)
        => skill != null && skill.CooldownRemaining <= 0f && skill.Affordable;
}
