using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Mage spell — <b>Meteor</b> (slot <b>3</b>): a ground-targeted strike. A SHOWER of <see cref="ticks"/>
/// meteors falls from the sky onto the cursor point (clamped to <see cref="castRange"/>), one per tick. Each
/// meteor that lands explodes and deals ONE damage tick — a burst of <see cref="hitsPerTick"/> separate rolls
/// (each with a per-hit <b>stun</b> chance) plus its own rising yellow combo TOTAL, via <see cref="MultiHitBurst"/>.
/// The whole skill deals <see cref="damageMult"/>× magic damage split across every hit, so more meteors = more
/// visual flair, not more power. The falls ARE the telegraph. Implements <see cref="IHotbarSkill"/>; the falls +
/// impact FX use <see cref="Projectile3D"/>.
/// </summary>
public class Player3DMeteor : MonoBehaviour, IHotbarSkill
{
    [Tooltip("Hotbar icon (Skill Icon pack). Optional — the bar falls back to the text label if unset.")]
    public Sprite icon;
    public KeyCode key = KeyCode.Alpha3;
    public int manaCost = 16;
    public float cooldown = 9f;
    [Tooltip("Max distance the impact point can be from the player (the reticle is clamped to this).")]
    public float castRange = 11f;
    [Tooltip("Impact AoE radius (the ground-target circle shows this).")]
    public float radius = 4.5f;
    [Tooltip("TOTAL damage to every mob in the blast, as a multiple of a normal magic hit (split across hits).")]
    public float damageMult = 2.4f;

    [Header("Multi-hit + status (RO-style: one meteor per tick, live per-tick combo total)")]
    [Tooltip("Number of meteors that fall = number of damage ticks. Each meteor that lands triggers one burst " +
             "of hits (and its own rising yellow total) on impact.")]
    public int ticks = 3;
    [Tooltip("Hits per tick (per meteor) — each is a SEPARATE roll (+ stun chance). Total hits = ticks × hitsPerTick.")]
    public int hitsPerTick = 5;
    [Tooltip("Seconds between hits within a tick.")]
    public float hitInterval = 0.2f;
    [Tooltip("Seconds between successive meteor IMPACTS — they start at staggered heights so they land this far " +
             "apart in time. ~ a tick's hit duration ((hitsPerTick−1)×hitInterval) keeps the bursts sequential.")]
    public float meteorInterval = 1.2f;
    [Tooltip("Horizontal random spread of each meteor's drop so the salvo reads as a shower, not one column.")]
    public float meteorScatter = 0.6f;
    [Range(0f, 1f)]
    [Tooltip("Chance PER HIT to stun a mob — independent roll each hit (so more hits = more stun chances).")]
    public float stunChance = 0.25f;
    [Tooltip("Stun duration (mob is frozen + shows a swirl).")]
    public float stunSeconds = 1.5f;

    [Header("Fall")]
    public float fallHeight = 9f;
    public float fallSpeed = 16f;

    [Header("Meteor FX (FX/Projectile/Fireball)")]
    public Texture2D meteorSheet;
    public int meteorFrameSize = 16;
    public int meteorFrames = 4;
    public float meteorFps = 14f;
    public float meteorScale = 2.4f;
    [Header("Impact FX (FX/Elemental/Explosion)")]
    public Texture2D impactSheet;
    public int impactFrameSize = 40;
    public int impactFrames = 9;
    public float impactScale = 2.0f;

    [Header("Impact screen shake (per meteor)")]
    [Tooltip("Camera shake strength (world units) on EACH meteor's impact. 0 = off.")]
    public float shakeStrength = 0.3f;
    [Tooltip("Camera shake duration (seconds) per meteor impact.")]
    public float shakeDuration = 0.18f;

    [Header("Audio")]
    public SfxSet castSfx;
    public SfxSet failSfx;
    [Tooltip("Plays on EVERY hit of the multi-hit — once per hit beat (NOT per mob, NOT per tick). The custom hit.wav.")]
    public SfxSet hitSfx;
    [Tooltip("Plays once per METEOR as it LAUNCHES — randomized Fireball variants. A fireball whoosh per meteor.")]
    public SfxSet meteorSfx;

    float nextUse, lastDuration;
    static readonly Color NoManaColor = new Color(0.55f, 0.6f, 1f);
    static readonly Color MeteorColor = new Color(1f, 0.55f, 0.3f);

    void Update()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return;
        if (DevSettings.Blocking) return;
        if (!Pressed() || PointerOverUI()) return;
        Activate();
    }

    public bool Activate()
    {
        if (Character.Instance == null || Character.Instance.IsDead) return false;
        if (StatusController.Player != null && StatusController.Player.Silenced)
        { DamagePopup.SpawnText(transform.position, "Silenced", new Color(0.7f, 0.5f, 1f)); return false; }
        if (Time.time < nextUse) return false;
        if (GroundTargeter.Active) { GroundTargeter.Cancel(); return false; }   // re-press cancels aiming

        // Enter AIM mode: a circle follows the cursor; left-click drops the meteor (CastAt), Esc cancels.
        // MP/cooldown are only spent on confirm, so cancelling costs nothing.
        GroundTargeter.Begin(transform, radius, castRange, CastAt);
        return true;
    }

    // Called by GroundTargeter when the player confirms the impact point.
    void CastAt(Vector3 center)
    {
        if (Character.Instance == null || Character.Instance.IsDead || Time.time < nextUse) return;

        if (!Character.Instance.SpendMana(manaCost))
        {
            Sfx.Play(failSfx);
            DamagePopup.SpawnText(transform.position, "No MP", NoManaColor);
            return;
        }

        lastDuration = cooldown;
        nextUse = Time.time + cooldown;
        Sfx.Play(castSfx);
        DamagePopup.SpawnText(center + Vector3.up * 0.6f, "Meteor!", MeteorColor);   // once, at cast

        // Drop ONE meteor per tick, staggered in TIME (meteorInterval apart) so each meteor is its own fall
        // animation with its own fireball whoosh on launch. Each impact fires its OWN damage tick (DetonateTick),
        // so the numbers land exactly when that meteor hits.
        StartCoroutine(MeteorShower(center));
    }

    // Spawns the meteors one at a time, meteorInterval apart — staggered LAUNCHES (a distinct fireball sound +
    // fall animation per meteor) AND staggered impacts. Coroutines keep running when the component is merely
    // disabled (enabled=false on a class switch); they only stop if the player object itself is destroyed.
    IEnumerator MeteorShower(Vector3 center)
    {
        int meteors = Mathf.Max(1, ticks);
        Vector3 land = center + Vector3.up * 0.3f;
        float fallTime = fallHeight / Mathf.Max(0.1f, fallSpeed);
        var wait = new WaitForSeconds(Mathf.Max(0.05f, meteorInterval));
        for (int m = 0; m < meteors; m++)
        {
            Vector3 from = center + Vector3.up * fallHeight
                                  + new Vector3(Random.Range(-meteorScatter, meteorScatter), 0f,
                                                Random.Range(-meteorScatter, meteorScatter));
            Sfx.Play(meteorSfx);   // fireball whoosh as THIS meteor launches (staggered → one distinct sound per meteor)
            // No impact FX on the projectile itself — DetonateTick fires one explosion pulse per meteor.
            Projectile3D.Spawn(meteorSheet, meteorFrameSize, meteorFrames, meteorFps, meteorScale,
                from, null, land, fallSpeed, 0.4f, fallTime + 1f, () => DetonateTick(center));
            if (m < meteors - 1) yield return wait;
        }
    }

    // One meteor's impact = ONE damage tick: a single burst of hitsPerTick separate rolls with its own rising
    // yellow combo total (overkill-safe → numbers keep showing on a mob that died partway) + one explosion pulse.
    void DetonateTick(Vector3 center)
    {
        CameraFollow3D.ShakeMain(shakeStrength, shakeDuration);   // screen shake on THIS meteor's impact
        if (Character.Instance == null) return;

        // Per-hit damage = the full meteor (damageMult) split across EVERY hit of ALL ticks (ticks × hitsPerTick),
        // so the 3-meteor shower deals the same total as one big strike — a visual split, not a power buff.
        int totalHits = Mathf.Max(1, ticks * hitsPerTick);
        MultiHitBurst.Run(center, radius, 1, hitsPerTick, hitInterval, 0f,   // 1 tick → inter-tick wait unused
            (ai, hitIndex) =>
            {
                if (Character.Instance == null) return 0;
                int power = Mathf.RoundToInt(Character.Instance.RollMagicDamage(out bool crit) * (damageMult / totalHits));
                int dealt = ai.Health.TakeMagicDamageOverkill(power, crit, Element.Fire);
                if (!ai.Health.IsDead && Random.value < stunChance) ai.Stun(stunSeconds);   // only stun the living
                return dealt;
            },
            c => SlashEffect.Spawn(impactSheet, impactFrameSize, impactFrames, 24f, c, impactScale),   // 1 explosion this meteor (no impact sound — fireball plays on launch)
            () => Sfx.Play(hitSfx));                                                                    // custom hit.wav on EVERY hit beat
    }

    // ---- IHotbarSkill ----
    public int SlotOrder => 2;
    public string ShortLabel => "Meteor";
    public string KeyLabel => "3";
    public int ManaCost => manaCost;
    public Sprite Icon => icon;
    public bool Affordable => Character.Instance != null && Character.Instance.CurrentMP >= manaCost;
    public float CooldownRemaining => Mathf.Max(0f, nextUse - Time.time);
    public float CooldownFraction01 => lastDuration <= 0.001f ? 0f : Mathf.Clamp01(CooldownRemaining / lastDuration);
    public Color Tint => MeteorColor;
    public bool InClass(PlayerClass c) => c == PlayerClass.Mage;

    static bool PointerOverUI() => EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

    bool Pressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && key == KeyCode.Alpha3 && Keyboard.current.digit3Key.wasPressedThisFrame) return true;
#endif
        return Input.GetKeyDown(key);
    }
}
