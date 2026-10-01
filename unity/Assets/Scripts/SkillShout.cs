/// <summary>
/// WHAT A SKILL IS CALLED OUT AS when it fires, and whether it is called out at all — "Blossom Wind style, 21st form,
/// Thousand Petals!!", "Last Petal!!", or nothing. Decided by the server alone, which then says the finished words to
/// everybody watching, the caster included, so every screen shows the same call.
///
/// <para>Every skill follows the same rule, ultimates included, and there is no wait between calls. <b>An efficient
/// cast</b> (see <see cref="SkillEfficiency"/>) makes the full style call — only people and shadows make it, since an
/// ordinary creature carries no style of its own, and only a form of a style has one to call. <b>Any other active
/// cast</b> calls its plain name. <b>A passive</b> is called out only when it is efficient.</para>
/// </summary>
public static class SkillShout
{
    /// <summary>The words to call out as <paramref name="def"/> fires, or null for silence.</summary>
    public static string Words(SkillDef def, bool efficient, bool mayCallStyle)
    {
        if (def == null || string.IsNullOrEmpty(def.displayName)) return null;

        if (efficient && mayCallStyle && SkillForms.IsForm(def))
            return WeaponTypes.StyleOf(def.weaponType) + " style, " + SkillForms.Ordinal(SkillForms.Number(def))
                 + " form, " + def.displayName + "!!";

        if (def.IsPassive && !efficient) return null;
        return def.displayName + "!!";
    }
}
