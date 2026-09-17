using UnityEngine;

/// <summary>
/// The ONE simulate-near / freeze-far switch for any unit — townsfolk, mobs, and anything else that costs real
/// per-frame work. Beyond <see cref="simulateRadius"/> from the player it disables the registered components
/// (mover, animator, visual driver) and reports <see cref="Simulating"/> = false so the unit's brain can early-out.
///
/// <para>WHY IT'S SHARED: this rule lived only on <see cref="TownNPC"/>, so mobs — the units there are most of —
/// never got it. A billboard mob could afford that (a quad with no Animator); a live-3D mob cannot. Centralizing it
/// means the rule is written once and a new unit type can't silently miss it. The RADIUS stays per-unit because the
/// right distance differs by role (a townsperson may freeze early; a mob must keep chasing from just off-screen).</para>
///
/// <para>PERFORMANCE: the distance test runs on a staggered ~0.4s cadence with a random per-unit phase — never a
/// per-frame scan for every unit — and compares SQUARED distances (no sqrt). Nothing here allocates after
/// <see cref="Freeze"/> is called at build time.</para>
/// </summary>
[DisallowMultipleComponent]
public class SimulationLod : MonoBehaviour
{
    [Tooltip("Beyond this distance from the player the registered components freeze. Must be comfortably WIDER " +
             "than the camera's view so a unit chasing you from just off-screen keeps moving.")]
    public float simulateRadius = 20f;

    /// <summary>False while this unit is frozen (too far from the player). A brain reads this and early-outs.</summary>
    public bool Simulating { get; private set; } = true;

    /// <summary>Raised when the frozen state flips. Lets a unit re-seed state that goes stale while frozen (e.g. a
    /// footstep gait whose last position is now far behind). Subscribed once at build — never per frame.</summary>
    public event System.Action<bool> OnSimulatingChanged;

    Behaviour[] frozen;
    float nextCheck;

    /// <summary>Register the components to disable while this unit is far away. Call ONCE when the unit is built —
    /// nulls are allowed (a rig may have no animator), and are skipped.</summary>
    public void Freeze(params Behaviour[] components) => frozen = components;

    void Awake() => nextCheck = Time.time + Random.value * 0.4f;   // desync the herd: no shared check frame

    // A POOLED unit comes back somewhere new, and it resumes with whatever frozen state it had when it was banked —
    // so a mob that froze far away and is then recycled right next to the player would stand motionless until its
    // ordinary ~0.4s tick came round. Re-evaluate promptly on every enable instead (still slightly desynced, so a
    // wave of respawns doesn't check on one frame).
    void OnEnable() => nextCheck = Time.time + Random.value * 0.1f;

    void Update()
    {
        if (Time.time < nextCheck) return;
        nextCheck = Time.time + 0.4f + Random.value * 0.2f;
        Evaluate(force: false);
    }

    /// <summary>While true this switch leaves the registered components completely alone, because something else owns
    /// them for now. Set while a unit is DEAD: the death fade deliberately switches its mover and sprite animator off,
    /// and without this a player who walked away and came back mid-fade would thaw the unit and switch them back on —
    /// a corpse that starts animating and steering again. Cleared by <see cref="Resync"/> on revive.</summary>
    public bool Suspended { get; private set; }

    /// <summary>Stop managing this unit's components until <see cref="Resync"/> revives it.</summary>
    public void Suspend() => Suspended = true;

    /// <summary>Hand every part this switch holds to something else for good — SWITCHED ON — and stop deciding. For a
    /// body another machine drives: its animator and its body driver must run whatever the distance, because what they
    /// show is what they are told. <see cref="Suspend"/> alone keeps whatever state the parts were left in, and a revive
    /// that re-evaluated the distance first can have just switched them off.</summary>
    public void Release()
    {
        Suspended = true;
        Simulating = true;
        ApplyFrozen(true);
    }

    // THE near/far decision, in one place so the timed tick and Resync can never answer it differently.
    // force = apply the answer to the components even when it hasn't changed (see Resync).
    void Evaluate(bool force)
    {
        if (Suspended) return;

        // IS ANYBODY NEAR ME, IN MY MAP — asked of the roll of who is actually here, which is the same question
        // this unit's brain already asks to choose who to chase. It used to hunt the scene for the ONE avatar at
        // this keyboard, which is a fact about a screen: a server has no avatar of its own, so every creature it
        // held measured itself against the wrong body — or against the idle one sitting in the first map — and
        // froze. MEASURED on a running server: a field creature reported not-simulating with its mover switched
        // off and its brain skipping every frame, while a player stood two metres away.
        //
        // On a machine with one person at it the roll holds exactly them, so nothing about playing alone changes.
        // Flat distance, ignoring height, for the same reason the leash and aggro radii are flat.
        bool near = PlayerUnits.AnyoneWithin(transform.position, simulateRadius, gameObject.scene.name);
        if (near == Simulating)
        {
            if (force) ApplyFrozen(near);
            return;
        }

        Simulating = near;
        ApplyFrozen(near);
        OnSimulatingChanged?.Invoke(near);
    }

    void ApplyFrozen(bool on)
    {
        if (frozen == null) return;
        for (int i = 0; i < frozen.Length; i++)
            if (frozen[i] != null) frozen[i].enabled = on;
    }

    /// <summary>Re-decide near/far RIGHT NOW and force the answer onto the registered components.
    ///
    /// <para>Call it after anything enables a unit's parts wholesale — the mob revive path does exactly that, turning
    /// the mover and animator back on regardless of distance. Two failures without it: a unit revived FAR away is left
    /// running while this switch still believes it is frozen, and since the switch only acts when its answer CHANGES,
    /// "still far" early-outs and it never gets put back to sleep; a unit revived NEAR would meanwhile sit frozen until
    /// the next tick. Deciding immediately fixes both directions.</para>
    ///
    /// <para>Also lifts <see cref="Suspended"/>: this is the revive beat, so the unit's parts are ours to manage again.</para></summary>
    public void Resync()
    {
        Suspended = false;
        Evaluate(force: true);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, simulateRadius);
    }
#endif
}
