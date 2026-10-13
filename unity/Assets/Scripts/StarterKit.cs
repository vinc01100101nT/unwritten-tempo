/// <summary>
/// WHAT A BRAND-NEW CHARACTER STARTS LIFE WITH — the one place it is decided. They arrive WEARING a Sword, a Chainmail
/// and Iron Sabatons — Raw, unfolded, Common, with no trademark and no word (<see cref="EquipmentInstance.CreateStarter"/>)
/// — and carrying nothing: every tutorial's first run takes nothing its steps use up, so the first folds at the smith
/// and the first healing potions bought at a Tool Dealer are what the tutorials give free.
/// </summary>
public static class StarterKit
{
    /// <summary>Each item a new character is given, and how many.</summary>
    public static readonly (string itemId, int count)[] Items = System.Array.Empty<(string, int)>();

    /// <summary>Each piece a new character starts wearing, by item id — each in its own item's slot.</summary>
    public static readonly string[] Worn = { "item.sword", "item.chainmail", "item.iron-sabatons" };

    /// <summary>The gold a new character is given.</summary>
    public const int Gold = 0;
}
