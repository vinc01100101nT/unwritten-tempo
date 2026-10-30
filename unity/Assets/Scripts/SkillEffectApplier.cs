using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The ONE place a data-authored <see cref="SkillEffect"/> (fired by a <see cref="SkillRunner"/> tick) becomes real
/// gameplay. It maps each intent onto the SAME combat sinks skills already call — a mob's <see cref="Health"/>, the
/// player's <see cref="Character"/>, the shared <see cref="Status"/> front door — so a data skill is just a new CALLER
/// of the existing damage/heal/status paths, never a second combat engine.
///
/// <para><b>Two passes per tick (caster side vs target side).</b> <see cref="SkillEffect.LandsOnCaster"/> partitions the
/// effect kinds. Pass 1 applies the CASTER effects (Dash / Guard / Buff / …) to the acting caster — once, regardless of
/// the tick's targeting, because "who does this tick hit" was never the question they answer. Pass 2 gathers the tick's
/// targets and applies the TARGET effects (Damage / Status / …) to them. No kind is in both passes, so a self-targeted
/// tick can't double-apply a caster effect and a mob-targeted tick can't swallow one.</para>
///
/// <para><b>Per-hit model (single-responsibility effects).</b> A binding is applied as a HIT LOOP whose length is the
/// Damage effect's <see cref="SkillEffect.hitCount"/> (1 if none). Each iteration is a "hit": the Damage effect lands
/// one strike, and every OTHER effect in the binding rolls its <see cref="SkillEffect.chance"/> THAT hit — a state, an
/// ailment, an execute, all on the same rule. Certain (chance 1, the default) simply means the roll always says yes.
/// So a stun-proc or execute never lives INSIDE the Damage effect; it's its own effect that triggers off the multi-hit.
/// Caster-side effects are applied once per landing, so their roll happens once no matter how many strikes there are.</para>
///
/// <para><b>Caster threading (network-readiness).</b> The acting caster is resolved ONCE at <see cref="ApplyBurst"/>
/// and threaded through the whole resolve, so no inner method reaches for the <c>Character.Instance</c> global. Today
/// the caster is whoever is using the skill — a player or a creature, as an <see cref="ICombatUnit"/> — and the entire
/// burst scales off THAT unit's own numbers. There is no fallback to the local player: a burst with no caster lands
/// nothing, because a skill nobody is using has no numbers to scale from.</para>
/// </summary>
public static class SkillEffectApplier
{
    // Reused snapshot of the mobs a burst reaches, so a scan allocates nothing per cast. Cleared at the start of each
    // ApplyBurst.
    static readonly List<MonsterAI3D> burstTargets = new List<MonsterAI3D>(32);

    /// <summary>Apply the binding's effects — CASTER effects to the caster, TARGET effects to whoever the tick reaches
    /// (an AoE scan, or the cast's designated unit for Single). Zero-alloc: a squared-distance sweep over the small
    /// Active list, no physics / Find / GetComponent.
    /// <paramref name="caster"/> is the unit using the skill, whose own numbers scale every roll.</summary>
    // The practice lift the caster puts on the cast currently applying, as a fraction (see
    // SkillEfficiency.OutputLift). Held here rather than passed through every signature because it is one value for a whole
    // burst — the same reason PassiveEvents holds its cause this way.
    static float masteryLift;

    // THE STAFF'S ORB LIFTS for the burst applying, one per orb element (index = OrbElement): a combination's part grows
    // with its own orb's level, so a Fire part reads the Fire orb's lift. Resolved once per burst like the lift above.
    static readonly float[] orbLift = new float[5];

    // What one hop of a bouncing chain is worth, as a share of its first hit (FlightMath.HopScale) — 1 for every burst
    // that is not a hop. Held the same way and for the same reason as the practice lift above: one value for a whole
    // burst, set at its entry and restored in its finally, so a passive a hop sets off casts at full strength rather
    // than at the hop's share.
    static float hopScale = 1f;

    // How much of a release-early skill's wind-up was filled (StrikeMath.ReleaseScale) — 1 for every other burst. DAMAGE
    // ONLY: a heal, a buff or a stack riding the same cast is untouched. Held per burst and restored exactly like
    // hopScale, and cleared for a passive fired inside a strike (StrikeIsolation).
    static float releaseScale = 1f;

    // A MULTI-HIT STRIKE WORKED OUT ONCE: the one strike's finished number is multiplied by its hit count, then shown
    // as that many pieces (BlowBatch.Split). Held for that one strike and restored, exactly like hopScale.
    static float strikeScale = 1f;

    /// <summary>Extra crit chance for the ONE strike being rolled right now (<see cref="SkillEffect.critChanceBonus"/>),
    /// 0 otherwise. Read by every unit's crit roll, set and restored around the strike.</summary>
    public static float StrikeCritBonus { get; private set; }

    /// <summary>A PASSIVE FIRED IN THE MIDDLE OF A STRIKE (a kill, a hit landing) is its own cast: it must not inherit
    /// the strike's hit-count scale, its crit bonus or its piece count. Held for the passive's run and put back after.</summary>
    public readonly struct StrikeIsolation : System.IDisposable
    {
        readonly float scale, critBonus, release;
        readonly int split;
        public StrikeIsolation(bool _)
        {
            scale = strikeScale; critBonus = StrikeCritBonus; split = BlowBatch.Split; release = releaseScale;
            strikeScale = 1f; StrikeCritBonus = 0f; BlowBatch.Split = 1; releaseScale = 1f;
        }
        public void Dispose() { strikeScale = scale; StrikeCritBonus = critBonus; BlowBatch.Split = split; releaseScale = release; }
    }

    public static void ApplyBurst(Vector3 point, float radius, SkillScope scope, SkillTargets targets,
                                  List<SkillEffect> effects, Transform singleTarget,
                                  string srcSymbol = null, string srcName = null, string srcId = null,
                                  Vector3? aimPoint = null, ICombatUnit caster = null, StackVars vars = default,
                                  bool efficient = false, bool casterPass = true, float hop = 1f,
                                  float release = 1f)
    {
        if (effects == null || effects.Count == 0) return;

        // The unit using the skill, threaded through the whole burst so nothing downstream reaches for a global.
        var ch = caster;
        if (ch == null) return;

        // What the caster's own practice is paying for this cast, resolved ONCE for the whole burst — looking it up
        // has no business running per hit. Restored on the way out because a passive can cast INSIDE this burst
        // (OnDamageDealt / OnKill fire synchronously below): without the restore, the inner cast's practice would go
        // on paying for the outer cast's remaining hits. An EFFICIENT cast adds its bonus on top (SkillEfficiency).
        float prevLift = masteryLift;
        float prevHop = hopScale;
        float prevRelease = releaseScale;
        float prevF = orbLift[1], prevE = orbLift[2], prevW = orbLift[3], prevA = orbLift[4];
        masteryLift = SkillEfficiency.OutputLift(ch.SkillOutputLift(srcId), efficient);
        var srcDef = SkillCatalog.Get(srcId);
        if (srcDef != null && srcDef.IsOrbCombo)
            for (int o = 1; o <= 4; o++)
                orbLift[o] = SkillEfficiency.OutputLift(ch.SkillOutputLift(OrbMath.SkillIdOf((OrbElement)o)), efficient);
        hopScale = Mathf.Max(0f, hop);
        releaseScale = Mathf.Max(0f, release);
        try
        {
            ApplyBurstTargets(point, radius, scope, targets, effects, singleTarget,
                              srcSymbol, srcName, srcId, aimPoint, ch, vars, casterPass, efficient);
        }
        finally
        {
            masteryLift = prevLift; hopScale = prevHop; releaseScale = prevRelease;
            orbLift[1] = prevF; orbLift[2] = prevE; orbLift[3] = prevW; orbLift[4] = prevA;
        }
    }

    // The body of a burst, split out ONLY so the lift above is restored on every exit — this method has several.
    static void ApplyBurstTargets(Vector3 point, float radius, SkillScope scope, SkillTargets targets,
                                  List<SkillEffect> effects, Transform singleTarget,
                                  string srcSymbol, string srcName, string srcId,
                                  Vector3? aimPoint, ICombatUnit ch, StackVars vars, bool casterPass, bool efficient)
    {

        // Where a relocation (Teleport / Dash) sends whoever it moves — the caster, or a mob when the author put the
        // effect on the target side: the AIM point, NOT the tick's VFX anchor point. The two differ once a ground cast
        // keeps origin(caster) ≠ target(aim), and a teleport must always go to the aimed spot regardless of which anchor
        // the VFX author chose for the visual. ONE destination per cast, read by both passes, so which side the author
        // pointed the effect at can't change where it lands. Defaults to point for old callers.
        Vector3 relocatePoint = aimPoint ?? point;

        // PASS 1 — CASTER effects (see SkillEffect.LandsOnCaster): they happen TO THE CASTER, so the tick's targeting has no say.
        // They run here exactly once, whatever the scope/affects are, and are excluded from the target pass below — so
        // "Single Target on a mob" can't swallow a Dash (the bug this split fixes), and a Self tick can't double-apply it.
        // A HOP of a bouncing chain skips it: the cast's own side — its buffs, its dash, its self-heal — happened once,
        // on the cast, and a jump is only the strike reaching someone else.
        Vector3 dashDest = relocatePoint;
        float dashGiveUp = 0f;
        float dashSeconds = casterPass
            ? ApplyCasterEffects(effects, relocatePoint, singleTarget, srcSymbol, srcName, srcId, ch, vars,
                                 out dashDest, out dashGiveUp) : 0f;

        // PASS 2 — TARGET effects: gather who the tick reaches, then run the hit loop on them.
        if (targets == SkillTargets.None) return;

        // A DASH STRIKES WHERE IT LANDS: when the caster pass sent the caster gliding, the strike waits for the trip
        // and lands on arrival (DashLandings), never as the dash leaves.
        if (dashSeconds > 0f && DashLandings.Hold(dashSeconds, dashGiveUp, dashDest, point, radius, scope, targets, effects,
                                                  singleTarget, srcSymbol, srcName, srcId, aimPoint, ch, vars, efficient,
                                                  releaseScale))
            return;

        // SINGLE scope: ignore the radius, land straight on the cast's designated unit. Falls back to Area if unset.
        if (scope == SkillScope.Single && singleTarget != null)
        {
            // THE PICKED UNIT, if it is an enemy of the caster: "mobs" is the side you fight, whichever side you are on
            // (see Sides). Anything else — yourself, a friend, one who fights for nobody — is reached as the caster's
            // own side is.
            var mob = singleTarget.GetComponentInParent<MonsterAI3D>();
            if (mob != null && mob.Health == null) return;
            ICombatUnit unit = mob != null ? mob.Health : PersonWithBody(singleTarget);
            if (unit != null && Sides.Hostile(ch, unit))
            {
                if ((targets & SkillTargets.Mobs) == 0) return;
                // A single target that died before this landed is still struck — its numbers show, and nothing else
                // lands on it (the dispatchers route a dead unit to the damage-only beat).
                if (mob != null)
                {
                    burstTargets.Clear(); burstTargets.Add(mob);
                    DispatchMobHits(effects, burstTargets, ch, relocatePoint, srcSymbol, srcName, srcId, vars);
                }
                else
                {
                    burstPeople.Clear(); burstPeople.Add(unit);
                    DispatchPeopleHits(effects, burstPeople, ch, relocatePoint, srcSymbol, srcName, srcId, vars);
                }
            }
            else if ((targets & SkillTargets.Player) != 0)
                ApplyEffectsToPlayer(effects, relocatePoint, srcSymbol, srcName, srcId, ch, vars);
            return;
        }

        float r2 = Mathf.Max(0.05f, radius); r2 *= r2;

        if ((targets & SkillTargets.Mobs) != 0)
        {
            GatherOpponents(point, r2, ch, burstPeople, burstTargets);
            DispatchPeopleHits(effects, burstPeople, ch, relocatePoint, srcSymbol, srcName, srcId, vars);
            DispatchMobHits(effects, burstTargets, ch, relocatePoint, srcSymbol, srcName, srcId, vars);
        }

        if ((targets & SkillTargets.Player) != 0)
        {
            // The player as a TARGET (a heal / a self-damage / an ailment on yourself) — deliberately NOT radius-gated:
            // no skill is a "hazard zone that hits the player if inside", and a ground cast's point is by definition
            // somewhere the caster isn't standing. Caster effects already ran in pass 1 and are not repeated here.
            if (ch != null && !ch.IsDead)
                ApplyEffectsToPlayer(effects, relocatePoint, srcSymbol, srcName, srcId, ch, vars);
        }
    }

    // ---- the Staff's helpers --------------------------------------------------------------------------------
    /// <summary>The level <paramref name="unit"/> has in an orb — every character knows each at level 1.</summary>
    public static int OrbLevel(ICombatUnit unit, OrbElement orb)
    {
        var mastery = MasteryOf(unit);
        return mastery != null ? mastery.LevelOf(OrbMath.SkillIdOf(orb)) : 1;
    }

    /// <summary>The masteries and skill levels of a person or a creature; null for anything else.</summary>
    public static MasteryProgress MasteryOf(ICombatUnit unit) =>
        unit is Character person ? person.Mastery : unit is Health creature ? creature.Mastery : null;

    // Is any skill of this unit mid-run? An orb press waits for it to end.
    static bool RunGoing(ICombatUnit unit)
    {
        var runners = unit != null && unit.Skills != null ? unit.Skills.Runners : null;
        if (runners == null) return false;
        for (int i = 0; i < runners.Length; i++)
            if (runners[i] != null && runners[i].RunInProgress) return true;
        return false;
    }

    static readonly List<ICombatUnit> copyPeople = new List<ICombatUnit>(8);
    static readonly List<MonsterAI3D> copyCreatures = new List<MonsterAI3D>(16);

    // A MARK CARRIED to the caster's enemies around the unit it lands on. A burn is copied at its own strength and time;
    // Chill raises each neighbour to the unit's own count at this effect's strength (Hoarfrost). A copy reads the
    // ORIGINAL, so it can never grow past it.
    static void CopyMark(SkillEffect e, ICombatUnit from, ICombatUnit caster, in StackVars vars)
    {
        var fromStates = from.States;
        if (fromStates == null || from.Body == null) return;
        float perTick = 0f, left = 0f, slow = 0f, seconds = 0f;
        int chill = 0;
        if (e.markKind == MarkKind.Burn)
        {
            var burn = fromStates.Get(StateId.Burn);
            if (burn == null) return;
            perTick = burn.magnitude; left = burn.Remaining;
            if (perTick <= 0f || left <= 0f) return;
        }
        else if (e.markKind == MarkKind.Chill)
        {
            chill = fromStates.ChillStacks;
            if (chill <= 0) return;
            slow = OutputMagnitude(e, caster, vars);
            seconds = ScaledSeconds(e, caster, vars);
        }
        else return;
        float r = Mathf.Max(0.1f, e.copyRadius.Resolve(vars));
        GatherOpponents(from.Body.position, r * r, caster, copyPeople, copyCreatures);
        for (int i = 0; i < copyCreatures.Count; i++)
        {
            var h = copyCreatures[i] != null ? copyCreatures[i].Health : null;
            if (h != null && !ReferenceEquals(h, from)) CopyOnto(h, e.markKind, perTick, left, chill, slow, seconds, caster);
        }
        for (int i = 0; i < copyPeople.Count; i++)
        {
            var p = copyPeople[i];
            if (p != null && !ReferenceEquals(p, from)) CopyOnto(p, e.markKind, perTick, left, chill, slow, seconds, caster);
        }
    }

    static void CopyOnto(ICombatUnit to, MarkKind mark, float perTick, float left, int chill, float slow, float seconds,
                         ICombatUnit caster)
    {
        if (to.IsDead || to.Body == null) return;
        if (mark == MarkKind.Burn) { Status.Apply(to.Body.gameObject, Ailment.Burn, left, perTick, caster); return; }
        var states = to.States;
        if (states == null) return;
        int add = MarkMath.CopyAdds(states.ChillStacks, chill);
        if (add > 0 && states.AddChill(add, seconds, 0, slow))
            Status.Apply(to.Body.gameObject, Ailment.Freeze, MarkMath.FreezeSeconds, 0f, caster);
    }

    // ---- "only if marked": the marks each unit carried when the tick reached it ----
    // Read once per unit, before any effect of the tick ran, so a mark that tick cashes still counts for the effects
    // after it (Scald puts out the burns, then chills exactly the units that were burning).
    static ICombatUnit gateUnit;
    static readonly int[] gateMarks = new int[5];

    static void ReadGateMarks(ICombatUnit unit)
    {
        gateUnit = unit;
        var states = unit != null ? unit.States : null;
        for (int m = 0; m < gateMarks.Length; m++)
            gateMarks[m] = states != null ? states.MarkCount((MarkKind)m) : 0;
    }

    // Only a tick with a gated effect pays for the read; every other tick on every other unit skips it.
    static void ReadGateMarksIfAsked(List<SkillEffect> effects, ICombatUnit unit)
    {
        gateUnit = null;
        for (int i = 0; i < effects.Count; i++)
            if (effects[i] != null && effects[i].onlyIfMarked != MarkKind.None) { ReadGateMarks(unit); return; }
    }

    static int GateCount(ICombatUnit unit, MarkKind mark)
    {
        if (ReferenceEquals(unit, gateUnit)) return gateMarks[(int)mark];
        return unit != null && unit.States != null ? unit.States.MarkCount(mark) : 0;
    }

    // How many stacks this apply grants on THIS unit: one per mark it carried, when the effect asks for that.
    static int StacksOn(SkillEffect e, ICombatUnit unit, ICombatUnit caster, in StackVars vars) =>
        e.stackPerMark && e.onlyIfMarked != MarkKind.None ? Mathf.Max(1, GateCount(unit, e.onlyIfMarked))
                                                           : ResolveStacks(e, caster, vars);

    // Weathering: the last Staff combination this unit cast is ready again — an ultimate's shared wait with it.
    static void ResetLastCombination(ICombatUnit unit)
    {
        var skills = unit != null ? unit.Skills : null;
        if (skills == null || string.IsNullOrEmpty(skills.LastCombinationId)) return;
        skills.RunnerById(skills.LastCombinationId)?.ResetCooldown();
        var def = SkillCatalog.Get(skills.LastCombinationId);
        if (def != null && def.IsUltimate && skills.SharedUltimateRemaining > 0f)
        {
            skills.ForgetSharedUltimate();
            GameNetwork.TellOwnerCooldownStarted(unit, SharedCooldown.UltimateKey, WorldClock.UtcNow);
        }
    }

    // ---- where a relocation lands -------------------------------------------
    // How far from the target a relocation stops, as a fraction of the caster's melee reach — just INSIDE it, the same
    // "always in trading range" margin a melee mob holds at (MonsterAI3D's stop clamp), so the strict InRange check that
    // gates a swing passes on arrival. Player3DAttacker.EffectiveRange stays the single owner of the reach itself.
    const float LandingReachFactor = 0.9f;

    // How far around the target each successive landing moves, for a star of `corners` points: cross to the far side
    // (180°) plus a nudge of 180/corners. At 5 corners that's the original 216° — the spot sits 18° off your own line
    // to the target, so every hop passes within a tenth of a unit of the target and you cut straight THROUGH it.
    // `corners` hops then visit `corners` evenly-spaced bearings and close the loop, tracing a star on the ground
    // rather than walking the polygon's rim (5 × 216° = 1080°, three full laps; 7 × 205.71° = 1440°, four).
    //
    // ODD corner counts only: an even count doesn't come out to whole laps, so the figure never closes. The authored
    // value is forced odd rather than rejected — a shape that quietly fails to close is worse than one nudged by one.
    static float LandingStepDegrees(int corners)
    {
        int c = Mathf.Max(3, corners);
        if ((c & 1) == 0) c++;
        return 180f + 180f / c;
    }

    /// <summary>Where a RELOCATION (Dash / Teleport) actually LANDS. Aiming at a unit means "get to it", not "stand
    /// inside it": the mover arrives at ITS OWN melee reach, on the far side, rotated by <see cref="LandingStepDegrees"/>
    /// from wherever it currently stands — so it never ends up stacked on the target's spot, every dash reads as a cut
    /// THROUGH the target, and <paramref name="corners"/> in a row trace a star on the ground. Derived from the mover's
    /// CURRENT bearing rather than a stored step index: no sequence state to reset, an interrupted chain resumes from
    /// wherever it stopped. A pure ground aim — no unit given and no mob on the spot — is used verbatim.
    ///
    /// <para><paramref name="mover"/> is WHOEVER IS BEING MOVED, which is not always the local player: an effect
    /// authored on the TARGET side relocates a mob, and once combat is server-authoritative the caster is some other
    /// connection's entity. This used to read a static <c>Player3DAttacker</c> resolved from the local player, so both
    /// the reach and the bearing came from YOU no matter who was actually moving — teleporting a mob sent it to a spot
    /// computed from the player's position and the player's weapon range.</para></summary>
    static Vector3 RelocationDest(Vector3 aim, Transform unit, ICombatUnit mover, ICombatUnit caster, int corners)
    {
        Transform body = mover?.Body;
        if (body == null) return aim;

        // GEAR ONLY, never what a skill is lending. This is the one reader that PLACES a body rather than judging a
        // blow: it wants striking distance, and striking distance is a fact about the weapon. Reading the lent
        // version put the mover as far past the target as the bonus was long — and further with every charge, so a
        // gap-closer whose own buff lengthened reach flung you further away the better it was working.
        float full = mover.EquippedReachFullSize;
        if (full <= 0f) return aim;   // reach unknown (avatar not spawned yet) — don't invent a landing spot

        // The thing we're landing next to: the cast's picked unit, else whatever mob the aim point is sitting on (a
        // Ground-delivered dash at a victim's feet aims at the mob's own position, with no unit pick to read).
        // Its SIZE matters: how close "just inside reach" is depends on both bodies, so resolve the target first and
        // blend against it. Using the mover's own pre-scaled reach here assumed the target was an ordinary sprite mob —
        // the same one-sided assumption that made the player and a shadow disagree about the identical pairing.
        Vector3 center;
        Transform targetT = unit;
        ICombatUnit target = null;
        if (targetT == null)
        {
            // SEARCH radius assumes the stand-in body (UnitScale.MobScale) — this only asks "is something of the other side standing on
            // the aimed spot", and widening it would silently make ground-aimed dashes snap from further away than they
            // used to.
            float searchR = full * UnitScale.MeleeBlend(mover.MeleeScale, UnitScale.MobScale) * LandingReachFactor;
            if (!OpponentAt(aim, searchR, caster, out center, out target)) return aim;
        }
        else
        {
            center = targetT.position;
            // once per relocation cast, never per frame
            target = UnitOn(targetT);
        }

        // LANDING distance blends the mover against the body it actually found.
        float reach = full * UnitScale.MeleeBlend(mover.MeleeScale,
                                                  target != null ? target.MeleeScale : UnitScale.MobScale)
                           * LandingReachFactor;
        if (reach <= 0f) return aim;

        // Bearing of the MOVER as it stands now, measured around the target, then stepped. A mover standing exactly on
        // the target has no bearing to step from — fall back to its facing so the hop still has a direction.
        Vector3 from = body.position - center; from.y = 0f;
        if (from.sqrMagnitude < 1e-6f) from = body.forward;
        float a = Mathf.Atan2(from.z, from.x) + LandingStepDegrees(corners) * Mathf.Deg2Rad;
        return new Vector3(center.x + Mathf.Cos(a) * reach, center.y, center.z + Mathf.Sin(a) * reach);
    }

    // ---- the other side ----------------------------------------------------
    // SkillTargets.Mobs is "the side you fight" — whoever Sides says is the caster's enemy. For a person that is the
    // creatures; for a creature it is PEOPLE and the creatures fighting for them; for a creature fighting for the
    // players it is the creatures. So a skill never lands on the caster's own side and always reaches its enemies.

    // Reused, like burstTargets: the people one tick reaches.
    static readonly List<ICombatUnit> burstPeople = new List<ICombatUnit>(8);

    // Reused by the re-aim and landing pickers, which can run while a tick is still walking burstPeople/burstTargets.
    static readonly List<ICombatUnit> pickPeople = new List<ICombatUnit>(8);
    static readonly List<MonsterAI3D> pickCreatures = new List<MonsterAI3D>(16);

    // Reused: the people filed in the caster's map, read by one gather at a time.
    static readonly List<ICombatUnit> peopleInMap = new List<ICombatUnit>(16);

    // Every living enemy of `caster` within the squared radius of `point` (planar): people into `people`, creatures into
    // `creatures`. Only from the caster's own map, read from the by-map filing of each — so a gather walks one map's
    // units, never every unit the server holds in every map. A caster whose map is not known reads everybody.
    static void GatherOpponents(Vector3 point, float radiusSqr, ICombatUnit caster,
                                List<ICombatUnit> people, List<MonsterAI3D> creatures)
    {
        people.Clear();
        creatures.Clear();
        var casterSide = Sides.Of(caster);
        if (casterSide == Side.None) return;
        string map = caster != null ? caster.Map : null;

        if (Sides.Hostile(casterSide, Side.Players))
        {
            peopleInMap.Clear();
            PlayerUnits.CollectIn(map, peopleInMap);
            var all = peopleInMap;
            for (int i = 0; i < all.Count; i++)
            {
                var person = all[i];
                if (PlayerUnits.Gone(person) || person.IsDead) continue;
                if (!string.IsNullOrEmpty(map) && person.Map != map) continue;
                var body = person.Body;
                if (body == null) continue;
                Vector3 d = body.position - point; d.y = 0f;
                if (d.sqrMagnitude > radiusSqr) continue;
                people.Add(person);
            }
            peopleInMap.Clear();
        }

        var list = string.IsNullOrEmpty(map) ? MonsterAI3D.Active : MonsterAI3D.ActiveIn(map);
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null || ai.Health == null || ai.Health.IsDead) continue;
            if (!Sides.Hostile(casterSide, ai.Health.side)) continue;
            Vector3 d = ai.transform.position - point; d.y = 0f;
            if (d.sqrMagnitude > radiusSqr) continue;
            creatures.Add(ai);
        }
    }

    /// <summary>The person whose body is <paramref name="t"/> (or holds it), or null when it is nobody's.</summary>
    static ICombatUnit PersonWithBody(Transform t)
    {
        if (t == null) return null;
        var all = PlayerUnits.Present;
        for (int i = 0; i < all.Count; i++)
        {
            var person = all[i];
            if (PlayerUnits.Gone(person)) continue;
            var body = person.Body;
            if (body != null && (body == t || t.IsChildOf(body))) return person;
        }
        return null;
    }

    /// <summary>The unit standing on a picked transform — a creature by its health, else a person by their body — or
    /// null. What a bouncing skill tick starts its chain from.</summary>
    public static ICombatUnit UnitStandingOn(Transform t) => t != null ? UnitOn(t) : null;

    // The unit standing on a picked transform: a creature by its health, else a person by their body.
    static ICombatUnit UnitOn(Transform t)
    {
        var creature = t.GetComponentInParent<Health>();
        if (creature != null) return creature;
        return PersonWithBody(t);
    }

    /// <summary>A living unit of <paramref name="caster"/>'s OTHER SIDE picked at random within
    /// <paramref name="radius"/> of <paramref name="point"/> — people for a creature, creatures for a person — or null
    /// when there is nobody. What a re-aiming run walks to next.</summary>
    public static Transform RandomOpponentNear(Vector3 point, float radius, ICombatUnit caster)
    {
        float r2 = Mathf.Max(0.01f, radius); r2 *= r2;
        GatherOpponents(point, r2, caster, pickPeople, pickCreatures);
        int count = pickPeople.Count + pickCreatures.Count;
        Transform pick = null;
        if (count > 0)
        {
            int i = CombatRng.Range(0, count);
            pick = i < pickPeople.Count ? pickPeople[i].Body : pickCreatures[i - pickPeople.Count].transform;
        }
        pickPeople.Clear();
        pickCreatures.Clear();
        return pick;
    }

    // The nearest living unit of the caster's other side within `radius` of `point` — what a relocation aimed at a
    // bare spot lands beside.
    static bool OpponentAt(Vector3 point, float radius, ICombatUnit caster, out Vector3 pos, out ICombatUnit hit)
    {
        pos = point; hit = null;
        GatherOpponents(point, radius * radius, caster, pickPeople, pickCreatures);
        float best = float.MaxValue;
        for (int i = 0; i < pickPeople.Count; i++)
        {
            Vector3 at = pickPeople[i].Body.position;
            Vector3 d = at - point; d.y = 0f;
            if (d.sqrMagnitude >= best) continue;
            best = d.sqrMagnitude; pos = at; hit = pickPeople[i];
        }
        for (int i = 0; i < pickCreatures.Count; i++)
        {
            Vector3 at = pickCreatures[i].transform.position;
            Vector3 d = at - point; d.y = 0f;
            if (d.sqrMagnitude >= best) continue;
            best = d.sqrMagnitude; pos = at; hit = pickCreatures[i].Health;
        }
        pickPeople.Clear();
        pickCreatures.Clear();
        return hit != null;
    }

    // ---- caster (pass 1) ----------------------------------------------------
    // Every CASTER-side effect in the list, applied ONCE to the acting caster. No hit loop: a caster effect isn't a
    // strike, so a multi-hit flurry must not dash you N times (target effects own hitCount, see HitLoops).
    // The longest glide the caster pass just sent the caster on, in seconds — set by the Dash case of ApplyOne and read
    // back by ApplyCasterEffects. One value for one caster pass, held the same way the lifts above are.
    static float casterGlideSeconds;
    // Where that glide was sent, and the longest it may take before a strike riding it is given up (DashLandings).
    static Vector3 casterGlideDest;
    static float casterGlideGiveUp;

    // Returns how long the longest glide it started on the caster lasts, 0 when it started none — with where it was
    // sent and the longest it may take.
    static float ApplyCasterEffects(List<SkillEffect> effects, Vector3 point, Transform aimUnit,
                                    string srcSymbol, string srcName, string srcId, ICombatUnit ch, in StackVars vars,
                                    out Vector3 glideDest, out float glideGiveUp)
    {
        glideDest = point; glideGiveUp = 0f;
        if (ch == null || ch.IsDead) return 0f;
        float outer = casterGlideSeconds, outerGiveUp = casterGlideGiveUp;
        Vector3 outerDest = casterGlideDest;
        casterGlideSeconds = 0f; casterGlideGiveUp = 0f; casterGlideDest = point;
        try
        {
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (SkillEffect.LandsOnCaster(e))
                    ApplyOne(e, ch, 0, ch, point, aimUnit, srcSymbol, srcName, srcId, vars);
            }
            glideDest = casterGlideDest; glideGiveUp = casterGlideGiveUp;
            return casterGlideSeconds;
        }
        finally { casterGlideSeconds = outer; casterGlideGiveUp = outerGiveUp; casterGlideDest = outerDest; }
    }

    /// <summary>True when <paramref name="effects"/> carries an all-in cost on the caster side — the tick that pays
    /// them for its whole cast.</summary>
    public static bool HasAllInCost(List<SkillEffect> effects)
    {
        if (effects == null) return false;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (e != null && SkillEffect.IsAllInCost(e.kind) && SkillEffect.LandsOnCaster(e)) return true;
        }
        return false;
    }

    /// <summary>Pay the all-in costs in <paramref name="effects"/> and return what they took, MP and HP together. The
    /// skill runner calls it ONCE per cast and hands the amount to every tick (<see cref="StackVars.GivenUp"/>). Only
    /// the caster-side ones count (the composer never authors one anywhere else, and the skill-data check fails one
    /// that is). Only the server pays: health and mana are the server's, and a strike that reads the amount lands only
    /// there.</summary>
    public static int PayAllInCosts(List<SkillEffect> effects, ICombatUnit ch)
    {
        if (ch == null || ch.IsDead || !GameServer.IsServer) return 0;
        int givenUp = 0;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (e == null || !SkillEffect.LandsOnCaster(e)) continue;
            if (e.kind == SkillEffectKind.SpendAllMana)
            {
                int mp = ch.CurrentMP;
                if (mp > 0 && ch.SpendMana(mp)) givenUp += mp;
            }
            else if (e.kind == SkillEffectKind.DropHealthToOne)
                givenUp += ch.GiveUpHealthDownToOne();
        }
        return givenUp;
    }

    // How wide a body a random landing spot must fit: the one routing width people walk by.
    public static float TripRadius => CharacterSpec.Active != null ? CharacterSpec.Active.routingRadius : 0.35f;

    // Back to where this unit returns to: a person to their save point — OWED to their shell's next frame, because an
    // item that ran this is spent after it fires, and a trip to another server saves them the moment it starts — a
    // creature to the home it spawned at, and anything with neither nowhere. The trip remembers the item that ran it,
    // so a trip that does not happen gives that item back.
    static void ReturnHome(ICombatUnit unit, string skillId)
    {
        if (unit is Character person)
        {
            var shell = GameNetwork.ShellFor(person);
            if (shell != null) shell.OweTripToSavePoint(ItemRunning(skillId));
            return;
        }
        if (unit is Health creature && creature.Brain != null) creature.TeleportTo(creature.Brain.Home);
    }

    // The item whose use runs `skillId`, or null — an item names its skill, so this is the one way back. Asked once per
    // wing used: a walk over every item the game has (under 200).
    static Item ItemRunning(string skillId)
    {
        if (string.IsNullOrEmpty(skillId)) return null;
        var all = ItemCatalog.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && all[i].useSkillId == skillId) return all[i];
        return null;
    }

    // A RANDOM LANDING FOUND BEFORE AN ITEM WAS SPENT, kept for the effect that uses it — so the spot the check found is
    // the spot the unit lands on, rolled once. Filed with the map it was found in: a unit that changed map in between
    // rolls again. At most one entry per unit, removed when used or when the item's skill did not run.
    static readonly Dictionary<ICombatUnit, (string map, Vector3 spot)> landingFound =
        new Dictionary<ICombatUnit, (string map, Vector3 spot)>();

    /// <summary>Forget a landing found for <paramref name="unit"/> that will not be used (its item's skill did not run).</summary>
    public static void ForgetLanding(ICombatUnit unit) { if (unit != null) landingFound.Remove(unit); }

    // Where a random landing puts `unit`: the spot found for it before its item was spent, or one rolled now.
    static bool LandingFor(ICombatUnit unit, Transform body, out Vector3 spot)
    {
        if (landingFound.TryGetValue(unit, out var held))
        {
            landingFound.Remove(unit);
            if (held.map == SpawnIds.MapOf(unit)) { spot = held.spot; return true; }
        }
        return Pathfinder3D.RandomSpotInArea(body.position, TripRadius, out spot);
    }

    /// <summary>Why <paramref name="def"/>'s effects could not happen to <paramref name="unit"/> right now, or null when
    /// they can. Asked before an ITEM runs its skill, so a wing that has nowhere to go is not spent: today only a random
    /// landing spot can be missing. Server only, once per use.</summary>
    public static string WhyItCannotLand(SkillDef def, ICombatUnit unit)
    {
        if (def == null || def.bindings == null || unit == null || unit.Body == null) return null;
        for (int b = 0; b < def.bindings.Count; b++)
        {
            var fx = def.bindings[b]?.effects;
            if (fx == null) continue;
            for (int i = 0; i < fx.Count; i++)
            {
                if (fx[i] == null || fx[i].kind != SkillEffectKind.TeleportToRandomSpot) continue;
                if (!Pathfinder3D.RandomSpotInArea(unit.Body.position, TripRadius, out var spot))
                {
                    landingFound.Remove(unit);
                    return "There is nowhere to land here.";
                }
                // THE SPOT IS KEPT FOR THE EFFECT, so it is not rolled a second time — a second roll could fail after the
                // wing has been spent.
                landingFound[unit] = (SpawnIds.MapOf(unit), spot);
                return null;
            }
        }
        return null;
    }

    // Take up to `most` MP from `unit` and hand exactly what was taken to the caster. Never from the caster itself — a
    // drain that lands on yourself would move your own MP from one pocket to the same pocket.
    static void DrainMana(ICombatUnit unit, ICombatUnit caster, int most)
    {
        if (most <= 0 || unit == null || unit == caster || unit.IsDead || !GameServer.IsServer) return;
        int taken = Mathf.Min(most, unit.CurrentMP);
        if (taken <= 0 || !unit.SpendMana(taken)) return;
        if (caster != null && !caster.IsDead) caster.GainMana(taken);
    }

    /// <summary>THE apply switch: ONE effect onto ONE unit — the player, a mob, anything that is an
    /// <see cref="ICombatUnit"/>. Every effect kind is implemented here exactly once, so every kind is available on
    /// every entity by construction.
    ///
    /// <para><b>This replaced three near-identical switches</b> (caster / mob target / player target). That split was
    /// the real reason a guard, shield, regen, parry, leech, reflect or stat buff could only ever land on the caster:
    /// not a rule anyone chose, just a case missing from two of the three copies. With one switch there is nowhere for
    /// a kind to be "forgotten" on one side of the fight.</para>
    ///
    /// <para><paramref name="ai"/> is the mob brain when the unit IS a mob (callers already hold it, so the mob-only
    /// sinks below cost no lookup) and null for the player. <paramref name="caster"/> is who is ACTING — every
    /// affinity/duration scale reads their ring investment, even when the effect lands on someone else, because the
    /// strength of a buff you place on an ally is yours to earn. <paramref name="relocatePoint"/> is where a Dash /
    /// Teleport sends whoever it moves.</para></summary>
    static void ApplyOne(SkillEffect e, ICombatUnit unit, int hit,
                         ICombatUnit caster,
                         Vector3 relocatePoint, Transform aimUnit,
                         string srcSymbol, string srcName, string srcId, in StackVars vars)
    {
        if (e == null || unit == null) return;
        var states = unit.States;
        if (states == null) return;
        if (e.onlyIfMarked != MarkKind.None && GateCount(unit, e.onlyIfMarked) <= 0) return;
        // The unit's BODY in the scene. Null only in the window before the player's avatar has spawned — the effects
        // that need a body (ailments, cleanse, relocation) sit on it, so they simply don't land yet.
        var body = unit.Body;

        // A durational effect tags its state entry with the SOURCE skill's icon + name so the buff tray / nameplate can
        // show it counting down. Set BEFORE the gameplay call (the store's Apply reads the latch), cleared AFTER.
        // The "what it does" line is built only for the PLAYER: a mob nameplate has no hover card, so building the
        // string for every mob of an AoE would be a per-hit allocation nobody reads.
        // Resolved ONCE here, and the ONLY duration this apply uses — the tray tag and the gameplay call now read the
        // same number, so a window the pile paid for can never land while the tray thinks the effect has no duration.
        // Asking the raw typed value was the bug: a number must resolve itself before anything interrogates it.
        float seconds = ScaledSeconds(e, caster, vars);
        bool tagDisplay = seconds > 0f;
        // A speed-earned count is deliberately UNCAPPED: the authored ceiling describes a pile the designer sized by
        // hand, and clamping an earned count to it would silently delete the charges a fast caster just paid for.
        if (tagDisplay) states.SetApplyContext(srcSymbol, srcName,
                                               unit is Character ? SkillTooltip.DescribeEffect(e) : null,
                                               e.stacking, StacksOn(e, unit, caster, vars),
                                               e.stacksFromAttackSpeed ? 0 : Mathf.Max(1, e.stackLimit.ResolveInt(vars)), srcId);
        switch (e.kind)
        {
            case SkillEffectKind.Damage:
                if (hit < EffectiveHits(e, caster, vars)) LandStrike(e, unit, caster, vars, overkill: false);
                break;
            case SkillEffectKind.Execute:
                if (Fires(e, hit, caster, vars)) ApplyExecute(e, unit, vars, caster);
                break;
            case SkillEffectKind.Heal:
                // Same one formula as the Damage side, so a lifesteal read off a pile can't drift from the hit that
                // read the same pile — one cast, one snapshot, two effects.
                // The pile-or-authored choice now lives in ScaledAmount, the same place every other flat number asks it.
                if (hit == 0)
                {
                    // HEALTH GIVEN BACK TO SOMEBODY ELSE is credited to the healer on whichever creature took it
                    // (KillLedger) — what was really restored, never what the skill offered.
                    var healed = unit as Character;
                    int hpBefore = healed != null ? healed.CurrentHP : 0;
                    int hpMax = healed != null ? healed.MaxHP : 0;
                    int anyBefore = unit.CurrentHP;
                    unit.Heal(RolledHeal(e, caster, vars, unit.MaxHP));
                    var healer = healed != null ? KillLedger.PersonBehind(caster) : null;
                    if (healer != null && !ReferenceEquals(healer, healed))
                        healed.Hurts.Credit(healer, healed.CurrentHP - hpBefore, hpMax - hpBefore);
                    if (unit.CurrentHP > anyBefore) HealDealt(caster, unit);
                }
                break;
            case SkillEffectKind.Status:
                if (body != null && Fires(e, hit, caster, vars)) Status.Apply(body.gameObject, e.ailment, seconds, OutputMagnitude(e, caster, vars), caster);
                break;
            // THE CASTER'S SUMMONS, topped back up. Who and how many is the caster's own answer, so this only asks.
            case SkillEffectKind.Summon:
                if (hit == 0 && body != null)
                {
                    var summons = body.GetComponent<CreatureSummons>();   // perf-ok: once per summon cast
                    if (summons != null) summons.SummonNow();
                }
                break;
            case SkillEffectKind.Cleanse:
                if (body != null && Fires(e, hit, caster, vars))
                {
                    var sc = Status.Of(body.gameObject);
                    if (sc != null) sc.Cleanse(Mathf.Max(1, StackedAmount(e, vars)));
                }
                break;
            // Both modifiers edit a pile that already exists, so they run ONCE per landing (hit 0) like every other
            // non-proc effect, and they name their own target — the tick's aim decides nothing here.
            case SkillEffectKind.ExtendDuration:
                if (hit == 0 && ModifierIsAuthored(e))
                    states.ExtendFrom(e.modifierSkillId, e.modifierStateId, ScaledModifierSeconds(e, caster, vars));
                break;
            case SkillEffectKind.AddStacks:
                if (hit == 0 && ModifierIsAuthored(e)) AddStacksToOtherSkill(e, states, caster, vars);
                break;
            // EVERY timed sub-stat change, buff and debuff alike, lands here — move speed, both regens, leech,
            // reflect, evasion, the lot. The amount is OUTPUT, so it takes both scalings: ring investment and the
            // caster's practice with this skill. A debuff is authored as a POSITIVE amount and negated
            // here, so nobody has to type a minus sign to make something worse.
            case SkillEffectKind.SubStatBuff:
            case SkillEffectKind.SubStatDebuff:
                if (Fires(e, hit, caster, vars))
                {
                    float amount = Mathf.Max(0f, OutputMagnitude(e, caster, vars));
                    if (e.kind == SkillEffectKind.SubStatDebuff) amount = -amount;
                    bool started = states.ApplySubStat(e.subStat, amount, seconds);
                    // A slow, haste or blind STARTING plays its cue on whoever it landed on, player or creature alike.
                    if (body != null && VfxCue.SubStatStartCue(e.subStat, started, amount, out var startCue))
                        VfxCue.Fire(startCue, body);
                }
                break;
            case SkillEffectKind.Shield:
                if (Fires(e, hit, caster, vars)) states.BeginShield(ScaledAmount(e, caster, vars), seconds);
                break;
            case SkillEffectKind.Parry:
                if (Fires(e, hit, caster, vars)) states.BeginParry(seconds);   // charges ride the apply context (stacks-per-apply)
                break;
            case SkillEffectKind.Brace:
                // Only the window is authored; the pile fills itself from whatever hits arrive (UnitMitigation.Deferred).
                if (Fires(e, hit, caster, vars)) states.BeginBrace(seconds);
                break;
            case SkillEffectKind.BoostOutgoingDamage:
                // Charges ride the apply context like Parry's; each carries the authored bonus, scaled by ring and by
                // practice for the same reason a stat buff's is — what one charge is worth is this effect's OUTPUT.
                if (Fires(e, hit, caster, vars)) states.BeginOutgoingBoost(seconds, OutputMagnitude(e, caster, vars));
                break;
            case SkillEffectKind.Charges:
                if (Fires(e, hit, caster, vars)) states.BeginCharges(seconds);   // charges ride the apply context
                break;
            case SkillEffectKind.BoostBasicAttackDamage:
                // The any-strike boost's twin, read the same way — only who may spend a charge differs.
                if (Fires(e, hit, caster, vars)) states.BeginBasicAttackBoost(seconds, OutputMagnitude(e, caster, vars));
                break;
            case SkillEffectKind.SureHit:
                if (Fires(e, hit, caster, vars)) states.BeginSureHit(seconds);   // charges ride the apply context
                break;
            case SkillEffectKind.ResetCooldown:
                // A modifier: once per landing, on the named skill of the unit it lands on, then it is over.
                if (hit == 0 && GameServer.IsServer)
                {
                    if (e.resetsLastCombination) ResetLastCombination(unit);
                    else if (!string.IsNullOrEmpty(e.modifierSkillId)) unit.Skills?.RunnerById(e.modifierSkillId)?.ResetCooldown();
                }
                break;
            case SkillEffectKind.ImmuneToDisables:
                // No magnitude: being stopped is binary. The whole knob is how long it lasts.
                if (Fires(e, hit, caster, vars)) states.BeginDisableImmunity(seconds);
                break;
            case SkillEffectKind.ElementConverter:
                // No magnitude and no stacking: the whole knob is which element, on which slot, for how long.
                if (Fires(e, hit, caster, vars))
                    states.BeginElement(SkillEffect.ConverterPile(e.converts), e.element, seconds);
                break;
            case SkillEffectKind.ReduceOutgoingDamage:
                // The boost's mirror, charges and all — authored as the CUT, and scaled the same two ways, so the
                // pair can never drift into one growing with investment while the other stays still.
                if (Fires(e, hit, caster, vars)) states.BeginOutgoingPenalty(seconds, OutputMagnitude(e, caster, vars));
                break;
            case SkillEffectKind.Taunt:
                // Force this unit onto the caster. Both sides implement it (a mob locks its chase, a player's
                // the auto-pilot AI can't retarget away), so a taunt is authored once and works on whoever it lands on.
                if (Fires(e, hit, caster, vars)) unit.ForceAggro(caster, seconds);
                break;
            // Paid once per cast by the skill runner (PayAllInCosts), which is what lets every strike read what they
            // took. Nothing to do here.
            case SkillEffectKind.SpendAllMana:
            case SkillEffectKind.DropHealthToOne:
                break;
            case SkillEffectKind.DrainMana:
                if (Fires(e, hit, caster, vars)) DrainMana(unit, caster, ScaledAmount(e, caster, vars));
                break;
            // A WING'S TRIP. Once per landing, never a chance, and only on the server — where anybody stands is its to
            // decide. A dead unit goes nowhere.
            case SkillEffectKind.ReturnToSavePoint:
                if (hit == 0 && GameServer.IsServer && !unit.IsDead) ReturnHome(unit, srcId);
                break;
            case SkillEffectKind.TeleportToRandomSpot:
                if (hit == 0 && GameServer.IsServer && !unit.IsDead && body != null)
                {
                    if (LandingFor(unit, body, out var spot))
                    {
                        if (unit is Character person) person.TripTo(spot);
                        else unit.TeleportTo(spot);
                        // AUTO-COMBAT STAYS ON across a Fly Wing, and the spot its Radius hunt keeps to moves with them.
                        if (unit is Character landed) GameNetwork.ShellFor(landed)?.AutoCombatLandedElsewhere();
                    }
                    else GameLog.Warn(LogCat.Server, SpawnIds.Label(unit) + " found nowhere to land on a random spot of '"
                                      + SpawnIds.MapOf(unit) + "'");
                }
                break;
            // ---- the Staff -----------------------------------------------------------------------------------------
            case SkillEffectKind.AddOrb:
                // An orb press while one of the caster's own runs is still going is refused, so the stats a run was
                // released with are the stats it ends with.
                if (hit == 0 && GameServer.IsServer && e.orb != OrbElement.None && !RunGoing(unit))
                {
                    var orbSkill = SkillCatalog.Get(OrbMath.SkillIdOf(e.orb));
                    // The orb's stat is its main number, raised like any other by its own level and its trademark.
                    states.AddOrb(e.orb, Mathf.Max(0f, OutputMagnitude(e, caster, vars)),
                                  orbSkill != null ? orbSkill.Symbol : "", orbSkill != null ? orbSkill.displayName : "");
                }
                break;
            case SkillEffectKind.Chill:
                if (GameServer.IsServer && Fires(e, hit, caster, vars)
                    && states.AddChill(StacksOn(e, unit, caster, vars), seconds, e.stackLimit.ResolveInt(vars),
                                       OutputMagnitude(e, caster, vars))
                    && body != null)
                    Status.Apply(body.gameObject, Ailment.Freeze, MarkMath.FreezeSeconds, 0f, caster);
                break;
            case SkillEffectKind.Static:
                if (GameServer.IsServer && Fires(e, hit, caster, vars))
                    states.AddStatic(StacksOn(e, unit, caster, vars), seconds, OutputMagnitude(e, caster, vars));
                break;
            case SkillEffectKind.CopyMark:
                if (hit == 0 && GameServer.IsServer) CopyMark(e, unit, caster, vars);
                break;
            // Lengthens by the effect's own seconds as authored — a level never grows a time, only how much stronger.
            case SkillEffectKind.ExtendMarks:
                if (hit == 0 && GameServer.IsServer)
                    states.ExtendMark(e.markKind, e.seconds.Resolve(vars), OutputMagnitude(e, caster, vars));
                break;
            case SkillEffectKind.Knockback:
                // Shove AWAY from the caster (magnitude = speed).
                if (hit == 0) unit.Shove(ShoveOrigin(unit, caster), ShoveSpeed(e, unit, vars),
                                         StackedShoveSeconds(e, vars), StackedShoveSeconds(e, vars));
                break;
            case SkillEffectKind.Pull:
                // Drag TOWARD the caster: shove away from a point mirrored through the target = toward the caster.
                if (hit == 0 && body != null)
                    unit.Shove(2f * body.position - ShoveOrigin(unit, caster), ShoveSpeed(e, unit, vars),
                               StackedShoveSeconds(e, vars), StackedShoveSeconds(e, vars));
                break;
            // `unit` is whoever is being moved — the caster for a Caster-side effect, a mob for a Target-side one.
            // Both relocations share this body: they differ only in HOW the mover travels, never in where it lands.
            //
            // NOTHING HERE DRAWS. The afterimage and the see-through fade are pictures, and this runs on the server,
            // which has no screen. The move carries WHICH effect caused it (RelocationLook), and every screen that
            // draws the move — the mover's own and everybody watching — reads the look out of its own copy of the skill.
            case SkillEffectKind.Teleport:
            case SkillEffectKind.Dash:
            {
                if (hit != 0) break;
                var look = RelocationLook.Of(srcId, e, vars);
                // A MOVE THAT STRIKES NOBODY LANDS WHERE IT WAS AIMED. Landing beside the nearest enemy at striking
                // distance is for a gap-closer; a getaway dash snapped onto a creature standing near the clicked spot
                // and flung its caster round to that creature's far side.
                var relocSkill = SkillCatalog.Get(srcId);
                bool exact = relocSkill != null && relocSkill.IsGetawayDash && aimUnit == null;
                // EVERY RELOCATION LANDS ON THE GROUND. The aimed spot's height is the player's word, and a landing
                // corner beside a creature carries the creature's height — on a slope either can sit under the
                // surface, and a body set down there is sealed in the ground with no way to walk out.
                var dest = exact ? relocatePoint
                                 : RelocationDest(relocatePoint, aimUnit, unit, caster, e.starCorners.ResolveInt(vars));
                if (GroundSnap.SurfaceNearestY(dest, out float landY)) dest.y = landY;
                if (e.kind == SkillEffectKind.Teleport)
                    unit.TeleportTo(dest, look);
                else
                {
                    // Face what the hop is AIMED AT for the whole trip, not the direction it flies. A relocation lands
                    // BESIDE its target (a star corner), so facing the travel line leaves the caster swinging at empty
                    // air on arrival — and nothing turns it back afterwards. No aimed unit = face travel, as before.
                    float speedMult = e.dashSpeedMult.Resolve(vars);
                    float easeSeconds = Mathf.Max(0.05f, e.dashEaseSeconds.Resolve(vars));
                    float trip = unit.GlideTo(dest, speedMult, e.dashEaseIn, e.dashEaseOut, easeSeconds,
                                              aimUnit != null ? aimUnit.position : (Vector3?)null, look);
                    if (unit == caster && trip >= casterGlideSeconds)
                    {
                        casterGlideSeconds = trip;
                        casterGlideDest = dest;
                        // THE LONGEST THIS DASH MAY TAKE: its skill's whole cast range at the same speed, never less
                        // than this trip. A caster still short of where it was sent by then was stopped on the way.
                        var skill = SkillCatalog.Get(srcId);
                        float range = skill != null ? skill.castRange : 0f;
                        casterGlideGiveUp = Mathf.Max(trip, range > 0f
                            ? unit.GlideSeconds(range, speedMult, e.dashEaseIn, e.dashEaseOut, easeSeconds) : 0f);
                    }
                }
                break;
            }
        }
        if (tagDisplay) states.ClearApplyContext();
    }

    // Where a shove is measured FROM: the caster's body. Falls back to the target's own spot when there's no caster,
    // which makes the shove direction arbitrary rather than crashing — same fallback the mob-only version had.
    static Vector3 ShoveOrigin(ICombatUnit unit, ICombatUnit caster)
    {
        if (caster != null && caster.Body != null) return caster.Body.position;
        return unit.Body != null ? unit.Body.position : unit.DamagePosition;
    }

    // The binding's strike count = the largest Damage hitCount (min 1). Each iteration is the trigger point where
    // proc effects roll.
    static int HitLoops(List<SkillEffect> effects, ICombatUnit caster, in StackVars vars)
    {
        int loops = 1;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (e != null && e.kind == SkillEffectKind.Damage) loops = Mathf.Max(loops, EffectiveHits(e, caster, vars));
        }
        return loops;
    }

    // A Damage effect's strike count = authored hitCount scaled by its hit-count affinity (ring investment).
    // hits = round(hitCount × mult). The ONE source of truth for both the flurry length (HitLoops) and each Damage's
    // per-strike cap, so they can't disagree. AffinityMult is stable within a cast (ring investment doesn't change).
    // Public so the tooltip shows the SAME live count the runtime will land (no parallel formula) — it passes the local
    // player, which is exactly whose tooltip it is.
    public static int EffectiveHits(SkillEffect e, ICombatUnit caster = null, StackVars vars = default)
        => Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, e.hitCount.ResolveInt(vars)) * RingHits(e, caster)));

    /// <summary>Does this strike belong to a run that draws ONE RISING YELLOW TOTAL instead of a number per beat?
    /// Two things have always drawn one: a skill that asks to show a single total across its repeats, and a strike
    /// that lands more than once.</summary>
    public static bool ShowsRunTotal(SkillDef def, SkillEffect e, ICombatUnit caster, in StackVars vars)
        => (def != null && def.showRepeatsTotalDamage) || (e != null && EffectiveHits(e, caster, vars) > 1);

    /// <summary>Whether the strike being LANDED RIGHT NOW draws a rising total — <see cref="ShowsRunTotal"/>'s answer for
    /// it, held for exactly as long as the strike takes to land. A creature announces each blow from inside its own
    /// intake (<c>Health.Show</c>), which knows the blow but not the skill behind it, so the strike leaves its answer
    /// here on the way in. Put back on the way out, so a strike set off by this one answers for itself, and a blow with
    /// no skill behind it reads false.</summary>
    public static bool StrikeShowsTotal { get; private set; }

    // Holds StrikeShowsTotal for one strike and puts the outer value back when it ends. A struct in a using statement,
    // so the per-hit path allocates nothing.
    readonly struct StrikeTotalScope : System.IDisposable
    {
        readonly bool outer;
        public StrikeTotalScope(bool showsTotal) { outer = StrikeShowsTotal; StrikeShowsTotal = showsTotal; }
        public void Dispose() => StrikeShowsTotal = outer;
    }

    // Single hit (the common case): apply instantly to each mob, unchanged. Multi-hit (hitCount > 1): worked out ONCE
    // per mob (FlurryOnce) and shown as its hits, cascading on the watching machine with ONE rising yellow total, like RO.
    static void DispatchMobHits(List<SkillEffect> effects, List<MonsterAI3D> mobs,
                                ICombatUnit caster, Vector3 relocatePoint,
                                string srcSymbol = null, string srcName = null, string srcId = null,
                                StackVars vars = default)
    {
        if (mobs.Count == 0) return;
        if (HitLoops(effects, caster, vars) <= 1)
        {
            for (int i = 0; i < mobs.Count; i++)
            {
                // Already dead: the strike's numbers only — every other effect is skipped, exactly as in a flurry beat.
                var mh = mobs[i] != null ? mobs[i].Health : null;
                if (mh != null && mh.IsDead)
                    ApplyHitToMob(effects, mobs[i], 0, caster, relocatePoint, srcSymbol, srcName, srcId, vars);
                else
                    ApplyEffectsToMob(effects, mobs[i], relocatePoint, caster, srcSymbol, srcName, srcId, vars);
            }
            return;
        }
        // A MULTI-HIT STRIKE IS WORKED OUT ONCE PER TARGET, Ragnarok's way: one roll, one blow through defence, shown
        // as its hits (FlurryOnce). All of it in one message.
        int loops = HitLoops(effects, caster, vars);
        using (BlowBatch.Begin())
            for (int i = 0; i < mobs.Count; i++)
                if (mobs[i] != null && mobs[i].Health != null)
                    FlurryOnce(effects, mobs[i].Health, loops, caster, relocatePoint, srcSymbol, srcName, srcId, vars);
    }

    /// <summary>A multi-hit strike on ONE unit, worked out once: every Damage effect lands ONE blow worth its hit count
    /// (one roll, one pass through defence, a parry or a barrier), announced as that many equal pieces; then every other
    /// effect gets its chance once per hit, as before. A unit already dead takes the numbers and nothing else.</summary>
    static void FlurryOnce(List<SkillEffect> effects, ICombatUnit unit, int loops, ICombatUnit caster, Vector3 point,
                           string srcSymbol, string srcName, string srcId, in StackVars vars)
    {
        bool deadBefore = unit.IsDead;
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (e == null || e.kind != SkillEffectKind.Damage) continue;
            int hits = EffectiveHits(e, caster, vars);
            float prevScale = strikeScale;
            int prevSplit = BlowBatch.Split;
            strikeScale = hits;
            BlowBatch.Split = hits;
            try { LandStrike(e, unit, caster, vars, overkill: true); }
            finally { strikeScale = prevScale; BlowBatch.Split = prevSplit; }
        }
        if (deadBefore) return;
        for (int hit = 0; hit < loops; hit++)
            for (int i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                if (e == null || e.kind == SkillEffectKind.Damage || unit.IsDead) continue;
                ApplyToTarget(e, unit, hit, caster, point, srcSymbol, srcName, srcId, vars);
            }
    }

    // A single-hit strike on a mob that is already dead: the strike's numbers only (overkill-safe), every other effect
    // skipped.
    static int ApplyHitToMob(List<SkillEffect> effects, MonsterAI3D ai, int hit,
                             ICombatUnit caster, Vector3 relocatePoint,
                             string srcSymbol = null, string srcName = null, string srcId = null,
                             StackVars vars = default)
    {
        if (ai == null || ai.Health == null) return 0;
        return ApplyHitToUnit(effects, ai.Health, hit, caster, relocatePoint, srcSymbol, srcName, srcId, vars);
    }

    // One strike on ONE unit, creature or person, already dead: its numbers only (overkill-safe), every other effect skipped.
    static int ApplyHitToUnit(List<SkillEffect> effects, ICombatUnit h, int hit,
                              ICombatUnit caster, Vector3 relocatePoint,
                              string srcSymbol, string srcName, string srcId, StackVars vars)
    {
        int dealt = 0;
        if (hit == 0) ReadGateMarksIfAsked(effects, h);
        for (int i = 0; i < effects.Count; i++)
        {
            var e = effects[i];
            if (e == null) continue;
            if (e.kind == SkillEffectKind.Damage)
            {
                if (hit >= EffectiveHits(e, caster, vars)) continue;
                // Overkill-safe, so a multi-hit flurry keeps its numbers, hit sparks AND per-strike sound climbing past
                // the kill — the whole count runs to the end, RO-style.
                dealt += LandStrike(e, h, caster, vars, overkill: true);
            }
            // A target that is already dead takes the flurry's numbers and nothing else: every other effect is skipped.
            else if (!h.IsDead)
                ApplyToTarget(e, h, hit, caster, relocatePoint, srcSymbol, srcName, srcId, vars);   // procs / status / one-shots
        }
        return dealt;
    }

    // Does this effect happen on THIS hit? Every state and proc asks, once per hit of the binding's multi-hit Damage,
    // so a 3-hit strike offers a buff or an ailment three chances to land. A certain effect (chance 1, the default)
    // answers yes every time — which is why "guaranteed" needs no special case anywhere.
    //
    // A caster-side effect never sees a hit index above 0 (it is applied once per landing, before targets are gathered),
    // so this is one roll for it, not one per strike.
    //
    // A chance of 0 means the effect can never happen. It is a real authored value, not "unset" — tools/check-skill-data.py
    // reports it, because an effect nobody can ever see is a mistake, never a design.
    //
    // The odds have their OWN scaling channel, on every kind alike: invest in it and a gamble lands more often. It used
    // to borrow the output channel, which meant an ailment could scale how OFTEN it landed or how BIG it was but never
    // both, and which of the two you got depended on a number somewhere else in the effect.
    static bool Fires(SkillEffect e, int hitIndex, ICombatUnit caster, in StackVars vars)
    {
        float odds = e.chance.Resolve(vars);
        if (odds <= 0f) return false;
        if (odds >= 1f) return true;
        return CombatRng.Chance(Mathf.Clamp01(odds * RingChance(e, caster)));
    }

    // ---- mob ----------------------------------------------------------------
    /// <summary>Run the hit loop for one MOB: each hit lands the Damage strike + rolls the binding's procs.</summary>
    public static void ApplyEffectsToMob(List<SkillEffect> effects, MonsterAI3D ai, Vector3 relocatePoint,
                                         ICombatUnit caster = null,
                                         string srcSymbol = null, string srcName = null, string srcId = null, StackVars vars = default)
    {
        if (effects == null || ai == null || ai.Health == null) return;
        ApplyEffectsToUnit(effects, ai.Health, relocatePoint, caster, srcSymbol, srcName, srcId, vars);
    }

    // The whole hit loop on ONE unit, creature or person, landing instantly.
    static void ApplyEffectsToUnit(List<SkillEffect> effects, ICombatUnit h, Vector3 relocatePoint, ICombatUnit caster,
                                   string srcSymbol, string srcName, string srcId, StackVars vars)
    {
        int loops = HitLoops(effects, caster, vars);
        ReadGateMarksIfAsked(effects, h);
        for (int n = 0; n < loops; n++)
            for (int i = 0; i < effects.Count; i++)
                ApplyToTarget(effects[i], h, n, caster, relocatePoint, srcSymbol, srcName, srcId, vars);
    }

    // One TARGET-side effect onto a gathered unit. Caster-side effects already ran in pass 1 and must not land again —
    // the partition is checked HERE, once, for every target (mob or player), so the two passes can never both fire an
    // effect. Which side an effect is on is the AUTHOR's choice (SkillEffect.side); Auto keeps the kind's old side.
    // A relocation authored on the TARGET side sends whoever it caught to the cast's aimed point — the same point the
    // caster side reads, so one cast has one destination however the author pointed the effect. No aimed UNIT is given
    // here on purpose: the aimed unit IS the one being moved, and landing it beside itself would move it nowhere.
    static void ApplyToTarget(SkillEffect e, ICombatUnit unit, int hit,
                              ICombatUnit caster, Vector3 relocatePoint,
                              string srcSymbol, string srcName, string srcId, in StackVars vars)
    {
        if (e == null || SkillEffect.LandsOnCaster(e)) return;
        ApplyOne(e, unit, hit, caster, relocatePoint, null, srcSymbol, srcName, srcId, vars);
    }

    // Execute: a self-contained bonus hit scaled by the TARGET's MISSING HP (proc'd per hit by the caller). magnitude =
    // execute power: bonus = roll x magnitude x (1 - HP fraction), so it's tiny at full HP and huge near death.
    // One implementation for every unit — it used to be two copies (one reading a mob's HP, one the player's) that had
    // to be kept in step by hand.
    static void ApplyExecute(SkillEffect e, ICombatUnit unit, in StackVars vars, ICombatUnit caster)
    {
        float power = e.magnitude.Resolve(vars);
        if (unit.MaxHP <= 0 || power <= 0f) return;
        float missing = 1f - Mathf.Clamp01((float)unit.CurrentHP / unit.MaxHP);
        if (missing <= 0f) return;
        int roll = RollPower(e, vars, OutputMult(e, caster) * releaseScale,
                             out bool crit, caster, unit);
        int bonus = Mathf.RoundToInt(roll * power * missing);
        if (bonus <= 0) return;
        // Its own damage number, so its own charge — an execute is a second outgoing hit riding the first, not part of it.
        bonus = UnitOffense.SpendCharges(caster != null ? caster.States : null, bonus);
        var resolved = new DamageInfo(bonus, e.element, e.damageType == DamageType.Magic, crit,
                                      attacker: caster);
        // The same "does this draw a rising total" answer a strike holds while it lands — this is the other place a
        // skill's damage reaches a creature, and the two must announce alike.
        using (new StrikeTotalScope(ShowsRunTotal(SkillCatalog.Get(PassiveEvents.CurrentSkillId), e, caster, vars)))
            CombatAuthority.Apply(unit, resolved);
    }

    // The creature-cast twin of DispatchMobHits: one hit lands instantly on each person caught; a multi-hit strike
    // cascades over the same interval, carrying the same cause and the same snapshot to its later beats.
    static void DispatchPeopleHits(List<SkillEffect> effects, List<ICombatUnit> people,
                                   ICombatUnit caster, Vector3 relocatePoint,
                                   string srcSymbol, string srcName, string srcId, StackVars vars)
    {
        if (people.Count == 0) return;
        int loops = HitLoops(effects, caster, vars);
        if (loops <= 1)
        {
            for (int i = 0; i < people.Count; i++)
            {
                var person = people[i];
                if (person.IsDead) ApplyHitToUnit(effects, person, 0, caster, relocatePoint, srcSymbol, srcName, srcId, vars);
                else ApplyEffectsToUnit(effects, person, relocatePoint, caster, srcSymbol, srcName, srcId, vars);
            }
            return;
        }
        // Worked out once per person, exactly as a creature's flurry is (FlurryOnce).
        using (BlowBatch.Begin())
            for (int i = 0; i < people.Count; i++)
                if (people[i] != null)
                    FlurryOnce(effects, people[i], loops, caster, relocatePoint, srcSymbol, srcName, srcId, vars);
    }

    // ---- player as a TARGET (pass 2) ----------------------------------------
    /// <summary>Run the hit loop for the PLAYER as a gathered TARGET (heal / self-damage / an ailment on yourself).
    /// Caster effects are NOT here — they run once in <see cref="ApplyBurst"/>'s caster pass. <paramref name="caster"/>
    /// is the unit using the skill (its own self-cast target).</summary>
    public static void ApplyEffectsToPlayer(List<SkillEffect> effects, Vector3 relocatePoint,
                                            string srcSymbol = null, string srcName = null, string srcId = null, ICombatUnit caster = null,
                                            StackVars vars = default)
    {
        var ch = caster;
        if (effects == null || ch == null) return;
        int loops = HitLoops(effects, ch, vars);
        for (int n = 0; n < loops; n++)
            for (int i = 0; i < effects.Count; i++)
                ApplyToTarget(effects[i], ch, n, ch, relocatePoint, srcSymbol, srcName, srcId, vars);
    }

    /// <summary>ONE Damage strike onto one unit — the single place a skill's damage is rolled and applied, whether it
    /// lands instantly or as a beat of a multi-hit cascade (<paramref name="overkill"/> is the only difference between
    /// those two). Returns the damage dealt, so a rising combo total can accumulate it.
    ///
    /// <para>TWO ways a strike is paid for, and they differ only in WHICH roll sits underneath: a strike authored to
    /// RESOLVE AS A BASIC ATTACK is the caster's own swing, and everything else is the ordinary skill roll. Either way
    /// the effect's damage multiplier scales it — that number always multiplies, whatever it was built from.</para></summary>
    static int LandStrike(SkillEffect e, ICombatUnit unit, ICombatUnit caster,
                          in StackVars vars, bool overkill)
    {
        // ONLY THE SERVER LANDS A STRIKE. A player's machine plays the picture of a cast and none of what it does, so a
        // strike reaching this line anywhere else is refused before a single die is rolled.
        if (!GameServer.IsServer) return 0;

        // Whether this strike draws a rising total is the authored skill's answer, held while it lands so the creature
        // can carry it when it announces the blow (Health.Show). A blow on a person is announced at the damage door.
        int dealt = 0;
        // A STRIKE THAT IS ONLY A PAYOFF, with nothing to pay out (no mark, no base), lands no blow at all — a "1" for
        // nothing would read as a hit. Its cash-in still runs below: a burn is put out whatever the strike was worth.
        if (!PaysNothing(e, caster, unit, vars))
            using (new StrikeTotalScope(ShowsRunTotal(SkillCatalog.Get(PassiveEvents.CurrentSkillId), e, caster, vars)))
                dealt = Strike(e, unit, caster, vars, overkill, out _, out _);
        if (e.cashMark != MarkKind.None) CashIn(e, unit, caster, vars);
        return dealt;
    }

    static bool PaysNothing(SkillEffect e, ICombatUnit caster, ICombatUnit unit, in StackVars vars) =>
        e.cashMark != MarkKind.None && !e.damageMult.useStacks && StrikeMult(e, caster, unit, vars).value <= 0f;

    // THE MARK GOES, once the strike that read it has landed. Burn is put out with the damage it had left landing at once.
    static void CashIn(SkillEffect e, ICombatUnit unit, ICombatUnit caster, in StackVars vars)
    {
        var marks = unit != null ? unit.States : null;
        if (marks == null || e.keepsMark) return;
        switch (e.cashMark)
        {
            case MarkKind.Burn:
            {
                var burn = marks.Get(StateId.Burn);
                if (burn == null) break;
                int left = MarkMath.BurnLeft(burn.magnitude, burn.Remaining, StatusController.DotInterval);
                // The payoff is this effect's output, so it grows with its orb like any other part.
                int payoff = Mathf.RoundToInt(left * Mathf.Max(0f, e.burnPayoff.Resolve(vars)) * OutputMult(e, caster));
                marks.RemoveAll(StateId.Burn);
                if (payoff > 0 && !unit.IsDead) CombatAuthority.ApplyTrue(unit, payoff, TrueDamageCause.Extinguished, caster);
                break;
            }
            case MarkKind.Chill:  marks.RemoveAll(StateId.Chill); break;
            case MarkKind.Static: marks.RemoveAll(StateId.Static); break;
            case MarkKind.Stone:  marks.RemoveStone(); break;
        }
    }

    /// <summary>Roll and land ONE authored strike — on the server, the only machine that lands one.</summary>
    /// <param name="resolved">The blow exactly as it was applied, handed back whole.</param>
    public static int Strike(SkillEffect e, ICombatUnit unit, ICombatUnit caster,
                             in StackVars vars, bool overkill, out bool crit, out DamageInfo resolved)
    {
        crit = false;
        resolved = default;
        float prevCritBonus = StrikeCritBonus;
        StrikeCritBonus = StrikeMath.CritBonus(e.critChanceBonus.Resolve(vars), e.critChanceBonusVsSlowed.Resolve(vars),
                                               unit != null && unit.States != null ? unit.States.MoveMult : 1f);
        try { return StrikeBody(e, unit, caster, vars, overkill, out crit, out resolved); }
        finally { StrikeCritBonus = prevCritBonus; }
    }

    static int StrikeBody(SkillEffect e, ICombatUnit unit, ICombatUnit caster,
                          in StackVars vars, bool overkill, out bool crit, out DamageInfo resolved)
    {
        crit = false;
        resolved = default;
        // Ring investment and the combo ramp are both OUTER multipliers of the finished number, so they travel together
        // as one scale — the pile's contribution answers to them exactly as the caster's own damage does.
        float outer = OutputMult(e, caster) * releaseScale;

        // A FLURRY BEAT ON A TARGET THAT IS ALREADY DEAD is presentation only: the number it would have been and the hit
        // cue — no charge spent, no swing announced, no passive fed. Decided here, the one place every strike is rolled,
        // so the server's own flurry and a strike a client named both get it.
        if (overkill && unit.IsDead)
        {
            bool basic = e.resolveAsBasicAttack && caster != null;
            int shown;
            // A sniper's ordinary attack is a magic shot (BasicSwing), so the number it would have been is a magic roll.
            bool magicShot = basic && WeaponTypes.ShootsMagic(caster.HeldWeapon);
            if (magicShot)
            {
                int roll = caster.RollSkillDamage(DamageType.Magic, unit.CritShieldLuk, out crit);
                shown = Mathf.Max(1, Mathf.RoundToInt(StrikeMult(e, caster, unit, vars).Resolve(vars, Mathf.Max(0f, outer), roll)));
            }
            else if (basic)
            {
                crit = caster.RollBasicCrit(unit);
                shown = Mathf.Max(1, Mathf.RoundToInt(StrikeMult(e, caster, unit, vars).Resolve(vars, Mathf.Max(0f, outer),
                                                                          caster.RollBasicDamage(crit))));
            }
            else shown = RollPower(e, vars, outer, out crit, caster, unit);
            resolved = new DamageInfo(shown, basic ? caster.BasicElement : e.element,
                                      magicShot || (!basic && e.damageType == DamageType.Magic), crit,
                                      attackerHit: basic ? caster.BasicHit : -1, overkill: true, attacker: caster);
            return CombatAuthority.Apply(unit, resolved);
        }

        // The caster swings; the skill only says how hard. The swing's own roll becomes the basis the damage
        // multiplier is applied to, and the pile meets the result — so the one thing the checkbox changes is WHICH
        // roll is underneath, which is the whole point of it.
        if (e.resolveAsBasicAttack)
        {
            int swung = BasicSwing.Land(caster, unit, StrikeMult(e, caster, unit, vars), vars, outer, overkill,
                                        // This swing has no wind-up of its own, so it announces the intent here rather
                                        // than never — a passive that answers "about to swing" (the auto-lunge) rides a
                                        // skill's swings exactly as it rides an ordinary one.
                                        announceIntent: true, out resolved);
            // The swing's crit, not the skill's: this branch never reaches the roll below, so without this a strike
            // that resolves as a basic attack could never report a crit at all.
            crit = resolved.isCrit;
            return swung;
        }

        int power = RollPower(e, vars, outer, out crit, caster, unit);
        // One strike, one charge. (The basic-attack branch above doesn't come back through here: the swing spends its
        // own, so nothing is charged twice.)
        power = UnitOffense.SpendCharges(caster != null ? caster.States : null, power);
        // −1 HIT = always-lands skill strike (no accuracy roll); DamageInfo defaults attackerHit to −1.
        resolved = new DamageInfo(power, e.element, e.damageType == DamageType.Magic, crit,
                                  overkill: overkill, attacker: caster);
        return CombatAuthority.Apply(unit, resolved);
    }

    // Damage = (the caster's rolled hit × this effect's damage multiplier) met by the pile, all × ring × combo. The
    // roll is the BASIS the multiplier is applied to, which is what puts both sides of the operation in damage.
    // Crit-aware so a tick crits like any other skill, and the defender's LUK shields against it exactly as it shields
    // against a swing. No allocation.
    static int RollPower(SkillEffect e, in StackVars vars, float outer, out bool crit, ICombatUnit caster, ICombatUnit unit)
    {
        crit = false;
        if (caster == null) return 1;
        // Magic → the user's magic attack (unmissable, never a crit). Physical → its physical attack. Crit type → the
        // physical roll with a FORCED crit (its armour-pierce is the deferred combat-pass TODO).
        int roll = caster.RollSkillDamage(e.damageType, unit.CritShieldLuk, out crit);
        return Mathf.Max(1, Mathf.RoundToInt(StrikeMult(e, caster, unit, vars).Resolve(vars, outer, roll)));
    }

    /// <summary>THE damage multiplier one strike lands with: the authored multiplier, plus what it grows by — the MP or HP
    /// the caster gave up in this cast (<see cref="StackVars.GivenUp"/>) and the metres between the caster and the unit
    /// struck. Both growths are added to the multiplier's own number, before the pile meets it, so a strike that grows
    /// reads the pile exactly as one that does not. With both growth numbers at 0 it is the authored multiplier,
    /// untouched — which is every strike authored before they existed.</summary>
    static ScaledNumber StrikeMult(SkillEffect e, ICombatUnit caster, ICombatUnit unit, in StackVars vars)
    {
        var mult = e.damageMult;
        float perPoint = vars.GivenUp > 0 ? e.damageMultPerPointGivenUp.Resolve(vars) : 0f;
        float perMetre = e.damageMultPerMetre.Resolve(vars);
        float metres = perMetre != 0f && caster != null && caster.Body != null && unit != null && unit.Body != null
            ? StrikeMath.Metres(caster.Body.position, unit.Body.position) : 0f;
        mult.value += StrikeMath.Growth(perPoint, vars.GivenUp, perMetre, metres);
        float perAccuracy = e.damageMultPerAccuracyOver.Resolve(vars);
        if (perAccuracy != 0f && caster != null && unit != null)
            mult.value += perAccuracy * StrikeMath.AccuracyOverPoints(
                Combat.RawHitPoints(caster.BasicHit, unit.Flee, MonsterAI3D.AttackersOn(unit)));
        float perSpeed = e.damageMultPerSpeedBonus.Resolve(vars);
        if (perSpeed != 0f && caster != null && caster.States != null)
            mult.value += perSpeed * StrikeMath.SpeedBonusPoints(caster.States.MoveMult);
        // THE STAFF'S CASH-INS read the mark BEFORE the strike lands; it is removed after (CashIn).
        var marks = unit != null ? unit.States : null;
        float perMark = e.damageMultPerMark.Resolve(vars);
        if (perMark != 0f && marks != null) mult.value += perMark * marks.MarkCount(e.cashMark);
        float perBurnSecond = e.damageMultPerBurnSecond.Resolve(vars);
        if (perBurnSecond != 0f && marks != null)
        {
            var burn = marks.Get(StateId.Burn);
            if (burn != null) mult.value += MarkMath.BurnAgeGrowth(perBurnSecond, Time.time - burn.startTime);
        }
        float perMana = e.damageMultPerMaxMana.Resolve(vars);
        if (perMana != 0f && caster != null) mult.value += MarkMath.MaxManaGrowth(perMana, caster.MaxMP);
        return mult;
    }

    // ---- affinity (ring-investment scaling) ---------------------------------
    // The multiplier for ONE affinity channel = 1 + Σ(normalised weight × the caster's gated ring % in that stat).
    // Empty/absent list ⇒ 1 (no scaling). Zero-alloc: one pass over the small authored list, no LINQ / temp collections.
    // The tooltip passes the local player, whose ring investment it is describing.
    public static float AffinityMult(List<StatWeight> weights, ICombatUnit caster = null)
    {
        if (weights == null || weights.Count == 0) return 1f;
        var ch = caster;
        if (ch == null) return 1f;
        float sum = 0f, acc = 0f;
        for (int i = 0; i < weights.Count; i++)
        {
            float w = weights[i].weight;
            if (w <= 0f) continue;
            sum += w;
            acc += w * ch.RingBonus(weights[i].stat);
        }
        return sum > 0f ? 1f + acc / sum : 1f;
    }

    // The affinity list scales the effect's main output (heal amount, DoT damage, buff amount, slow amount); the
    // duration list scales its seconds. A Status PROC spends its affinity on the CHANCE (see Fires), so its magnitude
    // stays raw. Everything falls back to ×1 when no weights are authored.
    // A MODIFIER's two numbers are the same two channels every other effect has, just stored in their own fields
    // because they describe another skill's pile rather than this effect's own body: the seconds it adds are a
    // DURATION, the stacks it adds are its OUTPUT. Routed here so a modifier scales off the ring and off the cast's
    // pile exactly like everything else, instead of being the one kind whose numbers are frozen.
    static float ScaledModifierSeconds(SkillEffect e, ICombatUnit caster, in StackVars vars) =>
        Mathf.Max(0f, e.modifierSeconds.Resolve(vars, RingDuration(e, caster)));
    // HOW MANY stacks a modifier hands over is its main number, so it is sized like every other output — by the pile
    // (Perfect Form: a run of hits read as a count, times a third, floored, so three in a row adds one and six adds
    // two), by the ring, and by THIS skill's own practice.
    static int ScaledModifierStacks(SkillEffect e, ICombatUnit caster, in StackVars vars) =>
        e.modifierStacks.ResolveInt(vars, OutputMult(e, caster));

    // The pile-only form, for the numbers no multiplier may touch. Just ONE kind of number is left here: a shove's
    // SPEED, which is movement rather than power — a rare weapon must not throw people further. The stat buffs and
    // the outgoing charges used to sit here too and no longer do: they are output, so they scale like output.
    static float StackedMagnitude(SkillEffect e, in StackVars vars) =>
        e.magnitude.Resolve(vars);
    // The same, for a cleanse's COUNT — a whole number of ailments removed, which no fractional multiplier can move
    // by less than one, so it reads the pile alone.
    static int StackedAmount(SkillEffect e, in StackVars vars) =>
        e.amount.ResolveInt(vars);
    // A shove's travel time is its own field because it is MOVEMENT, not a state window — but it is still this
    // effect's duration, so the pile lifts it through the same channel every other duration uses.
    // A worn Anchored shortens the shove by slowing it, never by cutting its time: the stagger the shove carries still
    // lasts what the author set, only the distance shrinks.
    static float ShoveSpeed(SkillEffect e, ICombatUnit mover, in StackVars vars) =>
        ArmourWords.CutBy(StackedMagnitude(e, vars), mover.States != null ? mover.States.WornShoveCut : 0f);

    static float StackedShoveSeconds(SkillEffect e, in StackVars vars) =>
        Mathf.Max(0.01f, e.shoveSeconds.Resolve(vars));

    // The flat-amount sibling of OutputMagnitude (a heal's HP, a shield's pool) — same scalings, ring and practice both.
    // A cleanse's count does NOT come through here: it is a whole number of ailments removed, so it reads the pile
    // alone (StackedAmount) and no multiplier can move it by less than a whole one.
    static int ScaledAmount(SkillEffect e, ICombatUnit caster, in StackVars vars) =>
        e.amount.ResolveInt(vars, OutputMult(e, caster));
    /// <summary>One heal's HP: a roll from the amount up to its top, both resolved the same way. Only the server's
    /// heal lands (Character.Heal / Health.Heal drop it on a watched copy), so only the server's roll counts.</summary>
    // A HEAL THAT GAVE HEALTH BACK is the healer's moment (OnHealDealt) — only when a skill cast it, never when an item
    // ran it, so a potion is not a heal "you cast".
    static void HealDealt(ICombatUnit caster, ICombatUnit healed)
    {
        if (caster == null || healed == null) return;
        var by = SkillCatalog.Get(PassiveEvents.CurrentSkillId);
        if (by == null || by.ItemUseEntry != null) return;
        PassiveEvents.Fire(caster, SkillTrigger.OnHealDealt, null, healed.Body);
    }

    static int RolledHeal(SkillEffect e, ICombatUnit caster, in StackVars vars, int healedMaxHP)
    {
        if (e.healShareOfMaxHP)
        {
            // A share of the healed unit's own max HP, grown by the skill's level like every other output.
            float mult = OutputMult(e, caster);
            int lowShare = ShareOfMaxHP(e.amount.Resolve(vars, mult), healedMaxHP);
            if (!e.healAmountMaxSet) return lowShare;
            return HealInRange(lowShare, ShareOfMaxHP(e.healAmountMax.Resolve(vars, mult), healedMaxHP), CombatRng.Value01());
        }
        int low = ScaledAmount(e, caster, vars);
        if (!e.healAmountMaxSet) return low;
        int high = e.healAmountMax.ResolveInt(vars, OutputMult(e, caster));
        return HealInRange(low, high, CombatRng.Value01());
    }
    /// <summary>A whole number from <paramref name="low"/> to <paramref name="high"/>, both included, picked by a roll
    /// in [0, 1). A top below the bottom heals the bottom.</summary>
    public static int HealInRange(int low, int high, float roll01) =>
        high <= low ? low : low + Mathf.Min(high - low, Mathf.FloorToInt((high - low + 1) * roll01));
    /// <summary>HP that <paramref name="share"/> of <paramref name="maxHP"/> is (0.1 of 500 = 50), rounded, never negative.</summary>
    public static int ShareOfMaxHP(float share, int maxHP) => Mathf.Max(0, Mathf.RoundToInt(share * Mathf.Max(0, maxHP)));
    /// <summary>Feed ANOTHER skill's pile. What one of its charges is worth is read from THAT skill's own authored
    /// effect — the single definition of its charge — so a pile that is running and one that is not both end up with
    /// identical stacks, and a modifier never has to invent a number. A skill that grants nothing of that kind (or was
    /// deleted) simply gives nothing.</summary>
    // Modifier targets already reported, so a skill cast every few seconds says it once. Keyed by the named skill,
    // which is what the author has to go fix.
    static readonly HashSet<string> reportedModifiers = new HashSet<string>();

    /// <summary>SAY SO when a modifier never named WHICH charges it edits. Every value of that setting is a real one,
    /// so an unauthored modifier would silently edit the first thing in the list — the same silent-wrong-target failure
    /// the stack gate hit, and the reason both carry a "was this authored" marker. Refuses to act rather than act
    /// wrongly.</summary>
    static bool ModifierIsAuthored(SkillEffect e)
    {
        if (e.modifierStateIdSet && !string.IsNullOrEmpty(e.modifierSkillId)) return true;
        if (reportedModifiers.Add(e.modifierSkillId ?? ""))
            GameLog.Error(LogCat.Unwritten, "Modifier: A skill edits another skill's charges but never names which skill or which " +
                           "charges, so it does nothing. Open it in the Skill Composer, finish the modifier, and save.");
        return false;
    }

    static void AddStacksToOtherSkill(SkillEffect e, UnitStates states, ICombatUnit caster, in StackVars vars)
    {
        var granter = SkillCatalog.Get(e.modifierSkillId);
        SkillEffect grant = null;
        if (granter?.bindings != null)
            for (int b = 0; b < granter.bindings.Count && grant == null; b++)
            {
                var fx = granter.bindings[b]?.effects;
                if (fx == null) continue;
                for (int i = 0; i < fx.Count; i++)
                    if (fx[i] != null && fx[i].stacking
                        && SkillEffect.TryStateFor(fx[i], out var id) && id == e.modifierStateId)
                    { grant = fx[i]; break; }
            }
        if (grant == null) return;
        // What ONE added stack is worth is the GRANTER's business, so its numbers are read with no pile behind them:
        // this cast's stacks scale what THIS effect does (how many it adds, below), never what the other skill's
        // charges are each worth. Ring investment still applies — that is the granter's own scaling, not the cast's.
        states.AddStacksTo(e.modifierSkillId, e.modifierStateId, ScaledModifierStacks(e, caster, vars),
                           ScaledMagnitude(grant, caster, StackVars.None), ScaledSeconds(grant, caster, StackVars.None),
                           granter.Symbol, granter.displayName, null);
    }

    // The THREE numbers every effect kind is made of, each scaled the same two ways at the same one place: by ring
    // investment (what you built) and by the cast's pile (what you earned this fight). Every kind flows through these,
    // so neither scaling has to be taught to a single effect — and a new kind inherits both by existing.
    // An effect's OUTPUT: the authored base value met by the pile through the one shared formula, then scaled by ring
    // investment. The author's number is never ignored and never replaced — it is the base the pile builds on.
    static float ScaledMagnitude(SkillEffect e, ICombatUnit caster, in StackVars vars) =>
        e.magnitude.Resolve(vars, RingOutput(e, caster));

    // An effect's own OUTPUT multiplier: what the ring's affinity earns, PLUS what the caster's own practice with
    // this skill is worth. Added, never multiplied — so mastery is worth the same +30 POINTS to a new character as
    // to a maxed one, instead of paying out more the more ring somebody has already built. Points, not percent: at
    // a ring term of 1 those 30 points ARE +30% output, and at a term of 2 the same 30 points work out to +15%.
    static float OutputMult(SkillEffect e, ICombatUnit caster) =>
        OutputScale(e, caster, e.orb != OrbElement.None && e.kind != SkillEffectKind.AddOrb ? orbLift[(int)e.orb] : masteryLift)
        * hopScale * strikeScale;

    /// <summary>THE multiplier on an effect's main number: the ring's term where <see cref="EffectScaling"/> gives it
    /// a ring row, plus the practice lift where it says practice raises it — ADDED, never multiplied. Public so the
    /// tooltip draws the same number the fight lands, from the same rule, with its own lift passed in.</summary>
    public static float OutputScale(SkillEffect e, ICombatUnit caster, float practiceLift) =>
        RingOutput(e, caster) + (EffectScaling.Boostable(e) ? practiceLift : 0f);

    // THE RING'S TERM ON EACH NUMBER, asked of the one rule: no row in the composer means the ring does not touch it,
    // here or anywhere. Each is ×1 when the rule says no, which is what an empty list already gave — so routing
    // through the rule moved no number (checked against every saved skill on 2026-09-21).
    static float RingOutput(SkillEffect e, ICombatUnit caster) =>
        EffectScaling.OutputRingRow(e) != null ? AffinityMult(e.affinity, caster) : 1f;
    static float RingChance(SkillEffect e, ICombatUnit caster) =>
        EffectScaling.ChanceRingRow(e) != null ? AffinityMult(e.chanceAffinity, caster) : 1f;
    static float RingDuration(SkillEffect e, ICombatUnit caster) =>
        EffectScaling.DurationRingRow(e) != null ? AffinityMult(e.durationAffinity, caster) : 1f;
    static float RingHits(SkillEffect e, ICombatUnit caster) =>
        EffectScaling.HitsRingRow(e) != null ? AffinityMult(e.hitCountAffinity, caster) : 1f;

    // The practice-lifted form of ScaledMagnitude, for every number that IS output: an ailment's damage per tick, how
    // far a sub-stat buff or debuff moves, and what one outgoing-damage charge is worth.
    // Deliberately a separate function: ScaledMagnitude also reads ANOTHER skill's authored charge when a modifier
    // feeds its pile, and one skill's practice must never travel into a second skill's numbers.
    static float OutputMagnitude(SkillEffect e, ICombatUnit caster, in StackVars vars) =>
        e.magnitude.Resolve(vars, OutputMult(e, caster));
    static float ScaledSeconds(SkillEffect e, ICombatUnit caster, in StackVars vars) =>
        e.seconds.Resolve(vars, RingDuration(e, caster));

    /// <summary>How many stacks this apply grants. Normally the authored number; when the effect is driven by attack
    /// speed it is instead HOW MANY TIMES THE CASTER WOULD SWING inside this effect's own window — the window it is
    /// actually getting (<see cref="ScaledSeconds"/>, so ring duration investment lengthens the window and earns the
    /// extra stacks that go with it) divided by the caster's live seconds-per-swing.
    ///
    /// <para>Rounded UP, which is what keeps two effects in exact proportion: taking half of a window is always the
    /// same number as halving that window's own count, so a 0.5 share is exactly half at every attack speed with
    /// nothing to keep in sync by hand.</para>
    ///
    /// <para>Falls back to the authored count when there is no swing rate to read (the avatar hasn't spawned) rather
    /// than granting nothing — a missing body must not silently erase a skill's charges.</para></summary>
    static int ResolveStacks(SkillEffect e, ICombatUnit caster, in StackVars vars)
    {
        int authored = Mathf.Max(1, e.stacksPerApply.ResolveInt(vars));
        if (!e.stacksFromAttackSpeed) return authored;
        float swing = caster != null ? caster.SwingTime : 0f;
        if (swing <= 0f) return authored;
        return Mathf.Max(1, Mathf.CeilToInt(ScaledSeconds(e, caster, vars) / swing
                                            * Mathf.Max(0f, e.stackShare.Resolve(vars))));
    }
}
