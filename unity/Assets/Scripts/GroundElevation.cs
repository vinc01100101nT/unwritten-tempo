using UnityEngine;

/// <summary>
/// The single source of truth for how ANYTHING walks — the player, every creature, every townsperson — and for the
/// route-finder that plans their walks. Two numbers: the tallest step a body steps straight up
/// (<see cref="MaxClimbStep"/>) and the steepest slope it walks (<see cref="MaxWalkSlopeDegrees"/>). Every body's
/// controller gets them from <see cref="ApplyWalkRules"/>, and the route-finder asks <see cref="Walkable"/>, so what a
/// body can walk and what a route plans for it are the same answer, for the player and a creature alike.
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

    /// <summary>The steepest slope any body walks up or down, in degrees. The player's legs and every creature's.</summary>
    public const float MaxWalkSlopeDegrees = 45f;
    static readonly float MaxWalkSlopeTan = Mathf.Tan(MaxWalkSlopeDegrees * Mathf.Deg2Rad);

    /// <summary>Can a body walk from one spot to a neighbouring one? <paramref name="rise"/> = the height change,
    /// <paramref name="run"/> = the level distance between them, and each slope is the ground's steepness at that spot
    /// as a tangent (rise per unit across; 0 = flat, see <see cref="SlopeTan"/>). Yes when neither spot is steeper than
    /// <see cref="MaxWalkSlopeDegrees"/> and the rise is no more than a step plus what the slope under them explains —
    /// so a 20° hillside is walked, while a 0.1 ledge between two flat spots is a wall, exactly as a body's controller
    /// treats them.</summary>
    public static bool Walkable(float rise, float run, float slopeTanA, float slopeTanB)
    {
        if (slopeTanA > MaxWalkSlopeTan || slopeTanB > MaxWalkSlopeTan) return false;
        return Mathf.Abs(rise) <= MaxClimbStep + run * (slopeTanA + slopeTanB) * 0.5f;
    }

    /// <summary>The steepness of ground with this surface normal, as a tangent: 0 flat, 1 at 45°, infinite for a wall.</summary>
    public static float SlopeTan(Vector3 normal)
        => normal.y <= 1e-4f ? float.PositiveInfinity : Mathf.Sqrt(normal.x * normal.x + normal.z * normal.z) / normal.y;

    /// <summary>Put the walking rules on a body's controller — the one place any body's step and slope are set, for the
    /// player and a creature alike. The step is kept under what this controller's capsule can physically take (a
    /// small creature's is tiny), which Unity otherwise refuses when the controller is switched on.</summary>
    public static void ApplyWalkRules(CharacterController cc)
    {
        if (cc == null) return;
        cc.slopeLimit = MaxWalkSlopeDegrees;
        cc.stepOffset = Mathf.Min(MaxClimbStep, StepBound(cc));
    }

    /// <summary>The tallest step this controller will accept, at its current size. PhysX counts the controller's
    /// height as the WHOLE capsule (the end caps are the radius), so the real allowance is the cylinder part plus the
    /// two caps, all scaled — kept 10% under that for the idle-breathe squash and float slop.</summary>
    public static float StepBound(CharacterController cc)
    {
        Vector3 s = cc.transform.lossyScale;
        float scaleY = Mathf.Abs(s.y);
        float scaleR = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
        float cylH = Mathf.Max(cc.height - 2f * cc.radius, 0f) * scaleY;
        return (cylH + 2f * cc.radius * scaleR) * 0.9f;
    }

    /// <summary>World-Y for an integer elevation level.</summary>
    public static float LevelToY(int level) => level * LevelHeight;

    /// <summary>How fast anything falls, in world units per second squared. ONE number for every unit: the player's
    /// controller and the NPC/mob pathing agent both apply it, and each used to keep its own serialized copy. They
    /// happened to agree (both 12) — which is exactly how this kind of duplicate survives. It costs nothing until
    /// someone retunes one side, and then the player and the mobs fall at different rates for no reason anyone chose.</summary>
    /// Tunable at runtime from the dev panel, like the unit-size dials — still ONE value for every unit.
    public static float Gravity = 12f;
}
