/// <summary>
/// WHAT A BRAND-NEW CHARACTER STARTS LIFE WITH — the one place it is decided. Only what a beginner needs to survive
/// the first field, never one of everything (decided by the user 2026-10-04):
///
/// <para><b>The forging bill, not the gear.</b> Gear is made, never handed out, so a new character gets the Copper and
/// gold that forge a Sword (or a Bow), a metal body piece and a metal shield at the smith, and fold each to +2 — 8 + 5 +
/// 5 Copper and 800 + 400 + 400 gold to forge, then six folds at 1 Copper and 50 gold each. Measured against the
/// first field's creatures, that set wins a level-1 fight with a Plopling every time and with a Flutterby four times in
/// five, where bare hands win one in five against the Plopling.</para>
///
/// <para><b>Ten Minor Healing Potions</b>, the only real healing early on: natural regeneration takes about 17 minutes
/// to refill a level-1 character, and each potion is a full heal until level 3.</para>
/// </summary>
public static class StarterKit
{
    /// <summary>Each item a new character is given, and how many.</summary>
    public static readonly (string itemId, int count)[] Items =
    {
        ("item.copper", 24),
        ("item.minor-healing-potion", 10),
    };

    /// <summary>The gold a new character is given.</summary>
    public const int Gold = 1900;
}
