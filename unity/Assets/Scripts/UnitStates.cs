using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Every durational state a unit can carry, unified into one id space (buffs + debuffs). Mapped from a skill's
/// <c>SkillEffectKind</c> / <c>Ailment</c> at apply time.
///
/// <para>⚠️ <b>APPEND-ONLY.</b> A character's running states are saved BY NAME (<see cref="TimedEffectSave"/>), so a
/// renamed member drops out of old saves — but a skill's combo gate SERIALIZES a value here
/// (<c>SkillDef.requireState</c>), which Unity stores by int — so inserting or reordering a member would silently
/// repoint every skill asset that references one. Add new states at the END, exactly like <c>SkillEffectKind</c>.</para></summary>
public enum StateId
{
    // ---- Debuffs — ailments (was StatusController) ----
    // These MIRROR the Ailment enum one-for-one, by index (SkillEffect.TryStateFor casts one to the other), so the
    // two lists must be edited together and in the same order. Slow and Blind used to sit here; they only ever moved
    // a sub-stat (move speed, accuracy), so they live in those piles now and are gone from both lists.
    Poison, Bleed, Burn,          // DoT: magnitude = damage per tick
    Stun, Freeze,                 // hard-CC: CannotAct
    Silence,                      // blocks casting
    // Taunt owns the WINDOW for every taunted unit, mob or player — the brains hold only who the taunter is. Knockback
    // and Pull are still declared-but-unwritten (they are instant shoves with no window to hold).
    Taunt, Knockback, Pull,
    // ---- Sub-stat piles — ONE per sub-stat, buff and debuff alike ----------------------------------------------
    // Each holds a SIGNED bonus, so a debuff is the same pile carrying a negative number — there is no mirror pile to
    // build or keep in step. One pile PER STAT (rather than one shared pile with the stat written inside it) because
    // a pile's identity is what the modifier effects and the stack gate name: two stats buffed by one skill have to
    // be two piles, or "which pile" cannot tell them apart.
    MovementSpeed,                // was Haste
    DamageBuff, AttackSpeedBuff, CastSpeedBuff,   // magnitude = the stat's bonus fraction
    HPRegen,                      // was Regen. magnitude = HP per SECOND (mirrors the character's mana trickle)
    Shield,                       // intAmount = barrier HP pool  [NOT a sub-stat — a finite pool]
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
    // Parry's mirror on the OUTGOING side: stacks = CHARGES, and one charge multiplies one attack this unit sends,
    // spent by that attack whether it hits or misses. Each stack carries its own bonus, so a charge is worth what the
    // skill that granted it said — never what a later, different grant says.
    OutgoingDamageBoost,
    // The boost's mirror, and a SEPARATE pile on purpose: sharing one would let a penalty charge landed by an enemy
    // spend the boost charge you were saving, so a debuff could quietly eat a buff. Held apart, a unit carrying both
    // simply pays both on the same attack.
    OutgoingDamagePenalty,        // stacks = CHARGES. One charge weakens one attack this unit sends, spent hit or miss.
    // "Nothing stops me." Blocks every disable while it is up — see StatusController for the exact list it turns away.
    // Timed, not charged: it is a window you bought, and one hit does not use it up.
    DisableImmune,

    // ---- The rest of the sub-stat piles ---------------------------------------------------------------------------
    // APPENDED, never inserted above: a StateId is stored as a plain int in every authored gate and modifier, so a new
    // member in the middle would silently repoint saved data at a different pile. Same signed-bonus rule as the piles
    // in the buff block near the top — these simply had no pile before this change.
    MPRegen, LifeSteal,
    Attack, MagicAttack,
    Defense, MagicDefense,
    Accuracy, Evasion,
    CritChance, CritDamage,
    MaxHP, MaxMP,
    BasicAttackRange,

    // ---- Element converters ---------------------------------------------------------------------------------------
    // What this unit's ordinary attacks land as, and what it is hit as, while a converter runs. ONE stack each, and the
    // stack's intAmount is the Element — read off that stack, never off the pile's sum. Never stacking: one element on
    // one slot has no "twice". See BeginElement.
    WeaponElement, ArmorElement,

    // ---- Appended after the converters (APPEND-ONLY, see above) ----------------------------------------------------
    // The block-chance sub-stat's pile. Only a skill fills it; gear has no block of its own.
    BlockChance,
    // The extra-bounce-jumps sub-stat's pile: whole jumps added to every bounce chain the unit starts.
    ExtraBounceJumps,
    // Plain charges: stacks = CHARGES and nothing else — no stat moves while they are held. Filed under the skill that
    // gave them; a trigger gate naming that skill and this pile spends them.
    Charges,
}

/// <summary>
/// The unified per-unit STATE STORE (buffs + debuffs) — the single source for BOTH gameplay queries and the on-screen
/// display (player buff tray + mob/other-player nameplate icons). It replaces the scattered
/// <see cref="StatusController"/> ailment arrays, the <see cref="Character"/> buff windows, and
/// <see cref="Health"/>'s vulnerability — each migrated onto it one family at a time, behaviour-preserving.
///
/// <para><b>Optimized as agreed:</b> one list of active states is the source of truth, but the hot gameplay scalars
/// (<see cref="MoveMult"/>, the sub-stat table, …) are <b>cached</b> and
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
    /// Display fields carry the source skill's symbol for the tray / nameplate.</summary>
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
        /// <summary>WHICH UNIT put this state on, when that is known — set for ailments, by the newest application. A
        /// damage-over-time tick credits this unit as the one who cost the target health, so a kill by poison pays
        /// whoever poisoned it. Null for states nobody in particular applied.</summary>
        public ICombatUnit appliedBy;
        /// <summary>True when this state was applied as a stacking pile. Decides how <see cref="magnitude"/> READS:
        /// stacking = the sum of per-stack DELTAS (a mult channel is 1 + sum); non-stacking = the absolute value.</summary>
        public bool stacking;
        public float magnitude;    // SUM across stacks (DoT dmg/tick · Slow/Haste factor · Guard cut · Vuln frac · buff bonus · leech/reflect frac · Blind HIT)
        public int intAmount;      // SUM across stacks (Shield pool · Regen HP/tick)
        public float startTime, endTime;   // endTime = the LAST stack to expire (when the state is fully gone)
        public float nextTick;     // DoT / Regen cadence marker (owner ticks it)
        // Display (for the buff tray + nameplate tiles)
        public string symbol;
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
    // DamageBuff is deliberately absent here — DamageBuffMult sums its piles on read.
    float moveMult = 1f, aspdBuffMult = 1f, castBuffMult = 1f, leechPct, reflectBonus;
    int shieldPool, parryCharges;
    // The live signed bonus on every sub-stat, refilled by Recompute. One slot per SubStat, allocated once — the
    // alternative (walking the piles on each read) is paid per frame by movement and per hit by damage and leech.
    readonly float[] subStatBonus = new float[SubStats.All.Length];
    bool cannotAct, silenced, taunted;
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
    // tray/nameplate symbol AND its stacking rules, WITHOUT threading either through every gameplay method. The applier
    // owns the lifecycle (SetApplyContext before the call, ClearApplyContext after); Apply only reads what it was given.
    string ctxSymbol, ctxName, ctxDesc, ctxSourceId;
    bool ctxStacking; int ctxStacksPerApply = 1, ctxStackLimit;

    /// <summary>Tag the NEXT applied state with its SOURCE skill, display info and stacking rules. Set by the skill
    /// applier before a durational gameplay call; always pair with <see cref="ClearApplyContext"/> after.
    /// <paramref name="stacking"/> off = today's single-instance, newest-wins behaviour.
    /// <paramref name="sourceSkillId"/> is the granting skill's id — it keys the pile (see
    /// <see cref="State.sourceSkillId"/>), so leaving it empty pools this state with every other sourceless one.</summary>
    public void SetApplyContext(string symbol, string name, string description,
                                bool stacking = false, int stacksPerApply = 1, int stackLimit = 0,
                                string sourceSkillId = null)
    {
        ctxSymbol = symbol; ctxName = name; ctxDesc = description;
        ctxSourceId = sourceSkillId ?? "";
        ctxStacking = stacking;
        ctxStacksPerApply = Mathf.Max(1, stacksPerApply);
        ctxStackLimit = stackLimit;
    }

    public void ClearApplyContext()
    {
        ctxSymbol = null; ctxName = null; ctxDesc = null; ctxSourceId = "";
        ctxStacking = false; ctxStacksPerApply = 1; ctxStackLimit = 0;
    }

    // Lazy self-expiry: prune (and recompute the cached scalars) ONLY when a state has actually elapsed. One float
    // compare on the common path — so there's no per-frame Update and no staleness, keeping the old lazy-expiry cost.
    void EnsureFresh()
    {
        if (WornChanged()) Recompute();
        if (active.Count > 0 && Time.time >= nextExpiry) Prune();
    }

    // ---- what the unit WEARS -----------------------------------------------------------------------------------
    // An armour piece's rolled word adds to the SAME sub-stat table a timed buff moves, folded in by Recompute — the one
    // lift that hands worn words to every reader of those stats, player and creature alike, with no per-stat code. The
    // gear sits on this same object for both kinds of unit; it is looked up once, and a change is noticed by comparing
    // one number, never by walking the set.
    UnitEquipment wornGear;
    bool wornLooked;
    int wornVersion = int.MinValue;
    EquipBonuses worn;

    bool WornChanged()
    {
        if (!wornLooked && this != null) { wornGear = GetComponent<UnitEquipment>(); wornLooked = true; }   // perf-ok: once per store
        if (wornGear == null) return false;
        int v = wornGear.Version;
        if (v == wornVersion) return false;
        wornVersion = v;
        worn = wornGear.Bonuses;
        return true;
    }

    /// <summary>Worn crit shield, as the LUK it is worth to the crit roll (Ironblood).</summary>
    public int WornCritShieldLuk { get { EnsureFresh(); return UnitStatMath.CritShieldAsLuk(worn.wordCritShieldPercent); } }
    /// <summary>How much shorter a disable or an ailment on this unit runs, as a fraction (Steadfast).</summary>
    public float WornDisableCut { get { EnsureFresh(); return Mathf.Clamp01(worn.wordDisableCutPercent / 100f); } }
    /// <summary>How much slower sprinting drains stamina, as a fraction (Tireless).</summary>
    public float WornStaminaCut { get { EnsureFresh(); return Mathf.Clamp01(worn.wordStaminaCutPercent / 100f); } }
    /// <summary>How much less far a knockback or a pull moves this unit, as a fraction (Anchored).</summary>
    public float WornShoveCut { get { EnsureFresh(); return Mathf.Clamp01(worn.wordShoveCutPercent / 100f); } }
    /// <summary>The worn cut to damage of <paramref name="attack"/>'s element, as a fraction (the garment wards).</summary>
    public float WardAgainst(Element attack) { EnsureFresh(); return Mathf.Clamp01(ArmourWords.WardFraction(worn, attack)); }

    /// <summary>Slow × Haste move-speed multiplier (1 = normal). Does NOT include the caster's action-lock (casting /
    /// aftercast root) — that stays with the mover, exactly as <c>StatusController.MoveMult</c> layered it.</summary>
    public float MoveMult { get { EnsureFresh(); return moveMult; } }
    /// <summary>Stunned or Frozen — can't act / move.</summary>
    public bool CannotAct { get { EnsureFresh(); return cannotAct; } }
    /// <summary>Spell-casting blocked (Silence).</summary>
    public bool Silenced { get { EnsureFresh(); return silenced; } }
    /// <summary>Outgoing-damage multiplier summed over EVERY live DamageBuff pile — the one answer to "how much is my
    /// damage buffed", for player and mob alike.
    ///
    /// <para>Reading only the FIRST pile here is the bug this replaced: piles are keyed by GRANTING SKILL, so Momentum
    /// and Lock Blades are two separate damage piles, and whichever landed first silently ate the other. Summing every
    /// pile of a sub-stat is now the shared rule (see <see cref="SubStatBonus"/>), so this cannot regress alone.</para></summary>
    public float DamageBuffMult => Mathf.Max(0f, 1f + SubStatBonus(SubStat.Damage));
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
    /// mechanic (regen/DoT tick, shield drain) and read/mutate its params on the unified entry — so the
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
            LogStacksGone(s, take, "spent");
            if (s.stacks.Count == 0) { active.RemoveAt(i); pool.Push(s); }
            else s.Refold();
            if (!all && spent >= count) break;
        }
        if (spent > 0) { Recompute(); OnChanged?.Invoke(); }
        return spent;
    }

    /// <summary>Push out the life of ONE pile — every stack of the state <paramref name="id"/> granted by
    /// <paramref name="sourceSkillId"/> gets <paramref name="seconds"/> more. Returns how many stacks were extended.
    ///
    /// <para><b>Extends what is THERE; it never creates.</b> A pile that has already lapsed is gone, and re-creating it
    /// here would mean inventing a stack's value out of nothing — a lengthener has no idea what one of that skill's
    /// charges is worth. So "extend a storm that ended" does nothing, which is the honest answer and the one a player
    /// can read off the screen.</para>
    ///
    /// <para>EVERY stack moves, not just the last: the pile drains one stack at a time, so lengthening only the final
    /// one would let the front of the pile keep expiring on the old clock.</para></summary>
    public int ExtendFrom(string sourceSkillId, StateId id, float seconds)
    {
        if (string.IsNullOrEmpty(sourceSkillId) || seconds <= 0f) return 0;
        EnsureFresh();
        int moved = 0;
        for (int i = 0; i < active.Count; i++)
        {
            var s = active[i];
            if (s.sourceSkillId != sourceSkillId || s.id != id) continue;
            for (int k = 0; k < s.stacks.Count; k++)
            {
                var st = s.stacks[k];
                st.endTime += seconds;
                s.stacks[k] = st;
                moved++;
            }
            s.Refold();
        }
        if (moved > 0) { Recompute(); OnChanged?.Invoke(); }
        return moved;
    }

    /// <summary>Add stacks to ONE pile — the state <paramref name="id"/> granted by <paramref name="sourceSkillId"/>.
    /// Returns how many were actually added. This is how a skill feeds ANOTHER skill's pile (a kill lengthening a
    /// storm), which the normal grant path cannot do: a grant always files under the skill that ran it.
    ///
    /// <para><b>A new stack matches what one of that skill's charges IS</b> — never a value invented here, which would
    /// be a second, silently different definition of the same charge. Running pile: copy its newest stack. Not running:
    /// the caller passes what the granting skill's own effect authors (<paramref name="magnitude"/> /
    /// <paramref name="seconds"/>), so the pile starts exactly as that skill would have started it. Either way the
    /// answer comes from the granter, so a skill that is merely OFF COOLDOWN can still be fed.</para></summary>
    public int AddStacksTo(string sourceSkillId, StateId id, int count, float magnitude, float seconds,
                           string symbol = null, string name = null, string description = null)
    {
        if (string.IsNullOrEmpty(sourceSkillId) || count <= 0) return 0;
        EnsureFresh();
        int added = 0;
        var s = Find(id, sourceSkillId);
        if (s != null && s.stacks.Count > 0)
        {
            var template = s.stacks[s.stacks.Count - 1];               // the newest — the freshest window on the pile
            int take = Mathf.Min(count, Mathf.Max(0, MaxStacks - s.stacks.Count));
            for (int k = 0; k < take; k++) s.stacks.Add(template);
            added = take;
        }
        else if (seconds > 0f)
        {
            // Nothing running: start the pile the granting skill's own way. Its window is what makes this honest — a
            // stack with no deadline would never leave, and a pile nobody can see ending is not a charge.
            s = pool.Count > 0 ? pool.Pop() : new State();
            s.id = id; s.sourceSkillId = sourceSkillId; s.stacking = true;
            s.startTime = Time.time; s.nextTick = 0f;
            s.symbol = symbol; s.name = name; s.description = description; s.tint = Color.white;
            s.stacks.Clear();
            added = Mathf.Min(count, MaxStacks);
            for (int k = 0; k < added; k++)
                s.stacks.Add(new State.Stack { endTime = Time.time + seconds, magnitude = magnitude, intAmount = 0 });
            active.Add(s);
        }
        if (added > 0) { s.Refold(); Recompute(); OnChanged?.Invoke(); }
        return added;
    }

    /// <summary>Move ONE sub-stat on THIS unit for a window — the single place every sub-stat buff and debuff lands,
    /// for the player and for a mob alike (a sub-stat belongs to the unit, not to who is driving it).
    ///
    /// <paramref name="bonus"/> is SIGNED and is already in the stat's own terms: a fraction of the stat for the
    /// percentage stats, an amount for the rest (<see cref="SubStats.IsPercent"/> decides which, and the caller has
    /// already asked). A debuff is simply a negative bonus — the same pile, so a buff and a debuff on one stat
    /// cancel out honestly instead of racing in two separate places.
    ///
    /// <para>A buff is worth exactly what it says: there is no growth-over-time channel any more (removed 2026-08-17,
    /// unused and unreadable on screen). Uptime is the support's contribution.</para></summary>
    /// <returns>TRUE when this apply STARTED a pile — the granting skill had none running on this stat — and false
    /// when it grew or refreshed one already running, or applied nothing. What a "this effect began" cue waits for.</returns>
    public bool ApplySubStat(SubStat stat, float bonus, float seconds)
    {
        if (seconds <= 0f || Mathf.Approximately(bonus, 0f)) return false;
        var pile = SubStats.PileFor(stat);
        EnsureFresh();   // a lapsed pile is gone before Apply looks, so re-applying it counts as a fresh start
        bool started = Find(pile, ctxSourceId) == null;
        Apply(pile, seconds, bonus);
        return started;
    }

    /// <summary>The live SIGNED bonus on one sub-stat, summed over every pile holding it — what a buff and a debuff on
    /// the same stat add up to. 0 when nothing is running.
    ///
    /// <para>Piles are keyed by GRANTING SKILL, so one stat can be held by several piles at once (two skills both
    /// raising evasion). They ADD: every pile of a stat sits in the same category, so a second is worth the same as
    /// the first no matter what is already up, and no ordering can change the answer.</para>
    ///
    /// <para>Read from a table <see cref="Recompute"/> already filled, not by walking the piles: several of these are
    /// read per frame (movement) or per hit (damage, leech), and the walk would be paid again at every one.</para></summary>
    public float SubStatBonus(SubStat stat)
    {
        EnsureFresh();
        return subStatBonus[(int)stat];
    }

    // ---- the state STARTERS — one per state family, valid on ANY unit ------------------------------------------
    // These used to live on Character (the player) and Health (the mob), which is the whole reason "a guard/shield/
    // parry/regen/leech/reflect can only be given to the player" was true: the store never cared who it sat on, but
    // only the player's class had a method to write them. They are all the same shape — validate, then one Apply — so
    // they belong to the store itself. Now a mob can be shielded, a player can be made vulnerable, and neither needed
    // a new code path. Keep new state families here, NOT on a unit class, so both sides inherit them for free.

    /// <summary>Seconds between heal-over-time ticks. Owned here because the state is; the unit's ticker reads it.</summary>
    public const float RegenInterval = 0.8f;

    // HP regen, mana leech and reflect used to have a starter each here. They are sub-stats now, so they arrive
    // through ApplySubStat like every other one — a second door to the same pile is exactly what that collapse
    // removed, and keeping one "because it reads nicely" is how the two drift apart.

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

    /// <summary>Hold plain charges for <paramref name="seconds"/>. HOW MANY comes from the apply context's
    /// stacks-per-apply, the same knob Parry's charges use. They change nothing on their own.</summary>
    public void BeginCharges(float seconds)
    {
        if (seconds <= 0f) return;
        Apply(StateId.Charges, seconds);
    }

    /// <summary>Hold outgoing-damage-boost charges for <paramref name="seconds"/> — each multiplies ONE attack this
    /// unit sends by 1 + <paramref name="bonus"/> and is spent by it. HOW MANY comes from the apply context's
    /// stacks-per-apply, the same knob Parry's charges use, so the stacks ARE the charges.</summary>
    public void BeginOutgoingBoost(float seconds, float bonus)
    {
        if (seconds <= 0f || bonus <= 0f) return;
        Apply(StateId.OutgoingDamageBoost, seconds, bonus);
    }

    /// <summary>Hold outgoing-damage-penalty charges for <paramref name="seconds"/> — each multiplies ONE attack this
    /// unit sends by 1 − <paramref name="cut"/> and is spent by it. The mirror of <see cref="BeginOutgoingBoost"/>
    /// down to the knob it reads: HOW MANY comes from the apply context's stacks-per-apply.</summary>
    public void BeginOutgoingPenalty(float seconds, float cut)
    {
        if (seconds <= 0f || cut <= 0f) return;
        Apply(StateId.OutgoingDamagePenalty, seconds, cut);
    }

    /// <summary>Turn away every disable for <paramref name="seconds"/> — see <see cref="StateId.DisableImmune"/>.
    /// A plain window: it is not spent by what it blocks, so one stun does not use it up.</summary>
    public void BeginDisableImmunity(float seconds)
    {
        if (seconds <= 0f) return;
        Apply(StateId.DisableImmune, seconds);
    }

    /// <summary>Convert this unit's weapon (<see cref="StateId.WeaponElement"/>) or armour (<see cref="StateId.ArmorElement"/>)
    /// to <paramref name="element"/> for <paramref name="seconds"/>. REPLACES whatever converted that slot before, whichever
    /// skill granted it: piles are keyed by the granting skill, so without clearing first a second converter would open
    /// a pile beside the first instead of taking its place. Stacking is forced off whatever the applier asked for.</summary>
    public void BeginElement(StateId slot, Element element, float seconds)
    {
        if (seconds <= 0f || (slot != StateId.WeaponElement && slot != StateId.ArmorElement)) return;
        while (Find(slot) != null) Remove(slot);
        bool askedToStack = ctxStacking;
        ctxStacking = false;
        Apply(slot, seconds, 0f, (int)element);
        ctxStacking = askedToStack;
    }

    /// <summary>The element a converter has put on <paramref name="slot"/>, when one is running.</summary>
    public bool TryConvertedElement(StateId slot, out Element element)
    {
        EnsureFresh();
        var s = Find(slot);
        if (s != null && s.stacks.Count > 0)
        {
            element = (Element)s.stacks[s.stacks.Count - 1].intAmount;
            return true;
        }
        element = Element.Neutral;
        return false;
    }

    /// <summary>What this unit's ordinary attacks land as: a weapon converter's element while one runs, otherwise
    /// <paramref name="natural"/> (the weapon's, or the creature's own). The ONE answer both kinds of unit read.</summary>
    public Element AttackElementOr(Element natural) =>
        TryConvertedElement(StateId.WeaponElement, out var converted) ? converted : natural;

    /// <summary>What this unit is hit as: an armour converter's element at <see cref="Elements.ConverterLevel"/> while
    /// one runs, otherwise its own element at its own level. The ONE answer both kinds of unit read.</summary>
    public Element DefendingElementOr(Element natural, int naturalLevel, out int level)
    {
        if (TryConvertedElement(StateId.ArmorElement, out var converted))
        {
            level = Elements.ConverterLevel;
            return converted;
        }
        level = naturalLevel;
        return natural;
    }

    /// <summary>Is every disable being turned away right now? Read by the flinch, the ailment door, the shove door and
    /// the cast wind-up, so one window covers all of them and none of them keeps its own copy of the answer.</summary>
    public bool DisablesBlocked => Has(StateId.DisableImmune);

    /// <summary>Lock this unit onto whoever taunted it for <paramref name="seconds"/> — see <see cref="StateId.Taunt"/>.
    /// A plain window like <see cref="BeginDisableImmunity"/>: nothing spends it, it simply runs out.
    ///
    /// <para>The window lives HERE, not in either brain, because both sides of the taunt need the same answer: the mob
    /// brain and the player rig each used to keep a private end-time, and the "no skills while taunted" rule has to be
    /// readable by a unit that has no brain in front of it at all. Who the taunter IS stays with the brain — that is
    /// the role-specific half; how long the lock lasts is the shared half.</para></summary>
    public void BeginTaunt(float seconds, ICombatUnit taunter)
    {
        if (seconds <= 0f) return;
        var s = Apply(StateId.Taunt, seconds);
        // The newest taunt decides who this unit is locked onto, so it also decides whose death releases it.
        if (s != null) s.appliedBy = PlayerUnits.Gone(taunter) ? null : taunter;
    }

    /// <summary>End every taunt whose taunter has died or is no longer in this unit's map — the lock is ONTO that
    /// unit, so without it there is nothing left to be locked onto. Called by the server's per-unit ticker; a
    /// player's machine never decides this, it only shows the state leaving when the server says so. Costs one
    /// cached bool while the unit is not taunted.</summary>
    public void EndTauntIfTaunterGone()
    {
        EnsureFresh();
        if (!taunted) return;
        var me = OwnerUnit();
        bool changed = false;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var s = active[i];
            if (s.id != StateId.Taunt || s.appliedBy == null) continue;
            var by = s.appliedBy;
            if (!PlayerUnits.Gone(by) && !by.IsDead && (me == null || by.Map == me.Map)) continue;
            LogStacksGone(s, s.stacks.Count, "ended, the taunter died or left the map", wholePile: true);
            active.RemoveAt(i);
            pool.Push(s);
            changed = true;
        }
        if (changed) { Recompute(); OnChanged?.Invoke(); }
    }

    /// <summary>Is this unit taunted right now? THE single answer — the mob brain's chase lock, the player rig's
    /// retarget lock and the skill door all read this one window rather than each timing their own. Cached like the
    /// other hot flags, because the brain asks every frame.</summary>
    public bool Taunted { get { EnsureFresh(); return taunted; } }

    // ---- consecutive hits on one body ---------------------------------------------------------------------------
    // Counted for EVERY unit, always, with nothing authored anywhere: a run of hits is a fact about the fight, not
    // something a skill hands out. Deliberately NOT a StateId — it has no window to show and nothing to spend, so a
    // state entry would carry a duration knob, a stack-limit knob and a consume knob that all did nothing.
    //
    // WHICH body the run is against, and WHICH LIFE of that body: a dead mob is pooled and returns as the SAME object,
    // so the body alone can't tell "still the one I was hitting" from "a brand-new mob wearing a corpse".
    Transform streakTarget;
    int streakTargetLife;

    /// <summary>This unit's unbroken run of hits on one body — 0 when there is no run. Read by a skill's stack gate;
    /// nothing spends it, because a run is ended by the fight, never by a skill cashing it.</summary>
    public int ConsecutiveHits { get; private set; }

    /// <summary>How many lives this unit has started. Bumped at each revive choke, and read by whoever is counting a
    /// run against this body — see <see cref="NoteAttackHit"/>.</summary>
    public int LifeCount { get; private set; }

    /// <summary>A new life starts here: nobody's run of hits on the old one carries over, and this unit's own run dies
    /// with the life that built it.</summary>
    public void NoteNewLife()
    {
        LifeCount++;
        BreakConsecutiveHits();
    }

    /// <summary>This unit just sent a hit at <paramref name="victim"/>. Called BEFORE the damage lands, so the count is
    /// already right when the victim's damage path fires the moment a skill answers. Switching bodies restarts the run
    /// AT this hit rather than wiping it — a new target's first hit is a run of one.</summary>
    public void NoteAttackHit(ICombatUnit victim)
    {
        var body = victim != null ? victim.Body : null;
        int life = victim != null && victim.States != null ? victim.States.LifeCount : 0;
        if (body != streakTarget || life != streakTargetLife)
        {
            streakTarget = body;
            streakTargetLife = life;
            ConsecutiveHits = 0;
        }
        ConsecutiveHits++;
    }

    /// <summary>That hit cost the victim nothing — a miss, a parry, a full absorb, an immune element — so the run is
    /// over. Separate from <see cref="NoteAttackHit"/> because the two answers arrive at different times: WHO is being
    /// hit is known before the hit, WHAT it actually dealt only after.</summary>
    public void BreakConsecutiveHits()
    {
        streakTarget = null;
        streakTargetLife = 0;
        ConsecutiveHits = 0;
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
            string symbol = armed.symbol; string label = armed.name; string desc = armed.description;
            armed.stacks.RemoveAt(0);
            LogStacksGone(armed, 1, "spent");
            if (armed.stacks.Count == 0) { active.RemoveAt(i); pool.Push(armed); }   // out of charges → the armed state (and its tile) go
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
                // Same symbol as the armed charges — it's the same skill — but tinted RED, because the two mean
                // opposite things: armed is protection you still hold, a debt is about to hurt you. The tray tints the
                // letters with this, so one value separates them with no second face to draw.
                debt.symbol = symbol; debt.name = label; debt.description = desc;
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

    /// <summary>Apply a state for <paramref name="seconds"/>. Two behaviours, chosen by the apply context
    /// (<see cref="SetApplyContext"/>) so no gameplay method needs a stacking parameter:
    /// <list type="bullet">
    /// <item><b>Non-stacking (default)</b> — <b>newest-wins</b>: the single stack's window + magnitude are REPLACED,
    /// even if weaker (the State-System doc's locked overwrite policy).</item>
    /// <item><b>Stacking</b> — APPENDS <c>stacksPerApply</c> stacks, each with its own window and its own magnitude;
    /// the total is their SUM. Past the limit the OLDEST stacks drop.</item>
    /// </list>
    /// Display args are optional — pass the source skill's symbol/name/tint for the tray / nameplate. Returns the entry so
    /// a caller can seed per-family extras (e.g. a DoT's first <see cref="State.nextTick"/>).</summary>
    public State Apply(StateId id, float seconds, float magnitude = 0f, int intAmount = 0,
                       string symbol = null, string name = null, Color tint = default, string description = null)
    {
        if (seconds <= 0f) return null;
        EnsureFresh();   // drop an already-lapsed entry first, so a re-apply is treated as FRESH (restarts startTime)
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
            s.appliedBy = null;   // a pooled entry must not carry an earlier life's inflictor
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
            int overLimit = s.stacks.Count - limit;
            while (s.stacks.Count > limit) s.stacks.RemoveAt(0);   // overflow drops the OLDEST
            if (overLimit > 0) LogStacksGone(s, overLimit, "dropped over the stack limit");
        }
        s.Refold();

        if (symbol == null) symbol = ctxSymbol;    // fall back to the applier's context (set for this apply)
        if (name == null) name = ctxName;
        if (description == null) description = ctxDesc;
        if (symbol != null) s.symbol = symbol;
        if (name != null) s.name = name;
        s.description = description;
        s.tint = tint == default ? Color.white : tint;
        Recompute();

        // A BUFF ARRIVING OR GROWING. Written here because this is the one door every state comes through, so no
        // way of granting one can forget to say so. A play-test spent two runs unable to tell whether a pile had
        // been granted at all, reading an empty store that was empty only because the reading was done wrong.
        if (GameLog.On(LogCat.Buff))
            GameLog.Info(LogCat.Buff, string.Concat(
                OwnerMap(), " ", s.id.ToString(), " x", s.stacks.Count.ToString(), " on ", OwnerName(),
                " from ", string.IsNullOrEmpty(s.sourceSkillId) ? "(unnamed)" : s.sourceSkillId,
                " for ", seconds.ToString("0.##"), "s"));

        OnChanged?.Invoke();
        return s;
    }

    /// <summary>Whose store this is, for a log line. The null test is Unity's, so it also catches a store whose
    /// body has been destroyed — a log line must never be the thing that breaks a fight.</summary>
    /// <summary>A pile losing stacks, or going altogether — "field_15 Parry on Alpha #1 from skill.sword.parry: 1 spent,
    /// 0 left". Written at every place a state loses stacks, so a charge that was used and a buff that ran out read as
    /// the two different things they are. Call BEFORE the state goes back to the pool: its fields are read here.
    /// <paramref name="wholePile"/> says the pile is going altogether, so nothing is left whatever its list still holds.</summary>
    void LogStacksGone(State s, int gone, string how, bool wholePile = false)
    {
        if (!GameLog.On(LogCat.Buff) || s == null) return;
        GameLog.Info(LogCat.Buff, string.Concat(new[]
        {
            OwnerMap(), " ", s.id.ToString(), " on ", OwnerName(), " from ",
            string.IsNullOrEmpty(s.sourceSkillId) ? "(unnamed)" : s.sourceSkillId,
            ": ", gone.ToString(), " ", how, ", ", wholePile ? "0" : s.stacks.Count.ToString(), " left",
        }));
    }

    string OwnerName()
    {
        var unit = OwnerUnit();
        return unit != null ? SpawnIds.Label(unit) : this != null ? gameObject.name : "somebody";
    }

    string OwnerMap()
    {
        var unit = OwnerUnit();
        return unit != null ? SpawnIds.MapOf(unit) : "nowhere";
    }

    // THE UNIT THIS STORE BELONGS TO, looked up once and only by the log line above: a store always sits on the same
    // object as the creature or character that owns it.
    ICombatUnit ownerUnit;
    bool ownerLooked;
    ICombatUnit OwnerUnit()
    {
        if (!ownerLooked && this != null) { ownerUnit = GetComponent<ICombatUnit>(); ownerLooked = true; }
        return ownerUnit;
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
        LogStacksGone(s, take, "spent");
        if (s.stacks.Count == 0) { active.Remove(s); pool.Push(s); }
        else s.Refold();
        Recompute();
        OnChanged?.Invoke();
        return take;
    }

    /// <summary>Spend ONE outgoing-damage-boost charge and report what it is worth as a multiplier — 1 when this unit
    /// holds none, so the caller can multiply unconditionally. The charge goes whether the attack that asked for it
    /// goes on to hit or miss: it was spent making the attack, exactly as an attack-intent gate is.
    ///
    /// <para>The bonus is read off THE CHARGE BEING SPENT rather than the pile's folded total, so a pile of three
    /// +100% charges is three doubled attacks, never one quadrupled one — the multiplicity is the stacking, and this
    /// kind owns none of its own. Oldest first, matching every other spend.</para></summary>
    public float SpendOutgoingBoost()
    {
        EnsureFresh();
        var s = Find(StateId.OutgoingDamageBoost);
        if (s == null || s.stacks.Count == 0) return 1f;
        float mult = 1f + s.stacks[0].magnitude;
        Consume(StateId.OutgoingDamageBoost, 1);
        return mult;
    }

    /// <summary>Spend ONE outgoing-damage-penalty charge and return what it leaves the attack worth (1 = none held).
    /// Floored at 0 so a 100%-cut charge means "this attack does nothing", never healing the target.
    ///
    /// <para>Read off THE CHARGE BEING SPENT, oldest first, for the same reason the boost is: three 40% charges are
    /// three separately-weakened attacks, not one attack cut by 120%.</para></summary>
    public float SpendOutgoingPenalty()
    {
        EnsureFresh();
        var s = Find(StateId.OutgoingDamagePenalty);
        if (s == null || s.stacks.Count == 0) return 1f;
        float mult = Mathf.Max(0f, 1f - s.stacks[0].magnitude);
        Consume(StateId.OutgoingDamagePenalty, 1);
        return mult;
    }

    /// <summary>Remove every pile one skill granted — its buff ends at once (a weapon's draw buff when that weapon is
    /// swapped out). No-op if it granted none.</summary>
    public void RemoveFrom(string sourceSkillId)
    {
        if (string.IsNullOrEmpty(sourceSkillId)) return;
        bool any = false;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var s = active[i];
            if (s.sourceSkillId != sourceSkillId) continue;
            LogStacksGone(s, s.stacks.Count, "removed", wholePile: true);
            active.RemoveAt(i);
            pool.Push(s);
            any = true;
        }
        if (!any) return;
        Recompute();
        OnChanged?.Invoke();
    }

    /// <summary>Remove a state now (e.g. Cleanse, or a consumed mark). No-op if absent.</summary>
    public void Remove(StateId id)
    {
        var s = Find(id);
        if (s == null) return;
        LogStacksGone(s, s.stacks.Count, "removed", wholePile: true);
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
        int barrierStacks = s.stacks.Count;
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
        int broke = barrierStacks - s.stacks.Count;
        if (broke > 0) LogStacksGone(s, broke, "used up");
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
            int stacksBefore = st.Count;
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
            if (st.Count < stacksBefore) LogStacksGone(s, stacksBefore - st.Count, "ran out");
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
        for (int i = 0; i < active.Count; i++)
        {
            LogStacksGone(active[i], active[i].stacks.Count, "cleared", wholePile: true);
            pool.Push(active[i]);
        }
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
    // ---- written into the character's save, and read back ---------------------------------------------------------

    /// <summary>Every live pile, for the character's save. Reads the piles WITHOUT sweeping out the expired ones: a
    /// sweep would move delayed damage that just ran out into the running total, which is no pile and would be lost —
    /// so that total rides along as a debt due at once. A taunt is never kept: it points at whoever taunted, who will
    /// not exist when the save is read back, and a taunt nobody holds would stop every skill for its whole window.</summary>
    public void CaptureForSave(List<SaveData.TimedEffect> into)
    {
        if (into == null) return;
        into.Clear();
        float now = Time.time;
        for (int i = 0; i < active.Count; i++)
        {
            if (active[i].id == StateId.Taunt) continue;
            var rec = TimedEffectSave.Capture(active[i], now);
            if (rec != null) into.Add(rec);
        }
        if (pendingDebt > 0) into.Add(TimedEffectSave.DebtDueNow(pendingDebt));
    }

    /// <summary>Replace everything in this store with what a save says was running, each clock resuming where it
    /// stopped. Server only — a player's machine is told its states by the server, never works them out. A pile from
    /// a skill that no longer exists is dropped, because it would show as a tile with no name.</summary>
    public void RestoreFromSave(SaveData.TimedEffect[] saved)
    {
        Clear();
        if (saved == null || saved.Length == 0) return;
        float now = Time.time;
        for (int i = 0; i < saved.Length; i++)
        {
            var rec = saved[i];
            if (!TimedEffectSave.TryKind(rec, out var id) || id == StateId.Taunt) continue;
            string sourceId = rec.sourceSkillId ?? "";
            var src = sourceId.Length > 0 ? SkillCatalog.Get(sourceId) : null;
            if (sourceId.Length > 0 && src == null) continue;

            var e = pool.Count > 0 ? pool.Pop() : new State();
            if (!TimedEffectSave.Restore(rec, now, e)) { pool.Push(e); continue; }
            e.id = id;
            e.sourceSkillId = sourceId;
            e.appliedBy = null;
            e.symbol = src != null ? src.Symbol : "";
            e.name = src != null ? src.displayName : "";
            e.tint = id == StateId.DeferredDamage ? DebtTint : Color.white;
            active.Add(e);

            if (GameLog.On(LogCat.Buff))
                GameLog.Info(LogCat.Buff, string.Concat(
                    OwnerMap(), " ", e.id.ToString(), " x", e.stacks.Count.ToString(), " on ", OwnerName(),
                    " from ", sourceId.Length == 0 ? "(unnamed)" : sourceId,
                    " restored from the save, ", Mathf.Max(0f, e.endTime - now).ToString("0.##"), "s left"));
        }
        Recompute();
        OnChanged?.Invoke();
    }

    // ---- the server's word, on a machine that only watches -------------------------------------------------------

    /// <summary>ONE state as it crosses the wire — what the owner's machine needs in order to SHOW a pile and to
    /// move at the speed it grants. Times travel as SECONDS LEFT rather than as a moment: two machines do not share
    /// a clock, and a moment taken from the other one is a moment in the wrong century.</summary>
    public struct Mirrored
    {
        public StateId id;
        public string sourceSkillId;
        public bool stacking;
        public float magnitude;
        public int intAmount;
        public float secondsLeft;
        public float secondsTotal;
        public int stacks;
        public string description;
        public Color tint;

        // The two-letter tag and the skill's name are NOT carried. Both are the GRANTING SKILL'S OWN, read straight
        // off its asset — and every machine already has every skill asset on disk, so sending them was sending a
        // person a copy of something they were holding. `sourceSkillId` above is what they are looked up by.
    }

    /// <summary>Fill <paramref name="into"/> with this store's live states, for sending to the machine of the person
    /// it belongs to. Caller-owned list, per the rule against handing back a fresh collection.</summary>
    public void SnapshotForOwner(List<Mirrored> into)
    {
        if (into == null) return;
        into.Clear();
        EnsureFresh();
        float now = Time.time;
        for (int i = 0; i < active.Count; i++)
        {
            var a = active[i];
            into.Add(new Mirrored
            {
                id = a.id,
                sourceSkillId = a.sourceSkillId ?? "",
                stacking = a.stacking,
                magnitude = a.magnitude,
                intAmount = a.intAmount,
                secondsLeft = Mathf.Max(0f, a.endTime - now),
                secondsTotal = Mathf.Max(0.01f, a.endTime - a.startTime),
                stacks = a.StackCount,
                description = a.description ?? "",
                tint = a.tint,
            });
        }
    }

    /// <summary>Replace everything in this store with what the server says is on this character.
    ///
    /// <para>A WHOLE PICTURE, not a list of changes: the server is the only thing that knows what is running, and a
    /// stream of edits would drift the first time one went missing. Whatever this machine had is dropped, so a pile
    /// its own ticking wrongly spent or expired is simply put back on the next word from the server.</para>
    ///
    /// <para>The stacks are rebuilt as N copies sharing the pile's own window, which is what the tray counts and what
    /// every reader of a total reads. A per-stack expiry is the granting machine's business — nothing here spends a
    /// charge, because nothing here decides anything.</para></summary>
    // THE HOVER SENTENCE THIS MACHINE ALREADY HOLDS, kept across a rebuild. The server sends that sentence once,
    // when a state first appears, and leaves it out of every push after — it never changes while the state runs, and
    // re-posting a paragraph on every tick of a fight is the whole of what this saves. Reused, so keeping it costs
    // no memory per push.
    static readonly Dictionary<(StateId, string), string> keptDescriptions =
        new Dictionary<(StateId, string), string>();

    public void MirrorFromServer(List<Mirrored> from)
    {
        keptDescriptions.Clear();
        for (int i = 0; i < active.Count; i++)
        {
            var had = active[i];
            if (!string.IsNullOrEmpty(had.description))
                keptDescriptions[(had.id, had.sourceSkillId ?? "")] = had.description;
        }

        for (int i = 0; i < active.Count; i++) { active[i].stacks.Clear(); pool.Push(active[i]); }
        active.Clear();

        float now = Time.time;
        if (from != null)
        {
            for (int i = 0; i < from.Count; i++)
            {
                var m = from[i];
                var e = pool.Count > 0 ? pool.Pop() : new State();
                e.id = m.id;
                e.sourceSkillId = m.sourceSkillId ?? "";
                e.stacking = m.stacking;
                // THE TAG AND THE NAME COME OFF THE SKILL, not off the wire. Identical values — this is where the
                // machine that decided the buff read them from too — so the tray, the nameplate and the hover card
                // all show exactly what they showed before.
                var src = SkillCatalog.Get(m.sourceSkillId);
                e.symbol = src != null ? src.Symbol : "";
                e.name = src != null ? src.displayName : "";
                // Blank means "the same as last time" — so what this machine was already showing is carried over
                // rather than lost. A state that went away and came back is not in the kept set, so its sentence
                // arrives again with it.
                e.description = !string.IsNullOrEmpty(m.description) ? m.description
                              : (keptDescriptions.TryGetValue((e.id, e.sourceSkillId), out var kept) ? kept : "");
                e.tint = m.tint;
                e.nextTick = 0f;
                e.endTime = now + m.secondsLeft;
                e.startTime = e.endTime - m.secondsTotal;
                e.stacks.Clear();
                int n = Mathf.Max(1, m.stacks);
                for (int k = 0; k < n; k++)
                    e.stacks.Add(new State.Stack { endTime = e.endTime, magnitude = m.magnitude / n, intAmount = m.intAmount / n });
                // The totals are the server's, written straight in — dividing them out per stack above is only so the
                // count reads right, and a remainder that rounding dropped must not quietly shrink the number.
                e.magnitude = m.magnitude;
                e.intAmount = m.intAmount;
                active.Add(e);
            }
        }

        nextExpiry = float.PositiveInfinity;
        for (int i = 0; i < active.Count; i++)
            if (active[i].endTime < nextExpiry) nextExpiry = active[i].endTime;

        Recompute();
        OnChanged?.Invoke();
    }

    void Recompute()
    {
        // Mult-style channels accumulate BONUSES (a ×1.3 pile contributes +0.30) and are converted back to a multiplier
        // once, at the end. Two +30% piles therefore read ×1.60, not ×1.69: every pile of a kind sits in the SAME
        // category, so a second one is worth the same as the first regardless of what's already running. Compounding
        // would make each buff's value depend on which others happen to be up, and make order matter.
        shieldPool = 0; parryCharges = 0;
        cannotAct = false; silenced = false; taunted = false;
        nextExpiry = float.MaxValue;
        System.Array.Clear(subStatBonus, 0, subStatBonus.Length);

        for (int i = 0; i < active.Count; i++)
        {
            var s = active[i];
            // Earliest STACK expiry drives EnsureFresh — a pile must wake up when its FIRST stack lapses, not its last.
            for (int k = 0; k < s.stacks.Count; k++)
                if (s.stacks[k].endTime < nextExpiry) nextExpiry = s.stacks[k].endTime;
            // EVERY sub-stat pile lands in the table the same way, by the same rule — a new sub-stat needs no case
            // here and cannot be forgotten. Signed, so a debuff pile subtracts. COMBINE, never overwrite: piles are
            // keyed per SOURCE skill, so one stat can be held by several at once.
            if (SubStats.TryStatFor(s.id, out var sub)) { subStatBonus[(int)sub] += s.magnitude; continue; }

            switch (s.id)
            {
                case StateId.Stun:
                case StateId.Freeze:          cannotAct = true; break;
                case StateId.Silence:         silenced = true; break;
                // Cached like the flags above because the mob brain reads it every frame, twice — once to pin the
                // chase and once to take the ranged standoff away.
                case StateId.Taunt:           taunted = true; break;
                // COMBINE, never overwrite — same rule as the sub-stat table above, for the piles that are not
                // sub-stats. Pools sum.
                case StateId.Shield:          shieldPool += s.intAmount; break;
                // Parry carries no per-stack value — the STACK COUNT *is* the charge count (one hit erased each).
                case StateId.Parry:           parryCharges += s.StackCount; break;
                // Poison/Bleed/Burn (DoT) + the HP-regen trickle tick in the (migrated) update loop; Taunt/Knockback/
                // Pull drive MonsterAI3D directly — none contribute a cached scalar, they're timer + display entries.
            }
        }
        // WHAT THE UNIT WEARS joins the same table (ArmourWords.FoldIntoSubStats owns the units).
        ArmourWords.FoldIntoSubStats(worn, subStatBonus);
        Headgear.FoldIntoSubStats(worn, subStatBonus);

        // Bonuses → multipliers, once. Floored at 0 so a stack of debuff-shaped piles can't drive a multiplier negative
        // (which would flip damage into healing).
        aspdBuffMult = Mathf.Max(0f, 1f + subStatBonus[(int)SubStat.AttackSpeed]);
        castBuffMult = Mathf.Max(0f, 1f + subStatBonus[(int)SubStat.CastSpeed]);
        leechPct     = subStatBonus[(int)SubStat.ManaLeech];
        reflectBonus = UnitStatMath.ReflectFraction(subStatBonus[(int)SubStat.Reflect]);
        moveMult     = UnitStatMath.MoveMult(subStatBonus[(int)SubStat.MovementSpeed]);
    }
}
