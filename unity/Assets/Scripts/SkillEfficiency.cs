/// <summary>
/// A SKILL'S EFFICIENCY — a critical hit for a whole cast. When a TRADEMARK SKILL fires (<see cref="Trademarks.Qualifies"/>:
/// a weapon's skill with a main number), the server rolls once, whatever weapon the caster holds; on success every main
/// number that cast runs is lifted by <see cref="OutputBonus"/> on top of everything else lifting it, and the cast
/// makes the full style call (<see cref="SkillShout"/>). A skill that is not a trademark skill has nothing to lift and
/// never rolls. The chance is the same for everyone and every skill.
/// </summary>
public static class SkillEfficiency
{
    public const float ProcChance = 0.05f;
    public const float OutputBonus = 0.20f;

    /// <summary>Is a cast efficient, given whether its skill is a trademark skill and a roll from 0 (inclusive) to 1
    /// (exclusive)?</summary>
    public static bool Procs(bool isTrademark, float roll01) => isTrademark && roll01 < ProcChance;

    /// <summary>The lift on a cast's main numbers once its efficiency is counted in.</summary>
    public static float OutputLift(float lift, bool efficient) => efficient ? lift + OutputBonus : lift;
}
