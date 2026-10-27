using UnityEngine;

/// <summary>An armour piece's rolled WORD — the armour's answer to a weapon's trademark (decided 2026-09-23). Rolled
/// once when the copy is made, from the words of the piece's SLOT, never changed afterwards, and part of its name. Each word does
/// exactly ONE thing, and each lives on only one slot, so a character can never wear the same word twice.
///
/// <para>Saved BY NAME in the copy's roll text, so this list can be reordered without repointing a saved piece.</para></summary>
public enum ArmourWord
{
    None,
    Ironblood, Steadfast, Wellspring,          // Head
    Bulwark, Sanguine, Fierce,                 // Body
    Emberward, Tideward, Stoneward, Galeward,  // Garment
    Tireless, Anchored, Wayfarer,              // Shoes
}

/// <summary>THE one place a word's slot, size and meaning live. Plain functions over plain numbers.</summary>
public static class ArmourWords
{
    static readonly ArmourWord[] HeadWords    = { ArmourWord.Ironblood, ArmourWord.Steadfast, ArmourWord.Wellspring };
    static readonly ArmourWord[] BodyWords    = { ArmourWord.Bulwark, ArmourWord.Sanguine, ArmourWord.Fierce };
    static readonly ArmourWord[] GarmentWords = { ArmourWord.Emberward, ArmourWord.Tideward, ArmourWord.Stoneward, ArmourWord.Galeward };
    static readonly ArmourWord[] ShoesWords   = { ArmourWord.Tireless, ArmourWord.Anchored, ArmourWord.Wayfarer };
    static readonly ArmourWord[] NoWords      = System.Array.Empty<ArmourWord>();

    /// <summary>The words a piece worn in <paramref name="slot"/> can roll. Empty for a weapon and anything unworn.</summary>
    public static ArmourWord[] For(EquipSlotType slot)
    {
        switch (slot)
        {
            case EquipSlotType.Head:    return HeadWords;
            case EquipSlotType.Armor:   return BodyWords;
            case EquipSlotType.Garment: return GarmentWords;
            case EquipSlotType.Boots:   return ShoesWords;
            default:                    return NoWords;
        }
    }

    /// <summary>The word a roll of <paramref name="roll"/> (0 up to the slot's word count) lands on — every word of the
    /// slot equally likely. None when the slot has no words.</summary>
    public static ArmourWord Pick(EquipSlotType slot, int roll)
    {
        var words = For(slot);
        return words.Length == 0 ? ArmourWord.None : words[Mathf.Clamp(roll, 0, words.Length - 1)];
    }

    // What one word is worth, by the piece's grade (Raw, F, E, D, C, B, A) — first guesses, to be tuned in play. Sized
    // for ONE piece, since a word can never appear twice on a character. Percentages are whole percent (8 = 8%).
    static readonly float[] Small      = { 1f, 2f, 3f, 4f, 5f, 6.5f, 8f };       // damage up
    // hard defence points (Bulwark) — the armour words' 6-point share of the 90% hard-defence budget
    static readonly float[] Bulwark    = { 1f, 1.5f, 2f, 3f, 4f, 5f, 6f };
    static readonly float[] Leech      = { 1f, 1.5f, 2f, 2.5f, 3f, 4f, 5f };     // life steal
    static readonly float[] CritShield = { 0.9f, 1.8f, 2.7f, 3.6f, 4.5f, 6f, 7.5f }; // whole percent off an attacker's crit chance
    static readonly float[] Shorter    = { 5f, 8f, 11f, 14f, 17f, 21f, 25f };    // disables shorter, sprint longer
    static readonly float[] Ward       = { 3f, 5f, 7f, 9f, 11f, 13f, 15f };      // one element's damage cut
    static readonly float[] ManaPerSec = { 0.5f, 0.75f, 1f, 1.25f, 1.5f, 2f, 2.5f };
    static readonly float[] HealthPerSec = { 1f, 1.5f, 2f, 2.5f, 3f, 4f, 5f };

    /// <summary>What <paramref name="word"/> is worth on a piece of grade <paramref name="q"/> — the finished number the
    /// server writes onto the copy. Whole percent for the percentage words, MP per second for Wellspring, HP per second for Wayfarer.</summary>
    public static float Amount(ArmourWord word, Quality q)
    {
        int i = Mathf.Clamp((int)q, 0, Small.Length - 1);
        switch (word)
        {
            case ArmourWord.Ironblood:  return CritShield[i];
            case ArmourWord.Steadfast:  return Shorter[i];
            case ArmourWord.Wellspring: return ManaPerSec[i];
            case ArmourWord.Bulwark:    return Bulwark[i];
            case ArmourWord.Sanguine:   return Leech[i];
            case ArmourWord.Fierce:     return Small[i];
            case ArmourWord.Emberward:
            case ArmourWord.Tideward:
            case ArmourWord.Stoneward:
            case ArmourWord.Galeward:   return Ward[i];
            case ArmourWord.Tireless:   return Shorter[i];
            case ArmourWord.Anchored:   return Shorter[i];
            case ArmourWord.Wayfarer:   return HealthPerSec[i];
            default:                    return 0f;
        }
    }

    /// <summary>Fold one piece's word into a worn total — the one place a word becomes a number the game reads.</summary>
    public static void AddWord(ArmourWord word, float amount, ref EquipBonuses eb)
    {
        switch (word)
        {
            case ArmourWord.Ironblood:  eb.wordCritShieldPercent += amount; break;
            case ArmourWord.Steadfast:  eb.wordDisableCutPercent += amount; break;
            case ArmourWord.Wellspring: eb.wordMPRegen += amount; break;
            case ArmourWord.Bulwark:
                // hard defence, physical and magic alike: whole points in, tenths of a percent stored
                int tenths = Mathf.RoundToInt(amount * 10f);
                eb.hardDefTenths += tenths;
                eb.hardMdefTenths += tenths;
                break;
            case ArmourWord.Sanguine:   eb.wordLifeStealPercent += amount; break;
            case ArmourWord.Fierce:     eb.wordDamagePercent += amount; break;
            case ArmourWord.Emberward:  eb.wardFirePercent += amount; break;
            case ArmourWord.Tideward:   eb.wardWaterPercent += amount; break;
            case ArmourWord.Stoneward:  eb.wardEarthPercent += amount; break;
            case ArmourWord.Galeward:   eb.wardWindPercent += amount; break;
            case ArmourWord.Tireless:   eb.wordStaminaCutPercent += amount; break;
            case ArmourWord.Anchored:   eb.wordShoveCutPercent += amount; break;
            case ArmourWord.Wayfarer:   eb.wordHPRegen += amount; break;
        }
    }

    /// <summary>Add a worn total's words into a unit's sub-stat table
    /// (indexed by <see cref="SubStat"/>), each in that stat's own units: a fraction for the percentage stats, HP or MP
    /// a second for regeneration. The ONE place worn gear becomes a sub-stat, called by the state
    /// store's recompute.</summary>
    public static void FoldIntoSubStats(in EquipBonuses eb, float[] table)
    {
        table[(int)SubStat.Damage]           += eb.wordDamagePercent / 100f;
        table[(int)SubStat.LifeSteal]        += eb.wordLifeStealPercent / 100f;
        table[(int)SubStat.MPRegen]          += eb.wordMPRegen;
        table[(int)SubStat.HPRegen]          += eb.wordHPRegen;
    }

    /// <summary><paramref name="value"/> cut by <paramref name="cut"/> (a fraction, 0.25 = a quarter less) — Steadfast on
    /// an ailment's seconds, Tireless on sprint's stamina drain, Anchored on a shove's speed.</summary>
    public static float CutBy(float value, float cut) => value * (1f - Mathf.Clamp01(cut));

    /// <summary>The worn ward against <paramref name="attack"/>, as a fraction (0.15 = 15% less damage).</summary>
    public static float WardFraction(in EquipBonuses eb, Element attack)
    {
        switch (attack)
        {
            case Element.Fire:  return eb.wardFirePercent / 100f;
            case Element.Water: return eb.wardWaterPercent / 100f;
            case Element.Earth: return eb.wardEarthPercent / 100f;
            case Element.Wind:  return eb.wardWindPercent / 100f;
            default:            return 0f;
        }
    }

    /// <summary>The line a piece's card shows for its word, in tooltip voice.</summary>
    public static string Describe(ArmourWord word, float amount)
    {
        // NUMBER FIRST, the way every stat line on the card reads. Bulwark is named as what it adds — hard defence,
        // physical and magic (AddWord) — because a critical hit skips both, which "damage taken" would hide.
        string pct = amount.ToString("0.#") + "%";
        switch (word)
        {
            case ArmourWord.Ironblood:  return "−" + pct + " critical hits against you";
            case ArmourWord.Steadfast:  return pct + " shorter stuns, freezes, silences and ailments on you";
            case ArmourWord.Wellspring: return "+" + amount.ToString("0.##") + " MP regeneration per second";
            case ArmourWord.Bulwark:    return "+" + pct + " DEF · +" + pct + " M.DEF";
            case ArmourWord.Sanguine:   return pct + " of damage dealt heals you";
            case ArmourWord.Fierce:     return "+" + pct + " damage dealt";
            case ArmourWord.Emberward:  return "−" + pct + " Fire damage taken";
            case ArmourWord.Tideward:   return "−" + pct + " Water damage taken";
            case ArmourWord.Stoneward:  return "−" + pct + " Earth damage taken";
            case ArmourWord.Galeward:   return "−" + pct + " Wind damage taken";
            case ArmourWord.Tireless:   return pct + " slower stamina drain while sprinting";
            case ArmourWord.Anchored:   return pct + " shorter knockbacks and pulls on you";
            case ArmourWord.Wayfarer:   return "+" + amount.ToString("0.##") + " HP regeneration per second";
            default:                    return "";
        }
    }
}
