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

    /// <summary>The skill's painted icon, else the picture of the item that runs it (a potion, a food, a hat), or null
    /// when it has neither — then the tile shows <see cref="TextFor(SkillDef)"/>.</summary>
    public static Sprite IconFor(SkillDef def)
        => def == null ? null : def.icon != null ? def.icon : ItemCatalog.IconOfSkill(def.id);

    public static Sprite IconFor(string skillId)
        => string.IsNullOrEmpty(skillId) ? null : IconFor(SkillCatalog.Get(skillId));

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

/// <summary>
/// THE one answer to "what does this running buff or debuff look like on a tile" — the buff tray and the plates over
/// other units both ask here, so a state can never wear one face in the tray and another over a head.
/// </summary>
public static class StateFace
{
    /// <summary>The tile behind a state from no skill — an ailment a creature's swing put on, say.</summary>
    public static readonly Color NoSkillBack = new Color(0.10f, 0.10f, 0.13f, 0.9f);

    /// <summary>The tile's colour: the granting skill's weapon colour, or <see cref="NoSkillBack"/>.</summary>
    public static Color Back(UnitStates.State s)
    {
        string skill = SkillOf(s);
        if (string.IsNullOrEmpty(skill)) return NoSkillBack;
        var w = SkillSymbol.ColorFor(skill);
        return new Color(w.r, w.g, w.b, NoSkillBack.a);
    }

    /// <summary>The granting skill's painted icon, else the ailment's own picture (an ailment a creature's swing put
    /// on comes from no skill), or null — then the tile shows <see cref="Letters"/>.</summary>
    public static Sprite Icon(UnitStates.State s)
    {
        var own = SkillSymbol.IconFor(SkillOf(s));
        return own != null ? own : AilmentIcon(s.id);
    }

    /// <summary>The skill whose face a state wears: the one that granted it, or for a held orb (granted by no skill
    /// id) that orb's own skill.</summary>
    static string SkillOf(UnitStates.State s)
    {
        if (!string.IsNullOrEmpty(s.sourceSkillId)) return s.sourceSkillId;
        var orb = OrbMath.OrbOfPile(s.id);
        return orb != OrbElement.None ? OrbMath.SkillIdOf(orb) : s.sourceSkillId;
    }

    /// <summary>The picture of an ailment (<see cref="UiIcons.Ailment"/>), or null for a state that is not one.</summary>
    public static Sprite AilmentIcon(StateId id) => UiIcons.Ailment(id);

    /// <summary>The granting skill's two letters, else the state's name, else its id.</summary>
    public static string Letters(UnitStates.State s)
        => !string.IsNullOrEmpty(s.symbol) ? s.symbol : string.IsNullOrEmpty(s.name) ? s.id.ToString() : s.name;

    /// <summary>The state's own colour on the face — how an armed charge and the debt it becomes are told apart.</summary>
    public static Color Tint(UnitStates.State s) => s.tint == default ? Color.white : s.tint;
}
