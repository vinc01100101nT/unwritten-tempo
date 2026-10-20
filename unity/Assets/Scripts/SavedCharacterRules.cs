/// <summary>A rebel's reason for fighting, worked out from who it was in the book.</summary>
public enum RebelReason
{
    RefusesThePen,
    DrawnAsVillain,
    LeftUnfinished,
}

/// <summary>
/// THE NUMBERS OF SAVED CHARACTERS, as plain functions of plain numbers — so each can be checked by calling it, with no
/// game running. Design and the reasons for each number: docs/3-world/saved-characters.md.
/// </summary>
public static class SavedCharacterRules
{
    /// <summary>The chance a beaten normal or elite turns back into a character.</summary>
    public const float TransformChance = 0.15f;

    /// <summary>How long every saved character stays — one that never fights, an ally and a rebel alike — before it
    /// leaves. A rebel beaten sooner is gone sooner.</summary>
    public const float StaySecondsForAll = 30f;

    /// <summary>How long a goodbye line stays readable before the character vanishes.</summary>
    public const float GoodbyeSeconds = 2f;

    /// <summary>How wide a saved character is drawn against its height: half its picture's own width.</summary>
    public const float DrawnWidth = 0.5f;

    /// <summary>How long the aura that marks a character that has just appeared plays before it fades out.</summary>
    public const float AppearAuraSeconds = 2f;

    /// <summary>How big that aura is drawn against its authored size: half.</summary>
    public const float AppearAuraScale = 0.5f;

    /// <summary>Whether a creature of <paramref name="kind"/> can turn back at all. A shadow never does.</summary>
    public static bool CanTurnBack(CreatureKind kind) => kind == CreatureKind.Normal || kind == CreatureKind.Elite;

    /// <summary>How many kills of its creature the gold for saving a never-fights character of <paramref name="tier"/>
    /// is worth.</summary>
    public static int RescueKills(GoldTier tier)
    {
        switch (tier)
        {
            case GoldTier.Ordinary: return 4;
            case GoldTier.Helpless: return 8;
            case GoldTier.Wealthy: return 12;
            default: return 0;
        }
    }

    /// <summary>The gold paid for saving a character of <paramref name="tier"/> that came out of a creature whose FILE
    /// says <paramref name="creatureLevel"/> — never the level it grew to in the fight, so feeding a creature blows
    /// cannot raise the pay. A kill is worth <see cref="CreatureMaterials.GoldPerLevel"/> gold a level. Only a
    /// never-fights character pays, and a kill of a creature of a higher multiplier is worth that many times more
    /// (<see cref="CreaturePay"/>).</summary>
    public static long RescueGold(SavedBehaviour behaviour, GoldTier tier, int creatureLevel, float multiplier)
        => behaviour == SavedBehaviour.NeverFights
            ? CreaturePay.Scaled((long)RescueKills(tier) * CreatureMaterials.GoldPerLevel * System.Math.Max(1, creatureLevel),
                                 multiplier)
            : 0;

    /// <summary>How many COMMON pieces' worth of its creature's material a saved never-fights character of
    /// <paramref name="tier"/> hands over with the gold. Each Common piece is worth two kills, so gold and material
    /// together come to 10, 20 or 30. <see cref="RescuePieces"/> turns that worth into pieces of the material's real tier.</summary>
    public static int RescueMaterials(SavedBehaviour behaviour, GoldTier tier)
    {
        if (behaviour != SavedBehaviour.NeverFights) return 0;
        switch (tier)
        {
            case GoldTier.Ordinary: return 3;
            case GoldTier.Helpless: return 6;
            case GoldTier.Wealthy: return 9;
            default: return 0;
        }
    }

    /// <summary>How many pieces of a <paramref name="materialTier"/> material that worth comes to: the whole pieces, plus
    /// one more on <paramref name="roll0To9999"/> below the remainder in ten-thousandths, so the average is exactly the worth
    /// at every tier. A Wealthy rescue (9 Common's worth) gives 9 Common, or 1 Uncommon + an 80% chance of a 2nd, or a
    /// 36% / 9% / 1.8% chance of one Rare / Epic / Legendary.</summary>
    public static int RescuePieces(SavedBehaviour behaviour, GoldTier tier, Rarity materialTier, int roll0To9999)
    {
        int common = RescueMaterials(behaviour, tier);
        if (common <= 0) return 0;
        long parts = (long)common * CreatureMaterials.SellValue(1, Rarity.Common) * 10000
                     / CreatureMaterials.SellValue(1, materialTier);
        return (int)(parts / 10000) + (roll0To9999 < parts % 10000 ? 1 : 0);
    }

    /// <summary>The kind whose find a rebel from a creature of <paramref name="kind"/> gives up: one kind up.</summary>
    public static CreatureKind LootKindOfRebel(CreatureKind kind) =>
        kind == CreatureKind.Normal ? CreatureKind.Elite : CreatureKind.Shadow;

    /// <summary>The multiplier a rebel's Temper Dust, Quench Oil, Inks and Salts drop at — one kind up, as its find is:
    /// a normal's rebel at ×30, an elite's at ×90, and never below the creature's own <paramref name="multiplier"/>.
    /// Its own material keeps the creature's own multiplier.</summary>
    public static float LootMultiplierOfRebel(CreatureKind kind, float multiplier)
    {
        float lifted = kind == CreatureKind.Normal ? RebelLootFromNormal : RebelLootFromElite;
        float own = CreaturePay.Clamp(multiplier);
        return own > lifted ? own : lifted;
    }

    /// <summary>What a rebel's materials drop at, from a normal creature and from an elite: one kind up (decided
    /// 2026-10-20 with the multiplier).</summary>
    public const float RebelLootFromNormal = 30f, RebelLootFromElite = 90f;

    /// <summary>The side a character of <paramref name="behaviour"/> stands on while it is here.</summary>
    public static Side SideOf(SavedBehaviour behaviour) =>
        behaviour == SavedBehaviour.Ally ? Side.Players
      : behaviour == SavedBehaviour.Rebel ? Side.Creatures
      : Side.None;

    /// <summary>How long a character of <paramref name="behaviour"/> stays before it leaves.</summary>
    public static float StaySeconds(SavedBehaviour behaviour) => StaySecondsForAll;

    /// <summary>Why a rebel from <paramref name="group"/> fights.</summary>
    public static RebelReason ReasonOf(CharacterGroup group) =>
        group == CharacterGroup.StorybookVillains ? RebelReason.DrawnAsVillain
      : group == CharacterGroup.Constructs ? RebelReason.LeftUnfinished
      : RebelReason.RefusesThePen;
}
