using System;
using UnityEngine;

/// <summary>
/// Who is signed in and which character they are playing. THE single answer to "whose data is this" — the
/// profile store asks it for the address and the token, and <see cref="SaveManager"/> asks it for the
/// character's appearance.
///
/// <para><b>The appearance held here is a COPY for the moment before the save arrives.</b> A character's body
/// and colours live in the save document — the one place they are stored — and the character list reads them
/// out of it. They are kept here too so the world can dress the player the instant they enter, without waiting
/// for a second request; the save confirms the same values a moment later.</para>
///
/// <para>Nothing is signed in until a sign-in flow fills this. Until then <see cref="IsActive"/> is false, and
/// the game has no character to load or save — which is the correct state for an online game sitting at its
/// sign-in screen, not an error.</para>
/// </summary>
public static class AccountSession
{
    /// <summary>Where the account service lives. Overridden by the ACCOUNTS_URL environment variable so a
    /// build can point at a real host without a rebuild — and, ahead of that, by a host typed on the device
    /// (<see cref="ServerAddress"/>), which is the only route a phone has since Android carries no environment.</summary>
    public static string ServerUrl =>
        ServerAddress.HasTypedHost
            ? $"http://{ServerAddress.TypedHost}:{ServerAddress.AccountsPort}"
            : Environment.GetEnvironmentVariable("ACCOUNTS_URL") ?? "http://localhost:8080";

    /// <summary>Where the CHARACTER server lives — the thing that owns the list of characters and their saves,
    /// as opposed to signing in, which stays with the account service above.
    ///
    /// <para><b>Empty means "the same place as everything else"</b>, which is how this has always worked and is
    /// still the default. Set CHAR_SERVER_URL to move characters onto their own server; leave it unset and not
    /// one request changes address. That is the whole migration: a setting, not a rebuild, and it can be put
    /// back just as fast if the new server misbehaves.</para></summary>
    public static string CharacterServerUrl =>
        Environment.GetEnvironmentVariable("CHAR_SERVER_URL") ?? "";

    /// <summary>The one-time secret the CHARACTER server hands out when a player asks to enter the world. It is
    /// what a map server matches an arriving connection against, and it is the only thing about this person that
    /// travels on that connection — their character was already sent there, by the character server, before they
    /// were told where to knock.
    ///
    /// <para>Good once and for about half a minute. Cleared as it is spent.</para></summary>
    public static string EntryTicket { get; set; }

    public static string AccessToken { get; private set; }
    public static string AccountId { get; private set; }

    public static string CharacterId { get; private set; }
    public static string CharacterName { get; private set; }

    /// <summary>The chosen body, and the recolour applied to it (parallel arrays: source colour → replacement).</summary>
    public static string ModelName { get; private set; }
    public static string[] DyeSources { get; private set; }
    public static string[] DyeTargets { get; private set; }

    /// <summary>True once a character has been chosen — the only condition under which anything can be
    /// loaded or saved, and the one condition under which the world may come up at all.</summary>
    public static bool IsActive => !string.IsNullOrEmpty(AccessToken) && !string.IsNullOrEmpty(CharacterId);

    /// <summary>Is this account trusted with the game's commands? Answered by the account service when this
    /// character's progress is read, and false until it says otherwise — so a game that never got an answer
    /// refuses commands rather than allowing them.
    ///
    /// <para><b>This copy decides nothing that matters to anybody else.</b> It spares a player from typing a
    /// command that was going to be refused, and it is what the commands that only touch their own game ask.
    /// Anything that changes the shared world is asked of the SERVER, which reads the same mark itself and
    /// believes no claim a game makes about its own player.</para></summary>
    public static bool IsGm { get; private set; }

    /// <summary>Record the mark the account service just answered with. Told to us by whoever read the
    /// character, rather than fetched again here, so there is one request and one answer.</summary>
    public static void SetGm(bool isGm) => IsGm = isGm;

    /// <summary>Raised whenever the signed-in character changes. <see cref="GameRoot"/> listens for it: the
    /// world is not brought up until this says somebody is actually playing.</summary>
    public static event Action OnChanged;

    public static void SignIn(string accountId, string accessToken)
    {
        AccountId = accountId;
        AccessToken = accessToken;
        IsGm = false;   // a fresh sign-in is untrusted until the account service says otherwise

        // NOT WHERE THE WORLD'S CLOCK COMES FROM ANY MORE. It used to be asked of the account service here,
        // which is a different machine from the one running the world — so the sun and every lasting timer were
        // read off two different clocks. The world's time now comes from the machine running the world, and it
        // arrives at the one moment that machine speaks to this one: when it hands over a body.
        OnChanged?.Invoke();
    }

    /// <summary>Choose the character to play. The appearance comes from the list the server answered with,
    /// which reads it out of that character's save — the same document the game loads a moment later.</summary>
    public static void PlayAs(string characterId, string characterName, string modelName,
                              string[] dyeSources, string[] dyeTargets)
    {
        CharacterId = characterId;
        CharacterName = characterName;
        ModelName = modelName;
        DyeSources = dyeSources ?? Array.Empty<string>();
        DyeTargets = dyeTargets ?? Array.Empty<string>();
        OnChanged?.Invoke();
    }

    public static void SignOut()
    {
        AccessToken = AccountId = CharacterId = CharacterName = ModelName = null;
        DyeSources = DyeTargets = null;
        IsGm = false;   // signed out is nobody, and nobody is trusted with commands
        OnChanged?.Invoke();
    }
}
