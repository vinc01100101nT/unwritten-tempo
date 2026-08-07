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

        /// <summary>This state's magnitude read as a MULTIPLIER — the ONE place the stacking encoding is applied, so no
        /// reader can get it wrong. A stacking state stores per-stack BONUSES (ten +3% stacks sum to 0.30 ⇒ ×1.30); a
        /// non-stacking one stores the multiplier outright (1.5 ⇒ ×1.5). Only mult-style channels (Haste, the stat
        /// buffs) use this; additive ones (DoT, shield, guard cut) read <see cref="magnitude"/> directly, since a plain
        /// sum is already correct for them.</summary>
        public float Mult => stacking ? 1f + magnitude : magnitude;

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
    float moveMult = 1f, guardCut, vulnMult = 1f, dmgBuffMult = 1f, aspdBuffMult = 1f, castBuffMult = 1f, leechPct, reflectBonus;
    int hitPenalty, shieldPool;
    bool cannotAct, silenced;
    float nextExpiry = float.MaxValue;   // earliest endTime across active states — the ONLY time EnsureFresh must prune

    /// <summary>Hard ceiling on stacks per state. Design may call a state "uncapped" (Momentum) — its FIFO equilibrium
    /// bounds it in practice — but an unbounded list is a memory/perf hazard, so this backstop exists and design never
    /// reaches it.</summary>
    public const int MaxStacks = 99;

    // Transient APPLY CONTEXT for the NEXT Apply — the skill applier sets it right before a durational gameplay call
    // (BeginGuard / BuffDamage / StatusController.Apply / …) so the created/refreshed entry gets the source skill's
    // tray/nameplate icon AND its stacking rules, WITHOUT threading either through every gameplay method. The applier
    // owns the lifecycle (SetApplyContext before the call, ClearApplyContext after); Apply only reads what it was given.
    Sprite ctxIcon; string ctxName, ctxDesc;
    bool ctxStacking; int ctxStacksPerApply = 1, ctxStackLimit;

    /// <summary>Tag the NEXT applied state with a source icon/name/description and its stacking rules. Set by the skill
    /// applier before a durational gameplay call; always pair with <see cref="ClearApplyContext"/> after.
    /// <paramref name="stacking"/> off = today's single-instance, newest-wins behaviour.</summary>
    public void SetApplyContext(Sprite icon, string name, string description,
                                bool stacking = false, int stacksPerApply = 1, int stackLimit = 0)
    {
        ctxIcon = icon; ctxName = name; ctxDesc = description;
        ctxStacking = stacking;
        ctxStacksPerApply = Mathf.Max(1, stacksPerApply);
        ctxStackLimit = stackLimit;
    }

    public void ClearApplyContext()
    {
        ctxIcon = null; ctxName = null; ctxDesc = null;
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
    public float DamageBuffMult { get { EnsureFresh(); return dmgBuffMult; } }
    public float AttackSpeedBuffMult { get { EnsureFresh(); return aspdBuffMult; } }
    public float CastSpeedBuffMult { get { EnsureFresh(); return castBuffMult; } }
    /// <summary>Fraction of damage dealt returned as MP (ManaLeech window), 0 when none.</summary>
    public float ManaLeechPct { get { EnsureFresh(); return leechPct; } }
    /// <summary>Extra reflect fraction (timed Reflect window), 0 when none.</summary>
    public float ReflectBonus { get { EnsureFresh(); return reflectBonus; } }
    /// <summary>Remaining barrier HP (Shield), 0 when none.</summary>
    public int ShieldPool { get { EnsureFresh(); return shieldPool; } }

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

    State Find(StateId id)
    {
        for (int i = 0; i < active.Count; i++) if (active[i].id == id) return active[i];
        return null;
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
        var s = Find(id);
        if (s == null)
        {
            s = pool.Count > 0 ? pool.Pop() : new State();
            s.id = id; s.startTime = now; s.nextTick = 0f;
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
                if (now >= st[k].endTime) { st.RemoveAt(k); changed = true; }
            if (st.Count == 0) { pool.Push(s); active.RemoveAt(i); changed = true; }
            else s.Refold();
        }
        if (changed) { Recompute(); OnChanged?.Invoke(); }
        return changed;
    }

    /// <summary>Wipe all states (on death / revive).</summary>
    public void Clear()
    {
        if (active.Count == 0) return;
        for (int i = 0; i < active.Count; i++) pool.Push(active[i]);
        active.Clear();
        Recompute();
        OnChanged?.Invoke();
    }

    // Fold the active list into the cached scalars. Runs ONLY on a set change — never per query. Neutral defaults when
    // a state is absent (mult = 1, cut/penalty = 0). Slow × Haste multiply, matching StatusController.MoveMult.
    //
    // MAGNITUDE ENCODING — the one rule that keeps stacking honest:
    //   • NON-stacking states store the ABSOLUTE value (a Haste of 1.5 IS ×1.5; a Slow of 0.5 IS half speed).
    //   • STACKING states store the PER-STACK DELTA, and s.magnitude is their SUM (10 stacks of 0.03 ⇒ 0.30).
    // Summing absolute multipliers would be nonsense (ten ×1.03 buffs are not ×10.3), so a stacking mult-style channel
    // reads 1 + sum and a stacking slow reads 1 − sum. Additive channels (guard cut, vuln, DoT, shield, leech, reflect,
    // blind) sum correctly either way, so they need no branch.
    void Recompute()
    {
        float slow = 1f, haste = 1f;
        guardCut = 0f; vulnMult = 1f;
        dmgBuffMult = 1f; aspdBuffMult = 1f; castBuffMult = 1f;
        leechPct = 0f; reflectBonus = 0f;
        hitPenalty = 0; shieldPool = 0;
        cannotAct = false; silenced = false;
        nextExpiry = float.MaxValue;

        for (int i = 0; i < active.Count; i++)
        {
            var s = active[i];
            // Earliest STACK expiry drives EnsureFresh — a pile must wake up when its FIRST stack lapses, not its last.
            for (int k = 0; k < s.stacks.Count; k++)
                if (s.stacks[k].endTime < nextExpiry) nextExpiry = s.stacks[k].endTime;
            float mult = s.Mult;   // stacking stores per-stack deltas — State.Mult owns that rule
            switch (s.id)
            {
                case StateId.Slow:            slow = Mathf.Clamp01(s.stacking ? 1f - s.magnitude : s.magnitude); break;
                case StateId.Haste:           haste = mult <= 0f ? 1f : mult; break;
                case StateId.Stun:
                case StateId.Freeze:          cannotAct = true; break;
                case StateId.Silence:         silenced = true; break;
                case StateId.Blind:           hitPenalty = Mathf.RoundToInt(s.magnitude); break;
                case StateId.Guard:           guardCut = Mathf.Clamp01(s.magnitude); break;
                case StateId.Vulnerability:   vulnMult = 1f + Mathf.Max(0f, s.magnitude); break;
                case StateId.DamageBuff:      dmgBuffMult = mult <= 0f ? 1f : mult; break;
                case StateId.AttackSpeedBuff: aspdBuffMult = mult <= 0f ? 1f : mult; break;
                case StateId.CastSpeedBuff:   castBuffMult = mult <= 0f ? 1f : mult; break;
                case StateId.ManaLeech:       leechPct = s.magnitude; break;
                case StateId.Reflect:         reflectBonus = s.magnitude; break;
                case StateId.Shield:          shieldPool = s.intAmount; break;
                // Poison/Bleed/Burn (DoT) + Regen tick in the (migrated) update loop; Taunt/Knockback/Pull drive
                // MonsterAI3D directly — none contribute a cached scalar, they're timer + display entries here.
            }
        }
        moveMult = slow * haste;
    }
}
