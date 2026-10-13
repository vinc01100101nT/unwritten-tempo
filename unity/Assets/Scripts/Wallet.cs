using System;

/// <summary>
/// The money of whoever is playing at THIS machine, for the things on screen that show it.
///
/// <para><b>It no longer holds the money.</b> The figure belongs to <see cref="Character"/>, next to everything
/// else that is true of a person. It used to be a single number belonging to the whole program, which reads as
/// harmless on a machine with one player at it and is one shared purse the moment a server holds several people
/// at once — every one of them spending the same coins.</para>
///
/// <para>Kept as a name the heads-up display can ask, so the counter and the bag footer did not all have to learn
/// how to find the character themselves. Everything that CHANGES money should go to the character it belongs to;
/// this is for reading, and for the one machine that has an obvious answer to "whose money".</para>
/// </summary>
public static class Wallet
{
    /// <summary>Fires whenever the local player's money changes. Arg = the new total.</summary>
    public static event Action<long> OnChanged;

    /// <summary>What the local player has. Nothing, before there is a character.</summary>
    public static long Gold => Character.Instance != null ? Character.Instance.Gold : 0;

    /// <summary>Add money to the local player, or take it away with a negative amount.</summary>
    public static void Add(long amount) => Character.Instance?.AddGold(amount);

    /// <summary>Set the local player's balance outright.</summary>
    public static void Set(long amount) => Character.Instance?.SetGold(amount);

    // ---- how the character's own event reaches the screen ------------------------------------------------
    //
    // The things that display money subscribe to THIS, once, and outlive any particular character — the counter
    // is built with the heads-up display and stays for the session. So this follows whichever character is
    // current and passes their news along, rather than each display having to notice a character being swapped
    // and re-subscribe. Called by Character as it comes and goes; nothing else should.

    static Character listeningTo;

    /// <summary>Called by <see cref="Character"/> when it becomes the local player's, so money news reaches the
    /// screen. Safe to call twice.</summary>
    public static void ListenTo(Character c)
    {
        if (listeningTo == c) return;
        if (listeningTo != null) listeningTo.OnGoldChanged -= Relay;
        listeningTo = c;
        if (listeningTo != null) listeningTo.OnGoldChanged += Relay;
        Relay(Gold);
    }

    /// <summary>Called by <see cref="Character"/> as it goes, so a departed one cannot go on talking.</summary>
    public static void StopListeningTo(Character c)
    {
        if (listeningTo != c) return;
        if (listeningTo != null) listeningTo.OnGoldChanged -= Relay;
        listeningTo = null;
    }

    static void Relay(long total) => OnChanged?.Invoke(total);
}
