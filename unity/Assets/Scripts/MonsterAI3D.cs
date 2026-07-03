using UnityEngine;

/// <summary>
/// Pure brain for a billboard mob. Wanders near its spawn,
/// CHASES the <see cref="Player3DController"/> once within <see cref="aggroRadius"/>, and gives up
/// past <see cref="leashRadius"/>. Movement is delegated to a <see cref="PathAgent3D"/> sibling
/// (routes around props via the grid A*); <see cref="BillboardAnimator3D"/> animates from that
/// motion. Reuses <see cref="Health"/> for HP/death and the existing <see cref="Character"/> for
/// touch-damage + XP (no-ops in the standalone spike, which has no Character yet).
/// </summary>
[RequireComponent(typeof(PathAgent3D))]
public class MonsterAI3D : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 2.5f;

    [Header("Senses (world units)")]
    public float aggroRadius = 6f;
    public float leashRadius = 10f;
    [Tooltip("Hold this far from the player. Must be < touchRange so it can actually land hits.")]
    public float stopDistance = 0.7f;

    [Header("Wander")]
    public float wanderRadius = 3f;
    public float wanderPauseMin = 1f;
    public float wanderPauseMax = 3f;

    public enum AttackType { Physical, Magic }

    [Header("Combat")]
    [Tooltip("Physical hits the player's Defense; Magic hits MagicDefense (and ignores Evasion).")]
    public AttackType attackType = AttackType.Physical;
    public int attack = 3;
    [Tooltip("This mob's attack ELEMENT (RO property), checked against the player's armor element. The mob's " +
             "OWN armor element (what the player's spells hit) lives on its Health component.")]
    public Element attackElement = Element.Neutral;

    [Header("On-hit status (RO — inflicted on the player when this mob lands a hit)")]
    [Tooltip("The ailment applied on a landed hit. Only used when the chance below is > 0.")]
    public Ailment onHitAilment = Ailment.Poison;
    [Range(0f, 1f)]
    [Tooltip("Chance per LANDED hit to inflict the ailment (0 = this mob inflicts no status).")]
    public float onHitAilmentChance = 0f;
    [Tooltip("Ailment duration in seconds.")]
    public float onHitAilmentSeconds = 4f;
    [Tooltip("Ailment magnitude — DoT damage/tick, Slow factor (0..1), or Blind HIT loss.")]
    public float onHitAilmentMagnitude = 3f;

    [Tooltip("Outgoing damage swings down to (1−spread)×attack each hit, RO-style (0 = always full attack).")]
    [Range(0f, 1f)] public float damageSpread = 0.25f;
    [Tooltip("HIT (RO accuracy). Rolled against the player's FLEE on each bite; lose and the attack MISSes (white).")]
    public int hit = 8;
    public int xpReward = 5;
    public float touchRange = 0.9f;
    public float touchInterval = 1f;

    [Header("Behavior style")]
    [Tooltip("0 = melee (close in and bite). >0 = HOLD this far from the player and attack from range " +
             "(a caster/archer kites). For ranged mobs set touchRange ≥ this so they can land hits while holding.")]
    public float preferredRange = 0f;
    [Tooltip("0 = never flee. Otherwise, once HP drops below this fraction of max while aggroed, the mob turns " +
             "and runs from the player — a skittish 'wounded animal' that breaks off when hurt.")]
    [Range(0f, 1f)] public float fleeHpFraction = 0f;

    [Header("Ranged projectile (magic mobs only)")]
    [Tooltip("Visible projectile lobbed by a Magic attacker; the hit lands on arrival. Null = instant magic touch.")]
    public Texture2D projectileSheet;
    public int projFrameSize = 16;
    public int projFrames = 4;
    public float projFps = 16f;
    public float projSpeed = 9f;
    public float projScale = 1.1f;

    [Header("Stun indicator (FX/Magic/Spirit)")]
    [Tooltip("Spinning swirl shown over the mob's head while stunned. Null = stun still freezes it (no swirl).")]
    public Texture2D stunSheet;
    public int stunFrameSize = 32;
    public int stunFrames = 5;

    static readonly Color MagicHitColor = new Color(0.7f, 0.5f, 1f);
    static readonly Color PhysHitColor  = new Color(1f, 0.35f, 0.35f);
    static readonly Color StunColor     = new Color(1f, 0.9f, 0.3f);

    PathAgent3D agent;
    Player3DAttacker playerAtk;   // cached once (Start) so the melee hold-distance can clamp to the player's reach without a per-frame GetComponent
    AttackLunge lunge;
    Transform player;
    Vector3 home;
    float nextWander;
    bool chasing;
    float nextTouch;
    float stunnedUntil;   // hit by a stagger (e.g. the Swordsman's Bash) — frozen until this time
    float slowUntil;      // chilled (e.g. Frost Nova) — moves at slowFactor speed until this time
    float slowFactor = 1f;
    StatusFX stunFx;      // looping swirl shown over the head while stunned (created on first stun)
    StatusController status;   // poison/bleed/burn/blind host (Stun/Slow still delegate to the fields above)

    /// <summary>This mob's <see cref="Health"/>, cached once. The cursor pick and auto-combat target
    /// scans read this instead of calling <c>GetComponent&lt;Health&gt;()</c> per mob, every frame.</summary>
    public Health Health { get; private set; }

    /// <summary>Live registry of active mobs, so cursor target-magnetism can scan candidates without a
    /// per-frame <c>FindObjectsByType</c>. Maintained on enable/disable (so it respects MobRespawn pooling).</summary>
    public static readonly System.Collections.Generic.List<MonsterAI3D> Active = new System.Collections.Generic.List<MonsterAI3D>();

    void Awake()
    {
        agent = GetComponent<PathAgent3D>();
        lunge = GetComponent<AttackLunge>();
        Health = GetComponent<Health>();
        status = Status.Of(gameObject);   // poison/bleed/burn ticks + silence/blind flags
        home = transform.position;
        UnitCrowd.Register(gameObject);   // pass through other units (no stacking / climbing on heads)

        // Floating HP bar + name under the sprite. Auto-added here so EVERY mob gets one (even those baked
        // before the nameplate existed — no re-bake). The training dummy keeps its own HP readout; the
        // boss's bigger plate is configured by BossBanner. DisallowMultipleComponent + this guard avoid a
        // duplicate when one is already present.
        if (GetComponent<TrainingDummy>() == null && !TryGetComponent<MobNameplate>(out _))
            gameObject.AddComponent<MobNameplate>();
    }

    void OnEnable()  { if (!Active.Contains(this)) Active.Add(this); }
    void OnDisable() => Active.Remove(this);

    void Start()
    {
        agent.moveSpeed = moveSpeed * UnitScale.ForMob;

        var pc = FindFirstObjectByType<Player3DController>();
        if (pc != null) { player = pc.transform; playerAtk = pc.GetComponent<Player3DAttacker>(); }

        // Crowd avoidance: spread out from other mobs (they no longer physically block each other),
        // but NOT from the player we're chasing — so melee can still close in.
        agent.avoidMask = UnitCrowd.Mask;
        agent.avoidIgnore = player;

        if (Health != null)
            Health.onDeath.AddListener(() => { if (Character.Instance != null) Character.Instance.AddXP(xpReward); });

        nextWander = Time.time + Random.value;
    }

    /// <summary>Briefly freeze this mob (chase + touch-attack) — a reusable stagger/crowd-control hook for
    /// skills like the Swordsman's Bash. Stacks by taking the later end time, not by adding.</summary>
    public void Stagger(float seconds)
    {
        if (seconds > 0f) stunnedUntil = Mathf.Max(stunnedUntil, Time.time + seconds);
    }

    /// <summary>Chill this mob to <paramref name="factor"/>× move speed (0..1) for <paramref name="seconds"/>s —
    /// a reusable slow for skills like the Mage's Frost Nova. Stacks by the later end time + stronger factor.</summary>
    public void Slow(float factor, float seconds)
    {
        if (seconds <= 0f) return;
        slowFactor = Mathf.Min(slowFactor < 1f && Time.time < slowUntil ? slowFactor : 1f, Mathf.Clamp01(factor));
        slowUntil = Mathf.Max(slowUntil, Time.time + seconds);
    }

    /// <summary>Stun this mob for <paramref name="seconds"/> — freezes it (reusing the <see cref="Stagger"/>
    /// freeze) and shows a spinning swirl over its head. A reusable status hook for per-hit procs (e.g. the
    /// Mage's multi-hit Meteor). Re-stunning while already stunned just refreshes the duration (no popup spam).</summary>
    public void Stun(float seconds)
    {
        if (seconds <= 0f) return;
        bool wasStunned = Time.time < stunnedUntil;
        Stagger(seconds);   // freeze chase + attack (shares stunnedUntil)
        if (stunSheet != null)
        {
            if (stunFx == null) stunFx = StatusFX.Create(transform, stunSheet, stunFrameSize, stunFrames, 12f, 0.7f, 1.15f);
            stunFx.Show(seconds);
        }
        if (!wasStunned) DamagePopup.SpawnText(UnitAnchor.Head(transform), "Stun!", StunColor);
    }

    /// <summary>Shove this mob directly away from <paramref name="worldFrom"/> at <paramref name="speed"/>
    /// for <paramref name="time"/>s and stagger it for <paramref name="stagger"/>s (so its AI doesn't fight
    /// the slide). Used by AoE skills like the Swordsman's Warcry. Reuses the cached <see cref="agent"/> —
    /// no per-call GetComponent.</summary>
    public void Knockback(Vector3 worldFrom, float speed, float time, float stagger)
    {
        Vector3 dir = transform.position - worldFrom; dir.y = 0f;
        if (dir.sqrMagnitude < 1e-4f) dir = transform.forward;   // standing on the source → any direction
        if (agent != null) agent.Knockback(dir, speed, time);
        Stagger(Mathf.Max(stagger, time));
    }

    /// <summary>Roll this mob's configured on-hit ailment onto the player — called only when a hit actually
    /// LANDED (so a MISS / Immune doesn't proc it). Applies straight to the player's status host.</summary>
    public void TryInflictOnHit()
    {
        if (onHitAilmentChance <= 0f) return;
        var ps = StatusController.Player;
        if (ps != null && Random.value < onHitAilmentChance)
            ps.Apply(onHitAilment, onHitAilmentSeconds, onHitAilmentMagnitude);
    }

    void Update()
    {
        if (Time.time < stunnedUntil) { if (agent != null) agent.Halt(); return; }   // staggered — frozen

        // Chill (Frost Nova): scale move speed while slowed; full speed once it expires. UnitScale.ForMob
        // couples speed to size (smaller mob = slower) so it keeps the same body-lengths/sec at any size.
        agent.moveSpeed = ((Time.time < slowUntil) ? moveSpeed * slowFactor : moveSpeed) * UnitScale.ForMob;

        Vector3 pos = transform.position;
        // A dead player is no target: leash off and resume wandering until they respawn.
        bool playerAlive = player != null && Character.Instance != null && !Character.Instance.IsDead;
        // Compare SQUARED distances against squared radii — avoids a sqrt per check, per mob, per frame.
        float toPlayerSqr = playerAlive ? PlanarSqr(pos, player.position) : Mathf.Infinity;

        bool was = chasing;
        if (chasing && toPlayerSqr > leashRadius * leashRadius) chasing = false;
        else if (!chasing && toPlayerSqr <= aggroRadius * aggroRadius) chasing = true;

        // Touch damage (Phase F parity). No-op in the spike: Character.Instance is null without Systems.
        // Use TRUE 3D distance (not planar) so a mob that has fallen off a cliff/edge below the terrain —
        // same XZ as the player but far below — can't keep biting an invisible, "there are no mobs" hit.
        float touch3DSqr = playerAlive ? (player.position - pos).sqrMagnitude : Mathf.Infinity;
        // MELEE contact range is authored at full unit size; rescale it to the current size (UnitScale) so a shrunk
        // mob bites at its actual body edge rather than across a fixed gap that dwarfs its sprite. RANGED mobs
        // (preferredRange > 0) keep their authored touchRange untouched — their reach is a deliberate standoff, not
        // a body-contact distance, so it must NOT shrink with unit size.
        float touch = preferredRange > 0f ? touchRange : touchRange * UnitScale.MeleeReachFactor;
        if (player != null && touch3DSqr <= touch * touch && Time.time >= nextTouch && Character.Instance != null)
        {
            // Magic mobs hit MagicDefense (and can't be dodged); physical mobs hit Defense/Evasion.
            int atk = Mathf.RoundToInt(attack * (1f - Random.Range(0f, damageSpread)));   // RO-style spread
            if (attackType == AttackType.Magic && projectileSheet != null)
            {
                // Ranged caster: lob a VISIBLE projectile at the player; the magic damage lands on arrival.
                int dmg = atk;            // capture for the on-arrive callback
                Element elem = attackElement;
                var self = this;          // for damage-reflect on arrival (guarded if we've since died)
                Vector3 from = UnitAnchor.Center(transform);   // cast from the mob's mid-body (feet-pivoted)
                Projectile3D.Spawn(projectileSheet, projFrameSize, projFrames, projFps, projScale,
                    from, player, UnitAnchor.Center(player), projSpeed, 0.45f, 3f, () =>
                    {
                        if (Character.Instance == null || Character.Instance.IsDead) return;
                        int d = Character.Instance.TakeMagicDamage(dmg, elem, self);
                        if (d > 0) { DamagePopup.Spawn(player.position, d, MagicHitColor); self.TryInflictOnHit(); }
                    });
            }
            else
            {
                // Blind cuts our accuracy: physical attacks roll a reduced HIT against the player's FLEE.
                int effHit = Mathf.Max(0, hit - (status != null ? status.HitPenalty : 0));
                int dealt = attackType == AttackType.Magic
                    ? Character.Instance.TakeMagicDamage(atk, attackElement, this)        // magic: MATK vs MDEF + element
                    : Character.Instance.TakeDamage(atk, effHit, attackElement, this);    // physical: HIT vs FLEE + element
                if (dealt > 0)
                {
                    DamagePopup.Spawn(player.position, dealt,
                        attackType == AttackType.Magic ? MagicHitColor : PhysHitColor);
                    TryInflictOnHit();
                }
            }
            nextTouch = Time.time + touchInterval;
            if (lunge != null) lunge.Punch();   // squash-stretch "chomp" tell
        }

        if (chasing)
        {
            Pursue(pos, toPlayerSqr);
        }
        else
        {
            if (was) { agent.Halt(); nextWander = Time.time; }
            if (agent.Arrived && Time.time >= nextWander)
            {
                Vector2 r = Random.insideUnitCircle * wanderRadius;
                agent.MoveTo(home + new Vector3(r.x, 0f, r.y));
                nextWander = Time.time + Random.Range(wanderPauseMin, wanderPauseMax);
            }
        }
    }

    // The chase brain, split by behavior style. All branches reuse the per-frame distance already computed
    // in Update — no extra Find/GetComponent/alloc (PERFORMANCE.md: nothing new in the per-mob hot path).
    void Pursue(Vector3 pos, float toPlayerSqr)
    {
        // Skittish: wounded enough → break off and run from the player.
        if (fleeHpFraction > 0f && Health != null && Health.Current <= Health.maxHealth * fleeHpFraction)
        {
            agent.MoveToward(AwayPoint(pos));
            return;
        }

        // Ranged/caster: hold the pocket at preferredRange — kite back if too close, close in if too far,
        // otherwise stand and let the touch-attack above fire (touchRange is set ≥ preferredRange). preferredRange
        // is a deliberate standoff (their "long range"), so it is NOT rescaled by unit size — ranged stays ranged.
        if (preferredRange > 0f)
        {
            float near = preferredRange * 0.8f;
            if (toPlayerSqr < near * near)            agent.MoveToward(AwayPoint(pos));
            else if (toPlayerSqr > preferredRange * preferredRange) agent.MoveToward(player.position);
            else                                       agent.Halt();
            return;
        }

        // Melee: close to stopDistance and bite (the original behavior). Contact distance IS authored at full size,
        // so rescale it to the current size (UnitScale) — matching the melee touch range scaled in Update.
        float stop = stopDistance * UnitScale.MeleeReachFactor;
        // INVARIANT: a melee mob must never hold farther than the PLAYER can reach, or the player whiffs every swing
        // while the mob bites freely (authored stopDistance can exceed the player's range, e.g. 0.7 vs 0.5). Clamp the
        // hold distance to just inside the player's effective melee reach so a halted mob is always in trading range.
        if (playerAtk != null) stop = Mathf.Min(stop, playerAtk.EffectiveRange * 0.9f);
        if (toPlayerSqr <= stop * stop) agent.Halt();
        else agent.MoveToward(player.position);
    }

    // A point ~2 units from the player, directly opposite this mob — the target for kiting / fleeing.
    // Planar; locals only (no allocation).
    Vector3 AwayPoint(Vector3 pos)
    {
        Vector3 away = pos - player.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = transform.forward;   // on top of the player → pick any direction
        away.Normalize();
        return pos + away * 2f;
    }

    static float PlanarSqr(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return (a - b).sqrMagnitude;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.55f, 0.2f, 0.7f);
        Gizmos.DrawWireSphere(transform.position, aggroRadius);
        Gizmos.color = new Color(1f, 0.25f, 0.25f, 0.5f);
        Gizmos.DrawWireSphere(transform.position, leashRadius);
    }
}
