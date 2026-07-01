using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Mage spell — <b>Frost Nova</b> (slot <b>2</b>): a point-blank ring of frost that deals magic damage to
/// every mob within <see cref="radius"/> and <see cref="MonsterAI3D.Slow"/>s them. No target needed.
/// Implements <see cref="IHotbarSkill"/>. Collects in-range mobs FIRST, then hits them — so a kill removing
/// a mob from <see cref="MonsterAI3D.Active"/> mid-loop can't make us skip the next one.
/// </summary>
public class Player3DFrostNova : MonoBehaviour, IHotbarSkill, IModifiableSkill
{
    [Tooltip("Hotbar icon (Skill Icon pack). Optional — the bar falls back to the text label if unset.")]
    public Sprite icon;
    public KeyCode key = KeyCode.Alpha2;
    public int manaCost = 12;
    public float cooldown = 7f;
    public float radius = 3.2f;
    [Tooltip("Damage to every mob in range, as a multiple of a normal magic hit.")]
    public float damageMult = 1.1f;
    [Range(0f, 1f)]
    [Tooltip("Move-speed multiplier applied to chilled mobs (0.5 = half speed).")]
    public float slowFactor = 0.5f;
    public float slowSeconds = 3f;

    [Header("Burst VFX (Effect Library — the ice nova at the player)")]
    [Tooltip("Effect-library id for the frost burst. Empty = none.")]
    public string burstVfxId = "aoe.snow";
    public float burstScale = 1f;

    [Header("Audio")]
    public SfxSet castSfx;
    public SfxSet failSfx;

    float nextUse, lastDuration;
    int castCount;             // for the Proc-phase "every Nth cast is free" modifier
    SkillModLoadout loadout;   // six-phase modifier loadout (lazily registered, born with one)
    static readonly Color NoManaColor = new Color(0.55f, 0.6f, 1f);
    static readonly Color FrostColor = new Color(0.6f, 0.85f, 1f);
    static readonly List<MonsterAI3D> targets = new List<MonsterAI3D>();

    // Frost Nova's modifier-eligibility identity (Charter §12): a magic, offensive AoE — draws the magic-offense
    // pool (cooldown/amp/element/stagger/slow/lifesteal/proc); the melee-only mods are filtered out.
    const string SkillId = "frost_nova";
    static readonly string[] Tags = { SkillTag.Magic, SkillTag.Offense, SkillTag.Aoe };

    public string SkillModId => SkillId;
    public string[] SkillModTags => Tags;

    void EnsureRegistered()
    {
        if (loadout != null || Character.Instance == null) return;
        loadout = Character.Instance.SkillMods.Register(SkillId, Tags);
    }

    void Update()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return;
        EnsureRegistered();   // keeps the loadout registered; triggering is via the hotbar binding (HotbarInput)
    }

    public bool Activate()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return false;
        if (StatusController.Player != null && StatusController.Player.Silenced)
        { DamagePopup.SpawnText(transform.position, "Silenced", new Color(0.7f, 0.5f, 1f)); return false; }
        if (Time.time < nextUse) return false;

        if (!Character.Instance.SpendMana(manaCost))
        {
            Sfx.Play(failSfx);
            DamagePopup.SpawnText(transform.position, "No MP", NoManaColor);
            nextUse = Time.time + 0.4f; lastDuration = 0.4f;
            return false;
        }

        EnsureRegistered();
        var ctx = SkillContext.Default(damageMult, 1f, 0f);
        loadout?.ApplyAll(ref ctx);                       // fold this skill's modifiers into the cast

        castCount++;
        bool free = ctx.EveryNthFree > 0 && castCount % ctx.EveryNthFree == 0;   // Proc: every Nth cast skips cooldown
        lastDuration = free ? 0f : cooldown * ctx.CooldownMult;
        nextUse = Time.time + lastDuration;
        Sfx.Play(castSfx);
        DamagePopup.SpawnText(transform.position + Vector3.up * 0.6f, "Frost Nova", FrostColor);
        if (!string.IsNullOrEmpty(burstVfxId)) Vfx.PlayAt(burstVfxId, transform.position + Vector3.up * 0.2f, Quaternion.identity, burstScale);

        Vector3 me = transform.position;
        float effRadius = radius * ctx.RangeMult;
        float r2 = effRadius * effRadius;
        Element element = ctx.ConvertElement ? ctx.NewElement : Element.Water;
        float chillFactor = Mathf.Min(slowFactor, ctx.SlowFactor);   // the deeper chill of the skill or a slow mod
        float chillSecs = Mathf.Max(slowSeconds, ctx.SlowSecs);
        int totalDealt = 0;
        targets.Clear();
        var list = MonsterAI3D.Active;
        for (int i = 0; i < list.Count; i++)
        {
            var ai = list[i];
            if (ai == null) continue;
            var h = ai.Health;
            if (h == null || h.IsDead) continue;
            Vector3 d = ai.transform.position - me; d.y = 0f;
            if (d.sqrMagnitude <= r2) targets.Add(ai);
        }

        for (int i = 0; i < targets.Count; i++)
        {
            var ai = targets[i];
            if (ai == null || ai.Health == null || ai.Health.IsDead) continue;
            int power = Mathf.RoundToInt(Character.Instance.RollMagicDamage(out bool crit) * Mathf.Max(1f, ctx.DamageMult));
            totalDealt += ai.Health.TakeMagicDamage(power, crit, element);
            ai.Slow(chillFactor, chillSecs);
            if (ctx.StaggerSecs > 0f) ai.Stagger(ctx.StaggerSecs);   // concussive
            if (ctx.RootSecs > 0f) ai.Slow(0f, ctx.RootSecs);        // (offense-tagged root, if it ever applies)
        }
        if (ctx.LifestealFracDealt > 0f && totalDealt > 0)
            Character.Instance.LeechHeal(Mathf.RoundToInt(totalDealt * ctx.LifestealFracDealt));
        return true;
    }

    // ---- IHotbarSkill ----
    public int SlotOrder => 1;
    public string ShortLabel => "Frost";
    public string KeyLabel => "2";
    public int ManaCost => manaCost;
    public Sprite Icon => icon;
    public bool Affordable => Character.Instance != null && Character.Instance.CurrentMP >= manaCost;
    public float CooldownRemaining => Mathf.Max(0f, nextUse - Time.time);
    public float CooldownFraction01 => lastDuration <= 0.001f ? 0f : Mathf.Clamp01(CooldownRemaining / lastDuration);
    public Color Tint => FrostColor;

    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    bool Pressed()
    {
        if (Hotkeys.Typing) return false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && key == KeyCode.Alpha2 && Keyboard.current.digit2Key.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(key);
    }
}
