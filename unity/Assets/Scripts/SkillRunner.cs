using UnityEngine;

/// <summary>
/// The runtime interpreter for a data-authored <see cref="SkillDef"/> — the ONE component that turns any composed
/// skill into a playable hotbar active, replacing a hand-written C# skill class per spell. It plays the def's
/// <see cref="VfxSequence"/> and, on each gameplay <see cref="VfxTick"/> the sequence raises, looks up the matching
/// <see cref="SkillTickBinding"/> and applies its effects through <see cref="SkillEffectApplier"/> — the shared
/// combat path. The FINAL numbers are rolled off the live caster stats there, so this class stores no damage; the
/// asset only says WHAT each tick does and by how much (× multiplier + per-level growth).
///
/// <para>Attach one per skill on the player (a build tool does this). It implements <see cref="IHotbarSkill"/> so
/// the bar shows it automatically. Aim mode (<see cref="SkillDef.aim"/>) decides how the cast point is picked.</para>
/// </summary>
public class SkillRunner : MonoBehaviour, IHotbarSkill
{
    [Tooltip("The authored skill this runner plays. Set by the build tool or in the inspector.")]
    public SkillDef def;
    [Range(1, 20)]
    [Tooltip("Current skill level (drives per-binding damage growth). 1 = authored base values.")]
    public int level = 1;

    float nextUse, lastDuration;
    VfxContext lastCtx;                 // captured per cast so OnTick can read the Single-scope target unit
    System.Action<VfxTick> onTick;      // cached hook (no per-play delegate alloc)
    System.Action<Vector3> onGround;    // cached ground-confirm hook
    System.Action<Transform> onUnit;    // cached single-target confirm hook
    static readonly Color NoManaColor = new Color(0.55f, 0.6f, 1f);

    void Awake()
    {
        onTick = OnTick;
        onGround = Cast;
        onUnit = CastTargeted;
    }

    // ---- activation ---------------------------------------------------------

    public bool Activate()
    {
        if (def == null || def.vfx == null) return false;
        if (Character.Instance == null || Character.Instance.IsDead) return false;
        if (StatusController.Player != null && StatusController.Player.Silenced)
        { BattleNotice.Warn("Silenced", new Color(0.7f, 0.5f, 1f)); return false; }
        if (Time.time < nextUse) return false;

        // Single-target skills must land on ONE valid unit — they can NEVER be ground-cast (a ground point is not a
        // target). This overrides the authored aim: enter the unified reticle, which validates the hovered unit's
        // KIND against the skill's target mask (and warns in-game on an illegal pick). The bindings are the single
        // source of truth for who a skill can hit, so targeting is derived from them, not a separate aim field.
        if (def.IsSingleTarget)
        {
            var mask = def.SingleTargetMask;
            if (mask == SkillTargets.None)
            { BattleNotice.Warn("No valid target"); return false; }
            if (UnitTargeter.Active) { UnitTargeter.Cancel(); return false; }   // re-press cancels aiming
            UnitTargeter.Begin(transform, def.castRange, mask, onUnit);
            return true;   // MP/cooldown spent on confirm (CastTargeted), so cancelling costs nothing
        }

        switch (def.aim)
        {
            case SkillAim.Ground:
                if (GroundTargeter.Active) { GroundTargeter.Cancel(); return false; }   // re-press cancels aiming
                GroundTargeter.Begin(transform, PreviewRadius(), def.castRange, onGround);
                return true;   // MP/cooldown are spent on confirm (Cast), so cancelling costs nothing

            case SkillAim.Target:
                var mob = NearestMob(transform.position, def.castRange);
                if (mob == null) return false;
                CastTargeted(mob.transform);
                return true;

            default: // Self
                Cast(transform.position);
                return true;
        }
    }

    // Ground/Self confirm: spend, gate cooldown, play the sequence centred on `center`.
    void Cast(Vector3 center)
    {
        if (!Spend()) return;
        lastCtx = VfxContext.At(center);
        PlayCycles();
    }

    // Target confirm: origin = caster, target/single-unit = the chosen unit (a mob, or the player for a self-cast).
    void CastTargeted(Transform unit)
    {
        if (unit == null || !Spend()) return;
        lastCtx = new VfxContext
        {
            origin = transform.position,
            target = unit.position,
            follow = null,
            targetUnit = unit,
            scaleMult = 1f,
        };
        PlayCycles();
    }

    // Play the composed VFX once, then (if the def asks for a shower) replay the WHOLE cycle repeatCount times,
    // repeatInterval apart, at the SAME cast-time context — carrierScatter spreads the individual falls. Repetition
    // of the whole effect is owned HERE (gameplay), not the Vfx Composer (which stays WYSIWYG: one cycle = one fall).
    void PlayCycles()
    {
        VfxDirector.Play(def.vfx, lastCtx, onTick);
        int extra = Mathf.Max(0, def.repeatCount - 1);
        if (extra > 0 && isActiveAndEnabled) StartCoroutine(PlayExtraCycles(extra, lastCtx));
    }

    System.Collections.IEnumerator PlayExtraCycles(int extra, VfxContext ctx)
    {
        float interval = def.repeatInterval > 0f ? def.repeatInterval
                       : (def.vfx != null && def.vfx.RestartInterval > 0f ? def.vfx.RestartInterval : 0.35f);
        var wait = new WaitForSeconds(interval);
        for (int i = 0; i < extra; i++)
        {
            yield return wait;
            VfxDirector.Play(def.vfx, ctx, onTick);
        }
    }

    bool Spend()
    {
        if (Character.Instance == null || Character.Instance.IsDead || Time.time < nextUse) return false;
        if (!Character.Instance.SpendMana(def.manaCost))
        {
            BattleNotice.Warn("No MP", NoManaColor);
            return false;
        }
        lastDuration = def.cooldown;
        nextUse = Time.time + lastDuration;
        return true;
    }

    // The heart of the code/data split: each tick the sequence raises, resolve its binding and apply — through the
    // ONE shared combat path. Unbound ticks (pure-visual beats) are ignored. Per-level growth rides in as extras.
    void OnTick(VfxTick tick)
    {
        var b = def.BindingFor(tick.tickId);
        if (b == null) return;
        int over = Mathf.Max(0, level - 1);
        float extraMult = b.scale != null ? b.scale.damageMultPerLevel * over : 0f;
        int extraFlat = b.scale != null ? b.scale.flatBonusPerLevel * over : 0;
        SkillEffectApplier.ApplyBurst(tick.point, b.radius, b.scope, b.targets, b.effects,
                                      lastCtx.targetUnit, extraMult, extraFlat);
    }

    // Largest bound radius, so the ground reticle previews the skill's real footprint.
    float PreviewRadius()
    {
        float r = 0f;
        if (def.bindings != null)
            for (int i = 0; i < def.bindings.Count; i++)
                if (def.bindings[i] != null && def.bindings[i].scope == SkillScope.Area) r = Mathf.Max(r, def.bindings[i].radius);
        return r > 0f ? r : 1.5f;
    }

    static MonsterAI3D NearestMob(Vector3 from, float maxRange)
    {
        var list = MonsterAI3D.Active;
        MonsterAI3D best = null; float bestSq = maxRange * maxRange;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null || ai.Health == null || ai.Health.IsDead) continue;
            Vector3 d = ai.transform.position - from; d.y = 0f;
            float sq = d.sqrMagnitude;
            if (sq <= bestSq) { bestSq = sq; best = ai; }
        }
        return best;
    }

    // ---- IHotbarSkill -------------------------------------------------------
    public int SlotOrder => def != null ? def.slotOrder : 100;
    public string ShortLabel => def != null ? def.shortLabel : "Skill";
    public string KeyLabel => def != null ? def.keyLabel : "";
    public Sprite Icon => def != null ? def.icon : null;
    public int ManaCost => def != null ? def.manaCost : 0;
    public bool Affordable => Character.Instance != null && def != null && Character.Instance.CurrentMP >= def.manaCost;
    public float CooldownRemaining => Mathf.Max(0f, nextUse - Time.time);
    public float CooldownFraction01 => lastDuration <= 0.001f ? 0f : Mathf.Clamp01(CooldownRemaining / lastDuration);
    public Color Tint => def != null ? def.tint : Color.white;
}
