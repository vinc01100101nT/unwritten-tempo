using UnityEngine;

/// <summary>
/// The player's animation BRAIN, kept separate from the visual so it isn't glued to render tech: given the player's
/// live state it decides WHICH <see cref="StanceId"/> to drive this frame. The live-3D visual
/// (<see cref="Character3DVisual"/>) calls this, so "player is swinging → Attack",
/// "airborne → Jump", "took a hit → Hurt" is authored ONCE and can't drift between the two presentations.
///
/// <para>Pure decision — no side effects, no allocation (returns a value struct). The caller feeds it the
/// momentary windows it can't poll (hurt/pickUp) and the two capability flags the resolved stance depends on
/// (does the visual have a Run / Jump clip), then applies the result to its own <see cref="StanceController"/>.</para>
/// </summary>
public static class PlayerStanceBrain
{
    /// <summary>The brain's per-frame verdict. <see cref="hasAction"/> false = clear the action layer (idle/held stance).</summary>
    public struct Decision
    {
        public bool hasAction;      // false → StanceController.ClearAction()
        public StanceId id;         // the action stance to drive when hasAction
        public float window;        // FitWindow seconds (Attack = swing time, Roll = dash length); for Jump/Fall = clip freeze seconds
        public bool release;        // Jump/Fall only: release the frozen clip and play the landing tail
        public Vector3 facing;      // world XZ intent (may be zero)
        public bool applyFacing;    // whether facing is meaningful this frame
    }

    /// <summary>Decide the player's action stance. <paramref name="speed"/> is the measured ground speed;
    /// <paramref name="hasRun"/>/<paramref name="hasJump"/>/<paramref name="hasSprint"/> are the visual's clip capabilities (Walk↔Run choice,
    /// Fall-reuses-Jump gate). Death is handled by the CALLER (each visual grounds its corpse differently).</summary>
    public static Decision Decide(Player3DController player, Player3DAttacker attacker, float speed,
                                  float walkThreshold, float hurtUntil, float pickUpUntil, bool hasRun, bool hasJump,
                                  bool hasSprint)
    {
        var d = new Decision();
        Vector3 dir = player.WorldFacing; dir.y = 0f;
        d.facing = dir; d.applyFacing = dir.sqrMagnitude > 1e-4f;

        // Free-fall (walked/ran off a ledge): hold the Jump clip frozen at the Jump binding's airborneFreeze frame.
        if (player.IsFalling && hasJump)
        {
            var jb = StanceCatalog.Get(StanceId.Jump);
            d.hasAction = true; d.id = StanceId.Fall; d.window = jb != null ? jb.airborneFreeze : 0f;
            return d;
        }

        // One-shot / action overrides, highest priority first. While one is active we feed only FACING (not
        // velocity) so movement can't cancel it — a swing/jump/dash on the run keeps playing.
        if (attacker != null && attacker.Swinging) { d.hasAction = true; d.id = WeaponStances.AttackFor(EquippedWeapon()); d.window = attacker.SwingTime; return d; }
        if (player.IsAirborne)                      { d.hasAction = true; d.id = StanceId.Jump;   d.window = player.JumpClipFreeze; d.release = player.JumpClipReleasing; return d; }
        // A Dash SKILL is a pure A→B glide, NOT locomotion — it plays NO animation (no Roll, no Run). Return no
        // action AND skip the velocity check below, so the character just slides while holding its idle/held pose.
        if (player.SkillGliding)                    { d.hasAction = false; return d; }
        if (player.Dashing || player.EDashing)      { d.hasAction = true; d.id = StanceId.Roll;   d.window = player.EDashing ? player.eDashDuration : 0.4f; return d; }
        if (Time.time < pickUpUntil)                { d.hasAction = true; d.id = StanceId.PickUp; return d; }
        // Hurt only flinches when fully idle — a hit never interrupts an action or a run.
        if (Time.time < hurtUntil && speed < walkThreshold) { d.hasAction = true; d.id = StanceId.Hurt; return d; }

        // Locomotion: RUN by default; V (Sprinting) upshifts to SPRINT, Shift (WalkingSlow) downshifts to WALK.
        // Sprint falls back to the Run clip until a distinct Sprint clip is authored. Facing from the player's INTENT.
        if (speed >= walkThreshold && dir.sqrMagnitude > 1e-4f)
        {
            d.hasAction = true;
            d.id = player.Sprinting ? (hasSprint ? StanceId.Sprint : StanceId.Run)
                 : player.WalkingSlow ? StanceId.Walk
                 : (hasRun ? StanceId.Run : StanceId.Walk);
            d.facing = dir.normalized; d.applyFacing = true;
            return d;
        }

        // Idle: clear the action so a held stance (Combat/Guard/…) shows, else the controller plays Idle.
        d.hasAction = false;
        return d;
    }

    // The player's equipped weapon TYPE (None when unarmed) — picks which Attack* variant the swing plays. Reads the
    // single owner (Character.Instance's Weapon slot); no allocation.
    static WeaponType EquippedWeapon()
    {
        var it = Character.Instance != null ? Character.Instance.GetEquipped(EquipSlotType.Weapon) : null;
        return it != null ? it.weaponType : WeaponType.None;
    }
}
