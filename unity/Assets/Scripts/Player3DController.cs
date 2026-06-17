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

    [Header("Idle breathing (procedural)")]
    public bool breathe = true;
    public float breatheAmount = 0.03f;
    public float breatheSpeed = 2.5f;

    [Header("Footsteps")]
    public SfxSet footstepSfx;
    [Tooltip("Seconds between footstep sounds while walking.")]
    public float stepInterval = 0.32f;
    float nextStep;

    /// <summary>True after pressing A, until the next click (the cursor shows the crosshair).</summary>
    public bool AttackArmed { get; private set; }

    enum Order { Idle, Move, AttackMove, Attack }
    Order order = Order.Idle;
    Vector3 movePoint;
    Health attackTarget;
    bool resumeAttackMove;

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
        baseScale = transform.localScale;
        breathePhase = Random.value * Mathf.PI * 2f;
        BuildAttackPoses();
        BuildDeadPose();
        targetMarker = TargetIndicator.Create();
        targetMarker.gameObject.AddComponent<Billboard>();   // face the camera so the arrow isn't edge-on when orbiting
    }

    // Slice the 4 directional attack poses straight from the texture (16px columns), ignoring the art's
    // auto-slices which are often broken on Ninja Adventure SeparateAnim sheets. Mirrors CharacterAnimator2D.
    void BuildAttackPoses()
    {
        if (attackSheet == null || attackCell <= 0) return;
        int cols = Mathf.Max(1, attackSheet.width / attackCell);
        attackPoses = new Sprite[4];
        for (int i = 0; i < 4 && i < cols; i++)
            attackPoses[i] = Sprite.Create(attackSheet,
                new Rect(i * attackCell, 0, attackCell, attackCell),
                new Vector2(0.5f, 0.5f), attackCell);   // pivot centre, ppu = cell — matches the walk frames
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
        if (attacker != null && attacker.Swinging) attackUntil = Time.time + attackPoseHold;
        Animate(moving);
        ApplyIdleScale(moving);
        Footsteps(moving);
    }

    // Tick footstep sounds while walking; reset so the first step plays promptly on the next move.
    void Footsteps(bool moving)
    {
        if (moving)
        {
            if (Time.time >= nextStep) { Sfx.Play(footstepSfx); nextStep = Time.time + stepInterval; }
        }
        else nextStep = 0f;
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

        if (KeyS()) { Stop(); return; }
        if (KeyA()) AttackArmed = true;
        if (KeyEsc()) AttackArmed = false;

        // Left-click drives the character (right-click is the camera orbit). A-armed = attack-move.
        // Ignore clicks that land on UI (e.g. the HUD) so they don't also order a move.
        if (LeftDown() && !PointerOverUI())
        {
            var mob = MonsterUnderCursor(cam);
            bool armed = AttackArmed; AttackArmed = false;
            if (mob != null) BeginAttack(mob, false);
            else if (GroundPoint(cam, out var gp)) { if (armed) BeginAttackMove(gp); else BeginMove(gp); }
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

            default:
                return Vector3.zero;
        }
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
        if (attacker != null && attacker.InRange(attackTarget))
        {
            routing = false; path.Clear();
            attacker.TryAttack(attackTarget);
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

    static readonly Collider[] acquireHits = new Collider[32];   // reused buffer (no per-frame GC)

    Health NearestMonsterWithin(float radius)
    {
        Health best = null; float bestSqr = radius * radius;
        int n = Physics.OverlapSphereNonAlloc(transform.position, radius, acquireHits);
        for (int i = 0; i < n; i++)
        {
            var h = acquireHits[i].GetComponentInParent<Health>();
            if (h == null || h.IsDead || h.GetComponent<MonsterAI3D>() == null) continue;
            float sqr = (h.transform.position - transform.position).sqrMagnitude;
            if (sqr <= bestSqr) { bestSqr = sqr; best = h; }
        }
        return best;
    }

    // ---- routing (Pathfinder3D) -----------------------------------------------
    void StartRoute(Vector3 goal)
    {
        var from = new Vector2(transform.position.x, transform.position.z);
        var to = new Vector2(goal.x, goal.z);
        var route = Pathfinder3D.FindPath(from, to, agentRadius);
        path.Clear();
        if (route != null) path.AddRange(route); else path.Add(to);
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
        var ray = cam.ScreenPointToRay(MousePos());
        if (Physics.Raycast(ray, out var hit, 500f))
        {
            var h = hit.collider.GetComponentInParent<Health>();
            if (h != null && !h.IsDead && h.GetComponent<MonsterAI3D>() != null) return h;
        }
        return null;
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
