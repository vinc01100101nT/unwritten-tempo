using System.Collections.Generic;

/// <summary>
/// Which FORM of its weapon's style a skill is — "Blossom Wind — 13th Form". A form's number is its place in the order
/// the weapon's skills are learned (mastery level, then id to break a tie), so it is read off the skill list and never
/// typed anywhere: author a new skill and every form after it moves up by one on its own.
/// </summary>
public static class SkillForms
{
    static IReadOnlyList<SkillDef> builtFrom;
    static readonly Dictionary<string, int> numberById = new Dictionary<string, int>();
    static readonly List<SkillDef> scratch = new List<SkillDef>();

    /// <summary>1 for the first skill a weapon teaches, and so on; 0 for a skill that belongs to no weapon.</summary>
    public static int Number(SkillDef def)
    {
        if (def == null || def.weaponType == WeaponType.None || string.IsNullOrEmpty(def.id)) return 0;
        var all = SkillCatalog.All;
        if (!ReferenceEquals(all, builtFrom)) Build(all);
        return numberById.TryGetValue(def.id, out int n) ? n : 0;
    }

    /// <summary>Is this skill a form of a style in the game — one of its weapon's skills, on a weapon with a style?</summary>
    public static bool IsForm(SkillDef def)
        => def != null && WeaponTypes.StyleOf(def.weaponType).Length > 0 && Number(def) > 0;

    /// <summary>"Blossom Wind — 13th Form", or empty when the skill's weapon has no style in the game yet.</summary>
    public static string Line(SkillDef def)
    {
        string style = def != null ? WeaponTypes.StyleOf(def.weaponType) : "";
        int n = Number(def);
        return style.Length == 0 || n == 0 ? "" : style + " — " + Ordinal(n) + " Form";
    }

    /// <summary>1st, 2nd, 3rd, 4th … 11th, 12th, 13th … 21st, 22nd.</summary>
    public static string Ordinal(int n)
    {
        int lastTwo = n % 100, last = n % 10;
        string suffix = lastTwo >= 11 && lastTwo <= 13 ? "th" : last == 1 ? "st" : last == 2 ? "nd" : last == 3 ? "rd" : "th";
        return n + suffix;
    }

    static void Build(IReadOnlyList<SkillDef> all)
    {
        numberById.Clear();
        scratch.Clear();
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && all[i].weaponType != WeaponType.None && !string.IsNullOrEmpty(all[i].id)) scratch.Add(all[i]);
        scratch.Sort((a, b) => a.weaponType != b.weaponType ? a.weaponType.CompareTo(b.weaponType)
                             : a.masteryLevel != b.masteryLevel ? a.masteryLevel.CompareTo(b.masteryLevel)
                             : string.CompareOrdinal(a.id, b.id));
        int n = 0;
        for (int i = 0; i < scratch.Count; i++)
        {
            n = i > 0 && scratch[i].weaponType == scratch[i - 1].weaponType ? n + 1 : 1;
            numberById[scratch[i].id] = n;
        }
        scratch.Clear();
        builtFrom = all;
    }
}
