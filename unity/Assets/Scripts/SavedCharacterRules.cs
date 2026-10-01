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

    /// <summary>What saving a never-fights character of <paramref name="tier"/> multiplies the mob's gold by.</summary>
    public static int GoldMultiplier(GoldTier tier)
    {
        switch (tier)
        {
            case GoldTier.Ordinary: return 20;
            case GoldTier.Helpless: return 40;
            case GoldTier.Wealthy: return 60;
            default: return 0;
        }
    }

    /// <summary>The gold paid for saving a character of <paramref name="tier"/>, given <paramref name="rolledMobGold"/> —
    /// one roll inside the mob's own gold range. Only a never-fights character pays.</summary>
    public static int RescueGold(SavedBehaviour behaviour, GoldTier tier, int rolledMobGold)
        => behaviour == SavedBehaviour.NeverFights ? System.Math.Max(0, rolledMobGold) * GoldMultiplier(tier) : 0;

    /// <summary>The kind whose drops a rebel from a creature of <paramref name="kind"/> gives up: one kind up.</summary>
    public static CreatureKind LootKindOfRebel(CreatureKind kind) =>
        kind == CreatureKind.Normal ? CreatureKind.Elite : CreatureKind.Shadow;

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
