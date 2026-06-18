using UnityEngine;

/// <summary>The player's active-skill identity. A class = which kit of <see cref="IHotbarSkill"/> components
/// is enabled (see <see cref="ClassState"/> / Player3DController.ApplyClass). NOT a stat lock.</summary>
public enum PlayerClass { Swordsman, Mage }

/// <summary>Persists the chosen class (PlayerPrefs) so it survives restarts and is read at boot.</summary>
public static class ClassState
{
    const string Key = "unwritten.class";
    static PlayerClass? current;

    public static PlayerClass Current
    {
        get { if (current == null) current = (PlayerClass)PlayerPrefs.GetInt(Key, 0); return current.Value; }
        set { current = value; PlayerPrefs.SetInt(Key, (int)value); PlayerPrefs.Save(); }
    }
}

/// <summary>
/// A player active that can show up on the HUD hotbar. Each Swordsman/class skill component
/// (<see cref="Player3DBash"/>, <see cref="Player3DCharge"/>, <see cref="Player3DCaster"/>…)
/// implements this so the <see cref="HotbarPanel"/> can render a slot for it — key, MP cost,
/// a cooldown sweep — and fire it on click, WITHOUT the hotbar knowing the concrete type.
///
/// This is the legibility layer for the kit: add a new active component that implements this
/// interface and it appears on the bar automatically (slot order = <see cref="SlotOrder"/>).
/// </summary>
public interface IHotbarSkill
{
    /// <summary>Left-to-right position on the bar (lower = further left).</summary>
    int SlotOrder { get; }
    /// <summary>Short label shown in the slot (fits a 48px cell), e.g. "Bash".</summary>
    string ShortLabel { get; }
    /// <summary>The hotkey glyph shown in the corner, e.g. "1" / "Q".</summary>
    string KeyLabel { get; }
    /// <summary>Slot icon (from the Skill Icon pack), or null to fall back to <see cref="ShortLabel"/>.</summary>
    Sprite Icon { get; }
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
    /// <summary>Whether this skill belongs to (shows up for) the given class. Universal skills return true
    /// for every class; class skills return true only for their own.</summary>
    bool InClass(PlayerClass c);
    /// <summary>Fire the skill now (same target/MP/cooldown rules as the keypress). Returns true if it fired.</summary>
    bool Activate();
}
