using UnityEngine;

/// <summary>
/// The single source of truth for the world's step-height rules — shared by the terrain builder (how tall one
/// painted elevation notch is), the player's collision capsule (how tall a step it auto-climbs), and the
/// pathfinder (how tall a rise mobs treat as a walkable stair vs a blocking cliff). Keeping all three on ONE
/// number is what stops the player and the mobs from disagreeing about which edges are curbs and which are cliffs.
///
/// The model: one painted notch = <see cref="LevelHeight"/>. A single-notch step (curb, gutter lip, one stair)
/// is BELOW <see cref="MaxClimbStep"/>, so it's walkable. A face two-or-more notches tall clears MaxClimbStep, so
/// it reads as a CLIFF — the capsule bumps it and the pathfinder routes around it. Stairs are just a run of
/// single-notch steps.
/// </summary>
public static class GroundElevation
{
    /// <summary>World-units of rise per painted elevation notch (gutter = −1, raised sidewalk = +1, …).</summary>
    public const float LevelHeight = 0.05f;

    /// <summary>Max rise between adjacent ground a unit will walk up (auto-climb / stair); a bigger rise is a
    /// cliff and blocks. Sits between one notch (0.05, walkable) and two (0.1, a cliff). The player capsule's
    /// stepOffset and the pathfinder's maxStep both read THIS so they can never drift apart.</summary>
    public const float MaxClimbStep = 0.075f;

    /// <summary>World-Y for an integer elevation level.</summary>
    public static float LevelToY(int level) => level * LevelHeight;

    /// <summary>How fast anything falls, in world units per second squared. ONE number for every unit: the player's
    /// controller and the NPC/mob pathing agent both apply it, and each used to keep its own serialized copy. They
    /// happened to agree (both 12) — which is exactly how this kind of duplicate survives. It costs nothing until
    /// someone retunes one side, and then the player and the mobs fall at different rates for no reason anyone chose.</summary>
    /// Tunable at runtime from the dev panel, like the unit-size dials — still ONE value for every unit.
    public static float Gravity = 12f;
}
