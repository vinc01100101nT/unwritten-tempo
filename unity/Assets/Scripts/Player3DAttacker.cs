using System.Collections;
using UnityEngine;

/// <summary>
/// 3D melee swing executor — lean billboard melee (no legacy 2D mouse
/// helpers, which <see cref="Player3DController"/> handles via a camera raycast). The controller
/// owns the target; this only knows how to swing: it gates on <see cref="cooldown"/> and applies
/// damage at the END of a short <see cref="windupTime"/>, so a swing can be cancelled before it
/// lands. Uses <see cref="Character"/>'s attack if present, else <see cref="damage"/> (the spike
/// has no Character, so it falls back).
/// </summary>
public class Player3DAttacker : MonoBehaviour
{
    [Tooltip("How close (world units, XZ) you must be to land a hit. This is the FULL-SIZE reach; the live reach " +
             "is this blended with the TARGET's size at each hit test (see RangeAgainst), so it tracks both bodies.")]
    public float range = 0.5f;
    [Tooltip("Seconds between auto-attacks.")]
    public float cooldown = 0.6f;
    [Tooltip("Delay between a swing starting and the hit landing (cancellable window).")]
    public float windupTime = 0.18f;
    // NO contact-fraction knob here any more. When in the swing a blow lands is now the SERVER's decision, and it
    // has no attacker component to read it from — so the number moved to the shared character spec, where both
    // machines read the same one. A copy here would let your animation and the server's blow drift apart.
    [Tooltip("Fallback damage when there's no Character (e.g. the standalone spike).")]
    public int damage = 3;
    // NO arrow knobs here any more. What an arrow looks like and how fast it travels belong to the ARROW, not to
    // whoever happens to be holding the bow — see Arrow — so a person's shot and a creature's are the same shot.
    [Header("Audio (used when the equipped weapon has none of its own)")]
    public SfxSet defaultAttackSfx;   // swing
    public SfxSet defaultHitSfx;      // impact

    float nextSwing;
    bool attemptOpen;                     // an announced attack that hasn't started or been cancelled yet (see AnnounceAttackIntent)
    Coroutine swing;
    // "Am I close enough to stand and fight?" — the SHARED answer, the same one every creature asks, so the person
    // at this keyboard and the thing biting them can never decide it by two different rules.
    UnitOffense.EngageLatch engagement;
    public bool Swinging => swing != null;

    /// <summary>
    /// Time-scale of the attack MOTION — 1 / attack-speed, so a faster ASPD shrinks every part of
    /// the swing (windup, slash FX, the held pose) the same way it shrinks the cooldown. &lt;1 means
    /// the animation plays quicker. The pose hold in <see cref="Player3DController"/> reads this too,
    /// so the whole attack stays in sync with the swing rate.
    /// </summary>
    public static float MotionScale =>
        Character.Instance != null ? 1f / Mathf.Max(0.01f, Character.Instance.AttackSpeedMult) : 1f;

    /// <summary>How long one whole swing lasts (start → next swing can begin), scaled by attack-speed. The attack
    /// animation is sped to play fully within this window (see Character3DPose); the hit lands partway through at
    /// <see cref="CharacterSpec.hitTiming"/> (default 70%) — the contact moment. Equals the swing interval, so DPS is unchanged —
    /// only WHEN inside the window the hit connects moves.
    ///
    /// <para>The character owns the interval and the attack-speed that scales it; this asks them. Only the standalone
    /// spike — a body with no character behind it — falls back to <see cref="cooldown"/>.</para></summary>
    public float SwingTime => Character.Instance != null
        ? Character.Instance.SwingTime
        : UnitOffense.AttackInterval(cooldown, 1f);   // same rule a mob's cadence uses

    /// <summary>The FULL-SIZE reach before unit-scaling: the equipped weapon's <see cref="Item.attackRange"/> when it
    /// sets one (Weapon slot, &gt; 0 — a bow/spear reaches farther than a fist), else the attacker's own default
    /// <see cref="range"/> (unarmed / the standalone spike with no Character). Read at use-time from the equipped
    /// weapon — same pattern as the audio lookup and the stance brain — so there's no per-equip copy to
    /// keep in sync; the Item stays the single owner of weapon reach.</summary>
    public float BaseRange
        => Character.Instance != null ? Character.Instance.MeleeReachFullSize
                                      : WeaponReach.Of(null, range, false);   // the SHARED rule — a mob resolves its reach through this same one

    /// <summary>Reach against the stand-in body (<see cref="UnitScale.MobScale"/>), kept only for the callers that have no
    /// target to measure against (the pickup radius, the editor gizmo). Every actual hit test uses
    /// <see cref="RangeAgainst"/> instead, which blends against the target that is really there.</summary>
    public float EffectiveRange => BaseRange * UnitScale.MeleeReachFactor;

    /// <summary>The player's reach AGAINST A SPECIFIC TARGET — the only correct form, because a melee blend is a fact
    /// about the PAIR of bodies, not about either one alone. A Medium creature (every shadow) is a character's size, so
    /// that pair blends to full reach; a smaller one blends down, a bigger one up (<see cref="MobSizes"/>).
    ///
    /// <para><see cref="EffectiveRange"/> assumes the stand-in body and now exists only for the callers that have
    /// NO target to measure against (the pickup radius, the editor gizmo). Every real hit test uses this.</para></summary>
    public float RangeAgainst(Health t)
        => BaseRange * UnitScale.MeleeBlend(UnitScale.CharacterScale,
                                            t != null ? t.MeleeScale : UnitScale.MobScale);

    /// <summary>Strict planar range — used to actually LAND a hit (TryAttack / the windup check).</summary>
    public bool InRange(Health t) => t != null && !t.IsDead && PlanarDist(t) <= RangeAgainst(t);

    /// <summary>Sticky version of <see cref="InRange"/> for the controller's stand-vs-chase decision:
    /// turns true once the target is inside reach by the one authored margin every character reads, and stays true
    /// until it is past reach — so it is never true while a swing cannot land, and the chase always picks up again.
    /// Landing a hit still uses the strict <see cref="InRange"/>.</summary>
    public bool EngagedWith(Health t)
    {
        if (t == null || t.IsDead) return engagement.Ask(null, 0f, 0f);
        float r = RangeAgainst(t);
        return engagement.Ask(t, PlanarDistSqr(t), r * r);
    }

    float PlanarDist(Health t)
    {
        Vector3 a = transform.position, b = t.transform.position;
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    // The same gap, left squared. Asked every frame while attacking, and the answer is only ever compared against
    // another distance — so the square root is bought only where a number is actually printed.
    float PlanarDistSqr(Health t)
    {
        Vector3 d = t.transform.position - transform.position;
        d.y = 0f;
        return d.sqrMagnitude;
    }

    /// <summary>Announce that we WANT to basic-attack <paramref name="t"/> — the one fire-site of
    /// <see cref="SkillTrigger.OnBasicAttackStart"/>. Called by the controller whenever a basic-attack target is held,
    /// BEFORE reach is judged (<see cref="EngagedWith"/>/<see cref="InRange"/>), so a passive that reacts to the intent
    /// — a gap-closer, a pre-swing self-buff, a shout — resolves while the attack is still pending instead of after it
    /// already hit or was refused for being out of reach.
    ///
    /// <para><b>The announce and the swing that follows are ONE attempt.</b> Announcing OPENS the attempt; it closes
    /// when the swing actually starts (<see cref="TryAttack"/>) or when the attempt is abandoned
    /// (<see cref="Cancel"/> — hard CC, a new order, combat ending). Exactly one announce can be open at a time, so a
    /// pre-attack passive fires once per swing BY CONSTRUCTION rather than on a timer of its own. An earlier version
    /// used a parallel intent clock; because a gap-closer's travel time pushed every swing past that clock's expiry,
    /// it had always elapsed by the time control returned and so gated nothing — the passive re-fired continuously,
    /// paced by its own dash rather than by attack speed.</para></summary>
    public void AnnounceAttackIntent(Health t)
    {
        if (t == null || t.IsDead || swing != null || attemptOpen || Time.time < nextSwing) return;
        attemptOpen = true;
        // The person at this keyboard — this component only ever exists on their own avatar.
        PassiveEvents.Fire(Character.Instance, SkillTrigger.OnBasicAttackStart, null, t.transform);
    }

    // WAS THE LAST SWING REFUSED FOR REACH. Remembered so the line below prints once when the target goes out of
    // reach and once when it comes back — never per frame, because this is asked every frame while attacking.
    bool wasOutOfReach;

    public void TryAttack(Health t)
    {
        // OUT OF REACH IS A REASON, NOT A SILENCE. A sword with no authored reach makes every swing impossible
        // and said nothing about it, so two play-tests reported "attacks do not work" and one of them had both
        // numbers in hand without comparing them. The numbers now say it themselves.
        bool outOfReach = t != null && !t.IsDead && !InRange(t);
        if (outOfReach != wasOutOfReach)
        {
            wasOutOfReach = outOfReach;
            if (GameLog.On(LogCat.Hit))
                GameLog.Info(LogCat.Hit, outOfReach
                    ? string.Concat(SpawnIds.MapOf(t), " CANNOT REACH ", SpawnIds.Label(t), " — it is ", PlanarDist(t).ToString("0.###"),
                                    " away and this weapon reaches ", RangeAgainst(t).ToString("0.###"))
                    : "target back in reach");
        }

        if (Time.time < nextSwing || swing != null || !InRange(t)) return;
        attemptOpen = false;   // the announced attack is happening — this attempt is resolved
        // AttackSpeed shortens the gap between swings AND the motion itself (faster = higher mult),
        // so the visible swing keeps pace with the cadence instead of lagging at high ASPD.
        nextSwing = Time.time + cooldown * MotionScale;

        // ASK AT THE START, not at the contact frame. The server decides when this blow lands, on its own clock, and
        // announces the swing to everybody watching from the moment it accepts it — so their view of the swing and
        // of the damage number come from one message and cannot disagree. Your own character still swings this frame
        // and waits for nothing; only the OUTCOME travels.
        //
        // A BOW ASKS HERE TOO, at the release. The server times the flight from the one arrow speed and lands the
        // blow when the shot arrives, and it tells every machine that can see you to draw the arrow. This machine
        // used to fly its own arrow and ask when that arrow landed — so how long a shot took was a fact about this
        // machine's frame rate, and nobody else ever saw an arrow at all.
        AskTheServerToSwing(t);

        Sfx.Play(WeaponSfx(w => w.attackSfx, defaultAttackSfx));   // swing
        // OnAttack (attacker-side "Active Verb") is dormant — re-enable once we compose per-weapon swing vfx/sfx.
        // VfxCue.Fire(VfxCueId.OnAttack, transform);
        swing = StartCoroutine(SwingThenHit(t));
    }

    // The equipped weapon's set if it has clips, else the player default.
    static SfxSet WeaponSfx(System.Func<Item, SfxSet> pick, SfxSet fallback)
    {
        var w = Character.Instance != null ? Character.Instance.GetEquipped(EquipSlotType.Weapon) : null;
        var s = w != null ? pick(w) : null;
        return s != null && !s.Empty ? s : fallback;
    }

    // Whether the swing currently playing was handed to the server at its start. False for the Character-less spike
    // and when there was nothing shared to aim at; the contact frame then asks the server instead (LandBasicHit).
    bool AskedTheServer;

    // Ask only when there is a fighter for the server to swing WITH. Without a character this is the spike — a scene
    // fixture with no stats and nothing on the wire — and it keeps landing its own flat blow as it always did.
    void AskTheServerToSwing(Health t)
    {
        AskedTheServer = Character.Instance != null && GameNetwork.AskToSwingAt(t);
    }

    IEnumerator SwingThenHit(Health t)
    {
        // The whole swing spans SwingTime (scales with attack-speed; Character3DPose speeds the clip to fill it).
        // The hit lands at the shared contact fraction OF that window — ~70% in — not at the very end. We then
        // hold `swing` for the remainder so the animation finishes and cadence (= SwingTime) is unchanged. The
        // server reads the same fraction to time the blow, so the animation and the blow cannot drift apart.
        float scale = MotionScale;
        float swingTime = SwingTime;
        float impactAt = swingTime * Mathf.Clamp01(CharacterSpec.Active.hitTiming);
        // Manual timer, not `new WaitForSeconds`: this coroutine starts EVERY swing under the auto-pilot AI,
        // and the duration varies with attack speed so a cached wait can't cover it (PERFORMANCE.md).
        for (float el = 0f; el < impactAt; el += Time.deltaTime) yield return null;

        // RO-style commit: once the swing has STARTED (gated on InRange in TryAttack), the blow LANDS regardless of how
        // far the target has since walked — the attack is animation-locked, not re-checked at impact. We only skip a
        // target that no longer exists or already died during the windup. (Melee is decided at swing start.)
        // NOTHING IS LANDED HERE ANY MORE, by any weapon. The server was asked when this swing began and lands the
        // blow on its own clock — at this same point of the swing for a melee weapon, and when the arrow arrives for
        // a bow. Landing it here as well would be the same blow twice.
        if (t != null && !t.IsDead && !AskedTheServer) LandBasicHit(t);

        // Let the rest of the swing play out before freeing the slot, so the pose/animation completes and the next
        // swing still starts one SwingTime after this one began (DPS unchanged).
        float rest = swingTime - impactAt;
        for (float el = 0f; el < rest; el += Time.deltaTime) yield return null;
        swing = null;
    }

    /// <summary>A swing the server could not be asked about at its start — the standalone spike, a body with no
    /// character behind it. A player's machine rolls nothing: it asks the server, which rolls the blow, lands it and
    /// tells every screen what it drew.</summary>
    void LandBasicHit(Health t)
    {
        if (t == null) return;
        GameNetwork.AskToSwingAt(t);
    }

    /// <summary>Abandon the current attack: drop an in-flight swing and CLOSE the attempt an
    /// <see cref="AnnounceAttackIntent"/> opened, so a cancelled attack always resolves (a stuck-open attempt would
    /// silence the pre-attack passive forever). Whatever the announce already spent stays spent — see StateGate.</summary>
    public void Cancel()
    {
        if (swing != null) { StopCoroutine(swing); swing = null; }
        attemptOpen = false;
        engagement.Ask(null, 0f, 0f);   // let go of "close enough to stand and fight" — the same reset it always did
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, Application.isPlaying ? EffectiveRange : range);
    }
}
