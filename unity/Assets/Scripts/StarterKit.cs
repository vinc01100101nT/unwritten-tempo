/// <summary>
/// WHAT A BRAND-NEW CHARACTER STARTS LIFE WITH — the one place it is decided. NOTHING, decided by the user 2026-10-07:
/// every tutorial's first run takes nothing its steps use up, so the forging bill and the potions a new character used
/// to be handed are what the tutorials give free instead — the first gear forged and folded at the smith, the first
/// healing potions bought at a Tool Dealer. Kept as a list so a new character's kit has one place to live.
/// </summary>
public static class StarterKit
{
    /// <summary>Each item a new character is given, and how many.</summary>
    public static readonly (string itemId, int count)[] Items = System.Array.Empty<(string, int)>();

    /// <summary>The gold a new character is given.</summary>
    public const int Gold = 0;
}
