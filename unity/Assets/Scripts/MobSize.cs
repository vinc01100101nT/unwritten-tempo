using UnityEngine;

/// <summary>A creature's size GROUP — the three a card or a weapon's size penalty will ask about. Never authored: it is
/// worked out from the creature's own size number (<see cref="MobSizes.GroupOf"/>). No asset stores it; the order
/// matters only because the weapon-vs-size chart's columns follow it.</summary>
public enum MobSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

/// <summary>
/// THE SIZE TABLE. A creature's size is ONE number, how many times a character's height it stands — Medium 1 is a
/// character, the player, the townsfolk and every shadow. Its body counts that same number for melee reach, so how big
/// it LOOKS and how far it REACHES read one number and can never drift apart. Which of the three size groups it belongs
/// to is worked out from that number by the ranges here, so the group can never disagree with the size either.
/// Plain arithmetic, no components: <see cref="MobBody"/> applies it.
/// </summary>
public static class MobSizes
{
    /// <summary>Below this a creature is Small.</summary>
    public const float SmallBelow = 0.6f;
    /// <summary>Above this a creature is Large; from <see cref="SmallBelow"/> up to and including this, Medium.</summary>
    public const float LargeAbove = 1.5f;
    /// <summary>The largest size a creature may have.</summary>
    public const float Largest = 5.2f;
    /// <summary>The smallest size a creature may have — above zero, so no creature can be authored invisible.</summary>
    public const float Smallest = 0.1f;
    /// <summary>A character's size, and every shadow's.</summary>
    public const float Character = 1f;

    /// <summary>An authored size held inside the range a creature may have.</summary>
    public static float Clamp(float size) => Mathf.Clamp(size, Smallest, Largest);

    /// <summary>Which size group a creature of this size belongs to.</summary>
    public static MobSize GroupOf(float size) =>
        size < SmallBelow ? MobSize.Small : size <= LargeAbove ? MobSize.Medium : MobSize.Large;

    /// <summary>The size a creature actually has: a shadow is a character, so it is always
    /// <see cref="Character"/> whatever its entry says; every other kind has the size it was authored with.</summary>
    public static float Of(CreatureKind kind, float authored) =>
        kind == CreatureKind.Shadow ? Character : Clamp(authored);

    /// <summary>World height, feet to crown, of a creature of size <paramref name="size"/> beside a character
    /// <paramref name="characterHeight"/> tall.</summary>
    public static float Height(float size, float characterHeight) => characterHeight * size;

    /// <summary>Melee-contact scale of a body of size <paramref name="size"/>, against a character's
    /// <paramref name="characterScale"/> — the value <see cref="UnitScale.MeleeBlend"/> takes.</summary>
    public static float MeleeScale(float size, float characterScale) => characterScale * size;

    /// <summary>The uniform scale that makes art whose crown sits <paramref name="crownAtScaleOne"/> above its feet at
    /// scale one stand <paramref name="height"/> tall. Art with no measured crown keeps scale one.</summary>
    public static float BodyScale(float height, float crownAtScaleOne) =>
        crownAtScaleOne > 1e-4f ? height / crownAtScaleOne : 1f;
}

/// <summary>
/// Puts a creature's SIZE on its body — the one door, used by the prefab baker and again by the spawner as each
/// creature is made, so a prefab baked before a size edit corrects itself. A flat creature is scaled so its art stands
/// its size's height; its collision capsule rides that scale; its routing footprint grows to fit a body wider than
/// <see cref="RoutingRadius"/> (never shrinks below it); and its brain learns the size its melee reach reads. A shadow's 3D body is
/// fitted by <see cref="MobShadow3DVisual"/> instead, so only its brain is told (a character's size, 1).
/// </summary>
public static class MobBody
{
    /// <summary>The routing footprint every flat creature has at least — what the router steers a body around
    /// obstacles with. A body wider than this routes with its own width instead.</summary>
    public const float RoutingRadius = 0.3f;

    public static void Fit(GameObject go, CreatureKind kind, float authored)
    {
        float size = MobSizes.Of(kind, authored);
        var brain = go.GetComponent<MonsterAI3D>();   // perf-ok: once per creature made
        if (brain != null) brain.size = size;
        if (CreatureKinds.Is3D(kind)) return;

        var anim = go.GetComponent<BillboardAnimator3D>();   // perf-ok: once per creature made
        if (anim == null) return;
        float height = MobSizes.Height(size, CharacterSpec.Active.bodyHeight * UnitScale.CharacterScale);
        float s = anim.FitToHeight(height);

        var cc = go.GetComponent<CharacterController>();   // perf-ok: once per creature made
        if (cc == null) return;
        // Resized, so the walking rule is put back on at the new size: the full step, its capsule grown to take it.
        GroundElevation.ApplyWalkRules(cc);
        var agent = go.GetComponent<PathAgent3D>();   // perf-ok: once per creature made
        if (agent != null) agent.agentRadius = Mathf.Max(RoutingRadius, cc.radius * s);
    }
}
