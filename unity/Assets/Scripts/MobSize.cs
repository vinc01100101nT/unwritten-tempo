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
    public const float SmallBelow = 0.8f;
    /// <summary>Above this a creature is Large; from <see cref="SmallBelow"/> up to and including this, Medium.</summary>
    public const float LargeAbove = 1.7f;
    /// <summary>The largest size a creature may have.</summary>
    public const float Largest = 5.4f;
    /// <summary>The smallest size a creature may have: Plopling's, the level-1 creature. HARD RULE (the user, 2026-10-05) —
    /// no creature is ever smaller than Plopling.</summary>
    public const float Smallest = 0.5f;
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

    /// <summary>How far above its feet a FLYING creature of size <paramref name="size"/> is drawn, so the middle of its
    /// body sits level with the crown of a character <paramref name="characterHeight"/> tall — the player's head.
    /// A creature twice a character's height or taller already has its middle there, so it is not raised.</summary>
    public static float FlyLift(float size, float characterHeight) =>
        Mathf.Max(0f, characterHeight - 0.5f * Height(size, characterHeight));

    /// <summary>A world-unit lift expressed in the art's own units at scale one, for a body drawn at
    /// <paramref name="scale"/> — what the picture's pivot is moved by.</summary>
    public static float LiftAtScaleOne(float lift, float scale) => scale > 1e-6f ? lift / scale : 0f;
}

/// <summary>
/// Puts a creature's SIZE on its body — the one door, used by the prefab baker and again by the spawner as each
/// creature is made, so a prefab baked before a size edit corrects itself. A flat creature is scaled so its art stands
/// its size's height; its collision capsule rides that scale; its routing footprint grows to fit a body wider than
/// <see cref="RoutingRadius"/> (never shrinks below it); and its brain learns the size its melee reach reads. A shadow's 3D body is
/// fitted by <see cref="MobShadow3DVisual"/> instead, so only its brain is told (a character's size, 1).
///
/// <para>A creature that FLIES has only its picture raised (<see cref="MobSizes.FlyLift"/>), together with every point
/// read off its body (<see cref="UnitAnchor"/>): its aim point, nameplate, numbers and the cursor's pick. Its walking
/// body, collision capsule and routing stay on the ground, and its melee reach is measured exactly as before.</para>
/// </summary>
public static class MobBody
{
    /// <summary>The routing footprint every flat creature has at least — what the router steers a body around
    /// obstacles with. A body wider than this routes with its own width instead.</summary>
    public const float RoutingRadius = 0.3f;

    /// <summary>The measurements a flat creature's picture was drawn at — what its body is fitted against: how far the
    /// tallest frame's crown stands above its feet and how wide the widest frame is painted, in world units at scale
    /// one, and how wide the body is drawn against its height (1 for every creature). Taken from the picture's own
    /// authored numbers, or a saved character's, never re-measured — so a machine that draws nothing fits the same body.</summary>
    public readonly struct FlatArt
    {
        public readonly bool exists;
        public readonly float crownAtScaleOne, paintedWidthAtScaleOne, drawnWidth;

        public FlatArt(float crownAtScaleOne, float paintedWidthAtScaleOne, float drawnWidth)
        {
            exists = true;
            this.crownAtScaleOne = crownAtScaleOne;
            this.paintedWidthAtScaleOne = paintedWidthAtScaleOne;
            this.drawnWidth = drawnWidth;
        }

        /// <summary>The numbers <paramref name="anim"/> was authored with; nothing to fit against when there is none.</summary>
        public static FlatArt Of(BillboardAnimator3D anim) =>
            anim != null ? new FlatArt(anim.crownAtScaleOne, anim.paintedWidthAtScaleOne, anim.DrawnWidth) : default;
    }

    public static void Fit(GameObject go, CreatureKind kind, float authored, bool flies, in FlatArt art)
    {
        float size = MobSizes.Of(kind, authored);
        var brain = go.GetComponent<MonsterAI3D>();   // perf-ok: once per creature made
        if (brain != null) brain.size = size;
        if (CreatureKinds.Is3D(kind) || !art.exists) return;

        // THE BODY: scaled so its art stands its size's height, and that height and the matching width stated as the
        // unit's real body (UnitBody), so its nameplate, health bar, aim point and click area use the measured body.
        float characterHeight = CharacterSpec.Active.bodyHeight * UnitScale.CharacterScale;
        float height = MobSizes.Height(size, characterHeight);
        float s = MobSizes.BodyScale(height, art.crownAtScaleOne);
        var scale = new Vector3(s * art.drawnWidth, s, s);
        go.transform.localScale = scale;
        if (art.crownAtScaleOne > 0f) UnitBody.Set(go, height, art.paintedWidthAtScaleOne * s * art.drawnWidth);
        float lift = flies ? MobSizes.FlyLift(size, characterHeight) : 0f;
        UnitBody.SetLift(go, lift);
        // …AND ITS PICTURE, on a machine that draws one: breathing and biting squash around this scale, and a flier's
        // picture is drawn the lift above its feet.
        var anim = go.GetComponent<BillboardAnimator3D>();   // perf-ok: once per creature made
        if (anim != null) anim.TakeFit(scale, lift);

        var cc = go.GetComponent<CharacterController>();   // perf-ok: once per creature made
        if (cc == null) return;
        // Resized, so the walking rule is put back on at the new size: the full step, its capsule grown to take it.
        GroundElevation.ApplyWalkRules(cc);
        var agent = go.GetComponent<PathAgent3D>();   // perf-ok: once per creature made
        if (agent != null) agent.agentRadius = Mathf.Max(RoutingRadius, cc.radius * s);
    }

    /// <summary>Take every part that draws a FLAT creature off <paramref name="go"/> — the turn to face the camera, the
    /// frames and breathing, the bite, the cast shadow and the depth order — and hide its sprite. The ONE list, used by
    /// the server (which draws nothing) and by a creature that becomes a live-3D character. The sprite is only hidden:
    /// <see cref="Health"/> and <see cref="DeathDissolve"/> require it.
    ///
    /// <para><paramref name="beforeItWakes"/>: the creature is still being made under a switched-off parent, so none of
    /// these parts has run a single line yet and they are removed on the spot. Otherwise each is switched off now and
    /// removed at the end of the frame, the only removal Unity allows on a live object.</para></summary>
    public static void DropFlatPicture(GameObject go, bool beforeItWakes)
    {
        Remove<AttackLunge>(go, beforeItWakes);
        Remove<BillboardShadow>(go, beforeItWakes);
        Remove<Billboard>(go, beforeItWakes);
        Remove<CameraDepthSorter>(go, beforeItWakes);
        Remove<BillboardAnimator3D>(go, beforeItWakes);
        var sprite = go.GetComponent<SpriteRenderer>();   // perf-ok: once per creature made
        if (sprite != null) sprite.enabled = false;
    }

    static void Remove<T>(GameObject go, bool now) where T : Behaviour
    {
        var part = go.GetComponent<T>();   // perf-ok: once per creature made
        if (part == null) return;
        if (now) { Object.DestroyImmediate(part); return; }
        part.enabled = false;
        Object.Destroy(part);
    }
}
