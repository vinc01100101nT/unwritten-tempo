using UnityEngine;

/// <summary>
/// The Staff's orbs, as plain numbers in and out (the arithmetic rule: no sum lives inside a behaviour).
///
/// <para>Three slots, four elements. A combination is written as three letters, F Fire, E Earth, W Wind, A Water — so
/// "FFE" is two Fire orbs and one Earth — and is matched by COUNTS, never by order.</para>
/// </summary>
public static class OrbMath
{
    /// <summary>How many orbs a caster holds at most; pressing one more drops the oldest.</summary>
    public const int Slots = 3;

    /// <summary>The orb level a pure combination (three of one element — an ultimate) opens at.</summary>
    public const int UltimateOrbLevel = 20;

    /// <summary>What each orb level after the first adds to the parts it grows: a level-35 orb lifts them +44.9%, the
    /// same as a level-10 skill of any other weapon (+45%), because four orbs carry what twenty skills carry elsewhere.</summary>
    public const float LevelPercent = 0.0132f;

    /// <summary>The lift an orb at <paramref name="level"/> puts on every part it grows. Level 1 is the part as authored.</summary>
    public static float LevelLift(int level) => Mathf.Max(0, level - 1) * LevelPercent;

    /// <summary>The most one element's held orbs can add to their stat, together — three level-35 orbs reach it.</summary>
    public const float StatCap = 0.15f;

    /// <summary>The stat an element's held orbs give, summed and capped.</summary>
    public static float HeldStat(float summed) => Mathf.Clamp(summed, 0f, StatCap);

    /// <summary>The orb a combination letter names, or None for any other character.</summary>
    public static OrbElement FromLetter(char c)
    {
        switch (char.ToUpperInvariant(c))
        {
            case 'F': return OrbElement.Fire;
            case 'E': return OrbElement.Earth;
            case 'W': return OrbElement.Wind;
            case 'A': return OrbElement.Water;
            default:  return OrbElement.None;
        }
    }

    /// <summary>One number for a set of counts — the same for every order the orbs were pressed in.</summary>
    public static int Key(int fire, int earth, int wind, int water) => fire * 1000 + earth * 100 + wind * 10 + water;

    /// <summary>The key of a combination string, or -1 when it is not exactly three valid letters.</summary>
    public static int KeyOf(string combo)
    {
        if (string.IsNullOrEmpty(combo) || combo.Length != Slots) return -1;
        int f = 0, e = 0, w = 0, a = 0;
        for (int i = 0; i < combo.Length; i++)
        {
            switch (FromLetter(combo[i]))
            {
                case OrbElement.Fire:  f++; break;
                case OrbElement.Earth: e++; break;
                case OrbElement.Wind:  w++; break;
                case OrbElement.Water: a++; break;
                default: return -1;
            }
        }
        return Key(f, e, w, a);
    }

    /// <summary>True when a combination is three of one element — an ultimate.</summary>
    public static bool IsPure(string combo) =>
        KeyOf(combo) >= 0 && combo.Length == Slots && combo[0] == combo[1] && combo[1] == combo[2];

    /// <summary>The orb skill's id for an element — the one place the four ids are written.</summary>
    public static string SkillIdOf(OrbElement e)
    {
        switch (e)
        {
            case OrbElement.Fire:  return "skill.staff.orb_fire";
            case OrbElement.Earth: return "skill.staff.orb_earth";
            case OrbElement.Wind:  return "skill.staff.orb_wind";
            case OrbElement.Water: return "skill.staff.orb_water";
            default:               return "";
        }
    }

    /// <summary>True when <paramref name="skillId"/> is one of the four orbs.</summary>
    public static bool IsOrbSkill(string skillId) =>
        !string.IsNullOrEmpty(skillId) && (skillId == SkillIdOf(OrbElement.Fire) || skillId == SkillIdOf(OrbElement.Earth)
                                           || skillId == SkillIdOf(OrbElement.Wind) || skillId == SkillIdOf(OrbElement.Water));

    /// <summary>A unit's held orbs as one number for the wire: three bits per orb, oldest first, then a bit saying a
    /// Staff is in hand. 0 = nothing to draw.</summary>
    public static int Pack(OrbElement[] oldestFirst, int count, bool staffInHand)
    {
        int packed = 0;
        int n = Mathf.Min(count, Slots);
        for (int i = 0; i < n; i++) packed |= ((int)oldestFirst[i] & 7) << (i * 3);
        if (staffInHand) packed |= 1 << (Slots * 3);
        return packed;
    }

    /// <summary>The <paramref name="index"/>-th held orb (oldest first) of a packed set, or None.</summary>
    public static OrbElement Unpack(int packed, int index) =>
        index < 0 || index >= Slots ? OrbElement.None : (OrbElement)((packed >> (index * 3)) & 7);

    /// <summary>True when the packed set says a Staff is in hand.</summary>
    public static bool StaffInHand(int packed) => (packed & (1 << (Slots * 3))) != 0;

    /// <summary>The game element an orb is.</summary>
    public static Element ElementOf(OrbElement e)
    {
        switch (e)
        {
            case OrbElement.Fire:  return Element.Fire;
            case OrbElement.Earth: return Element.Earth;
            case OrbElement.Wind:  return Element.Wind;
            case OrbElement.Water: return Element.Water;
            default:               return Element.Neutral;
        }
    }

    /// <summary>The state pile that holds an element's orbs.</summary>
    public static StateId PileOf(OrbElement e)
    {
        switch (e)
        {
            case OrbElement.Fire:  return StateId.OrbFire;
            case OrbElement.Earth: return StateId.OrbEarth;
            case OrbElement.Wind:  return StateId.OrbWind;
            default:               return StateId.OrbWater;
        }
    }

    /// <summary>The sub-stat an element's held orbs raise: Fire magic attack, Earth defence, Wind cast speed, Water max MP.</summary>
    public static SubStat StatOf(OrbElement e)
    {
        switch (e)
        {
            case OrbElement.Fire:  return SubStat.MagicAttack;
            case OrbElement.Earth: return SubStat.Defense;
            case OrbElement.Wind:  return SubStat.CastSpeed;
            default:               return SubStat.MaxMP;
        }
    }

    /// <summary>The orb element a pile holds, or None for any other pile.</summary>
    public static OrbElement OrbOfPile(StateId id)
    {
        switch (id)
        {
            case StateId.OrbFire:  return OrbElement.Fire;
            case StateId.OrbEarth: return OrbElement.Earth;
            case StateId.OrbWind:  return OrbElement.Wind;
            case StateId.OrbWater: return OrbElement.Water;
            default:               return OrbElement.None;
        }
    }

    /// <summary>How many strikes a run that grows with cast speed plays: the authored count times one plus the cast-speed
    /// bonus, rounded down, never fewer than authored and never past the ceiling.</summary>
    public static int RepeatsWithCastSpeed(int authored, float castSpeedBonus, int ceiling)
    {
        int n = Mathf.FloorToInt(Mathf.Max(1, authored) * (1f + Mathf.Max(0f, castSpeedBonus)));
        return Mathf.Clamp(n, Mathf.Max(1, authored), Mathf.Max(Mathf.Max(1, authored), ceiling));
    }
}

/// <summary>
/// The four element marks and their caps. Each element leaves one mark, and only the element that beats it cashes it in:
/// Water puts out Burn, Wind scatters Chill, Earth grounds Static, Fire melts Stone. Burn and Chill never sit on the
/// same unit: the mark already there wins.
/// </summary>
public static class MarkMath
{
    /// <summary>A full Chill count: this many stacks freeze the unit.</summary>
    public const int FreezeAt = 3;
    /// <summary>How long a freeze made by a full Chill count lasts — far longer than any stun, as any hit that costs
    /// health breaks it.</summary>
    public const float FreezeSeconds = 10f;
    /// <summary>How much of a freeze one burn attempt melts: a burn never lands on a frozen unit, it shortens the ice
    /// instead — once a second on burning ground, so the freeze runs out twice as fast.</summary>
    public const float BurnMeltsFreezeSeconds = 1f;

    /// <summary>A freeze's end, <paramref name="seconds"/> sooner.</summary>
    public static float Melted(float endTime, float seconds) => endTime - Mathf.Max(0f, seconds);

    /// <summary>How much each Chill stack slows, as a fraction of movement speed.</summary>
    public const float ChillSlowPerStack = 0.15f;
    /// <summary>The most Static stacks one unit can hold.</summary>
    public const int MaxStatic = 5;
    /// <summary>How much each Static stack cuts attack speed, as a fraction.</summary>
    public const float StaticPerStack = 0.06f;
    /// <summary>The most Stone charges the Staff can keep on one unit, summed over every Staff skill.</summary>
    public const int MaxStone = 5;
    /// <summary>The most one Stone charge can weaken an attack — well short of nothing.</summary>
    public const float StoneCutCap = 0.3f;
    /// <summary>A FROZEN unit, scattered by Wind, pays out as this many Chill stacks — the shatter.</summary>
    public const int FrozenCountsAs = 6;
    /// <summary>The most seconds of burn age a strike counts.</summary>
    public const float BurnAgeCap = 10f;
    /// <summary>The most a strike's damage multiplier grows from max MP.</summary>
    public const float MaxManaGrowthCap = 2f;

    /// <summary>Chill stacks after adding <paramref name="add"/> to <paramref name="held"/>, under a per-skill ceiling
    /// (0 = none), and whether that count freezes the unit.</summary>
    public static int ChillAfter(int held, int add, int skillCeiling, out bool freezes)
    {
        int ceiling = skillCeiling > 0 ? Mathf.Min(skillCeiling, FreezeAt) : FreezeAt;
        int n = Mathf.Max(held, Mathf.Min(held + Mathf.Max(0, add), ceiling));
        freezes = n >= FreezeAt;
        return n;
    }

    /// <summary>The burn damage still to come: damage per tick times the whole ticks left.</summary>
    public static int BurnLeft(float perTick, float secondsLeft, float tickInterval) =>
        tickInterval <= 0f ? 0 : Mathf.Max(0, Mathf.RoundToInt(Mathf.Max(0f, perTick) * Mathf.Floor(Mathf.Max(0f, secondsLeft) / tickInterval)));

    /// <summary>What a strike's multiplier grows by for a burn <paramref name="ageSeconds"/> old, counted at most
    /// <see cref="BurnAgeCap"/> seconds.</summary>
    public static float BurnAgeGrowth(float perSecond, float ageSeconds) =>
        perSecond * Mathf.Clamp(ageSeconds, 0f, BurnAgeCap);

    /// <summary>What a strike's multiplier grows by from the caster's max MP, at most <see cref="MaxManaGrowthCap"/>.</summary>
    public static float MaxManaGrowth(float perPoint, int maxMp) =>
        Mathf.Clamp(perPoint * Mathf.Max(0, maxMp), 0f, MaxManaGrowthCap);

    /// <summary>The marks a strike cashes, as its payoff count: a frozen unit scattered by Wind counts as a full shatter.</summary>
    public static int ChillCount(int stacks, bool frozen) => frozen ? FrozenCountsAs : Mathf.Max(0, stacks);

    /// <summary>The element a mark turns its unit's armour into, at level 1: Burn Fire, Chill (and a freeze) Water,
    /// Static Wind, Stone Earth.</summary>
    public static Element ElementOf(MarkKind mark)
    {
        switch (mark)
        {
            case MarkKind.Burn:   return Element.Fire;
            case MarkKind.Chill:  return Element.Water;
            case MarkKind.Static: return Element.Wind;
            case MarkKind.Stone:  return Element.Earth;
            default:              return Element.Neutral;
        }
    }

    /// <summary>One stack of <paramref name="mark"/> made <paramref name="grow"/> times as strong (1.2 = +20%) — a Stone
    /// charge never past <see cref="StoneCutCap"/>.</summary>
    public static float Strengthened(MarkKind mark, float magnitude, float grow)
    {
        float v = Mathf.Max(0f, magnitude) * Mathf.Max(0f, grow);
        return mark == MarkKind.Stone ? Mathf.Min(v, StoneCutCap) : v;
    }

    /// <summary>How many of a mark a copy adds to a unit holding <paramref name="held"/>, from one holding
    /// <paramref name="source"/>: it is raised to the source's count, never past it.</summary>
    public static int CopyAdds(int held, int source) => Mathf.Max(0, source - Mathf.Max(0, held));

    /// <summary>The element <paramref name="last"/> beats (Fire beats Earth,
    /// Earth beats Wind, Wind beats Water, Water beats Fire). Any other element leaves the unit as it is (Neutral).</summary>
    public static Element WeakTo(Element last)
    {
        switch (last)
        {
            case Element.Fire:  return Element.Earth;
            case Element.Earth: return Element.Wind;
            case Element.Wind:  return Element.Water;
            case Element.Water: return Element.Fire;
            default:            return Element.Neutral;
        }
    }
}
