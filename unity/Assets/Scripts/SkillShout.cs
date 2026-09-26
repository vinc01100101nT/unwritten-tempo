/// <summary>
/// WHAT A FORM IS CALLED OUT AS, and whether it is called out at all — "Blossom Wind style, 21st form, Thousand
/// Petals!!", "Last Petal!!", or nothing. Decided by the server alone, which then says the finished words to everybody
/// watching, the caster included, so every screen shows the same call and no player's machine keeps a timer.
///
/// <para><b>The full style call</b> is for an ultimate every time, and for the caster's weapon's trademark form when
/// no style call has been made in the last <see cref="StyleCallSeconds"/>. Only people and shadows make it — an
/// ordinary creature carries no style of its own. <b>Every other pressed form</b> calls its plain name, at most once
/// every <see cref="FormCallSeconds"/>, and a style call counts as one.</para>
/// </summary>
public static class SkillShout
{
    public const double StyleCallSeconds = 30;
    public const double FormCallSeconds = 5;

    /// <summary>When one caster last made each kind of call. Server-only; one per person and one per creature.</summary>
    public sealed class Clock
    {
        public double lastStyleCall = double.NegativeInfinity;
        public double lastFormCall = double.NegativeInfinity;
    }

    /// <summary>The words to call out for a pressed <paramref name="def"/> at <paramref name="now"/>, or null for
    /// silence (not a form, or too soon after the last call). Updates <paramref name="clock"/> when it speaks.</summary>
    public static string Decide(Clock clock, SkillDef def, bool isTrademark, bool mayCallStyle, double now)
    {
        if (clock == null || !SkillForms.IsForm(def)) return null;

        bool styleCall = mayCallStyle && (def.IsUltimate || (isTrademark && now - clock.lastStyleCall >= StyleCallSeconds));
        if (styleCall)
        {
            clock.lastStyleCall = now;
            clock.lastFormCall = now;
            return WeaponTypes.StyleOf(def.weaponType) + " style, " + SkillForms.Ordinal(SkillForms.Number(def))
                 + " form, " + def.displayName + "!!";
        }

        if (now - clock.lastFormCall < FormCallSeconds) return null;
        clock.lastFormCall = now;
        return def.displayName + "!!";
    }
}
