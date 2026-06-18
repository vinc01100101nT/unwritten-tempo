using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The 2.5D player with your Dota-style controls (ported from PlayerCommander). Left-click drives
/// the character; right-click is the camera orbit (<see cref="CameraFollow3D"/>):
///   • Left-click ground   → move there (A* around props).
///   • Left-click a mob    → attack it (close in, then auto-swing).
///   • A, then left-click  → ATTACK-MOVE to the point, auto-engaging any mob within
///                           <see cref="acquireRange"/>; left-click a mob = attack it.
///   • S                   → STOP: cancel move / attack-move and any in-flight swing.
/// Movement routes via <see cref="Pathfinder3D"/> + a CharacterController; 4-dir billboard
/// animation from motion. Click-to-move only (no WASD). Exposes <see cref="AttackArmed"/> and a
/// static <see cref="MonsterUnderCursor"/> for <see cref="GameCursor3D"/>.
/// </summary>
[RequireComponent(typeof(SpriteRenderer), typeof(CharacterController))]
public class Player3DController : MonoBehaviour
{
    [Header("Walk frames (column = facing), filled by Build 2.5D Spike")]
    public Sprite[] down, up, left, right;

    [Header("Death pose (SeparateAnim/Dead.png) — shown while the player is dead")]
    [Tooltip("Single defeated-pose texture; sliced in Awake (like attackSheet) so it serializes as an asset ref.")]
    public Texture2D deadSheet;
    Sprite deadSprite;

    [Header("Attack pose strip (SeparateAnim/Attack.png) — sliced at runtime [down,up,left,right]")]
    [Tooltip("The 4-direction attack-pose texture; sliced in Awake so the art's (often broken) auto-slices don't matter.")]
    public Texture2D attackSheet;
    public int attackCell = 16;
    [Tooltip("Seconds to hold the attack pose after a swing starts.")]
    public float attackPoseHold = 0.22f;
    Sprite[] attackPoses;
    float attackUntil;

    public float moveSpeed = 4f;
    public float framesPerSecond = 8f;
    public float arriveDistance = 0.12f;
    [Tooltip("Clearance kept from props when routing.")]
    public float agentRadius = 0.35f;
    [Tooltip("Constant downward pull to keep the CharacterController grounded.")]
    public float gravity = 12f;
    [Tooltip("During attack-move, auto-engage a mob within this range.")]
    public float acquireRange = 5f;

    [Header("Target magnetism (soft-snap)")]
    [Tooltip("A mob whose on-screen sprite is within this many pixels of the cursor counts as hovered/" +
             "clickable, so you don't have to pixel-aim a small billboard. 0 = off (exact aim only).")]
    public float snapRadiusPixels = 72f;
    static float snapRadius = 72f;   // static mirror so the static MonsterUnderCursor can read it

    [Header("Idle breathing (procedural)")]
    public bool breathe = true;
    public float breatheAmount = 0.03f;
    public float breatheSpeed = 2.5f;

    [Header("Footsteps")]
    public SfxSet footstepSfx;
    [Tooltip("Seconds between footstep sounds while walking.")]
    public float stepInterval = 0.32f;
    float nextStep;

    [Header("Auto-combat (T to toggle, S to stop)")]
    [Tooltip("While auto-combat roams, it engages the nearest mob within this range and walks to it. " +
             "Larger = ranges farther from where you stand to find fights.")]
    public float roamRange = 14f;

    /// <summary>True after pressing A, until the next click (the cursor shows the crosshair).</summary>
    public bool AttackArmed { get; private set; }
    /// <summary>True while roam auto-combat is running (drives the on-screen indicator).</summary>
    public bool AutoActive => order == Order.AutoCombat;

    enum Order { Idle, Move, AttackMove, Attack, AutoCombat }
    Order order = Order.Idle;
    Vector3 movePoint;
    Health attackTarget;
    bool resumeAttackMove;
    AutoCombatIndicator autoIndicator;

    SpriteRenderer sr;
    CharacterController controller;
    Player3DAttacker attacker;
    TargetIndicator targetMarker;
    Camera cam;
    Vector2 facing = Vector2.down;
    float timer; int frame;
    Vector3 baseScale;
    float breathePhase;
    readonly List<Vector2> path = new List<Vector2>();
    int wp; bool routing;
    float nextChaseRepath;
    Vector3 worldFacing = Vector3.back;          // last move/aim world dir; facing derived camera-relative

    void Awake()
    {
        sr = GetComponent<SpriteRenderer>();
        controller = GetComponent<CharacterController>();
        attacker = GetComponent<Player3DAttacker>();
        snapRadius = snapRadiusPixels;   // publish to the static cursor pick
        baseScale = transform.localScale;
        breathePhase = Random.value * Mathf.PI * 2f;
        BuildAttackPoses();
        BuildDeadPose();
        targetMarker = TargetIndicator.Create();
        targetMarker.gameObject.AddComponent<Billboard>();   // face the camera so the arrow isn't edge-on when orbiting
        autoIndicator = AutoCombatIndicator.Create();        // "AUTO-COMBAT" screen tag, hidden until toggled
    }

    // Slice the 4 directional attack poses straight from the texture (16px columns), ignoring the art's
    // auto-slices which are often broken on Ninja Adventure SeparateAnim sheets. Mirrors CharacterAnimator2D.
    void BuildAttackPoses()
    {
        if (attackSheet == null || attackCell <= 0) return;
        // The 4 poses are a 1-row strip [down,up,left,right] — share them via the slice cache.
        attackPoses = SpriteSheetCache.Strip(attackSheet, attackCell, 4);   // pivot centre, ppu = cell
    }

    // Slice the single defeated-pose frame from the Dead.png texture (top-left cell), like the
    // attack poses — so the field can serialize as a plain Texture2D asset reference.
    void BuildDeadPose()
    {
        if (deadSheet == null) return;
        int size = Mathf.Min(attackCell, Mathf.Min(deadSheet.width, deadSheet.height));
        deadSprite = Sprite.Create(deadSheet,
            new Rect(0, deadSheet.height - size, size, size),
            new Vector2(0.5f, 0.5f), size);
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;

        // Dead: no input, no orders — show the defeated pose and keep the corpse pinned to the
        // ground (gravity) until the death screen respawns us.
        if (Character.Instance != null && Character.Instance.IsDead)
        {
            if (order != Order.Idle) Stop();
            if (autoIndicator != null) autoIndicator.Show(false);   // dying cancels auto-combat
            if (deadSprite != null) sr.sprite = deadSprite;
            attackUntil = 0f;   // drop any held attack pose
            transform.localScale = baseScale;
            controller.Move(Vector3.down * gravity * Time.deltaTime);
            return;
        }

        ReadInput();
        Vector3 move = Tick();

        bool moving = move.sqrMagnitude > 0.001f;
        if (moving) worldFacing = move;
        facing = Facing3D.FromWorldDir(worldFacing);   // camera-relative — frames track the camera as it orbits
        controller.Move((move * moveSpeed + Vector3.down * gravity) * Time.deltaTime);

        // Hold the attack pose while a swing is in flight (refreshed each swinging frame, with a tail).
        // The hold scales with attack speed (same MotionScale as the swing) so the pose snaps back
        // faster at high ASPD instead of bleeding into the next swing.
        if (attacker != null && attacker.Swinging) attackUntil = Time.time + attackPoseHold * Player3DAttacker.MotionScale;
        Animate(moving);
        ApplyIdleScale(moving);
        Footsteps(moving);
        if (autoIndicator != null) autoIndicator.Show(AutoActive);   // show the "AUTO" tag only while roaming
    }

    // Tick footstep sounds while walking. nextStep is a persistent cooldown — we DON'T reset it when
    // momentarily stopped, so a stand/chase stutter (e.g. pacing a kiting mob at range-edge) can't
    // machine-gun the step SFX every other frame; steps still fire at most once per stepInterval.
    void Footsteps(bool moving)
    {
        if (moving && Time.time >= nextStep) { Sfx.Play(footstepSfx); nextStep = Time.time + stepInterval; }
    }

    // Procedural idle breathing — a soft squash-stretch while standing still; base scale otherwise
    // (moving or mid-swing). Matches the mob breathing in BillboardAnimator3D.
    void ApplyIdleScale(bool moving)
    {
        bool attacking = Time.time < attackUntil;
        if (breathe && !moving && !attacking)
        {
            float e = Mathf.Sin(Time.time * breatheSpeed + breathePhase);
            transform.localScale = new Vector3(
                baseScale.x * (1f + breatheAmount * e),
                baseScale.y * (1f - breatheAmount * e),
                baseScale.z);
        }
        else transform.localScale = baseScale;
    }

    // ---- input: Dota controls -------------------------------------------------
    void ReadInput()
    {
        if (cam == null) return;
        if (DevSettings.Blocking) return;   // cursor is over the dev-settings panel — don't leak clicks/keys into the world

        if (KeyS()) { Stop(); return; }
        if (KeyT()) { ToggleAuto(); return; }   // T toggles roam auto-combat; S stops it
        if (KeyA()) AttackArmed = true;
        if (KeyEsc()) AttackArmed = false;

        // Left-click drives the character (right-click is the camera orbit). Attacking now REQUIRES A:
        //   • plain left-click            → MOVE (even onto a mob — it no longer auto-attacks),
        //   • A-armed + click on a mob    → ATTACK it,
        //   • A-armed + click on ground   → ATTACK-MOVE there.
        // Ignore clicks that land on UI (e.g. the HUD) so they don't also order a move.
        if (LeftDown() && !PointerOverUI())
        {
            bool armed = AttackArmed; AttackArmed = false;
            if (armed)
            {
                var mob = MonsterUnderCursor(cam);
                if (mob != null) BeginAttack(mob, false);
                else if (GroundPoint(cam, out var gp)) BeginAttackMove(gp);
            }
            else if (GroundPoint(cam, out var gp)) BeginMove(gp);
        }
    }

    // ---- per-order behavior (returns the horizontal step direction) -----------
    Vector3 Tick()
    {
        switch (order)
        {
            case Order.Move:
                if (!routing) { order = Order.Idle; return Vector3.zero; }
                return FollowRoute();

            case Order.AttackMove:
                var near = NearestMonsterWithin(acquireRange);
                if (near != null) { BeginAttack(near, true); return AttackStep(); }
                if (!routing) { order = Order.Idle; return Vector3.zero; }
                return FollowRoute();

            case Order.Attack:
                return AttackStep();

            case Order.AutoCombat:
                return AutoStep();

            default:
                return Vector3.zero;
        }
    }

    // Roam auto-combat: re-acquire the nearest mob within roamRange forever (never goes Idle, never
    // leaves the AutoCombat order), walking to it and swinging in range. Mirrors AttackStep but loops
    // back to acquisition instead of stopping when a target dies. Any manual order (move/attack) or S
    // exits by switching the order out from under it.
    Vector3 AutoStep()
    {
        if (attackTarget == null || attackTarget.IsDead)
        {
            attackTarget = NearestMonsterWithin(roamRange);
            if (targetMarker != null) targetMarker.Follow(attackTarget != null ? attackTarget.transform : null);
            routing = false; path.Clear();
            if (attackTarget == null) return Vector3.zero;   // nothing in range — stand and wait for a wanderer
        }
        if (attacker != null && attacker.EngagedWith(attackTarget))
        {
            routing = false; path.Clear();
            attacker.TryAttack(attackTarget);    // only actually swings when strictly InRange + off cooldown
            FaceTarget();
            return Vector3.zero;                 // in range: stand and swing
        }
        if (!routing || Time.time >= nextChaseRepath) { StartRoute(attackTarget.transform.position); nextChaseRepath = Time.time + 0.3f; }
        return routing ? FollowRoute() : Vector3.zero;
    }

    // T: flip roam auto-combat on/off. Pressing it again (or S, or any manual order) turns it off.
    void ToggleAuto()
    {
        if (order == Order.AutoCombat) { Stop(); return; }
        ClearCombat();              // drop any in-flight swing / stale target / marker
        AttackArmed = false;
        routing = false; path.Clear();
        order = Order.AutoCombat;   // AutoStep acquires a target next tick
    }

    Vector3 AttackStep()
    {
        if (attackTarget == null || attackTarget.IsDead)
        {
            attackTarget = null; routing = false; path.Clear();
            if (targetMarker != null) targetMarker.Follow(null);
            if (resumeAttackMove) { BeginAttackMove(movePoint); return Vector3.zero; }   // keep clearing the area
            order = Order.Idle;
            return Vector3.zero;
        }
        if (attacker != null && attacker.EngagedWith(attackTarget))
        {
            routing = false; path.Clear();
            attacker.TryAttack(attackTarget);    // only actually swings when strictly InRange + off cooldown
            FaceTarget();
            return Vector3.zero;                 // in range: stand and swing
        }
        if (!routing || Time.time >= nextChaseRepath) { StartRoute(attackTarget.transform.position); nextChaseRepath = Time.time + 0.3f; }
        return routing ? FollowRoute() : Vector3.zero;
    }

    // ---- orders ---------------------------------------------------------------
    void BeginMove(Vector3 point)       { ClearCombat(); order = Order.Move;       movePoint = point; StartRoute(point); }
    void BeginAttackMove(Vector3 point) { ClearCombat(); order = Order.AttackMove; movePoint = point; StartRoute(point); }

    void BeginAttack(Health mob, bool resume)
    {
        if (attacker != null) attacker.Cancel();
        order = Order.Attack; attackTarget = mob; resumeAttackMove = resume;
        routing = false; path.Clear();
        if (targetMarker != null) targetMarker.Follow(mob != null ? mob.transform : null);
    }

    /// <summary>Cancel any move/attack order (used on map arrival by PersistentPlayer3D).</summary>
    public void Halt() => Stop();

    /// <summary>Re-publish <see cref="snapRadiusPixels"/> to the static the cursor pick reads, so the
    /// dev-settings panel can tune target magnetism live (the field is only mirrored in Awake otherwise).</summary>
    public void ApplySnapRadius() => snapRadius = snapRadiusPixels;

    void OnDestroy() { if (autoIndicator != null) Destroy(autoIndicator.gameObject); }   // its own canvas — clean it up

    void Stop()
    {
        AttackArmed = false; order = Order.Idle;
        routing = false; path.Clear(); attackTarget = null; resumeAttackMove = false;
        if (attacker != null) attacker.Cancel();   // drop an in-flight swing's damage
        if (targetMarker != null) targetMarker.Follow(null);
    }

    void ClearCombat()
    {
        if (attacker != null) attacker.Cancel();
        attackTarget = null; resumeAttackMove = false;
        if (targetMarker != null) targetMarker.Follow(null);
    }

    void FaceTarget()
    {
        if (attackTarget == null) return;
        Vector3 d = attackTarget.transform.position - transform.position; d.y = 0f;
        if (d.sqrMagnitude > 1e-4f) worldFacing = d;   // facing derived camera-relative in Update
    }

    // Nearest living mob within radius (planar XZ). Scans the MonsterAI3D registry rather than a physics
    // OverlapSphere — the old fixed 32-collider buffer overflowed with props/terrain at large roamRange in
    // a dense scene, returning colliders in arbitrary order and silently dropping mobs (auto-combat then
    // just stood still). The registry is unbounded, prop-free, and cheaper.
    Health NearestMonsterWithin(float radius)
    {
        Health best = null; float bestSqr = radius * radius;
        Vector3 me = transform.position;
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null) continue;
            var h = ai.Health;                                  // cached on the mob — no per-scan GetComponent
            if (h == null || h.IsDead) continue;
            Vector3 d = ai.transform.position - me; d.y = 0f;   // planar distance — ignore height
            float sqr = d.sqrMagnitude;
            if (sqr <= bestSqr) { bestSqr = sqr; best = h; }
        }
        return best;
    }

    // ---- routing (Pathfinder3D) -----------------------------------------------
    void StartRoute(Vector3 goal)
    {
        var from = new Vector2(transform.position.x, transform.position.z);
        var to = new Vector2(goal.x, goal.z);
        // Fills `path` in place (allocation-free); player uses the full default expansion budget.
        if (!Pathfinder3D.FindPath(from, to, agentRadius, path))
            { path.Clear(); path.Add(to); }
        wp = 0; routing = path.Count > 0;
    }

    Vector3 FollowRoute()
    {
        if (wp >= path.Count) { routing = false; return Vector3.zero; }
        Vector3 to = WaypointDelta();
        if (to.magnitude <= arriveDistance)
        {
            wp++;
            if (wp >= path.Count) { routing = false; return Vector3.zero; }
            to = WaypointDelta();
        }
        return to.sqrMagnitude > 1e-4f ? to.normalized : Vector3.zero;
    }

    Vector3 WaypointDelta()
    {
        Vector2 c = path[wp];
        Vector3 d = new Vector3(c.x, transform.position.y, c.y) - transform.position; d.y = 0f;
        return d;
    }

    // ---- animation ------------------------------------------------------------
    void Animate(bool moving)
    {
        // Attacking: hold the directional attack pose (overrides walk/idle), like the old 2D animator.
        if (Time.time < attackUntil)
        {
            var pose = AttackPoseFor(facing);
            if (pose != null) { sr.sprite = pose; frame = 0; timer = 0f; return; }
        }

        var clip = ClipFor(facing);
        if (clip == null || clip.Length == 0) return;
        if (moving)
        {
            timer += Time.deltaTime;
            float ft = 1f / Mathf.Max(framesPerSecond, 0.01f);
            while (timer >= ft) { timer -= ft; frame = (frame + 1) % clip.Length; }
        }
        else { frame = 0; timer = 0f; }
        sr.sprite = clip[Mathf.Min(frame, clip.Length - 1)];
    }

    Sprite[] ClipFor(Vector2 f)
    {
        if (f.y > 0f) return up;
        if (f.y < 0f) return down;
        if (f.x < 0f) return left;
        return right;
    }

    // attackPoses order matches the sliced Attack.png: [0]=down [1]=up [2]=left [3]=right.
    Sprite AttackPoseFor(Vector2 f)
    {
        if (attackPoses == null || attackPoses.Length < 4) return null;
        int i = f.y > 0f ? 1 : f.y < 0f ? 0 : f.x < 0f ? 2 : 3;
        return attackPoses[i];
    }

    // ---- raycast + input helpers (new Input System OR legacy) -----------------
    /// <summary>The living mob under the cursor (camera raycast), or null. Static so GameCursor3D shares it.</summary>
    public static Health MonsterUnderCursor(Camera cam)
    {
        if (cam == null) return null;
        Vector2 mp = MousePos();

        // 1) Exact aim: a direct collider hit always wins (you're pointing right at it).
        var ray = cam.ScreenPointToRay(mp);
        if (Physics.Raycast(ray, out var hit, 500f))
        {
            var h = hit.collider.GetComponentInParent<Health>();
            if (h != null && !h.IsDead && h.GetComponent<MonsterAI3D>() != null) return h;
        }

        // 2) Soft-snap (magnetism): if no exact hit, grab the nearest LIVING mob whose on-screen sprite
        //    is within snapRadius pixels of the cursor — so small billboards don't demand pixel-perfect aim.
        if (snapRadius <= 0f) return null;
        Health best = null; float bestSqr = snapRadius * snapRadius;
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null) continue;
            var h = ai.Health;                                  // cached on the mob — no per-scan GetComponent
            if (h == null || h.IsDead) continue;
            // Aim at mid-body (a bit above the feet) so the snap point matches the sprite, not the ground.
            Vector3 sp = cam.WorldToScreenPoint(ai.transform.position + Vector3.up * 0.4f);
            if (sp.z <= 0f) continue;                               // behind the camera
            float sqr = ((Vector2)sp - mp).sqrMagnitude;
            if (sqr <= bestSqr) { bestSqr = sqr; best = h; }
        }
        return best;
    }

    static bool GroundPoint(Camera cam, out Vector3 point)
    {
        point = default;
        if (cam == null) return false;
        var ray = cam.ScreenPointToRay(MousePos());
        if (Physics.Raycast(ray, out var hit, 500f)) { point = hit.point; return true; }
        if (Mathf.Abs(ray.direction.y) > 1e-4f)         // fallback: flat ground plane at y≈0
        {
            float t = -ray.origin.y / ray.direction.y;
            if (t > 0f) { point = ray.GetPoint(t); return true; }
        }
        return false;
    }

    static Vector2 MousePos()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null) return Mouse.current.position.ReadValue();
#endif
        return Input.mousePosition;
    }

    static bool LeftDown()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;
#endif
        return Input.GetMouseButtonDown(0);
    }

    /// <summary>True when the cursor is over a UI element (HUD), so a click shouldn't also move the player.</summary>
    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    static bool KeyA()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.aKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.A);
    }

    static bool KeyT()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.T);
    }

    static bool KeyS()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.sKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.S);
    }

    static bool KeyEsc()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(KeyCode.Escape);
    }

    void OnDrawGizmosSelected()
    {
        if (routing && path.Count > 0)
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);
            Vector3 prev = transform.position;
            for (int i = wp; i < path.Count; i++)
            {
                Vector3 p = new Vector3(path[i].x, transform.position.y, path[i].y);
                Gizmos.DrawLine(prev, p); Gizmos.DrawWireSphere(p, 0.1f); prev = p;
            }
        }
        Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, acquireRange);
    }
}
