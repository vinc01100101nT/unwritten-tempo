/// <summary>
/// WHAT A BRAND-NEW CHARACTER STARTS LIFE WITH — the one place it is decided. They arrive WEARING a Raw +0 Sword and a
/// Raw +5 Chainmail, Iron Sabatons and Chain Mantle — Common, with no trademark and no word
/// (<see cref="EquipmentInstance.CreateStarter"/>) — and carrying 100 Newbie Potions, which can never leave them. The
/// armour is folded already so a new player survives the newbie maps (at least 5 blows from any Raw +0 creature there);
/// the Sword is left unfolded for "Your First Gear" to teach folding on, free the first time.
/// </summary>
public static class StarterKit
{
    /// <summary>Each item a new character is given, and how many.</summary>
    public static readonly (string itemId, int count)[] Items = { ("item.newbie-potion", 100) };

    /// <summary>Each piece a new character starts wearing, by item id, and its folds — each in its own item's slot.</summary>
    public static readonly (string itemId, int folds)[] Worn =
    {
        ("item.sword", 0), ("item.chainmail", 5), ("item.iron-sabatons", 5), ("item.chain-mantle", 5),
    };

    /// <summary>The gold a new character is given.</summary>
    public const int Gold = 0;
}
