using UnityEngine;

/// <summary>How a whole combo chain resolves its aim, ONCE, at trigger — the most-demanding aim among its members
/// wins (see the PRD "Skill Combo System"). The chosen unit/point then feeds every step.</summary>
public enum ComboAim
{
    /// <summary>Every member is Self/Target — no reticle; the chain just casts.</summary>
    None,
    /// <summary>At least one member is ground/AoE (and none single-target) — pick one ground POINT up front.</summary>
    Ground,
    /// <summary>At least one member is single-target — pick one UNIT up front (beats Ground).</summary>
    Unit,
}

/// <summary>
/// One authored combo: an ordered set of <see cref="Slots"/> skill ids (by <see cref="SkillDef.id"/>) bound to a
/// single hotkey. Pure data + derived reads — the execution lives in <see cref="ComboRunner"/>, the ownership/
/// uniqueness in <see cref="ComboBook"/>. The member skills are referenced by id (never copied), so a chain reads
/// the live <see cref="SkillDef"/> from <see cref="SkillCatalog"/> and the live <see cref="SkillRunner"/> from
/// <see cref="PlayerSkills"/> — one owner per skill, no drift.
///
/// <para>Shared cooldown is the <b>max</b> of the members' cooldowns (gated by the slowest skill), so a heavy
/// finisher can't get a cheap effective cooldown by riding alongside three fast skills. The cooldown clock is a
/// runtime field here (not serialized) started at trigger, so a broken/failed chain still spends it.</para>
/// </summary>
[System.Serializable]
public class ComboChain
{
    /// <summary>Skills per chain — the ramp has four rungs (1× / 1.5× / 2.5× / 4×).</summary>
    public const int Slots = 4;

    /// <summary>Fewest filled slots for a chain to be playable — a single skill is just a solo cast, not a combo.
    /// Shorter chains (2–3) fire and ramp only over their filled slots; the full four earns the top 4× finisher.</summary>
    public const int MinSlots = 2;

    [Tooltip("Ordered member skills by SkillDef.id (\"\" = empty slot). Length is fixed to Slots.")]
    public string[] skillIds = new string[Slots];
    [Tooltip("The key that fires this whole chain. None = unbound (never fires).")]
    public KeyCode hotkey = KeyCode.None;

    // Runtime-only shared-cooldown clock — set at trigger by StartCooldown, read by Ready/CooldownRemaining.
    [System.NonSerialized] float cooldownUntil;

    /// <summary>The skill id in slot <paramref name="i"/>, or "" when empty/out of range.</summary>
    public string Id(int i) => (skillIds != null && i >= 0 && i < skillIds.Length) ? (skillIds[i] ?? "") : "";
    public bool HasSlot(int i) => !string.IsNullOrEmpty(Id(i));

    /// <summary>All four slots filled — the top of the ramp (its finisher earns the 4× rung).</summary>
    public bool IsComplete
    {
        get { for (int i = 0; i < Slots; i++) if (!HasSlot(i)) return false; return true; }
    }

    /// <summary>Effective chain length = the leading run of filled slots, stopping at the first gap. This is what
    /// <see cref="ComboRunner"/> actually fires (it walks slots left-to-right and halts at the first empty one), so
    /// a chain with a hole is only as long as its filled prefix.</summary>
    public int RunLength
    {
        get { int n = 0; for (int i = 0; i < Slots; i++) { if (!HasSlot(i)) break; n++; } return n; }
    }

    /// <summary>Playable: at least <see cref="MinSlots"/> skills filled left-to-right (no leading gap). Replaces the
    /// old all-four gate so 2- and 3-skill chains still trigger — the ramp just tops out lower.</summary>
    public bool IsRunnable => RunLength >= MinSlots;

    /// <summary>Index of the chain's finisher = its last filled slot (RunLength − 1), or −1 when empty. The HUD reads
    /// this so a short chain shows its real last skill's icon and multiplier, not the hardcoded slot 4.</summary>
    public int FinisherSlot => RunLength - 1;

    public int FilledCount
    {
        get { int n = 0; for (int i = 0; i < Slots; i++) if (HasSlot(i)) n++; return n; }
    }

    /// <summary>Contains this skill id in any slot.</summary>
    public bool Contains(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        for (int i = 0; i < Slots; i++) if (Id(i) == id) return true;
        return false;
    }

    /// <summary>The authored <see cref="SkillDef"/> for slot <paramref name="i"/> (from the catalog), or null.</summary>
    public SkillDef Def(int i) => SkillCatalog.Get(Id(i));

    /// <summary>The live <see cref="SkillRunner"/> on the player for slot <paramref name="i"/>, or null if the player
    /// isn't built yet / doesn't have that skill component.</summary>
    public SkillRunner Runner(int i) => PlayerSkills.ById(Id(i)) as SkillRunner;

    // ---- shared cooldown (max of members) -----------------------------------

    /// <summary>Shared cooldown seconds = the MAX of the member skills' own cooldowns. 0 when no members resolve.</summary>
    public float SharedCooldown()
    {
        float max = 0f;
        for (int i = 0; i < Slots; i++) { var d = Def(i); if (d != null) max = Mathf.Max(max, d.cooldown); }
        return max;
    }

    /// <summary>Start the shared cooldown NOW (called at trigger, so a chain that later breaks still pays it).</summary>
    public void StartCooldown() => cooldownUntil = Time.time + SharedCooldown();
    public bool Ready => Time.time >= cooldownUntil;
    public float CooldownRemaining => Mathf.Max(0f, cooldownUntil - Time.time);
    public float CooldownFraction01
    {
        get { float cd = SharedCooldown(); return cd <= 0.001f ? 0f : Mathf.Clamp01(CooldownRemaining / cd); }
    }

    // ---- aim resolution (once, at trigger) ----------------------------------

    /// <summary>The chain's aim mode: Unit if ANY member is single-target, else Ground if ANY is ground/AoE, else None.</summary>
    public ComboAim AimMode
    {
        get
        {
            bool hasGround = false;
            for (int i = 0; i < Slots; i++)
            {
                var d = Def(i);
                if (d == null) continue;
                if (d.Reticle == CastReticle.Unit) return ComboAim.Unit;   // most demanding — short-circuit
                if (d.Reticle == CastReticle.Ground) hasGround = true;
            }
            return hasGround ? ComboAim.Ground : ComboAim.None;
        }
    }

    /// <summary>Union of every single-target member's legal target kinds — what the up-front unit reticle validates.</summary>
    public SkillTargets SingleTargetMask
    {
        get
        {
            SkillTargets m = SkillTargets.None;
            for (int i = 0; i < Slots; i++) { var d = Def(i); if (d != null && d.IsSingleTarget) m |= d.SingleTargetMask; }
            return m;
        }
    }

    /// <summary>The reticle reach for the up-front pick — the widest member cast range (fallback 11).</summary>
    public float MaxCastRange
    {
        get { float r = 0f; for (int i = 0; i < Slots; i++) { var d = Def(i); if (d != null) r = Mathf.Max(r, d.castRange); } return r > 0f ? r : 11f; }
    }

    /// <summary>The widest ground footprint among members — sizes the ground reticle ring (fallback 1.5).</summary>
    public float PreviewRadius
    {
        get
        {
            float r = 0f;
            for (int i = 0; i < Slots; i++) { var d = Def(i); if (d != null && d.Reticle == CastReticle.Ground) r = Mathf.Max(r, d.LargestAreaRadius); }
            return r > 0f ? r : 1.5f;
        }
    }
}
