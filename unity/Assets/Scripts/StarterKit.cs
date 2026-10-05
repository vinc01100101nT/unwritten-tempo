/// <summary>
/// WHAT A BRAND-NEW CHARACTER STARTS LIFE WITH — the one place it is decided. Only what a beginner needs to survive
/// the first field, never one of everything (decided by the user 2026-10-04):
///
/// <para><b>The forging bill, not the gear.</b> Gear is made, never handed out, so a new character gets the Copper and
/// gold that forge a Sword, a Chainmail and Iron Sabatons at the smith, and fold each to +5 (decided 2026-10-05) — 8 + 5
/// + 5 Copper and 800 + 400 + 400 gold to forge, then fifteen folds at 1 Copper and 50 gold each.</para>
///
/// <para><b>Ten Minor Healing Potions</b>, the only real healing early on: natural regeneration takes about 17 minutes
/// to refill a level-1 character, and each potion is a full heal until level 3.</para>
/// </summary>
public static class StarterKit
{
    /// <summary>Each item a new character is given, and how many.</summary>
    public static readonly (string itemId, int count)[] Items =
    {
        ("item.copper", 33),
        ("item.minor-healing-potion", 10),
    };

    /// <summary>The gold a new character is given.</summary>
    public const int Gold = 2350;
}
