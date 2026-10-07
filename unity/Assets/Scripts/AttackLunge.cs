using UnityEngine;

/// <summary>
/// The bite of a flat creature whose picture sheet has no attack frame (every ordinary creature), sold with its body
/// instead of a pose, the way a Ragnarok Poring attacks: it squashes flat like a pressed spring, stretches up tall
/// leaning toward whoever it is biting, slams into them — flattening wide as it hops in — and wobbles back to round.
///
/// <para>Purely a picture, played on the machines WATCHING the creature from the swing beat the server announces
/// (<see cref="BillboardAnimator3D.ShowAttack"/>); the server itself draws nothing. It moves the body's scale, a small
/// sideways tip (<see cref="Billboard.lean"/>) and a short hop toward the target, and hands every one of them back
/// exactly as it found them: the hop is taken off again before anything else moves the body each frame, so the
/// position the server sends is never pushed around by it.</para>
/// </summary>
[DefaultExecutionOrder(-50)]
public class AttackLunge : MonoBehaviour
{
    [Tooltip("How long one whole bite takes, in seconds — squash, stretch, slam and wobble. A creature that bites faster " +
             "than this plays it faster, so each bite finishes before the next begins.")]
    public float biteSeconds = 0.5f;
    [Tooltip("How flat it presses down before the bite, and how flat it spreads on impact (0.25 = 25% wider and shorter).")]
    public float squash = 0.25f;
    [Tooltip("How tall it stretches as it rears up toward the target (0.25 = 25% taller and narrower).")]
    public float stretchTall = 0.25f;
    [Tooltip("How far it tips toward the target while rearing up and slamming, in degrees.")]
    public float leanDegrees = 15f;
    [Tooltip("How far it hops toward the target on the slam, as a share of the gap between them.")]
    public float hopShare = 0.3f;
    [Tooltip("The longest hop toward the target on the slam, in metres.")]
    public float hopMax = 0.35f;
    [Tooltip("How many times it wobbles back and forth while settling back to round.")]
    public float wobbles = 2f;

    // The bite's shape, as shares of its length: the press down, the rear up, the slam, then the wobble to the end.
    const float PressEnd = 0.16f, RearEnd = 0.36f, SlamEnd = 0.5f;

    Vector3 buildScale;   // fallback capture, used only when this mob has no BillboardAnimator3D to own the rest scale
    BillboardAnimator3D anim;
    Billboard billboard;

    Transform target;
    float startedAt, length;
    bool biting;
    Vector3 hopApplied;   // the hop currently added to the body's position, taken off again before anything else moves it

    /// <summary>True while a bite is playing — idle breathing yields to it, but not while a bite is only waiting to begin.</summary>
    public bool Biting => biting && Time.time >= startedAt;

    void Awake()
    {
        anim = GetComponent<BillboardAnimator3D>();
        billboard = GetComponent<Billboard>();
        buildScale = transform.localScale;
    }

    // The rest scale the bite squashes around — READ from the sprite animator, which owns this mob's live body scale,
    // so the bite and the idle breathe can't disagree about what "rest" means. The local capture is only a fallback
    // for a mob that has no sprite animator at all.
    Vector3 BaseScale => anim != null ? anim.BaseScale : buildScale;

    /// <summary>Play one bite toward <paramref name="aimedAt"/> (null = in place, no tip and no hop) that SLAMS IN
    /// <paramref name="connectsIn"/> seconds from now — the moment the server lands the blow. It starts just early enough
    /// for that, and is squeezed shorter than <see cref="biteSeconds"/> when the blow lands sooner than a whole bite's
    /// run-up. 0 = no blow lands with this swing (a throw), so it bites at once. A new bite replaces one still playing.</summary>
    public void Bite(Transform aimedAt, float connectsIn)
    {
        TakeOffHop();
        if (billboard != null) billboard.lean = 0f;
        transform.localScale = BaseScale;
        target = aimedAt;
        if (connectsIn > 0f)
        {
            length = Mathf.Min(biteSeconds, connectsIn / SlamEnd);
            startedAt = Time.time + connectsIn - SlamEnd * length;
        }
        else
        {
            length = biteSeconds;
            startedAt = Time.time;
        }
        biting = length > 0f;
        // Switched on only while a bite plays, so a creature standing still costs nothing a frame.
        if (biting) enabled = true;
    }

    void Update() => TakeOffHop();

    void LateUpdate()
    {
        if (!biting) { enabled = false; return; }
        float p = (Time.time - startedAt) / length;
        if (p < 0f) return;   // waiting for its run-up to begin
        if (p >= 1f) { Finish(); return; }

        // Toward the target, flat on the ground, and which side of the screen it is on.
        Vector3 to = target != null ? target.position - transform.position : Vector3.zero;
        to.y = 0f;
        float gap = to.magnitude;   // perf-ok: once per frame per biting creature, only while a bite plays
        float across = gap > 0.001f ? Vector3.Dot(to, CameraRig.RightXZ) : 0f;
        float side = across > 0.001f ? 1f : across < -0.001f ? -1f : 0f;

        float wide, tip, hop;
        if (p < PressEnd)
        {
            // Pressed down like a spring.
            wide = squash * Ease(p / PressEnd);
            tip = 0f; hop = 0f;
        }
        else if (p < RearEnd)
        {
            // Rears up tall, tipping toward the target.
            float e = Ease((p - PressEnd) / (RearEnd - PressEnd));
            wide = Mathf.Lerp(squash, -stretchTall, e);
            tip = e; hop = 0f;
        }
        else if (p < SlamEnd)
        {
            // Slams in: hops forward and spreads flat on impact.
            float e = Ease((p - RearEnd) / (SlamEnd - RearEnd));
            wide = Mathf.Lerp(-stretchTall, squash, e);
            tip = 1f; hop = e;
        }
        else
        {
            // Wobbles back to round, the hop and the tip easing home.
            float k = (p - SlamEnd) / (1f - SlamEnd);
            float fade = (1f - k) * (1f - k);
            wide = squash * fade * Mathf.Cos(k * wobbles * 2f * Mathf.PI);
            float home = 1f - Ease(k);
            tip = home; hop = home;
        }

        Vector3 b = BaseScale;
        transform.localScale = new Vector3(b.x * (1f + wide), b.y * (1f - wide), b.z);
        if (billboard != null) billboard.lean = leanDegrees * tip * side;
        if (gap > 0.001f)
        {
            hopApplied = to * (Mathf.Min(gap * hopShare, hopMax) * hop / gap);
            transform.position += hopApplied;
        }
    }

    static float Ease(float x) => x * x * (3f - 2f * x);

    void TakeOffHop()
    {
        if (hopApplied == Vector3.zero) return;
        transform.position -= hopApplied;
        hopApplied = Vector3.zero;
    }

    void Finish()
    {
        biting = false;
        target = null;
        TakeOffHop();
        if (billboard != null) billboard.lean = 0f;
        transform.localScale = BaseScale;
        enabled = false;
    }

    // A body banked mid-bite (switched off for the pool) is handed back whole — upright, unshifted and at rest scale —
    // or it would come back tipped and squashed. A live-3D creature destroys this component alone and sets its own
    // unscaled root, which a scale written here would undo, so the scale is only put back when the WHOLE object goes off.
    void OnDisable()
    {
        bool wholeBodyOff = !gameObject.activeInHierarchy;
        biting = false;
        target = null;
        TakeOffHop();
        if (billboard != null) billboard.lean = 0f;
        if (wholeBodyOff) transform.localScale = BaseScale;
    }
}
