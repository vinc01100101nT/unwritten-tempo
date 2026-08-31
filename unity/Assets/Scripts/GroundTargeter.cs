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

    // Projectile aim: a FIXED-length ground telegraph pointing from the caster toward the cursor — a DIRECTION hint
    // only, deliberately NOT reaching the landing/travel distance (the shot flies its own travel range). It's a row of
    // solid arrowhead CHEVRONS (same proven mesh primitive as the tip head) that march toward the tip and fade in from
    // the caster, capped by a big solid arrowhead. The chevron mesh is rebuilt each frame to animate the march; the
    // parent is positioned/rotated to face the aim. All built in local space pointing along +Z.
    bool projectilePreview;
    Transform arrow;             // parent; positioned at the caster, rotated to face the aim direction
    Material shaftMat;           // the marching-chevrons material (freed on cleanup)
    Material headMat;            // the solid tip-arrowhead material (freed on cleanup)
    Mesh shaftMesh;              // the marching chevrons; verts rebuilt each frame so they flow toward the tip
    Mesh headMesh;               // the tip arrowhead mesh (freed on cleanup)
    Vector3[] chevVerts;         // reused vertex buffer (ChevCount arrowheads × 3), no per-frame alloc
    Color[] chevCols;            // reused colour buffer (per-vertex alpha = fade by distance)
    float scroll;                // running distance the chevrons have marched toward the tip

    const float ShaftLen = 2.2f;      // FIXED march length (world units) — a direction cue, not the travel distance
    const int ChevCount = 5;          // how many chevrons march along the strip
    const float ChevHalfW = 0.16f;    // half-width of each marching chevron
    const float ChevHalfLen = 0.14f;  // half-length (along travel) of each marching chevron
    const float ScrollSpeed = 1.3f;   // world units/sec the chevrons flow toward the tip
    const float HeadHalfW = 0.34f;    // half-spread of the big tip arrowhead
    const float HeadLen = 0.6f;       // how far the tip arrowhead extends past the strip

    /// <summary>Enter aim mode, placeable within <paramref name="castRange"/> of <paramref name="player"/>. On
    /// left-click confirm, calls <paramref name="onConfirm"/> with the chosen ground point. The reticle is the
    /// cast-circle effect <paramref name="circleId"/> (empty = no visible reticle), tinted and scaled to the
    /// skill's footprint by the caller.</summary>
    public static void Begin(Transform player, float castRange, System.Action<Vector3> onConfirm,
                             string circleId, Color circleTint, float circleRadius, bool projectilePreview = false)
    {
        // Quick cast (Settings ▸ Gameplay): skip the reticle entirely and place the skill under the cursor,
        // clamped to range the same way the reticle clamps it. Every caster inherits this because it sits in
        // the aim step itself. With no ground under the cursor there's nowhere to fire, so the reticle opens.
        if (GameSettings.QuickCastGroundTarget && QuickPoint(player, castRange, out Vector3 quick))
        { active = false; onConfirm?.Invoke(quick); return; }

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

    static bool QuickPoint(Transform player, float castRange, out Vector3 point)
    {
        point = Vector3.zero;
        var cam = Camera.main;
        if (cam == null || !Player3DController.GroundUnderCursor(cam, out Vector3 p)) return false;
        point = ClampToRange(player != null ? player.position : Vector3.zero, p, castRange);
        return true;
    }

    // Keep the aim point inside castRange (planar) of the caster — the one rule for both the reticle and quick cast.
    static Vector3 ClampToRange(Vector3 me, Vector3 p, float castRange)
    {
        Vector3 off = p - me; off.y = 0f;
        if (castRange > 0f && off.sqrMagnitude > castRange * castRange)
            p = me + off.normalized * castRange + Vector3.up * (p.y - me.y);
        return p;
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
        if (arrow != null)
        {
            if (shaftMat != null) { Destroy(shaftMat); shaftMat = null; }
            if (headMat != null) { Destroy(headMat); headMat = null; }
            if (shaftMesh != null) { Destroy(shaftMesh); shaftMesh = null; }
            if (headMesh != null) { Destroy(headMesh); headMesh = null; }
            Destroy(arrow.gameObject);
            arrow = null;
        }
    }

    // Build the telegraph ONCE in local space along +Z (flat on XZ): the marching-chevron mesh (filled each frame) plus
    // a static big arrowhead at the tip. Later frames only move/rotate the parent and rebuild the chevron verts.
    void BuildArrow()
    {
        Color tint = circleTint; tint.a = 1f;

        arrow = new GameObject("AimArrow") { hideFlags = HideFlags.DontSaveInEditor }.transform;
        arrow.SetParent(transform, false);

        // Marching chevrons: ChevCount solid arrowhead triangles (3 verts each). Positions/colours are set every frame
        // in UpdateArrow (they slide toward the tip and fade in); here we just allocate the buffers + fixed triangles.
        chevVerts = new Vector3[ChevCount * 3];
        chevCols = new Color[ChevCount * 3];
        var tris = new int[ChevCount * 3];
        for (int i = 0; i < tris.Length; i++) tris[i] = i;
        shaftMesh = new Mesh { name = "MarchingChevrons" };
        shaftMesh.MarkDynamic();
        shaftMesh.vertices = chevVerts;   // real positions filled in UpdateArrow
        shaftMesh.triangles = tris;
        MakeMeshObject("MarchingChevrons", shaftMesh, out shaftMat);

        // Big solid arrowhead at the tip: a triangle from z = ShaftLen out to z = ShaftLen + HeadLen.
        headMesh = new Mesh { name = "ArrowHead" };
        headMesh.vertices = new[] {
            new Vector3(-HeadHalfW, 0f, ShaftLen - 0.05f), new Vector3(HeadHalfW, 0f, ShaftLen - 0.05f),
            new Vector3(0f, 0f, ShaftLen + HeadLen) };
        headMesh.colors = new[] { tint, tint, tint };
        headMesh.triangles = new[] { 0, 2, 1 };
        headMesh.RecalculateBounds();
        MakeMeshObject("ArrowHead", headMesh, out headMat);
    }

    void MakeMeshObject(string name, Mesh mesh, out Material mat)
    {
        var go = new GameObject(name) { hideFlags = HideFlags.DontSaveInEditor };
        go.transform.SetParent(arrow, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mat = new Material(Shader.Find("Sprites/Default"));   // unlit, alpha-blended, vertex-coloured, double-sided
        mr.sharedMaterial = mat;
        mr.sortingOrder = 1000;
    }

    // Point the (pre-built) telegraph from the caster toward the cursor, then march the chevrons toward the tip. Fixed
    // length — it never reaches the aim point (that's the message: direction only, not where the shot lands).
    void UpdateArrow(Vector3 me)
    {
        if (arrow == null) BuildArrow();

        Vector3 dir = point - me; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = player != null ? player.forward : Vector3.forward;
        dir.Normalize();
        arrow.SetPositionAndRotation(me + CircleLift, Quaternion.LookRotation(dir, Vector3.up));

        // March each chevron toward the tip (+Z), wrapping back to the caster; alpha fades in with distance so they
        // appear to stream out of the caster. Rebuild the tiny mesh each frame (ChevCount×3 verts — trivial).
        if (shaftMesh != null)
        {
            scroll += ScrollSpeed * Time.deltaTime;
            float spacing = ShaftLen / ChevCount;
            Color tint = circleTint; tint.a = 1f;
            for (int i = 0; i < ChevCount; i++)
            {
                float z = Mathf.Repeat(i * spacing + scroll, ShaftLen);   // 0 (caster) .. ShaftLen (tip)
                Color c = tint; c.a = Mathf.Lerp(0.10f, 1f, z / ShaftLen);
                int b = i * 3;
                chevVerts[b]     = new Vector3(-ChevHalfW, 0f, z - ChevHalfLen);
                chevVerts[b + 1] = new Vector3(ChevHalfW, 0f, z - ChevHalfLen);
                chevVerts[b + 2] = new Vector3(0f, 0f, z + ChevHalfLen);     // apex forward → points toward the tip
                chevCols[b] = chevCols[b + 1] = chevCols[b + 2] = c;
            }
            shaftMesh.SetVertices(chevVerts);
            shaftMesh.SetColors(chevCols);
            shaftMesh.RecalculateBounds();
        }
    }

    void Update()
    {
        if (!Active) return;
        if (cam == null) cam = Camera.main;

        if (EscDown()) { Cancel(); Hotkeys.MarkCancelHandled(); return; }

        if (Player3DController.GroundUnderCursor(cam, out Vector3 p))
        {
            Vector3 me = player != null ? player.position : Vector3.zero;
            p = ClampToRange(me, p, castRange);
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

    // The one pointer gate (mouse on the desktop, a finger on a phone). Aiming confirms on RELEASE there, so a
    // drag can slide the reticle before it commits.
    static bool LeftDown() => PointerInput.PrimaryDown;

    static bool EscDown() => Hotkeys.CancelDown();
}
