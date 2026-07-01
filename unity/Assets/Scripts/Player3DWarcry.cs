using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// The Swordsman's AoE active — <b>Warcry</b> (skill slot <b>4</b>): a player-centered shout that damages
/// AND knocks back every mob within <see cref="radius"/>, staggering them briefly. No target needed — it's
/// a point-blank area effect, so it fires whenever off-cooldown and affordable.
///
/// Like the other actives it's a thin input/MP/cooldown wrapper implementing <see cref="IHotbarSkill"/>
/// (so it appears on the HUD hotbar automatically). The scan reuses the <see cref="MonsterAI3D.Active"/>
/// registry + cached <c>.Health</c> (no OverlapSphere/GetComponent), and the shove is delegated to
/// <see cref="MonsterAI3D.Knockback"/>. It fires at most every few seconds, so it's not a per-frame hot path.
/// </summary>
public class Player3DWarcry : MonoBehaviour, IHotbarSkill, IModifiableSkill
{
    [Tooltip("Hotbar icon (Skill Icon pack). Optional — the bar falls back to the text label if unset.")]
    public Sprite icon;
    [Tooltip("Key that triggers Warcry (skill slot 4).")]
    public KeyCode key = KeyCode.Alpha4;
    [Tooltip("Mana spent per use.")]
    public int manaCost = 7;
    [Tooltip("Seconds between uses.")]
    public float cooldown = 10f;
    [Tooltip("Radius of the shout (world units, XZ).")]
    public float radius = 3.5f;
    [Tooltip("Damage to every mob in range, as a multiple of a normal swing.")]
    public float damageMult = 1.4f;

    [Header("Knockback")]
    [Tooltip("Initial shove speed (eases out over knockbackTime). Distance ≈ ½·speed·time ≈ 1.4 units.")]
    public float knockbackSpeed = 13f;
    [Tooltip("How long the shove lasts.")]
    public float knockbackTime = 0.22f;
    [Tooltip("Seconds each hit mob is frozen (≥ knockbackTime so it doesn't fight the slide).")]
    public float staggerSecs = 0.5f;

    [Header("Shockwave VFX (Effect Library — the burst at the player)")]
    [Tooltip("Effect-library id for the warcry shockwave. Empty = none.")]
    public string shockVfxId = "aoe.red-energy-explosion";
    public float shockScale = 1f;

    [Header("Audio")]
    public SfxSet warcrySfx;   // the shout / shockwave boom
    public SfxSet failSfx;     // not enough mana

    float nextUse;
    float lastDuration;        // the cooldown actually applied last use (for the hotbar's sweep fraction)
    int castCount;             // for the Proc-phase "every Nth cast is free" modifier
    SkillModLoadout loadout;   // six-phase modifier loadout (lazily registered, born with one)

    // Warcry's modifier-eligibility identity (Charter §12): a melee, physical, offensive AoE — it draws the
    // wide melee-offense pool (damage/element/stagger/slow/root/lifesteal/cooldown/shield/proc).
    const string SkillId = "warcry";
    static readonly string[] Tags = { SkillTag.Melee, SkillTag.Physical, SkillTag.Offense, SkillTag.Aoe };

    public string SkillModId => SkillId;
    public string[] SkillModTags => Tags;

    void EnsureRegistered()
    {
        if (loadout != null || Character.Instance == null) return;
        loadout = Character.Instance.SkillMods.Register(SkillId, Tags);
    }

    static readonly Color NoManaColor = new Color(0.55f, 0.6f, 1f);
    static readonly Color WarcryColor = new Color(1f, 0.7f, 0.3f);

    void Update()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return;
        EnsureRegistered();   // keeps the loadout registered; triggering is via the hotbar binding (HotbarInput)
    }

    /// <summary>Let out the Warcry now — shared by the keypress and the HUD hotbar click. Damages + shoves
    /// every mob in range. Returns true if it fired (off cooldown + enough MP); it can fire into empty space.</summary>
    public bool Activate()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return false;
        if (Time.time < nextUse) return false;

        if (!Character.Instance.SpendMana(manaCost))
        {
            Sfx.Play(failSfx);
            DamagePopup.SpawnText(transform.position, "No MP", NoManaColor);
            nextUse = Time.time + 0.4f; lastDuration = 0.4f;   // brief lockout so it can't spam the popup
            return false;
        }

        EnsureRegistered();
        var ctx = SkillContext.Default(damageMult, 1f, staggerSecs);
        loadout?.ApplyAll(ref ctx);                       // fold this skill's modifiers into the cast

        castCount++;
        bool free = ctx.EveryNthFree > 0 && castCount % ctx.EveryNthFree == 0;   // Proc: every Nth cast skips cooldown
        lastDuration = free ? 0f : cooldown * ctx.CooldownMult;
        nextUse = Time.time + lastDuration;
        if (ctx.ShieldSecs > 0f && ctx.ShieldCut > 0f)    // bulwark: brief self-shield on cast
            Character.Instance.BeginGuard(ctx.ShieldSecs, ctx.ShieldCut);
        Sfx.Play(warcrySfx);
        DamagePopup.SpawnText(transform.position + Vector3.up * 0.6f, "Warcry!", WarcryColor);
        if (!string.IsNullOrEmpty(shockVfxId)) Vfx.PlayAt(shockVfxId, transform.position + Vector3.up * 0.2f, Quaternion.identity, shockScale);

        Vector3 me = transform.position;
        float effRadius = radius * ctx.RangeMult;         // reach mods widen the shout
        float r2 = effRadius * effRadius;
        Element element = ctx.ConvertElement ? ctx.NewElement : Element.Neutral;
        float shoveSpeed = Mathf.Max(knockbackSpeed, ctx.KnockbackSpeed);
        int totalDealt = 0;

        // Collect every in-range mob FIRST, then hit them in a second pass. We must NOT damage while
        // iterating MonsterAI3D.Active: a one-shot kill removes the mob from Active mid-loop (Health.Die →
        // DeathDissolve disables MonsterAI3D → OnDisable → Active.Remove), which shifts the indices and makes
        // an index loop skip the next mob — so ~half a one-shot AoE was being missed. The snapshot is immune.
        targets.Clear();
        var list = MonsterAI3D.Active;            // registry scan — no OverlapSphere, cached .Health
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null) continue;
            var h = ai.Health;
            if (h == null || h.IsDead) continue;
            Vector3 d = ai.transform.position - me; d.y = 0f;   // planar — ignore height
            if (d.sqrMagnitude <= r2) targets.Add(ai);
        }

        for (int i = 0; i < targets.Count; i++)
        {
            var ai = targets[i];
            if (ai == null || ai.Health == null || ai.Health.IsDead) continue;
            int power = Mathf.RoundToInt(Character.Instance.RollDamage(out bool crit, 0, ctx.ForceCrit) * Mathf.Max(1f, ctx.DamageMult));
            totalDealt += ai.Health.TakeDamage(power, crit, -1, element);   // AoE always lands (no FLEE roll)
            ai.Knockback(me, shoveSpeed, knockbackTime, Mathf.Max(staggerSecs, ctx.StaggerSecs));
            if (ctx.SlowSecs > 0f && ctx.SlowFactor < 1f) ai.Slow(ctx.SlowFactor, ctx.SlowSecs);
            if (ctx.RootSecs > 0f) ai.Slow(0f, ctx.RootSecs);   // entangling
        }
        if (ctx.LifestealFracDealt > 0f && totalDealt > 0)
            Character.Instance.LeechHeal(Mathf.RoundToInt(totalDealt * ctx.LifestealFracDealt));
        return true;
    }

    // Reused scratch for the two-pass AoE scan (allocation-free; single-threaded, one player).
    static readonly System.Collections.Generic.List<MonsterAI3D> targets = new System.Collections.Generic.List<MonsterAI3D>();

    // ---- IHotbarSkill (drives the HUD hotbar slot) ----
    public int SlotOrder => 3;            // bar order: Bash(1) · Charge(2) · Guard(3) · Warcry(4) · Bolt(Q)
    public string ShortLabel => "Warcry";
    public string KeyLabel => "4";
    public int ManaCost => manaCost;
    public Sprite Icon => icon;
    public bool Affordable => Character.Instance != null && Character.Instance.CurrentMP >= manaCost;
    public float CooldownRemaining => Mathf.Max(0f, nextUse - Time.time);
    public float CooldownFraction01 => lastDuration <= 0.001f ? 0f : Mathf.Clamp01(CooldownRemaining / lastDuration);
    public Color Tint => WarcryColor;   // rallying orange

    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    bool Pressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && key == KeyCode.Alpha4 && Keyboard.current.digit4Key.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(key);
    }
}
