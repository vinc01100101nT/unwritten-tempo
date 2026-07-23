using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// A reusable ground-target reticle for placed AoE skills (e.g. the Mage's Meteor). <see cref="Begin"/>
/// puts the player into AIM mode: the skill's CAST-CIRCLE effect follows the cursor on the ground, clamped
/// to <c>castRange</c> from the player. <b>Left-click</b> confirms (fires the skill's callback with the point);
/// <b>Esc</b> (or re-pressing the skill) cancels. The circle telegraph is the SAME effect the wind-up shows,
/// so aim → wind-up → fire reads as one continuous ring at the impact point (no separate outline).
///
/// If the caller passes no circle id (an un-authored skill), aiming still works — there's just no visible
/// reticle. While aiming, <see cref="Active"/> is true and <see cref="Player3DController"/> skips its
/// click/move input that frame (same pattern as <see cref="DevSettings.Blocking"/>) so the confirm click
/// doesn't also order a move. Self-boots a single hidden instance on first use.
/// </summary>
public class GroundTargeter : MonoBehaviour
{
    /// <summary>True while the player is aiming a placed skill — gameplay click input is suppressed.
    /// Self-heals: if the instance was destroyed (scene unload) this reads false, so input can't get stuck.</summary>
    public static bool Active => active && inst != null;

    static bool active;
    static GroundTargeter inst;

    Transform player;
    float castRange;
    System.Action<Vector3> onConfirm;
    Camera cam;
    Vector3 point;
    bool hasPoint;

    // The circle telegraph: the skill's cast-circle effect, spawned lazily on the first valid ground point (so it
    // never flashes at world origin) and moved with the cursor via this object's transform (the Attach follows it).
    string circleId;
    Color circleTint;
    float circleRadius;   // the skill's real AoE world radius; the telegraph is sized to land its ring here
    Vfx.EffectHandle circle;
    static readonly Vector3 CircleLift = new Vector3(0f, 0.05f, 0f);   // hover just above the ground (avoid z-fighting)

    // Projectile aim: instead of an AoE ring, draw a line-arrow from the caster to the aim point (range-clamped). The
    // width tapers wide toward the target so it reads as a direction arrow.
    bool projectilePreview;
    LineRenderer line;

    /// <summary>Enter aim mode, placeable within <paramref name="castRange"/> of <paramref name="player"/>. On
    /// left-click confirm, calls <paramref name="onConfirm"/> with the chosen ground point. The reticle is the
    /// cast-circle effect <paramref name="circleId"/> (empty = no visible reticle), tinted and scaled to the
    /// skill's footprint by the caller.</summary>
    public static void Begin(Transform player, float castRange, System.Action<Vector3> onConfirm,
                             string circleId, Color circleTint, float circleRadius, bool projectilePreview = false)
    {
        if (inst == null)
        {
            var go = new GameObject("GroundTargeter") { hideFlags = HideFlags.DontSaveInEditor };
            inst = go.AddComponent<GroundTargeter>();
        }
        inst.player = player;
        inst.castRange = Mathf.Max(0f, castRange);
        inst.onConfirm = onConfirm;
        inst.hasPoint = false;
        inst.circleId = circleId;
        inst.circleTint = circleTint;
        inst.circleRadius = circleRadius <= 0f ? 1.5f : circleRadius;
        inst.projectilePreview = projectilePreview;
        active = true;
    }

    public static void Cancel()
    {
        active = false;
        if (inst == null) return;
        inst.onConfirm = null;
        inst.StopCircle();
    }

    void StopCircle()
    {
        // Immediate hard-clear (no fade tail): a targeting reticle must vanish the instant aim ends — on confirm AND
        // on any FAILED release (no MP / cooldown / silence). A soft fade here is masked by the wind-up circle on a
        // successful cast, but on a failed cast it lingers with nothing replacing it (reads as "stuck").
        if (circle != null) { circle.Stop(true); circle = null; }
        if (line != null) { if (line.material != null) Destroy(line.material); Destroy(line.gameObject); line = null; }
    }

    // Draw / move the aim arrow from the caster to the current (range-clamped) aim point. Lazily built on first use.
    void UpdateArrow(Vector3 me)
    {
        if (line == null)
        {
            var go = new GameObject("AimArrow") { hideFlags = HideFlags.DontSaveInEditor };
            go.transform.SetParent(transform, false);
            line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.numCapVertices = 2;
            line.positionCount = 2;
            line.startWidth = 0.06f; line.endWidth = 0.30f;   // taper wide toward the target → reads as an arrow
            line.sortingOrder = 1000;
            Color c = circleTint; c.a = Mathf.Max(0.6f, c.a);
            line.startColor = c; line.endColor = c;
        }
        line.enabled = true;
        line.SetPosition(0, me + CircleLift);
        line.SetPosition(1, point + CircleLift);
    }

    void Update()
    {
        if (!Active) return;
        if (cam == null) cam = Camera.main;

        if (EscDown()) { Cancel(); return; }

        if (Player3DController.GroundUnderCursor(cam, out Vector3 p))
        {
            // Clamp the reticle to castRange (planar) from the player.
            Vector3 me = player != null ? player.position : Vector3.zero;
            Vector3 off = p - me; off.y = 0f;
            if (castRange > 0f && off.magnitude > castRange)
                p = me + off.normalized * castRange + Vector3.up * (p.y - me.y);
            point = p; hasPoint = true;
            // Park this object at the cursor point so the attached circle tracks it; spawn the circle on first use.
            transform.position = p;
            if (projectilePreview)
                UpdateArrow(me);
            else if (circle == null && !string.IsNullOrEmpty(circleId))
                circle = Vfx.AttachGroundCircle(circleId, transform, CircleLift, circleRadius, circleTint);
        }

        if (LeftDown() && !PointerOverUI() && hasPoint)
        {
            var cb = onConfirm; var pt = point;
            Cancel();          // leave aim mode BEFORE firing so the skill's cooldown sweep starts cleanly
            cb?.Invoke(pt);
        }
    }

    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    static bool LeftDown()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) return Mouse.current.leftButton.wasPressedThisFrame;
#endif
        return Input.GetMouseButtonDown(0);
    }

    static bool EscDown() => Hotkeys.CancelDown();
}
