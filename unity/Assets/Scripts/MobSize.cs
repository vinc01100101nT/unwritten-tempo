using UnityEngine;

/// <summary>A creature's body size. Medium is a character's size — the player, the townsfolk and every shadow stand
/// exactly this tall. Serialized by int on every creature asset, so order is load-bearing: append, never insert.</summary>
public enum MobSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
    XL = 3,
}

/// <summary>
/// THE SIZE TABLE — the one place a size's number is written. A creature stands <see cref="Multiple"/> times a
/// character's height, and its body counts that same multiple for melee reach, so how big it LOOKS and how far it
/// REACHES read one number and can never drift apart. Plain arithmetic, no components: <see cref="MobBody"/> applies it.
/// </summary>
public static class MobSizes
{
    /// <summary>How many times a character's height this size stands — and, the same number, how big its body counts
    /// for melee contact.</summary>
    public static float Multiple(MobSize s)
    {
        switch (s)
        {
            case MobSize.Small: return 0.3f;
            case MobSize.Large: return 2.4f;
            case MobSize.XL:    return 5.2f;
            default:            return 1f;
        }
    }

    /// <summary>The size a creature actually has: a shadow is a character, so it is always Medium whatever its asset
    /// says; every other kind has the size it was authored with.</summary>
    public static MobSize Of(CreatureKind kind, MobSize authored) =>
        kind == CreatureKind.Shadow ? MobSize.Medium : authored;

    /// <summary>World height, feet to crown, of a creature of size <paramref name="s"/> beside a character
    /// <paramref name="characterHeight"/> tall.</summary>
    public static float Height(MobSize s, float characterHeight) => characterHeight * Multiple(s);

    /// <summary>Melee-contact scale of a body of size <paramref name="s"/>, against a character's
    /// <paramref name="characterScale"/> — the value <see cref="UnitScale.MeleeBlend"/> takes.</summary>
    public static float MeleeScale(MobSize s, float characterScale) => characterScale * Multiple(s);

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
/// fitted by <see cref="MobShadow3DVisual"/> instead, so only its brain is told (Medium).
/// </summary>
public static class MobBody
{
    /// <summary>The routing footprint every flat creature has at least — what the router steers a body around
    /// obstacles with. A body wider than this routes with its own width instead.</summary>
    public const float RoutingRadius = 0.3f;

    public static void Fit(GameObject go, CreatureKind kind, MobSize authored)
    {
        MobSize size = MobSizes.Of(kind, authored);
        var brain = go.GetComponent<MonsterAI3D>();   // perf-ok: once per creature made
        if (brain != null) brain.size = size;
        if (CreatureKinds.Is3D(kind)) return;

        var anim = go.GetComponent<BillboardAnimator3D>();   // perf-ok: once per creature made
        if (anim == null) return;
        float height = MobSizes.Height(size, CharacterSpec.Active.bodyHeight * UnitScale.CharacterScale);
        float s = anim.FitToHeight(height);

        var cc = go.GetComponent<CharacterController>();   // perf-ok: once per creature made
        if (cc == null) return;
        MobRespawn.ClampStepOffset(cc);
        var agent = go.GetComponent<PathAgent3D>();   // perf-ok: once per creature made
        if (agent != null) agent.agentRadius = Mathf.Max(RoutingRadius, cc.radius * s);
    }
}
