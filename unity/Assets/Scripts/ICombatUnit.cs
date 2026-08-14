using UnityEngine;

/// <summary>
/// ANY combatant in the game — the player's <see cref="Character"/> or a mob's <see cref="Health"/> — behind ONE
/// handle. This is the type every gameplay effect targets, so a skill effect never has to ask "is this the player or a
/// mob"; it asks the unit. Extends <see cref="IDamageable"/> (which already unified the damage sink) with the rest of
/// what an effect needs: the state store, the heal sink, the HP readout, and the scene handles.
///
/// <para><b>Why this exists.</b> The applier used to carry THREE near-identical switches — one for the caster, one for
/// a mob target, one for the player target — so every effect kind had to be implemented up to three times, and any
/// kind implemented in only one of them was silently unavailable on the other side. That is how "a stat buff can only
/// ever land on the caster" became a hard limit nobody chose: it was not a rule, it was a missing switch case. With one
/// handle there is ONE switch, and every effect is available to every entity by construction.</para>
///
/// <para>Deliberately NOT a base class: <see cref="Character"/> and <see cref="Health"/> already exist with different
/// lifetimes and different owners (the player's stats are a persistent data object, a mob's HP is on its body), and
/// nothing here needs shared storage — only a shared vocabulary.</para>
/// </summary>
public interface ICombatUnit : IDamageable
{
    /// <summary>This unit's durational buffs/debuffs. Already shared by both sides — the store never cared who owned
    /// it, which is why every state works on any unit the moment an effect can reach it.</summary>
    UnitStates States { get; }

    /// <summary>The unit's BODY in the scene — what a shove moves, what a facing turns, what a VFX parents to. Not
    /// always the same object the component sits on: the player's stats live on a persistent data object while the
    /// avatar is spawned separately (see memory "player-two-gameobjects-one-store").</summary>
    Transform Body { get; }

    /// <summary>Current hit points — read by the effects whose strength scales off how hurt the target is.</summary>
    int CurrentHP { get; }

    /// <summary>Hit points at full health.</summary>
    int MaxHP { get; }

    /// <summary>This unit's melee reach at FULL SIZE — its weapon's range, or its unarmed reach. Deliberately NOT
    /// scaled: contact distance depends on how big BOTH bodies are, so the caller blends it against the other unit
    /// (<see cref="UnitScale.MeleeBlend"/>). Storing a pre-scaled reach on one side is what let the player and a
    /// same-sized shadow mob disagree about the identical pairing.</summary>
    float MeleeReachFullSize { get; }

    /// <summary>How big this unit's BODY is, for melee-contact maths — the shared character size for anything built to
    /// the <see cref="CharacterSpec"/> body, the shrunken mob size for a sprite mob. Reported, never folded into a
    /// distance, so any measurement between two units blends the real pair.</summary>
    float MeleeScale { get; }

    /// <summary>Restore hit points (clamped at full, ignored when dead).</summary>
    void Heal(int amount);

    // ---- movement + aggro: the same verbs on every unit ---------------------------------------------------------
    // Each side drives a different rig (the player's controller vs a mob's pathing agent), but the CAPABILITY is
    // identical and named identically, so an effect never has to know which kind of thing it is moving.

    /// <summary>Blink instantly to <paramref name="dest"/> — no travel, no animation.</summary>
    void TeleportTo(Vector3 dest);

    /// <summary>Glide from here to <paramref name="dest"/> at <paramref name="speedMult"/> × this unit's own run
    /// speed. A fixed A→B move, not gated locomotion — it ignores the usual movement gates so a dash always travels.
    /// The ease flags shape the ramp (in = start slow, out = stop gently) over <paramref name="easeSeconds"/>.</summary>
    void GlideTo(Vector3 dest, float speedMult, bool easeIn, bool easeOut, float easeSeconds);

    /// <summary>Shove this unit directly AWAY from <paramref name="worldFrom"/> at <paramref name="speed"/> for
    /// <paramref name="seconds"/>, stunning its own steering for <paramref name="stagger"/>s so it doesn't fight the
    /// slide. Pull is the same verb aimed from a mirrored point.</summary>
    void Shove(Vector3 worldFrom, float speed, float seconds, float stagger);

    /// <summary>Force this unit to fixate on <paramref name="source"/> for <paramref name="seconds"/> — it engages
    /// that target and cannot drop it, leash away, or flee while the lock holds.</summary>
    void ForceAggro(ICombatUnit source, float seconds);
}
