using System;
using UnityEngine;

/// <summary>
/// Dota-style order controller — the brain that turns mouse/keyboard input into player
/// intent and drives the movement (<see cref="PlayerController2D"/>) and melee
/// (<see cref="PlayerAttacker"/>) services.
///
/// Controls:
///   • Right-click ground   → MOVE there.
///   • Right-click a monster → ATTACK it (walk into range, then auto-swing).
///   • Right-click an ally   → FOLLOW it (never attacks).
///   • A, then left-click    → ATTACK-CLICK: a monster = attack it; ground = ATTACK-MOVE
///                             (advance to the point, auto-engaging any monster that comes
///                             within <see cref="acquireRange"/>).
///   • S                     → STOP: cancel movement, the current target / attack-move, AND
///                             an in-flight swing's damage. Also fires <see cref="onStop"/>
///                             so a future skill caster can abort its cast here.
///
/// Movement is routed: the sibling <see cref="PlayerController2D"/> delegates to a
/// <see cref="PathAgent"/>, so moves/chases automatically path AROUND walls and crates.
/// </summary>
[RequireComponent(typeof(PlayerController2D))]
[RequireComponent(typeof(PlayerAttacker))]
public class PlayerCommander : MonoBehaviour
{
    [Tooltip("During attack-move, a monster this close (world units) is auto-acquired.")]
    public float acquireRange = 5f;
    [Tooltip("How close the player tries to stay when following an ally.")]
    public float followDistance = 1.4f;

    [Header("Click-marker colors")]
    public Color moveMarkerColor = new Color(0.40f, 1f, 0.55f);   // green = plain move
    public Color attackMarkerColor = new Color(1f, 0.45f, 0.30f); // red = attack-move

    /// <summary>Raised when the player issues Stop (S). A future skill caster can subscribe
    /// to abort an in-progress cast / animation when this fires.</summary>
    public event Action onStop;

    /// <summary>True while waiting for the attack-click after pressing A (the cursor shows
    /// the attack-move crosshair). Read by <see cref="GameCursor"/>.</summary>
    public bool AttackArmed { get; private set; }

    enum Order { Idle, Move, AttackMove, Attack, Follow }
    Order order = Order.Idle;

    Vector2 movePoint;        // destination for Move / AttackMove
    Health attackTarget;      // current Attack target
    Transform followTarget;   // current Follow target
    bool resumeAttackMove;    // after a kill mid attack-move, return to movePoint

    PlayerController2D move;
    PlayerAttacker attacker;
    PathAgent agent;
    TargetIndicator indicator;
    Camera cam;

    void Awake()
    {
        move = GetComponent<PlayerController2D>();
        attacker = GetComponent<PlayerAttacker>();
        cam = Camera.main;
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;
        ReadInput();
        Tick();
    }

    // ---- input ---------------------------------------------------------------

    void ReadInput()
    {
        // S — stop everything.
        if (Input.GetKeyDown(KeyCode.S)) { Stop(); return; }

        // A — arm an attack-click (the next left-click becomes an attack order).
        if (Input.GetKeyDown(KeyCode.A)) AttackArmed = true;

        bool overUI = PlayerAttacker.PointerOverUI();

        // Left-click resolves an armed attack-click.
        if (AttackArmed && Input.GetMouseButtonDown(0) && !overUI)
        {
            AttackArmed = false;
            var enemy = PlayerAttacker.MonsterUnderCursor(cam);
            if (enemy != null) BeginAttack(enemy, resume: false);
            else BeginAttackMove(MouseWorld());
            return;
        }

        // Esc disarms an attack-click without issuing anything.
        if (AttackArmed && Input.GetKeyDown(KeyCode.Escape)) AttackArmed = false;

        // Right-click — move / attack / follow (also disarms an attack-click).
        if (Input.GetMouseButtonDown(1) && !overUI)
        {
            AttackArmed = false;

            var enemy = PlayerAttacker.MonsterUnderCursor(cam);
            if (enemy != null) { BeginAttack(enemy, resume: false); return; }

            var ally = PlayerAttacker.FriendlyUnitUnderCursor(cam);
            if (ally != null && ally.transform != transform) { BeginFollow(ally.transform); return; }

            BeginMove(MouseWorld());
        }
    }

    // ---- per-state behavior --------------------------------------------------

    void Tick()
    {
        switch (order)
        {
            case Order.Idle:
                break;

            case Order.Move:
                if (move.Arrived) GoIdle();
                break;

            case Order.AttackMove:
                var near = NearestMonsterWithin(acquireRange);
                if (near != null) { BeginAttack(near, resume: true); break; }
                if (move.Arrived) GoIdle();
                break;

            case Order.Attack:
                if (attackTarget == null || attackTarget.IsDead)
                {
                    if (resumeAttackMove) BeginAttackMove(movePoint);   // keep clearing the area
                    else GoIdle();
                    break;
                }
                if (attacker.InRange(attackTarget))
                {
                    if (!move.Arrived) move.Halt();
                    attacker.TryAttack(attackTarget);
                }
                else move.MoveToward(attackTarget.transform.position);
                break;

            case Order.Follow:
                if (followTarget == null) { GoIdle(); break; }
                float d = Vector2.Distance(transform.position, followTarget.position);
                if (d > followDistance) move.MoveToward(followTarget.position);
                else if (!move.Arrived) move.Halt();
                break;
        }
    }

    // ---- orders --------------------------------------------------------------

    void BeginMove(Vector2 point)
    {
        ClearCombat();
        order = Order.Move;
        movePoint = point;
        move.MoveTo(point);
        ClickMarker.Spawn(point, moveMarkerColor);
    }

    void BeginAttackMove(Vector2 point)
    {
        ClearCombat();
        order = Order.AttackMove;
        movePoint = point;
        move.MoveTo(point);
        ClickMarker.Spawn(point, attackMarkerColor);
    }

    void BeginAttack(Health enemy, bool resume)
    {
        attacker.Cancel();          // drop any swing aimed at a previous target
        followTarget = null;
        order = Order.Attack;
        attackTarget = enemy;
        resumeAttackMove = resume;  // keep movePoint when this came from an attack-move
        ShowIndicator(enemy.transform);
        SetAvoid(enemy.transform);  // don't dodge the unit we're trying to hit
    }

    void BeginFollow(Transform ally)
    {
        ClearCombat();
        order = Order.Follow;
        followTarget = ally;
        SetAvoid(ally);             // flow toward the ally without dodging it
    }

    /// <summary>Stop and cancel everything (S). Damage from an in-flight swing is dropped.</summary>
    public void Stop()
    {
        AttackArmed = false;
        order = Order.Idle;
        move.Halt();
        ClearCombat();
        onStop?.Invoke();           // future: abort skill casts / channels here
    }

    void GoIdle()
    {
        order = Order.Idle;
        move.Halt();
        ClearCombat();
    }

    void ClearCombat()
    {
        attacker.Cancel();
        attackTarget = null;
        followTarget = null;
        resumeAttackMove = false;
        ShowIndicator(null);
        SetAvoid(null);             // clear the attack-target exemption (player avoidance is off anyway)
    }

    // Tell the mover which unit (if any) NOT to steer around — its current target.
    void SetAvoid(Transform target)
    {
        if (agent == null) agent = GetComponent<PathAgent>();
        if (agent != null) agent.avoidIgnore = target;
    }

    // ---- helpers -------------------------------------------------------------

    void ShowIndicator(Transform t)
    {
        if (t != null && indicator == null) indicator = TargetIndicator.Create();
        if (indicator != null) indicator.Follow(t);
    }

    Vector2 MouseWorld()
    {
        if (cam == null) return transform.position;
        Vector3 w = cam.ScreenToWorldPoint(Input.mousePosition);
        return new Vector2(w.x, w.y);
    }

    Health NearestMonsterWithin(float radius)
    {
        Health best = null;
        float bestSqr = radius * radius;
        foreach (var c in Physics2D.OverlapCircleAll(transform.position, radius))
        {
            var h = c.GetComponentInParent<Health>();
            if (h == null || h.IsDead || h.GetComponent<MonsterAI>() == null) continue;
            float sqr = ((Vector2)h.transform.position - (Vector2)transform.position).sqrMagnitude;
            if (sqr <= bestSqr) { bestSqr = sqr; best = h; }
        }
        return best;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, acquireRange);
    }
}
