using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reusable 3D mover for the 2.5D world — the spatial twin of the 2D <see cref="PathAgent"/>.
/// Routes on the XZ plane via <see cref="Pathfinder3D"/> and walks through a CharacterController
/// (so it slides along props). Mirrors PathAgent's verbs so a brain like <see cref="MonsterAI3D"/>
/// drives it identically:
///   • <see cref="MoveTo"/>     — route to a fixed point, stop on arrival.
///   • <see cref="MoveToward"/> — chase a moving target, re-routing on a cadence.
///   • <see cref="Halt"/>       — stop now.
/// Exposes <see cref="Facing"/> + <see cref="Moving"/> so <see cref="BillboardAnimator3D"/> can
/// animate from the motion. (The player keeps its own routing for now; this is for mobs.)
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PathAgent3D : MonoBehaviour
{
    public float moveSpeed = 3f;
    public float agentRadius = 0.35f;
    public float arriveRadius = 0.1f;
    public float gravity = 12f;
    [Tooltip("Seconds between re-routes while chasing a moving target.")]
    public float repathInterval = 0.35f;
    [Tooltip("Re-route early if the chase target drifts at least this far from the last route goal.")]
    public float repathThreshold = 0.6f;

    public Vector2 Facing { get; private set; } = Vector2.down;   // cardinal (x, z)
    public bool Moving { get; private set; }
    public bool Arrived => !active;

    CharacterController cc;
    readonly List<Vector2> path = new List<Vector2>();
    int wp;
    bool active, stopOnArrive;
    Vector2 lastGoal;
    float nextRepath;

    void Awake() => cc = GetComponent<CharacterController>();

    public void MoveTo(Vector3 worldPoint)
    {
        stopOnArrive = true;
        Repath(new Vector2(worldPoint.x, worldPoint.z));
        active = true;
    }

    public void MoveToward(Vector3 movingTarget)
    {
        stopOnArrive = false;
        Vector2 g = new Vector2(movingTarget.x, movingTarget.z);
        if (!active || (g - lastGoal).sqrMagnitude > repathThreshold * repathThreshold || Time.time >= nextRepath)
            Repath(g);
        active = true;
    }

    public void Halt() { active = false; path.Clear(); wp = 0; Moving = false; }

    void Repath(Vector2 goal)
    {
        lastGoal = goal;
        nextRepath = Time.time + repathInterval;
        var from = new Vector2(transform.position.x, transform.position.z);
        var route = Pathfinder3D.FindPath(from, goal, agentRadius);
        path.Clear();
        if (route != null) path.AddRange(route); else path.Add(goal);   // stuck → straight line
        wp = 0;
    }

    void Update()
    {
        Vector3 dir = Vector3.zero;
        bool moving = false;

        if (active && wp < path.Count)
        {
            Vector3 to = WaypointDelta(wp);
            bool last = wp == path.Count - 1;
            float reach = (last && stopOnArrive) ? arriveRadius : Mathf.Max(arriveRadius, 0.12f);
            if (to.magnitude <= reach)
            {
                wp++;
                if (wp >= path.Count) { if (stopOnArrive) Halt(); }   // chase: idle here until next repath
                else to = WaypointDelta(wp);
            }
            if (active && wp < path.Count && to.sqrMagnitude > 1e-4f) { dir = to.normalized; moving = true; }
        }

        // Move (with a constant downward pull to stay grounded; CharacterController slides on props).
        cc.Move((dir * moveSpeed + Vector3.down * gravity) * Time.deltaTime);

        Moving = moving;
        if (moving)
            Facing = Facing3D.FromWorldDir(dir);
    }

    Vector3 WaypointDelta(int i)
    {
        Vector3 d = new Vector3(path[i].x, transform.position.y, path[i].y) - transform.position;
        d.y = 0f;
        return d;
    }
}
