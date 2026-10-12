using UnityEngine;

/// <summary>
/// AUTO-COMBAT'S LEGS — the hunt this machine walks while the server says auto-combat is on (<see cref="AutoCombatClient"/>).
///
/// <para>Steering is this machine's because walking is (the movement exception in CLAUDE.md): it chooses which creature
/// to go for by the hunt mode, walks there, swings, and presses the four auto-skill slots when each is ready. Every swing
/// and every press is an ordinary request the server judges like any other. It never turns itself on; anything that
/// takes the reins — a click, WASD, a skill aimed by hand, a trip — ends it, and the server is told.</para>
///
/// <para>The three hunts (<see cref="HuntMode"/>): <b>Standing</b> fights only what is within reach and never takes a
/// step; <b>Radius</b> starts a fight only within <see cref="AutoCombatMath.StartWithin"/> of the spot it was switched
/// on at, drops one that leaves <see cref="AutoCombatMath.KeepWithin"/>, and walks back to the spot when idle;
/// <b>Free Roam</b> goes for the nearest creature on the map.</para>
/// </summary>
public partial class Player3DController
{
    // Where Radius hunts around: where auto-combat was switched on, or where a Fly Wing last set them down.
    Vector3 autoAnchor;
    // Whether this run of the hunt has its spot yet — cleared whenever auto-combat is off, so each new "on" starts fresh
    // while a hunt that a dash briefly interrupted carries on from the same spot.
    bool autoHasAnchor;
    int autoLandingsSeen;
    float nextAutoSkillLook;
    float nextHomeRepath;

    // Every frame: the body follows the server's word. Off → the hunt stops. On → the hunt runs, and comes back by itself
    // after anything that briefly took the body without the player asking (a skill's dash, a passive's lunge); only the
    // player's own orders end it (EndHuntForAnOrder).
    void SyncAutoCombat()
    {
        AutoCombatClient.GiveToCharacterIfOwed();

        if (!AutoCombatClient.On)
        {
            if (order == Order.AutoPilot) Stop();
            autoHasAnchor = false;
            autoLandingsSeen = AutoCombatClient.Landings;
            return;
        }

        if (!autoHasAnchor) { autoAnchor = transform.position; autoHasAnchor = true; }
        // A FLY WING: the hunt goes on from where it set them down.
        if (AutoCombatClient.Landings != autoLandingsSeen)
        {
            autoLandingsSeen = AutoCombatClient.Landings;
            autoAnchor = transform.position;
        }
        if (order != Order.AutoPilot && !dashing) EnterAutoCombat();
    }

    /// <summary>The player gave an order of their own — a click, WASD, a skill walked into range by hand, a halt. A hunt
    /// running is over, here and on the server. Every order-starting method calls this first; nothing else does.</summary>
    void EndHuntForAnOrder()
    {
        if (order == Order.AutoPilot) AutoCombatClient.StopHere();
    }

    void EnterAutoCombat()
    {
        ClearCombat();
        AttackArmed = false;
        routing = false; path.Clear();
        order = Order.AutoPilot;
        nextAutoSkillLook = 0f;
    }

    // One frame of the hunt: the order's step (see Tick).
    Vector3 AutoStep()
    {
        var mode = AutoCombatClient.Settings.Hunt;

        // A taunt outranks the hunt: while the lock holds, the taunter IS the target, wherever it stands — the same rule
        // a taunted creature obeys (MonsterAI3D.Taunted).
        bool taunted = Taunted;
        if (taunted && attackTarget != tauntSource)
        {
            attackTarget = tauntSource;
            if (targetMarker != null) targetMarker.Follow(tauntSource.transform);
            routing = false; path.Clear();
        }

        // A FIGHT THE MODE NO LONGER ALLOWS is let go — a creature that walked off, or a chase that went too far.
        if (!taunted && attackTarget != null && !attackTarget.IsDead && !AutoMayKeep(attackTarget, mode)) LetGoOfTarget();

        if (attackTarget == null || attackTarget.IsDead)
        {
            attackTarget = AutoAcquire(mode);
            if (targetMarker != null) targetMarker.Follow(attackTarget != null ? attackTarget.transform : null);
            routing = false; path.Clear();
            if (attackTarget == null) return AutoIdleStep(mode);
        }

        PressAutoSkills();
        if (mode == HuntMode.Standing && !taunted) return StandAndSwing();
        return PursueAttackTarget();
    }

    void LetGoOfTarget()
    {
        if (attacker != null) attacker.Cancel();
        attackTarget = null;
        routing = false; path.Clear();
        if (targetMarker != null) targetMarker.Follow(null);
    }

    bool AutoMayKeep(Health target, HuntMode mode)
    {
        switch (mode)
        {
            case HuntMode.Standing: return attacker != null && attacker.EngagedWith(target);
            case HuntMode.Radius:   return AutoCombatMath.MayKeepFightingAt(autoAnchor, transform.position, target.transform.position);
            default:                return true;
        }
    }

    // THE NEXT CREATURE TO FIGHT, by the mode's rule, nearest to where the player stands. The creature registry, never
    // a physics query (see NearestMonsterWithin for why).
    Health AutoAcquire(HuntMode mode)
    {
        Health best = null;
        float bestSqr = float.MaxValue;
        Vector3 me = transform.position;
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null) continue;
            var h = ai.Health;
            if (h == null || h.IsDead || !Sides.HostileToPeople(h)) continue;
            Vector3 at = ai.transform.position;
            if (mode == HuntMode.Standing && (attacker == null || !attacker.InRange(h))) continue;
            if (mode == HuntMode.Radius && !AutoCombatMath.MayStartFightAt(autoAnchor, at)) continue;
            float sqr = AutoCombatMath.FlatSqr(me, at);
            if (sqr < bestSqr) { bestSqr = sqr; best = h; }
        }
        return best;
    }

    // Nothing to fight: Radius walks back to its spot; the others wait where they are.
    Vector3 AutoIdleStep(HuntMode mode)
    {
        if (mode != HuntMode.Radius || !AutoCombatMath.ShouldWalkBack(autoAnchor, transform.position))
        {
            routing = false; path.Clear();
            return Vector3.zero;
        }
        if (!routing || Time.time >= nextHomeRepath) { StartRoute(autoAnchor); nextHomeRepath = Time.time + 1f; }
        return routing ? FollowRoute() : Vector3.zero;
    }

    // Standing's swing: the weapon out, the swing announced and thrown when in reach — and never a step toward anything.
    Vector3 StandAndSwing()
    {
        if (!WeaponDrawClient.Ready) { StopSprinting(); WeaponDrawClient.AskToDraw(); }
        if (attacker != null) attacker.AnnounceAttackIntent(attackTarget);
        routing = false; path.Clear();
        if (attacker != null && attacker.EngagedWith(attackTarget))
        {
            attacker.TryAttack(attackTarget);
            FaceTarget();
        }
        return Vector3.zero;
    }

    // THE AUTO-SKILL SLOTS, looked at a few times a second: the first one ready is pressed at the current target, one
    // press per look. Ready is the same test the bar draws by (HotbarReady); the server still says yes or no.
    void PressAutoSkills()
    {
        if (Time.time < nextAutoSkillLook || attackTarget == null) return;
        nextAutoSkillLook = Time.time + AutoCombatMath.SecondsBetweenSkillLooks;
        var slots = AutoCombatClient.Settings.slots;
        if (slots == null) return;
        for (int i = 0; i < slots.Length; i++)
        {
            if (string.IsNullOrEmpty(slots[i])) continue;
            var runner = PlayerSkills.ById(slots[i]) as SkillRunner;
            if (runner == null || !HotbarReady.Now(runner)) continue;
            if (runner.ActivateAt(attackTarget.transform)) return;
        }
    }
}
