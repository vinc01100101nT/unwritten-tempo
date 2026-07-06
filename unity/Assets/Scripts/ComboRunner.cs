using System.Collections;
using UnityEngine;

/// <summary>
/// The EXECUTOR for chained-skill combos — the single choke point that turns one hotkey press into an auto-fired
/// sequence of a chain's four skills (see the PRD "Skill Combo System" and its data owner <see cref="ComboBook"/>).
/// Lives on the player. Each frame it polls the chains' hotkeys through the central <see cref="Hotkeys"/> gate; on a
/// trigger it resolves the chain's aim ONCE (unit or ground pick, or none), then a coroutine fires each member
/// through <see cref="SkillRunner.ComboCastGround"/>/<see cref="SkillRunner.ComboCastUnit"/> — reusing every skill's
/// own cast → release-animation → aftercast lifecycle, so combos add zero new animations.
///
/// <para><b>Interrupt model.</b> Between steps it watches <see cref="StatusController.CannotAct"/> — the codebase's
/// existing hard-CC flag (Stun/Freeze, deliberately NOT the self-imposed cast/aftercast lock). A hard-CC mid-chain
/// breaks it; the shared cooldown was already spent at trigger (<see cref="ComboChain.StartCooldown"/>), so a failed
/// chain still costs it. Silence / empty mana fizzle the next step the same way (via <see cref="SkillRunner"/>'s
/// combo-spend gate). Plain damage never interrupts.</para>
///
/// <para>Perf: the per-frame poll is a tiny fixed loop with no allocation; the sequence is an event-driven coroutine
/// that <c>yield return null</c>s (no <c>WaitForSeconds</c> garbage) and reuses cached aim-callback delegates so a
/// trigger allocates nothing beyond the one coroutine.</para>
/// </summary>
[DisallowMultipleComponent]
public class ComboRunner : MonoBehaviour
{
    /// <summary>The player's runner (set in Awake) — for HUD/debug to read <see cref="Running"/>.</summary>
    public static ComboRunner Active { get; private set; }

    Coroutine running;
    ComboChain pendingChain;                 // chain awaiting its up-front aim pick (read by the cached callbacks)
    System.Action<Transform> onUnitPicked;   // cached — no per-trigger delegate allocation
    System.Action<Vector3> onGroundPicked;

    /// <summary>True while a chain is mid-sequence (blocks a second combo / re-trigger).</summary>
    public bool Running => running != null;

    void Awake()
    {
        Active = this;
        onUnitPicked = OnUnitPicked;
        onGroundPicked = OnGroundPicked;
    }

    void OnDestroy() { if (Active == this) Active = null; }

    void Update()
    {
        if (running != null || Character.Instance == null) return;
        // An aim reticle (this combo's, or a solo skill's) is open → don't start another combo this frame.
        if (UnitTargeter.Active || GroundTargeter.Active) return;

        var chains = Character.Instance.Combos.Chains;
        if (chains == null) return;
        for (int i = 0; i < chains.Count; i++)
        {
            var c = chains[i];
            if (c == null || c.hotkey == KeyCode.None || !c.IsRunnable) continue;
            if (Hotkeys.Down(c.hotkey)) { TryStart(c); break; }
        }
    }

    /// <summary>Fire a chain from a click on the HUD combo bar (same gating as its hotkey).</summary>
    public void TriggerChain(ComboChain c) { if (c != null && c.IsRunnable) TryStart(c); }

    void TryStart(ComboChain c)
    {
        if (running != null || Dead()) return;
        var sc = StatusController.Player;
        // Can't launch a combo while already casting/recovering (ActionLocked) or unable to cast (silenced).
        if (sc != null && (sc.ActionLocked || sc.Silenced)) return;
        if (!c.Ready) { BattleNotice.Warn("Combo cooling down"); return; }

        pendingChain = c;
        switch (c.AimMode)
        {
            case ComboAim.Unit:
                UnitTargeter.Begin(transform, c.MaxCastRange, c.SingleTargetMask, onUnitPicked);
                break;
            case ComboAim.Ground:
                GroundTargeter.Begin(transform, c.MaxCastRange, onGroundPicked, "", Color.white, c.PreviewRadius);
                break;
            default:
                BeginRun(c, null, transform.position);   // Self/Target-only chain — no pick needed
                break;
        }
    }

    // The targeters only invoke these on CONFIRM (cancel just clears them), so reaching here means a valid pick and
    // the combo commits. The chosen unit's position is snapshotted as the ground anchor so ground steps still land at
    // the last-known spot if the unit later dies.
    void OnUnitPicked(Transform unit)
    {
        var c = pendingChain; pendingChain = null;
        if (c == null || unit == null) return;
        BeginRun(c, unit, unit.position);
    }

    void OnGroundPicked(Vector3 point)
    {
        var c = pendingChain; pendingChain = null;
        if (c == null) return;
        BeginRun(c, null, point);
    }

    void BeginRun(ComboChain c, Transform unit, Vector3 point)
    {
        if (running != null) return;
        c.StartCooldown();                       // shared max-CD starts at trigger → a broken chain still pays it
        running = StartCoroutine(RunChain(c, unit, point));
    }

    IEnumerator RunChain(ComboChain c, Transform unit, Vector3 point)
    {
        var sc = StatusController.Player;
        float gap = ComboBook.StepGap;

        for (int i = 0; i < c.RunLength; i++)
        {
            if (Dead()) break;
            var sr = c.Runner(i);
            if (sr == null) break;               // the player doesn't have this skill component → chain ends

            float mult = ComboBook.SlotMultiplier(i);
            var res = FireStep(sr, c.Def(i), unit, point, mult);
            if (res == StepResult.Break) break;              // caster can't act / no mana / silenced → hard fizzle
            if (res == StepResult.SkipNoTarget) continue;    // chosen unit gone → skip this step, keep the chain alive

            yield return null;                   // let the just-fired skill establish its self-lock this frame

            // Wait out this skill's own cast + aftercast; a hard-CC landing mid-way breaks the chain.
            while (sc != null && sc.ActionLocked)
            {
                if (sc.CannotAct || Dead()) { running = null; yield break; }
                yield return null;
            }
            if (Dead()) break;

            // Inter-step window (tunable): a readable beat AND the moment a hard-CC can interrupt an all-instant chain.
            for (float t = 0f; t < gap; t += Time.deltaTime)
            {
                if ((sc != null && sc.CannotAct) || Dead()) { running = null; yield break; }
                yield return null;
            }
        }
        running = null;
    }

    enum StepResult { Fired, SkipNoTarget, Break }

    // Fire one member at the chain's resolved aim. Single-target members bind to the chosen unit (skip if it died);
    // Target members auto-pick nearest; Ground members land at the unit (live) or the ground anchor; Self on the caster.
    StepResult FireStep(SkillRunner sr, SkillDef def, Transform unit, Vector3 point, float mult)
    {
        if (def == null) return StepResult.Break;

        if (def.IsSingleTarget)
        {
            if (unit == null) return StepResult.SkipNoTarget;               // chosen unit gone → soft skip
            return sr.ComboCastUnit(unit, mult) ? StepResult.Fired : StepResult.Break;
        }
        if (def.aim == SkillAim.Target)
        {
            Transform t = unit != null ? unit : NearestMob(transform.position, def.castRange);
            if (t == null) return StepResult.SkipNoTarget;
            return sr.ComboCastUnit(t, mult) ? StepResult.Fired : StepResult.Break;
        }
        if (def.aim == SkillAim.Ground)
            return sr.ComboCastGround(unit != null ? unit.position : point, mult) ? StepResult.Fired : StepResult.Break;

        // Self
        return sr.ComboCastGround(transform.position, mult) ? StepResult.Fired : StepResult.Break;
    }

    static bool Dead() => Character.Instance == null || Character.Instance.IsDead;

    // Nearest living mob within range on the XZ plane — reuses the shared registry, squared-distance, zero-alloc.
    static Transform NearestMob(Vector3 from, float maxRange)
    {
        var list = MonsterAI3D.Active;
        Transform best = null;
        float bestSq = maxRange * maxRange;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null || ai.Health == null || ai.Health.IsDead) continue;
            Vector3 d = ai.transform.position - from; d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq <= bestSq) { bestSq = sq; best = ai.transform; }
        }
        return best;
    }
}
