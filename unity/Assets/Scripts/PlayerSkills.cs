using UnityEngine;

/// <summary>
/// Runtime lookup from a stable skill id to the live <see cref="IHotbarSkill"/> that plays it — what the hotbar,
/// the key router and the skill menu use to fetch a skill's icon/label/tint and fire it without knowing concrete
/// types. The id is the composed skill's <see cref="SkillDef.id"/>.
///
/// <para>The answer comes from the CHARACTER, which owns the ordered skill list and builds a skill's component the
/// first time it is asked for. This used to keep its own cache, rebuilt by scanning every component on the player —
/// a second copy of a list the character already had, which had to be told by hand whenever the real one changed.
/// One owner now, so there is nothing to refresh and nothing that can go stale.</para>
/// </summary>
public static class PlayerSkills
{
    /// <summary>The live skill for <paramref name="id"/>, or null when it is not one of this character's.</summary>
    public static IHotbarSkill ById(string id)
    {
        var me = Character.Instance;
        return me != null ? me.RunnerById(id) : null;
    }

    /// <summary>True once the player's skills are resolvable.</summary>
    public static bool Ready => Character.Instance != null && Character.Instance.HasSkills;
}
