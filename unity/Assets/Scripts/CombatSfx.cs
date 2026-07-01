using UnityEngine;

/// <summary>
/// The "received" combat SFX layer — impact / whiff sounds that play FROM THE RECEIVER'S position (the unit
/// that got hit), always 3D-positional via <see cref="Sfx.PlayAt"/>. So a blow lands where the TARGET is,
/// not where the attacker stands: a long-range hit sounds off at the distant mob (correctly panned + faded
/// by distance, sitting in the spatial/environment mix) instead of blasting from the player. Lives on
/// <c>GameSystems</c> (the persistent Systems layer), wired at author-time by <c>SystemsSceneBuilder</c>.
///
/// <para>One logical cue per combat verb:
///   • <see cref="Hit"/>   — a BASIC attack lands (crit = the louder <see cref="crit"/> variant).
///   • <see cref="SkillHit"/> — any SKILL / magic strike lands. THROTTLED to one voice per frame, so an AoE
///     that hits N mobs on the same frame plays a single impact instead of an N-voice wall of mush.
///   • <see cref="Miss"/>  — an attack whiffed (rolled against the target's FLEE); melee vs ranged variant.
/// The receiver (mob <see cref="Health"/>) calls these — it already knows crit, the miss roll, and its own
/// position — so every damage source routes through the same spatial impact without per-attacker wiring.</para>
/// </summary>
public class CombatSfx : MonoBehaviour
{
    public static CombatSfx Instance { get; private set; }

    [Tooltip("Basic attack lands.")]              public SfxSet hit;
    [Tooltip("Basic attack CRITs (louder).")]     public SfxSet crit;
    [Tooltip("Any skill / magic strike lands.")]  public SfxSet skillHit;
    [Tooltip("Melee attack whiffs (FLEE).")]      public SfxSet missMelee;
    [Tooltip("Ranged attack whiffs (FLEE).")]     public SfxSet missRanged;

    int lastSkillFrame = -1;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>A basic attack landed at <paramref name="pos"/> (the target). <paramref name="isCrit"/> picks
    /// the louder crit variant.</summary>
    public static void Hit(Vector3 pos, bool isCrit)
    {
        if (Instance != null) Sfx.PlayAt(isCrit ? Instance.crit : Instance.hit, pos);
    }

    /// <summary>A skill / magic strike landed at <paramref name="pos"/>. Coalesced to one voice per frame so an
    /// AoE hitting many mobs the same frame is a single impact, not one clip per mob.</summary>
    public static void SkillHit(Vector3 pos)
    {
        if (Instance == null) return;
        if (Instance.lastSkillFrame == Time.frameCount) return;
        Instance.lastSkillFrame = Time.frameCount;
        Sfx.PlayAt(Instance.skillHit, pos);
    }

    /// <summary>An attack whiffed at <paramref name="pos"/> (the target). <paramref name="ranged"/> picks the
    /// ranged whiff over the melee one.</summary>
    public static void Miss(Vector3 pos, bool ranged)
    {
        if (Instance != null) Sfx.PlayAt(ranged ? Instance.missRanged : Instance.missMelee, pos);
    }
}
