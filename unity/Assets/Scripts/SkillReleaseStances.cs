/// <summary>
/// The single owner of "which <see cref="StanceId"/>s are valid SKILL-RELEASE cues" — the subset a
/// <see cref="SkillDef"/> may pick as its <b>skill type</b>. <see cref="StanceId"/> stays the one vocabulary of poses
/// (SSOT); this only curates the slice the Skill Composer offers and validates against, so the composer dropdown and
/// any runtime check read ONE list instead of each hard-coding the nine ids. Append here whenever a new release cue is
/// added to <see cref="StanceId"/>.
/// </summary>
public static class SkillReleaseStances
{
    /// <summary>The release cues, in menu order. The Skill Composer prepends a "None" entry (no release animation).</summary>
    public static readonly StanceId[] All =
    {
        StanceId.AoeSpell1, StanceId.AoeSpell2, StanceId.AoeSpell3,
        StanceId.SingleSpell1, StanceId.SingleSpell2, StanceId.SingleSpell3,
        StanceId.PhysicalSkill1, StanceId.PhysicalSkill2, StanceId.PhysicalSkill3,
    };

    /// <summary>Human label for a release cue (the composer dropdown uses these, e.g. "AOE Spell Release 1").</summary>
    public static string Label(StanceId id)
    {
        switch (id)
        {
            case StanceId.AoeSpell1: return "AOE Spell Release 1";
            case StanceId.AoeSpell2: return "AOE Spell Release 2";
            case StanceId.AoeSpell3: return "AOE Spell Release 3";
            case StanceId.SingleSpell1: return "Single Spell Release 1";
            case StanceId.SingleSpell2: return "Single Spell Release 2";
            case StanceId.SingleSpell3: return "Single Spell Release 3";
            case StanceId.PhysicalSkill1: return "Physical Skill 1";
            case StanceId.PhysicalSkill2: return "Physical Skill 2";
            case StanceId.PhysicalSkill3: return "Physical Skill 3";
            default: return id.ToString();
        }
    }

    /// <summary>True if <paramref name="id"/> is one of the release cues.</summary>
    public static bool Contains(StanceId id)
    {
        for (int i = 0; i < All.Length; i++) if (All[i] == id) return true;
        return false;
    }
}
