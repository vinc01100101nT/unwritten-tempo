using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Every durational state a unit can carry, unified into one id space (buffs + debuffs). Mapped from a skill's
/// <c>SkillEffectKind</c> / <c>Ailment</c> at apply time.
///
/// <para>⚠️ <b>APPEND-ONLY.</b> States themselves are never saved, but a skill's combo gate SERIALIZES a value here
/// (<c>SkillDef.requireState</c>), which Unity stores by int — so inserting or reordering a member would silently
/// repoint every skill asset that references one. Add new states at the END, exactly like <c>SkillEffectKind</c>.</para></summary>
public enum StateId
{
    // ---- Debuffs — ailments (was StatusController) ----
    Poison, Bleed, Burn,          // DoT: magnitude = damage per tick
    Stun, Freeze,                 // hard-CC: CannotAct
    Slow,                         // magnitude = move-speed factor (0..1)
    Silence,                      // blocks casting
    Blind,                        // magnitude = HIT (accuracy) points lost
    Vulnerability,                // magnitude = extra-damage fraction (VulnMult = 1 + frac)
    Taunt, Knockback, Pull,       // control (mob-side gameplay stays in MonsterAI3D; here for the timer + icon)
    // ---- Buffs — self states (was Character buff windows / StatusController.Haste) ----
    Haste,                        // magnitude = move-speed multiplier (>=1)
    Guard,                        // magnitude = incoming-damage cut (0..1)
    DamageBuff, AttackSpeedBuff, CastSpeedBuff,   // magnitude = the stat multiplier
    Regen,                        // intAmount = HP per tick
    Shield,                       // intAmount = barrier HP pool
    ManaLeech,                    // magnitude = fraction of damage dealt returned as MP
    Reflect,                      // magnitude = fraction of damage taken bounced back
    Parry,                        // stacks = CHARGES; each negates one incoming hit outright, then is spent
    // Delaying a hit needs TWO states, because they are two different things with two different lifetimes. Merging
    // them is impossible, not merely untidy: spending the last charge must make the ARMED state vanish, while the DEBT
    // it created has to outlive it and still come due.
    Brace,                        // ARMED capacity. stacks = CHARGES, exactly like Parry — each holds one hit, then is spent.
    // Named for WHAT IT IS, not for the skill that fills it: any skill that postpones damage owes into this. (Brace
    // above keeps its name for the same reason Parry does — one combat verb naming the effect, the state and the
    // skill is this codebase's established pattern; see SkillEffectKind.Parry / StateId.Parry / skill.sword.parry.)
    DeferredDamage,               // The DEBT. stacks = one delayed hit each, intAmount = its damage; the SUM comes due at expiry.
}

/// <summary>
/// The unified per-unit STATE STORE (buffs + debuffs) — the single source for BOTH gameplay queries and the on-screen
/// display (player buff tray + mob/other-player nameplate icons). It replaces the scattered
/// <see cref="StatusController"/> ailment arrays, the <see cref="Character"/> buff windows, and
/// <see cref="Health"/>'s vulnerability — each migrated onto it one family at a time, behaviour-preserving.
///
/// <para><b>Optimized as agreed:</b> one list of active states is the source of truth, but the hot gameplay scalars
/// (<see cref="MoveMult"/>, <see cref="GuardCut"/>, <see cref="VulnMult"/>, the buff mults, …) are <b>cached</b> and
/// recomputed only when the set CHANGES (apply / remove / prune) — so every reader stays <b>O(1)</b>, matching today's
/// field reads. Entry objects are <b>pooled</b>, so apply/remove is <b>zero-alloc</b>. No per-query list scan, no GC.</para>
///
/// <para><b>PHASE 1 — foundation only.</b> This holds the store, the cached query scalars, and the display list.
/// It is not yet attached, written, or read anywhere; each state family is migrated onto it in later phases so combat
/// never breaks. Active behaviours that need the unit's HP/damage sinks (DoT damage, Regen heal, Shield drain-on-hit),
/// the VFX auras/cues, and the mob-AI delegation (Stun/Slow/Knockback) are wired during those migrations. Stacks /
/// owner (the markings system) are deferred, but the list shape leaves room for them.</para>
/// </summary>
public class UnitStates : MonoBehaviour
{
    /// <summary>One active state on the unit, holding one or more STACKS. <see cref="magnitude"/> / <see cref="intAmount"/>
    /// are the AGGREGATE across stacks (their meaning is per <see cref="StateId"/> — see the enum), so every existing
    /// reader keeps working untouched: a non-stacking state is simply one stack, and its aggregate IS that stack's value.
    /// Display fields carry the source skill's icon for the tray / nameplate.</summary>
    public class State
    {
        /// <summary>One application. Each stack carries its OWN magnitude and its OWN expiry — that's what makes a
        /// "charge" (N stacks granted together, sharing a window) and a Momentum-style pile (one stack per hit, each on
        /// its own timer) the SAME mechanism. Struct + a pooled list ⇒ adding/removing a stack never allocates.</summary>
        public struct Stack
        {
            public float endTime;
            public float magnitude;
            public int intAmount;
        }

        public StateId id;
        /// <summary>WHO granted this pile — the source skill's id ("" for engine-applied states with no skill behind
        /// them). Part of the entry's IDENTITY, not just display: two skills that both buff attack keep SEPARATE piles,
        /// so a finisher keyed to one can't cash the other's (Deathblow must eat Momentum, never Lock Blades' charges).
        /// The mechanical effect is still generic — <see cref="id"/> decides what the stacks DO; this decides whose
        /// they are.</summary>
        public string sourceSkillId = "";
        /// <summary>True when this state was applied as a stacking pile. Decides how <see cref="magnitude"/> READS:
        /// stacking = the sum of per-stack DELTAS (a mult channel is 1 + sum); non-stacking = the absolute value.</summary>
        public bool stacking;
        public float magnitude;    // SUM across stacks (DoT dmg/tick · Slow/Haste factor · Guard cut · Vuln frac · buff bonus · leech/reflect frac · Blind HIT)
        public int intAmount;      // SUM across stacks (Shield pool · Regen HP/tick)
        public float startTime, endTime;   // endTime = the LAST stack to expire (when the state is fully gone)
        public float nextTick;     // DoT / Regen cadence marker (owner ticks it)
        public float rampDelay, rampPerStep, rampInterval, rampCap;   // DamageBuff ramp params (all 0 = a flat buff)
        // Display (for the buff tray + nameplate icons)
        public Sprite icon;
        public string name;
        public string description;
        public Color tint;

        /// <summary>Live stacks, oldest-APPLIED first. Consumption and overflow both take from the front, so the stack
        /// nearest the end of its life is spent before a fresh one. Pooled with the State (cleared, capacity kept).</summary>
        public readonly List<Stack> stacks = new List<Stack>(4);
        /// <summary>How many stacks are live (1 for a non-stacking state).</summary>
        public int StackCount => stacks.Count;

        /// <summary>This state's magnitude read as a MULTIPLIER — the ONE place the encoding is applied, so no reader
        /// can get it wrong. Magnitude is ALWAYS a BONUS FRACTION (0.3 ⇒ +30% ⇒ ×1.30), stacking or not: a stacking
        /// state's is the SUM of its per-stack bonuses (ten 0.03 stacks ⇒ 0.30), a non-stacking state's is simply its
        /// own. Only mult-style channels (Haste, the stat buffs) use this; additive ones (DoT, shield, guard cut) read
        /// <see cref="magnitude"/> directly, since a plain sum is already correct for them.</summary>
        public float Mult => 1f + magnitude;

        public float Remaining => Mathf.Max(0f, endTime - Time.time);
        public float Fraction01 => endTime > startTime ? Mathf.Clamp01((endTime - Time.time) / (endTime - startTime)) : 0f;

        /// <summary>Recompute the aggregates from the live stacks. The ONE place the sum is derived, so no caller can
        /// hand-maintain a parallel total.</summary>
        public void Refold()
        {
            float mag = 0f, last = 0f;
            int amt = 0;
            for (int i = 0; i < stacks.Count; i++)
            {
                mag += stacks[i].magnitude;
                amt += stacks[i].intAmount;
                if (stacks[i].endTime > last) last = stacks[i].endTime;
            }
            magnitude = mag; intAmount = amt; endTime = last;
        }
    }

    readonly List<State> active = new List<State>(8);
    readonly Stack<State> pool = new Stack<State>(8);   // reused entries → apply/remove never allocates

    /// <summary>The live states (expired ones swept by <see cref="Prune"/>) — read-only, for the tray / nameplate.</summary>
    public IReadOnlyList<State> Active { get { EnsureFresh(); return active; } }

    /// <summary>Fired when the SET of states changes (apply / remove / prune) — display rebuilds its icons on this.</summary>
    public event Action OnChanged;

    // ---- cached derived scalars (recomputed only on change; every getter below is O(1)) --------------------------
    // DamageBuff is deliberately absent here — it is computed live by DamageBuffMult so its ramp can't go stale.
    float moveMult = 1f, guardCut, vulnMult = 1f, aspdBuffMult = 1f, castBuffMult = 1f, leechPct, reflectBonus;
    int hitPenalty, shieldPool, parryCharges;
    bool cannotAct, silenced;
    float nextExpiry = float.MaxValue;   // earliest endTime across active states — the ONLY time EnsureFresh must prune

    /// <summary>Hard ceiling on stacks per state. Design may call a state "uncapped" (Momentum) — its FIFO equilibrium
    /// bounds it in practice — but an unbounded list is a memory/perf hazard, so this backstop exists and design never
    /// reaches it.</summary>
    public const int MaxStacks = 99;

    /// <summary>Tray tint for a BANKED-damage pile — red, so a debt you owe can't be mistaken for the protection that
    /// created it. Kept light enough that the icon underneath still reads.</summary>
    static readonly Color DebtTint = new Color(1f, 0.45f, 0.45f);

    // Transient APPLY CONTEXT for the NEXT Apply — the skill applier sets it right before a durational gameplay call
    // (BeginGuard / BuffDamage / StatusController.Apply / …) so the created/refreshed entry gets the source skill's
    // tray/nameplate icon AND its stacking rules, WITHOUT threading either through every gameplay method. The applier
    // owns the lifecycle (SetApplyContext before the call, ClearApplyContext after); Apply only reads what it was given.
    Sprite ctxIcon; string ctxName, ctxDesc, ctxSourceId;
    bool ctxStacking; int ctxStacksPerApply = 1, ctxStackLimit;

    /// <summary>Tag the NEXT applied state with its SOURCE skill, display info and stacking rules. Set by the skill
    /// applier before a durational gameplay call; always pair with <see cref="ClearApplyContext"/> after.
    /// <paramref name="stacking"/> off = today's single-instance, newest-wins behaviour.
    /// <paramref name="sourceSkillId"/> is the granting skill's id — it keys the pile (see
    /// <see cref="State.sourceSkillId"/>), so leaving it empty pools this state with every other sourceless one.</summary>
    public void SetApplyContext(Sprite icon, string name, string description,
                                bool stacking = false, int stacksPerApply = 1, int stackLimit = 0,
                                string sourceSkillId = null)
    {
        ctxIcon = icon; ctxName = name; ctxDesc = description;
        ctxSourceId = sourceSkillId ?? "";
        ctxStacking = stacking;
        ctxStacksPerApply = Mathf.Max(1, stacksPerApply);
        ctxStackLimit = stackLimit;
    }

    public void ClearApplyContext()
    {
        ctxIcon = null; ctxName = null; ctxDesc = null; ctxSourceId = "";
        ctxStacking = false; ctxStacksPerApply = 1; ctxStackLimit = 0;
    }

    // Lazy self-expiry: prune (and recompute the cached scalars) ONLY when a state has actually elapsed. One float
    // compare on the common path — so there's no per-frame Update and no staleness, keeping the old lazy-expiry cost.
    void EnsureFresh() { if (active.Count > 0 && Time.time >= nextExpiry) Prune(); }

    /// <summary>Slow × Haste move-speed multiplier (1 = normal). Does NOT include the caster's action-lock (casting /
    /// aftercast root) — that stays with the mover, exactly as <c>StatusController.MoveMult</c> layered it.</summary>
    public float MoveMult { get { EnsureFresh(); return moveMult; } }
    /// <summary>Stunned or Frozen — can't act / move.</summary>
    public bool CannotAct { get { EnsureFresh(); return cannotAct; } }
    /// <summary>Spell-casting blocked (Silence).</summary>
    public bool Silenced { get { EnsureFresh(); return silenced; } }
    /// <summary>HIT (accuracy) points to subtract from this unit's attacks while Blinded.</summary>
    public int HitPenalty { get { EnsureFresh(); return hitPenalty; } }
    /// <summary>Incoming-damage cut fraction (0..1) from Guard.</summary>
    public float GuardCut { get { EnsureFresh(); return guardCut; } }
    /// <summary>Incoming-damage multiplier (>=1) from Vulnerability.</summary>
    public float VulnMult { get { EnsureFresh(); return vulnMult; } }
    /// <summary>Outgoing-damage multiplier from EVERY live DamageBuff pile, each pile's own RAMP folded in — the one
    /// answer to "how much is my damage buffed", for player and mob alike.
    ///
    /// <para>Computed on read instead of cached like its siblings, on purpose: a ramp grows on its OWN clock (a step
    /// every <c>rampInterval</c>) with no expiry to wake the cache, so a cached figure would sit stale between steps.
    /// The loop is over the handful of live states and allocates nothing.</para>
    ///
    /// <para>Reading only the FIRST pile here is the bug this replaced: piles are keyed by GRANTING SKILL, so Momentum
    /// and Lock Blades are two separate DamageBuff entries, and whichever landed first silently ate the other.</para></summary>
    public float DamageBuffMult
    {
        get
        {
            EnsureFresh();
            float bonus = 0f;
            for (int i = 0; i < active.Count; i++)
            {
                var s = active[i];
                if (s.id != StateId.DamageBuff) continue;
                float m = s.Mult;
                if (s.rampPerStep > 0f)   // ramping buff: base + perStep for each interval elapsed past the delay
                {
                    float el = Time.time - s.startTime - s.rampDelay;
                    if (el > 0f)
                    {
                        m += s.rampPerStep * Mathf.FloorToInt(el / s.rampInterval);
                        if (s.rampCap > 0f) m = Mathf.Min(m, s.rampCap);
                    }
                }
                bonus += m - 1f;
            }
            return Mathf.Max(0f, 1f + bonus);
        }
    }
    public float AttackSpeedBuffMult { get { EnsureFresh(); return aspdBuffMult; } }
    public float CastSpeedBuffMult { get { EnsureFresh(); return castBuffMult; } }
    /// <summary>Fraction of damage dealt returned as MP (ManaLeech window), 0 when none.</summary>
    public float ManaLeechPct { get { EnsureFresh(); return leechPct; } }
    /// <summary>Extra reflect fraction (timed Reflect window), 0 when none.</summary>
    public float ReflectBonus { get { EnsureFresh(); return reflectBonus; } }
    /// <summary>Remaining barrier HP (Shield), 0 when none.</summary>
    public int ShieldPool { get { EnsureFresh(); return shieldPool; } }
    /// <summary>Parry charges held, 0 when none — each negates one incoming hit. Cached like every other scalar so the
    /// per-hit mitigation check stays O(1); spend one with <see cref="Consume"/>.</summary>
    public int ParryCharges { get { EnsureFresh(); return parryCharges; } }

    /// <summary>True if this state is currently active on the unit.</summary>
    public bool Has(StateId id)
    {
        EnsureFresh();
        for (int i = 0; i < active.Count; i++) if (active[i].id == id) return true;
        return false;
    }

    /// <summary>Seconds left on a state (0 if absent). For HUD pips / "guard remaining" style readouts.</summary>
    public float Remaining(StateId id)
    {
        EnsureFresh();
        var s = Find(id);
        return s != null ? s.Remaining : 0f;
    }

    /// <summary>The live entry for <paramref name="id"/>, or null. For the few systems that own a state's ACTIVE
    /// mechanic (regen/DoT tick, shield drain, damage ramp) and read/mutate its params on the unified entry — so the
    /// store stays the single source while the delicate per-tick math stays in its existing owner.</summary>
    public State Get(StateId id) { EnsureFresh(); return Find(id); }

    // First entry of this KIND, whoever granted it. For the readers that ask "am I slowed / how long is my guard" —
    // questions about the effect, not about whose it is.
    State Find(StateId id)
    {
        for (int i = 0; i < active.Count; i++) if (active[i].id == id) return active[i];
        return null;
    }

    // The one entry that is BOTH this kind and this source — the identity a pile is applied and cashed under.
    State Find(StateId id, string sourceSkillId)
    {
        string src = sourceSkillId ?? "";
        for (int i = 0; i < active.Count; i++)
            if (active[i].id == id && active[i].sourceSkillId == src) return active[i];
        return null;
    }

    /// <summary>Live stacks of ONE pile: the state <paramref name="id"/> granted by <paramref name="sourceSkillId"/>.
    /// Both halves are required, and that is the point. Keying by skill alone was ambiguous the moment one skill
    /// granted two stacking states — a skill handing out charges to spend AND parry charges to survive on would have
    /// its two piles counted as one total and spent for each other. The pair (who granted it, which state it is)
    /// names exactly one pile.</summary>
    public int StacksFrom(string sourceSkillId, StateId id)
    {
        if (string.IsNullOrEmpty(sourceSkillId)) return 0;
        EnsureFresh();
        int n = 0;
        for (int i = 0; i < active.Count; i++)
            if (active[i].sourceSkillId == sourceSkillId && active[i].id == id) n += active[i].StackCount;
        return n;
    }

    /// <summary>The total VALUE the stacks granted by <paramref name="sourceSkillId"/> are carrying — the sibling of
    /// <see cref="StacksFrom"/> for piles whose stacks mean an AMOUNT rather than a tally. ONE number, whichever
    /// channel the pile uses, so a consumer never branches on how its granter happened to store things.
    ///
    /// <para><b>A percentage arrives already converted.</b> <see cref="State.magnitude"/> is ALWAYS a bonus fraction
    /// (see <see cref="State.Mult"/>): +30% is stored as 0.3, and ten +3% stacks fold to 0.30. So "30% Attack" is read
    /// as the flat number <c>0.3</c> with no conversion here — the store did it at apply time, in one place.</para>
    ///
    /// <para>Both channels are summed because a pile carries ONE kind of value, never both: a flat pile (a Brace's
    /// delayed damage) leaves the fraction at 0, a percentage pile leaves the flat at 0. That invariant is what makes
    /// a single total honest — a state whose stacks carried both meanings at once would be two things wearing one
    /// name, which the store's one-responsibility rule already forbids.</para></summary>
    public float CarriedFrom(string sourceSkillId, StateId id)
    {
        if (string.IsNullOrEmpty(sourceSkillId)) return 0f;
        EnsureFresh();
        float total = 0f;
        for (int i = 0; i < active.Count; i++)
            if (active[i].sourceSkillId == sourceSkillId && active[i].id == id)
                total += active[i].magnitude + active[i].intAmount;
        return total;
    }

    /// <summary>Spend stacks of ONE pile — the state <paramref name="id"/> granted by <paramref name="sourceSkillId"/>
    /// — oldest first, or ALL of them. Returns how many were spent. Nothing else is touched: naming both halves is
    /// what stops a skill's second stacking state being spent by a consumer that meant the first (see
    /// <see cref="StacksFrom"/>).</summary>
    public int ConsumeFrom(string sourceSkillId, StateId id, int count, bool all = false)
    {
        if (string.IsNullOrEmpty(sourceSkillId)) return 0;
        EnsureFresh();
        int spent = 0;
        for (int i = active.Count - 1; i >= 0; i--)   // reverse: entries emptied by the spend are removed in-loop
        {
            var s = active[i];
            if (s.sourceSkillId != sourceSkillId || s.id != id) continue;
            int take = all ? s.stacks.Count : Mathf.Clamp(count - spent, 0, s.stacks.Count);
            if (take <= 0) continue;
            s.stacks.RemoveRange(0, take);           // oldest first — spend the stack nearest its expiry
            spent += take;
            if (s.stacks.Count == 0) { active.RemoveAt(i); pool.Push(s); }
            else s.Refold();
            if (!all && spent >= count) break;
        }
        if (spent > 0) { Recompute(); OnChanged?.Invoke(); }
        return spent;
    }

    /// <summary>Apply a timed stat buff to THIS unit — the ONE place a Buff effect lands, for the player and for a mob
    /// alike (a buff belongs to the unit, not to who is driving it). <paramref name="mult"/> is a MULTIPLIER (×1.3);
    /// the store keeps a bonus fraction, so the conversion happens here, once.
    ///
    /// <para>The RAMP (<paramref name="rampPerStep"/> &gt; 0) only means anything on the Damage buff — it is the
    /// "keep it up to reach the ceiling" support loop — and is read live by <see cref="DamageBuffMult"/>. Refreshing a
    /// live buff keeps its ramp progress (Apply preserves startTime); a lapsed one restarts from base.</para></summary>
    public void ApplyStatBuff(BuffStat stat, float mult, float seconds,
                              float rampDelay = 0f, float rampPerStep = 0f, float rampInterval = 5f, float rampCap = 0f)
    {
        if (seconds <= 0f) return;
        StateId id = stat == BuffStat.AttackSpeed ? StateId.AttackSpeedBuff
                   : stat == BuffStat.CastSpeed   ? StateId.CastSpeedBuff
                                                  : StateId.DamageBuff;
        var s = Apply(id, seconds, Mathf.Max(0.01f, mult) - 1f);
        if (s == null || id != StateId.DamageBuff) return;
        s.rampDelay = Mathf.Max(0f, rampDelay);
        s.rampPerStep = Mathf.Max(0f, rampPerStep);
        s.rampInterval = Mathf.Max(0.1f, rampInterval);
        s.rampCap = rampCap;
    }

    // ---- the state STARTERS — one per state family, valid on ANY unit ------------------------------------------
    // These used to live on Character (the player) and Health (the mob), which is the whole reason "a guard/shield/
    // parry/regen/leech/reflect can only be given to the player" was true: the store never cared who it sat on, but
    // only the player's class had a method to write them. They are all the same shape — validate, then one Apply — so
    // they belong to the store itself. Now a mob can be shielded, a player can be made vulnerable, and neither needed
    // a new code path. Keep new state families here, NOT on a unit class, so both sides inherit them for free.

    /// <summary>Seconds between heal-over-time ticks. Owned here because the state is; the unit's ticker reads it.</summary>
    public const float RegenInterval = 0.8f;

    /// <summary>Cut <paramref name="cutFraction"/> (0..1) off every incoming hit for <paramref name="seconds"/>.</summary>
    public void BeginGuard(float seconds, float cutFraction)
    {
        if (seconds <= 0f) return;
        Apply(StateId.Guard, seconds, Mathf.Clamp01(cutFraction));
    }

    /// <summary>Heal <paramref name="hpPerTick"/> every <see cref="RegenInterval"/>s for <paramref name="seconds"/>.
    /// The HP is handed out by the unit's ticker (a state store has no HP of its own).</summary>
    public void BeginRegen(int hpPerTick, float seconds)
    {
        if (hpPerTick <= 0 || seconds <= 0f) return;
        var s = Apply(StateId.Regen, seconds, 0f, hpPerTick);
        // Seed the cadence only for a state that is actually NEW (Apply zeroes nextTick when it creates the entry).
        // Re-seeding on every apply pushed the next heal a full interval away each time, so stacking or refreshing
        // faster than the interval starved the tick — the DoTs already avoid this by seeding only when not active.
        if (s != null && s.nextTick <= 0f) s.nextTick = Time.time + RegenInterval;
    }

    /// <summary>For <paramref name="seconds"/>, return <paramref name="fraction"/> (0..1) of the damage this unit
    /// DEALS as MP.</summary>
    public void BeginManaLeech(float seconds, float fraction)
    {
        if (seconds <= 0f || fraction <= 0f) return;
        Apply(StateId.ManaLeech, seconds, Mathf.Clamp01(fraction));
    }

    /// <summary>For <paramref name="seconds"/>, bounce <paramref name="fraction"/> (0..1) of the damage this unit
    /// TAKES back at whoever dealt it.</summary>
    public void BeginReflect(float seconds, float fraction)
    {
        if (seconds <= 0f || fraction <= 0f) return;
        Apply(StateId.Reflect, seconds, Mathf.Clamp01(fraction));
    }

    /// <summary>Raise a barrier of <paramref name="amount"/> HP for <paramref name="seconds"/> that soaks incoming
    /// hits before they reach HP.</summary>
    public void BeginShield(int amount, float seconds)
    {
        if (amount <= 0 || seconds <= 0f) return;
        Apply(StateId.Shield, seconds, 0f, amount);   // intAmount = barrier HP pool
    }

    /// <summary>Hold parry charges for <paramref name="seconds"/> — each negates one incoming hit outright. HOW MANY
    /// charges comes from the apply context's stacks-per-apply, so the stacks ARE the charges.</summary>
    public void BeginParry(float seconds)
    {
        if (seconds <= 0f) return;
        Apply(StateId.Parry, seconds);
    }

    /// <summary>Open a BRACE window for <paramref name="seconds"/>: incoming damage stops reaching HP and is recorded
    /// instead, one stack per hit (see <see cref="AddDeferredHit"/>), coming due as a lump when the window ends.
    /// Applied with ONE zero-value stack — the window itself — so the entry exists (and shows its timer) before any
    /// hit lands; recorded hits are appended to it and carry the whole total in <see cref="State.intAmount"/>.</summary>
    public void BeginBrace(float seconds)
    {
        if (seconds <= 0f) return;
        Apply(StateId.Brace, seconds);   // HOW MANY charges rides the apply context's stacks-per-apply, exactly like Parry
    }

    /// <summary>Record one hit against an open Brace window instead of letting it reach HP; returns false (nothing
    /// recorded) when no window is open. The stack expires WITH the window rather than on its own clock — a hit taken
    /// in the last half-second must come due with all the others, not outlive them.
    ///
    /// <para>Stacks are appended directly, the same way <see cref="DrainShield"/> edits them, because the apply
    /// context that <see cref="Apply"/> reads belongs to whoever cast the skill — and this runs deep inside a damage
    /// intake, where there is no cast in progress to borrow that context from.</para></summary>
    public bool AddDeferredHit(int dmg)
    {
        if (dmg <= 0) return false;
        EnsureFresh();

        // EVERY armed pile spends a charge on this ONE hit — they are not a queue. Two delaying skills up at once are
        // two skills that each answered the same blow, so each pays a charge for it, and each ends up owing its share.
        int piles = 0;
        for (int i = 0; i < active.Count; i++)
            if (active[i].id == StateId.Brace && active[i].stacks.Count > 0) piles++;
        if (piles == 0) return false;   // nothing armed → the hit lands normally

        // SPLIT the damage across them so the TOTAL owed is exactly the hit you took — never a multiple of it. Integer
        // shares plus the remainder handed out one each, so the parts always add back up to dmg with nothing lost or
        // invented by rounding.
        int share = dmg / piles, extra = dmg % piles, handled = 0;

        // Walk BACKWARDS: a pile that runs out of charges is removed from this very list, and going down means those
        // removals can't shuffle entries we haven't visited yet.
        for (int i = active.Count - 1; i >= 0 && handled < piles; i--)
        {
            var armed = active[i];
            if (armed.id != StateId.Brace || armed.stacks.Count == 0) continue;

            int part = share + (handled < extra ? 1 : 0);   // the remainder rides the first few piles
            handled++;

            // Spend one charge, oldest first — the same order Parry spends its charges and the store spends every
            // pile. Spent on USE and never refunded (StateGate's locked rule).
            float due = armed.endTime;            // captured BEFORE the pile can drop: the debt inherits ITS window's deadline
            string owner = armed.sourceSkillId;   // the debt belongs to the skill that armed it, so a consumer can name it
            Sprite icon = armed.icon; string label = armed.name; string desc = armed.description;
            armed.stacks.RemoveAt(0);
            if (armed.stacks.Count == 0) { active.RemoveAt(i); pool.Push(armed); }   // out of charges → the armed state (and its icon) go
            else armed.Refold();

            if (part <= 0) continue;   // more piles than damage: the charge is still spent, this one just owes nothing

            // The debt, filed under the SKILL THAT ARMED IT so its own consumer can find it, and carrying that pile's
            // own deadline — two braces with different windows come due at their own times, not a shared one.
            var debt = Find(StateId.DeferredDamage, owner);
            if (debt == null)
            {
                debt = pool.Count > 0 ? pool.Pop() : new State();
                debt.id = StateId.DeferredDamage; debt.startTime = Time.time; debt.nextTick = 0f;
                debt.sourceSkillId = owner ?? "";
                debt.stacking = true;              // it IS a pile: one stack per hit, and its total is their sum
                debt.stacks.Clear();
                // Same icon as the armed charges — it's the same skill — but tinted RED, because the two mean opposite
                // things: armed is protection you still hold, a debt is about to hurt you. The tray multiplies the
                // sprite by this (BuffBarPanel.cs:134), so one value separates them with no second icon to draw.
                debt.icon = icon; debt.name = label; debt.description = desc;
                debt.tint = DebtTint;
                active.Add(debt);
            }
            debt.stacks.Add(new State.Stack { endTime = due, magnitude = 0f, intAmount = part });
            debt.Refold();
        }

        Recompute();
        OnChanged?.Invoke();
        return true;
    }

    /// <summary>Damage currently owed across EVERY delaying skill (0 when none) — what comes due if nobody cashes it.
    /// Summed over all debt piles, never just the first: with two skills up, one pile is only part of what you owe.
    /// Read from the debt, never from the armed charges — idle charges owe nothing.</summary>
    public int DamageOwed
    {
        get
        {
            EnsureFresh();
            int owed = 0;
            for (int i = 0; i < active.Count; i++)
                if (active[i].id == StateId.DeferredDamage) owed += active[i].intAmount;
            return owed;
        }
    }

    /// <summary>Mark this unit to TAKE <paramref name="extraFraction"/> more damage (0.3 = +30%) for
    /// <paramref name="seconds"/>.</summary>
    public void BeginVulnerable(float extraFraction, float seconds)
    {
        if (seconds <= 0f) return;
        Apply(StateId.Vulnerability, seconds, Mathf.Max(0f, extraFraction));
    }

    /// <summary>Apply a state for <paramref name="seconds"/>. Two behaviours, chosen by the apply context
    /// (<see cref="SetApplyContext"/>) so no gameplay method needs a stacking parameter:
    /// <list type="bullet">
    /// <item><b>Non-stacking (default)</b> — <b>newest-wins</b>: the single stack's window + magnitude are REPLACED,
    /// even if weaker (the State-System doc's locked overwrite policy).</item>
    /// <item><b>Stacking</b> — APPENDS <c>stacksPerApply</c> stacks, each with its own window and its own magnitude;
    /// the total is their SUM. Past the limit the OLDEST stacks drop.</item>
    /// </list>
    /// Display args are optional — pass the source skill's icon/name/tint for the tray / nameplate. Returns the entry so
    /// a caller can seed per-family extras (e.g. a DoT's first <see cref="State.nextTick"/>).</summary>
    public State Apply(StateId id, float seconds, float magnitude = 0f, int intAmount = 0,
                       Sprite icon = null, string name = null, Color tint = default, string description = null)
    {
        if (seconds <= 0f) return null;
        EnsureFresh();   // drop an already-lapsed entry first, so a re-apply is treated as FRESH (restarts startTime/ramp)
        float now = Time.time;
        // Keyed by KIND + SOURCE: re-applying from the same skill grows that skill's pile, while a different skill
        // buffing the same stat opens its own. Newest-wins and the stack limit therefore apply PER SOURCE, which is
        // what stops one skill's refresh from resetting another's window.
        var s = Find(id, ctxSourceId);
        if (s == null)
        {
            s = pool.Count > 0 ? pool.Pop() : new State();
            s.id = id; s.startTime = now; s.nextTick = 0f;
            s.sourceSkillId = ctxSourceId ?? "";
            s.stacks.Clear();
            active.Add(s);
        }

        s.stacking = ctxStacking;   // decides how magnitude reads (absolute vs sum-of-deltas) — see Recompute's note
        var stack = new State.Stack { endTime = now + seconds, magnitude = magnitude, intAmount = intAmount };
        if (!ctxStacking)
        {
            s.stacks.Clear();          // NEWEST-WINS: one stack, replaced outright
            s.stacks.Add(stack);
        }
        else
        {
            int limit = ctxStackLimit > 0 ? Mathf.Min(ctxStackLimit, MaxStacks) : MaxStacks;
            for (int i = 0; i < ctxStacksPerApply; i++) s.stacks.Add(stack);
            while (s.stacks.Count > limit) s.stacks.RemoveAt(0);   // overflow drops the OLDEST
        }
        s.Refold();

        if (icon == null) icon = ctxIcon;          // fall back to the applier's context (set for this apply)
        if (name == null) name = ctxName;
        if (description == null) description = ctxDesc;
        if (icon != null) s.icon = icon;
        if (name != null) s.name = name;
        s.description = description;
        s.tint = tint == default ? Color.white : tint;
        Recompute();
        OnChanged?.Invoke();
        return s;
    }

    /// <summary>How many stacks of <paramref name="id"/> are live (0 if absent). The gate reads this for "min stacks".</summary>
    public int StackCount(StateId id)
    {
        EnsureFresh();
        var s = Find(id);
        return s != null ? s.StackCount : 0;
    }

    /// <summary>Spend up to <paramref name="count"/> stacks (oldest first), or ALL of them when
    /// <paramref name="all"/> is set. Returns how many were actually spent. The state is dropped once empty, clearing
    /// its icon. Callers must COMPUTE BEFORE CONSUMING — every computation reads the live store, never a snapshot.</summary>
    public int Consume(StateId id, int count, bool all = false)
    {
        EnsureFresh();
        var s = Find(id);
        if (s == null) return 0;
        int take = all ? s.stacks.Count : Mathf.Clamp(count, 0, s.stacks.Count);
        if (take <= 0) return 0;
        s.stacks.RemoveRange(0, take);           // oldest first — spend the stack nearest its expiry
        if (s.stacks.Count == 0) { active.Remove(s); pool.Push(s); }
        else s.Refold();
        Recompute();
        OnChanged?.Invoke();
        return take;
    }

    /// <summary>Remove a state now (e.g. Cleanse, or a consumed mark). No-op if absent.</summary>
    public void Remove(StateId id)
    {
        var s = Find(id);
        if (s == null) return;
        active.Remove(s);
        pool.Push(s);
        Recompute();
        OnChanged?.Invoke();
    }

    /// <summary>Drain the Shield barrier by up to <paramref name="dmg"/>; returns the amount soaked (0 if no barrier).
    /// The barrier entry is dropped when it breaks (pool hits 0), so its icon clears. Owner (Character) pops the number.</summary>
    public int DrainShield(int dmg)
    {
        if (dmg <= 0) return 0;
        EnsureFresh();
        var s = Find(StateId.Shield);
        if (s == null || s.intAmount <= 0) return 0;
        int soaked = Mathf.Min(s.intAmount, dmg);
        // Drain the OLDEST barrier stacks first (same order stacks expire and are spent), so several shields layered
        // on one unit break in a predictable order instead of all thinning at once.
        int left = soaked;
        for (int i = 0; i < s.stacks.Count && left > 0; i++)
        {
            var st = s.stacks[i];
            int take = Mathf.Min(st.intAmount, left);
            st.intAmount -= take; left -= take;
            s.stacks[i] = st;
        }
        for (int i = s.stacks.Count - 1; i >= 0; i--)
            if (s.stacks[i].intAmount <= 0) s.stacks.RemoveAt(i);   // spent barrier stacks drop out
        if (s.stacks.Count == 0) { active.Remove(s); pool.Push(s); }   // barrier broke → drop it (and its icon)
        else s.Refold();
        Recompute();
        OnChanged?.Invoke();
        return soaked;
    }

    /// <summary>Drop expired STACKS (and any state left with none); returns true if the set changed. Each stack dies on
    /// its own timer, so a pile thins one stack at a time. Cheap fixed loop — safe to call every frame.</summary>
    public bool Prune()
    {
        bool changed = false;
        float now = Time.time;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var s = active[i];
            var st = s.stacks;
            for (int k = st.Count - 1; k >= 0; k--)
                if (now >= st[k].endTime)
                {
                    // A DELAYED hit that ran out of time is a bill that must still be paid — capture it on the way out.
                    // Collected HERE, in the sweeper, because pruning happens from many places (any query, the tray,
                    // the nameplate): whoever drops the stack, the debt is banked for the ticker instead of vanishing.
                    // A stack a consumer CASHED never reaches this line, which is what separates "spent" from "expired".
                    if (s.id == StateId.DeferredDamage) pendingDebt += st[k].intAmount;
                    st.RemoveAt(k); changed = true;
                }
            if (st.Count == 0) { pool.Push(s); active.RemoveAt(i); changed = true; }
            else s.Refold();
        }
        if (changed) { Recompute(); OnChanged?.Invoke(); }
        return changed;
    }

    /// <summary>Damage whose delay ran out and which nobody cashed — collected by <see cref="Prune"/>, drained here by
    /// the one per-unit ticker that owns actually dealing it. Returns 0 (and stays 0) when nothing came due.
    ///
    /// <para>Kept as a running total rather than a per-pile lookup so it survives however many delaying skills are up,
    /// and so a pile pruned by an unrelated query can't slip through unbilled.</para></summary>
    public int TakeExpiredDebt() { int owed = pendingDebt; pendingDebt = 0; return owed; }
    int pendingDebt;

    /// <summary>Wipe all states (on death / revive).</summary>
    public void Clear()
    {
        pendingDebt = 0;   // death clears the slate: nothing owed follows you through a respawn
        if (active.Count == 0) return;
        for (int i = 0; i < active.Count; i++) pool.Push(active[i]);
        active.Clear();
        Recompute();
        OnChanged?.Invoke();
    }

    // Fold the active list into the cached scalars. Runs ONLY on a set change — never per query. Neutral defaults when
    // a state is absent (mult = 1, cut/penalty = 0). Slow × Haste multiply, matching StatusController.MoveMult.
    //
    // MAGNITUDE ENCODING — ONE rule, no stacking branch: magnitude is ALWAYS a BONUS FRACTION.
    //   • 0.3 means +30%, whether it came from one non-stacking buff or ten stacks of 0.03.
    //   • Mult-style channels therefore read 1 + magnitude, and Slow reads 1 − magnitude, unconditionally.
    // It used to depend on `stacking` — non-stacking stored the absolute multiplier (1.3 = ×1.3) while stacking stored
    // per-stack deltas — so the SAME authored number meant +30% on one skill and +130% on another. Nothing in the
    // composer or the tooltip could show which, and a stacking buff authored as 1.03 silently read as +103% per stack.
    // Converting to a fraction happens ONCE, at each entry point (StatusController.Haste, Character.Buff*, the Slow
    // applies), so callers keep their natural units and the store keeps one meaning. Additive channels (guard cut,
    // vuln, DoT, shield, leech, reflect, blind) were always fractions and are unaffected.
    void Recompute()
    {
        // Mult-style channels accumulate BONUSES (a ×1.3 pile contributes +0.30) and are converted back to a multiplier
        // once, at the end. Two +30% piles therefore read ×1.60, not ×1.69: every pile of a kind sits in the SAME
        // category, so a second one is worth the same as the first regardless of what's already running. Compounding
        // would make each buff's value depend on which others happen to be up, and make order matter.
        float slow = 1f, hasteBonus = 0f, vulnBonus = 0f;
        float aspdBonus = 0f, castBonus = 0f;   // DamageBuff is not cached here — see DamageBuffMult
        guardCut = 0f;
        leechPct = 0f; reflectBonus = 0f;
        hitPenalty = 0; shieldPool = 0; parryCharges = 0;
        cannotAct = false; silenced = false;
        nextExpiry = float.MaxValue;

        for (int i = 0; i < active.Count; i++)
        {
            var s = active[i];
            // Earliest STACK expiry drives EnsureFresh — a pile must wake up when its FIRST stack lapses, not its last.
            for (int k = 0; k < s.stacks.Count; k++)
                if (s.stacks[k].endTime < nextExpiry) nextExpiry = s.stacks[k].endTime;
            float mult = s.Mult;   // magnitude is a bonus fraction — State.Mult owns that rule
            switch (s.id)
            {
                case StateId.Slow:            slow = Mathf.Min(slow, Mathf.Clamp01(1f - s.magnitude)); break;   // harshest slow wins
                case StateId.Haste:           hasteBonus += (mult <= 0f ? 1f : mult) - 1f; break;
                case StateId.Stun:
                case StateId.Freeze:          cannotAct = true; break;
                case StateId.Silence:         silenced = true; break;
                case StateId.Blind:           hitPenalty += Mathf.RoundToInt(s.magnitude); break;
                // COMBINE, never overwrite. Since piles are keyed per SOURCE skill, the same kind can be active more
                // than once (Momentum and a War Banner both buffing attack) — an assignment here would silently drop
                // whichever the loop saw first. Everything ADDS (mult-style channels add their bonuses, pools sum);
                // only Slow takes the strongest, since two slows aren't a bigger slow.
                case StateId.Guard:           guardCut = Mathf.Clamp01(guardCut + s.magnitude); break;
                case StateId.Vulnerability:   vulnBonus += Mathf.Max(0f, s.magnitude); break;
                // DamageBuff is NOT summed here — DamageBuffMult walks the piles live so each pile's ramp stays current.
                case StateId.AttackSpeedBuff: aspdBonus += (mult <= 0f ? 1f : mult) - 1f; break;
                case StateId.CastSpeedBuff:   castBonus += (mult <= 0f ? 1f : mult) - 1f; break;
                case StateId.ManaLeech:       leechPct += s.magnitude; break;
                case StateId.Reflect:         reflectBonus += s.magnitude; break;
                case StateId.Shield:          shieldPool += s.intAmount; break;
                // Parry carries no per-stack value — the STACK COUNT *is* the charge count (one hit erased each).
                case StateId.Parry:           parryCharges += s.StackCount; break;
                // Poison/Bleed/Burn (DoT) + Regen tick in the (migrated) update loop; Taunt/Knockback/Pull drive
                // MonsterAI3D directly — none contribute a cached scalar, they're timer + display entries here.
            }
        }
        // Bonuses → multipliers, once. Floored at 0 so a stack of debuff-shaped piles can't drive a multiplier negative
        // (which would flip damage into healing).
        vulnMult     = Mathf.Max(0f, 1f + vulnBonus);
        aspdBuffMult = Mathf.Max(0f, 1f + aspdBonus);
        castBuffMult = Mathf.Max(0f, 1f + castBonus);
        moveMult = slow * Mathf.Max(0f, 1f + hasteBonus);
    }
}
