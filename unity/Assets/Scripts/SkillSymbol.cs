using UnityEngine;

/// <summary>
/// THE one answer to "what does this skill look like on a tile" — its two letters, and the colour of the weapon it
/// belongs to. Every surface that used to draw a skill icon asks here instead: the hotbar, the Skills window, the
/// buff tray, the hover plate and the unit status row, so all five can never disagree about a skill's face.
/// </summary>
public static class SkillSymbol
{
    /// <summary>A skill with no weapon has no colour of its own; this is what it wears instead.</summary>
    public static readonly Color NoWeapon = new Color(0.42f, 0.45f, 0.50f, 1f);

    /// <summary>The two letters, falling back to the skill's whole name when nobody authored a symbol.</summary>
    public static string TextFor(SkillDef def) => def != null ? def.Symbol : "";

    /// <summary>The colour of the skill's weapon, or <see cref="NoWeapon"/> when it belongs to none — or when nobody
    /// has picked that weapon's colour yet.</summary>
    public static Color ColorFor(SkillDef def)
    {
        if (def == null || def.weaponType == WeaponType.None) return NoWeapon;
        return WeaponColors.Load()?.Lookup(def.weaponType) ?? NoWeapon;
    }

    /// <summary>The same pair, looked up by skill id — for the state store, which knows only which skill granted a
    /// buff. An id nobody knows reads blank rather than inventing a face for it.</summary>
    public static string TextFor(string skillId)
        => string.IsNullOrEmpty(skillId) ? "" : TextFor(SkillCatalog.Get(skillId));

    public static Color ColorFor(string skillId)
        => string.IsNullOrEmpty(skillId) ? NoWeapon : ColorFor(SkillCatalog.Get(skillId));
}
