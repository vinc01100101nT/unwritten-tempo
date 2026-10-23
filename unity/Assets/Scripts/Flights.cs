using System.Collections.Generic;
using Mirror;
using UnityEngine;

/// <summary>
/// EVERYTHING IN THE AIR, on the machine that owns the world — an arrow, a thrown shuriken, a skill strike bouncing
/// from one enemy to the next — and the ONE place each of them lands. A person's ranged attack, a creature's, and a
/// bouncing skill all come through here, so a blow that travels has one store, one rule for when it lands and one
/// picture, whoever threw it.
///
/// <para><b>Each hop is decided when the flight ARRIVES, never in advance.</b> Targets move, die and are reused while
/// a blade is in the air, so the store checks everything again at every arrival — the thrower is still here, still
/// in the same map, still the same life, still holding what it threw; the target is still alive, still the same life,
/// still in that map and still an enemy — and only then lands the hit and picks the next target, the nearest enemy it
/// has not hit yet within the jump range of the last.</para>
///
/// <para><b>It lands through the existing doors.</b> An ordinary attack lands as the thrower's own swing (a person
/// through <see cref="BasicSwing"/>, a creature through its own swing so its on-hit ailment and experience come with
/// it), scaled by the hop's share; a skill hop runs the skill's target effects through
/// <see cref="SkillEffectApplier.ApplyBurst"/> as a single-target burst with no caster pass. Crit, miss, element,
/// charges, passives and kill credit therefore work on every hop with nothing written twice.</para>
///
/// <para><b>Every machine that can see the thrower is told each leg</b> — where it goes and the exact moments it
/// leaves and arrives on the shared clock — and draws it (<see cref="FlightPictures"/>). Nothing about the hit travels
/// with the leg: the blow is announced by the unit it lands on, at the moment it lands.</para>
///
/// <para>Nothing here allocates per flight: entries are pooled, and the only lists are reused.</para>
/// </summary>
public static class Flights
{
    /// <summary>What is drawn flying — chosen by each watching machine from what was thrown (<see cref="LookOf"/>),
    /// never by the server.</summary>
    public enum Look : byte
    {
        /// <summary>The weapon the thrower is holding, spinning — a thrown shuriken.</summary>
        HeldWeapon = 0,
        /// <summary>A plain streak — an arrow, or a bouncing skill with no look of its own.</summary>
        Streak = 1,
        /// <summary>An Effect Library effect, named by the leg.</summary>
        Effect = 2,
        /// <summary>A real arrow model pointing along its flight — what a bow shoots (<see cref="Arrow.Model"/>).</summary>
        ArrowModel = 3,
    }

    /// <summary>A WATCHING MACHINE: what to draw flying for a leg, from what was thrown. A skill's bounce draws its
    /// binding's own effect when it names one (<paramref name="effectId"/>); anything else is the weapon itself when
    /// it is one that flies, a plain streak when not. Read from this machine's own copy of the skill.</summary>
    public static Look LookOf(WeaponType thrown, string skillId, int binding, out string effectId)
    {
        effectId = null;
        if (!string.IsNullOrEmpty(skillId) && binding >= 0)
        {
            var def = SkillCatalog.Get(skillId);
            var b = def != null && binding < def.bindings.Count ? def.bindings[binding] : null;
            if (b != null && !string.IsNullOrEmpty(b.bounceEffectId)) { effectId = b.bounceEffectId; return Look.Effect; }
        }
        if (thrown == WeaponType.Bow) return Look.ArrowModel;
        return WeaponTypes.ThrowsItself(thrown) ? Look.HeldWeapon : Look.Streak;
    }

    sealed class Flight
    {
        public ICombatUnit thrower;
        public int throwerLife;
        public string map;
        public int connectionId;            // -1 for a creature
        public bool basic;                  // an ordinary attack, as opposed to a skill's bounce
        public WeaponType weapon;           // what it threw — a swap mid-flight ends the chain
        public Quality quality;

        public ICombatUnit target;
        public int targetLife;
        public Vector3 lastPoint;           // where the last leg ended — the next leg starts here
        public int hop;                     // 0 = the first hit
        public int jumpsLeft;
        public float range, kept;
        public bool returns, returning;
        public double arrivesAt;

        public ushort blade;
        public int binding;                 // a skill's bounce: which of the skill's tick bindings it is; -1 for a throw

        // A skill's bounce: the effects that land on every hop, and the cast they belong to.
        public List<SkillEffect> effects;
        public StackVars vars;
        public ulong chain;
        public string skillId, symbol, skillName;
        public bool efficient;
        public float release = 1f;

        public readonly List<ICombatUnit> hit = new List<ICombatUnit>(FlightMath.MaxJumps + 1);

        public void Clear()
        {
            thrower = null; target = null; effects = null; map = null; binding = -1;
            skillId = null; symbol = null; skillName = null; hit.Clear(); returning = false; release = 1f;
        }
    }

    static readonly List<Flight> live = new List<Flight>(32);
    static readonly Stack<Flight> spare = new Stack<Flight>(32);
    static readonly List<ICombatUnit> peopleScratch = new List<ICombatUnit>(16);
    static ushort nextBlade;

    static Flight Rent() => spare.Count > 0 ? spare.Pop() : new Flight();

    /// <summary>THROW an ordinary ranged attack from <paramref name="thrower"/> at <paramref name="target"/>: an arrow
    /// for a bow or a firearm, a bouncing blade that comes back for a weapon that flies itself. The caller has already
    /// judged that the throw is allowed (reach, map, side); everything after it is judged again on arrival.
    /// <paramref name="connectionId"/> is the thrower's connection, or -1 for a creature.</summary>
    public static void ThrowBasic(ICombatUnit thrower, ICombatUnit target, int connectionId)
    {
        if (!GameServer.IsServer || PlayerUnits.Gone(thrower) || PlayerUnits.Gone(target) || thrower.Body == null
            || target.Body == null) return;

        var weapon = thrower.HeldWeapon;
        bool flies = WeaponTypes.ThrowsItself(weapon);
        var f = Rent();
        f.thrower = thrower;
        f.throwerLife = thrower.States != null ? thrower.States.LifeCount : 0;
        f.map = thrower.Map;
        f.connectionId = connectionId;
        f.basic = true;
        f.weapon = weapon;
        f.quality = thrower.HeldQuality;
        f.hop = 0;
        f.jumpsLeft = FlightMath.TotalJumps(flies ? FlightMath.ThrowJumps : 0, ExtraJumps(thrower));
        f.range = FlightMath.ThrowJumpRange;
        f.kept = FlightMath.ThrowDamageKept;
        f.returns = flies;
        f.binding = -1;
        f.skillId = null;
        f.blade = ++nextBlade;
        f.lastPoint = UnitAnchor.Center(thrower.Body);
        live.Add(f);
        StartLeg(f, target);
    }

    /// <summary>Carry a single-target skill tick on from <paramref name="firstVictim"/>, which the tick has just landed
    /// on, to <paramref name="binding"/>'s bounce jumps. The effects that land on every hop are the tick's own target
    /// effects, measured with the cast's snapshot (<paramref name="vars"/>); the cast's own side never runs again.</summary>
    public static void StartSkillBounce(ICombatUnit caster, ICombatUnit firstVictim, SkillTickBinding binding,
                                        List<SkillEffect> effects, StackVars vars, ulong chain,
                                        string skillId, string symbol, string skillName, bool efficient,
                                        float release = 1f)
    {
        if (!GameServer.IsServer || binding == null || binding.bounceJumps <= 0) return;
        if (PlayerUnits.Gone(caster) || caster.IsDead || PlayerUnits.Gone(firstVictim) || firstVictim.Body == null) return;

        int jumps = FlightMath.TotalJumps(binding.bounceJumps, ExtraJumps(caster));
        if (jumps <= 0) return;

        var f = Rent();
        f.thrower = caster;
        f.throwerLife = caster.States != null ? caster.States.LifeCount : 0;
        f.map = caster.Map;
        f.connectionId = -1;
        f.basic = false;
        f.weapon = caster.HeldWeapon;
        f.quality = caster.HeldQuality;
        f.hop = 0;
        f.jumpsLeft = jumps;
        f.range = binding.bounceRange;
        f.kept = binding.bounceDamageKept;
        f.returns = binding.bounceReturns;
        var def = SkillCatalog.Get(skillId);
        f.binding = def != null ? def.bindings.IndexOf(binding) : -1;
        f.blade = ++nextBlade;
        f.effects = effects;
        f.vars = vars;
        f.chain = chain;
        f.skillId = skillId; f.symbol = symbol; f.skillName = skillName;
        f.efficient = efficient;
        f.release = release;
        f.hit.Add(firstVictim);
        f.lastPoint = UnitAnchor.Center(firstVictim.Body);
        live.Add(f);
        if (!JumpOn(f)) Finish(f, "nobody near the first hit");
    }

    /// <summary>Forget every flight <paramref name="connectionId"/> threw — they are leaving this machine, and anything
    /// that landed for them after their save would happen to a copy about to be thrown away.</summary>
    public static void DropThrower(int connectionId)
    {
        if (connectionId < 0) return;
        for (int i = live.Count - 1; i >= 0; i--)
            if (live[i].connectionId == connectionId) Finish(live[i], "the thrower left");
    }

    /// <summary>The store's clock, walked once a frame by the server. Walked backwards so an arrival can end its flight
    /// in place without disturbing the ones still in the air.</summary>
    public static void Tick()
    {
        if (live.Count == 0) return;
        double now = NetworkTime.time;
        for (int i = live.Count - 1; i >= 0; i--)
        {
            if (i >= live.Count) continue;   // an arrival above ended more than one flight
            var f = live[i];
            if (now < f.arrivesAt) continue;
            Arrive(f);
        }
    }

    static float ExtraJumps(ICombatUnit unit) =>
        unit != null && unit.States != null ? unit.States.SubStatBonus(SubStat.ExtraBounceJumps) : 0f;

    // One leg arriving.
    static void Arrive(Flight f)
    {
        if (f.returning) { Finish(f, null); return; }

        // THE THROWER, asked first. Gone, another life, another map, or on the way out: nothing more happens and
        // nothing more is drawn — there is nobody here to draw it for.
        var t = f.thrower;
        if (PlayerUnits.Gone(t)) { Finish(f, "the thrower is gone"); return; }
        if (t.States != null && t.States.LifeCount != f.throwerLife) { Finish(f, "the thrower died and came back"); return; }
        if (t.Map != f.map) { Finish(f, "the thrower changed map"); return; }
        if (f.connectionId >= 0 && GameNetwork.IsLeaving(f.connectionId)) { Finish(f, "the thrower is leaving"); return; }

        // Dead, or no longer holding what it threw: the blade comes home and lands nothing.
        if (t.IsDead || (f.basic && (t.HeldWeapon != f.weapon || t.HeldQuality != f.quality || !t.SwingsRanged)))
        {
            ComeBack(f, t.IsDead ? "the thrower died" : "the thrower changed weapon");
            return;
        }

        var victim = f.target;
        if (TargetStillThere(f, victim))
        {
            Land(f, victim);
            f.hit.Add(victim);
            if (!PlayerUnits.Gone(victim) && victim.Body != null) f.lastPoint = UnitAnchor.Center(victim.Body);
        }
        else if (!PlayerUnits.Gone(victim) && victim.Body != null) f.lastPoint = UnitAnchor.Center(victim.Body);

        // The landing can have killed the thrower (a reflect, a thorn) — asked again before going on.
        if (PlayerUnits.Gone(t)) { Finish(f, "the thrower is gone"); return; }
        if (t.IsDead) { ComeBack(f, "the thrower died"); return; }

        if (JumpOn(f)) return;
        ComeBack(f, f.jumpsLeft > 0 ? "nobody left to jump to" : "out of jumps");
    }

    // Start the next jump, if there is one to make and somebody to make it to.
    static bool JumpOn(Flight f)
    {
        if (f.jumpsLeft <= 0) return false;
        var next = NearestNotHit(f.lastPoint, f.range, f.thrower, f.map, f.hit);
        if (next == null) return false;
        f.jumpsLeft--;
        f.hop++;
        StartLeg(f, next);
        return true;
    }

    // Home to the thrower's hand if this flight returns; otherwise it is simply over.
    static void ComeBack(Flight f, string why)
    {
        if (!f.returns || PlayerUnits.Gone(f.thrower) || f.thrower.Body == null) { Finish(f, why); return; }
        f.returning = true;
        Vector3 from = f.lastPoint;
        Vector3 to = UnitAnchor.Center(f.thrower.Body);
        double now = NetworkTime.time;
        f.arrivesAt = now + FlightMath.LegSeconds(from, to);
        GameNetwork.AnnounceFlightLeg(f.thrower.Body, f.blade, f.thrower.Body, MapManager.InMap(to, f.map),
                                      now, f.arrivesAt, f.weapon, f.skillId, f.binding, f.map);
        Log(f, why != null ? "flies back — " + why : "flies back");
    }

    static void StartLeg(Flight f, ICombatUnit to)
    {
        f.target = to;
        f.targetLife = to.States != null ? to.States.LifeCount : 0;
        Vector3 toPoint = UnitAnchor.Center(to.Body);
        double now = NetworkTime.time;
        f.arrivesAt = now + FlightMath.LegSeconds(f.lastPoint, toPoint);
        GameNetwork.AnnounceFlightLeg(f.thrower.Body, f.blade, to.Body, MapManager.InMap(toPoint, f.map),
                                      now, f.arrivesAt, f.weapon, f.skillId, f.binding, f.map);
        if (GameLog.On(LogCat.Hit))
            Log(f, (f.hop == 0 ? "thrown at " : "jumps to ") + SpawnIds.Label(to) + " (hop " + f.hop + ")");
    }

    static bool TargetStillThere(Flight f, ICombatUnit victim)
    {
        if (PlayerUnits.Gone(victim) || victim.IsDead) return false;
        if (victim.States != null && victim.States.LifeCount != f.targetLife) return false;
        if (victim.Map != f.map) return false;
        return Sides.Hostile(f.thrower, victim);
    }

    static void Land(Flight f, ICombatUnit victim)
    {
        float scale = FlightMath.HopScale(f.kept, f.hop);
        if (f.basic)
        {
            // A creature's own swing door carries its on-hit ailment and its combat experience onto the unit struck.
            if (f.thrower is Health creature && creature.Brain != null)
                creature.Brain.LandSwing(victim, announceIntent: false, power: scale);
            else
                BasicSwing.Land(f.thrower, victim, ScaledNumber.Of(scale), StackVars.None,
                                outer: 1f, overkill: false, announceIntent: false, out _);
            return;
        }

        var prev = PassiveEvents.Push(f.chain, f.skillId);
        try
        {
            SkillEffectApplier.ApplyBurst(victim.Body.position, 0f, SkillScope.Single, SkillTargets.Mobs, f.effects,
                                          victim.Body, f.symbol, f.skillName, f.skillId,
                                          caster: f.thrower, vars: f.vars, efficient: f.efficient,
                                          casterPass: false, hop: scale, release: f.release);
        }
        finally { PassiveEvents.Pop(prev); }
    }

    // The nearest living enemy of the thrower within `range` of `from` (flat), in `map`, that this chain has not hit.
    static ICombatUnit NearestNotHit(Vector3 from, float range, ICombatUnit thrower, string map, List<ICombatUnit> hit)
    {
        var side = Sides.Of(thrower);
        if (side == Side.None) return null;
        float bestSqr = range * range;
        ICombatUnit best = null;

        if (Sides.Hostile(side, Side.Players))
        {
            peopleScratch.Clear();
            PlayerUnits.CollectIn(map, peopleScratch);
            for (int i = 0; i < peopleScratch.Count; i++)
            {
                var person = peopleScratch[i];
                if (PlayerUnits.Gone(person) || person.IsDead || person.Body == null || hit.Contains(person)) continue;
                if (!string.IsNullOrEmpty(map) && person.Map != map) continue;
                Vector3 d = person.Body.position - from; d.y = 0f;
                if (d.sqrMagnitude > bestSqr) continue;
                bestSqr = d.sqrMagnitude; best = person;
            }
            peopleScratch.Clear();
        }

        var creatures = string.IsNullOrEmpty(map) ? MonsterAI3D.Active : MonsterAI3D.ActiveIn(map);
        for (int i = 0; i < creatures.Count; i++)
        {
            var ai = creatures[i];
            var h = ai != null ? ai.Health : null;
            if (h == null || h.IsDead || !Sides.Hostile(side, h.side) || hit.Contains(h)) continue;
            Vector3 d = ai.transform.position - from; d.y = 0f;
            if (d.sqrMagnitude > bestSqr) continue;
            bestSqr = d.sqrMagnitude; best = h;
        }
        return best;
    }

    static void Finish(Flight f, string why)
    {
        if (why != null) Log(f, "ends — " + why);
        live.Remove(f);
        f.Clear();
        spare.Push(f);
    }

    static void Log(Flight f, string what)
    {
        if (!GameLog.On(LogCat.Hit) || PlayerUnits.Gone(f.thrower)) return;
        GameLog.Info(LogCat.Hit, string.Concat(SpawnIds.MapOf(f.thrower), " ", SpawnIds.Label(f.thrower), " blade ",
                                               f.blade.ToString(), " ", what));
    }
}
