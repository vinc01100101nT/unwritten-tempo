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
    /// <summary>Where the account service lives: ACCOUNTS_URL, from the environment so a build can point at a
    /// real host without a rebuild. Empty when nobody set it — there is no address written here to fall back on.
    ///
    /// <para>A copy handed to a player has neither, and is aimed by the settings file it was built with — the
    /// same one setting, read by <see cref="ServerAddress.Setting"/>, which is why a whole address including
    /// <c>https://</c> can be given here without a line of this changing.</para></summary>
    public static string ServerUrl => ServerAddress.Setting("ACCOUNTS_URL") ?? "";

    /// <summary>Where the CHARACTER server lives — the thing that owns the list of characters and their saves,
    /// as opposed to signing in, which stays with the account service above.
    ///
    /// <para><b>Empty is not a working setting any more.</b> The account service no longer answers for
    /// characters at all — the routes that once did were the one door through which a player could write their
    /// own save, and were removed on 2026-09-20 — so a request sent there for a character is refused, not served.</para>
    ///
    /// <para><b>Empty also means nobody can enter the world</b>, because the character server is the only thing
    /// that hands out an entry ticket and says which machine holds the map. A copy handed to a player therefore
    /// carries this address in its settings file alongside the account service's — two addresses, not one.</para></summary>
    public static string CharacterServerUrl =>
        ServerAddress.Setting("CHAR_SERVER_URL") ?? "";

    /// <summary>The one-time secret the CHARACTER server hands out when a player asks to enter the world. It is
    /// what a map server matches an arriving connection against, and it is the only thing about this person that
    /// travels on that connection — their character was already sent there, by the character server, before they
    /// were told where to knock.
    ///
    /// <para>Good once and for about half a minute. Cleared as it is spent.</para></summary>
    public static string EntryTicket { get; set; }

    /// <summary>WHICH MAP THE CHARACTER SERVER IS SENDING THIS PERSON TO — the map they logged out in, decided by the
    /// one thing that knows, at the moment it hands out the ticket above. Empty for a character nobody has played
    /// yet, and empty on a server, which is never sent anywhere.
    ///
    /// <para>Read once, by the boot gate, to decide which map comes up underneath the persistent layer. Without it
    /// the game loaded its authored starting map whoever was signing in, announced THAT to the world server as where
    /// the player was standing, and only walked back to the real map once a SECOND request for the character had
    /// come back. So every single sign-in filed the player in the wrong map for a moment, and any interruption
    /// inside that moment left the starting town as the last thing written down about where they were.</para>
    ///
    /// <para>Set on every entry, including to empty, so it can never be a previous session's answer.</para></summary>
    public static string EntryMap { get; set; }

    public static string AccessToken { get; private set; }
    public static string AccountId { get; private set; }

    public static string CharacterId { get; private set; }
    public static string CharacterName { get; private set; }

    /// <summary>The chosen body, and the recolour applied to it (parallel arrays: source colour → replacement).</summary>
    public static string ModelName { get; private set; }
    public static string[] DyeSources { get; private set; }
    public static string[] DyeTargets { get; private set; }

    /// <summary>The chosen hairstyle and its colour, as the list answered — both empty for a character made before
    /// hairstyles (see <see cref="HeadPieces.Resolve"/>).</summary>
    public static string HairStyle { get; private set; }
    public static string HairColour { get; private set; }

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
                              string[] dyeSources, string[] dyeTargets, string hairStyle, string hairColour)
    {
        CharacterId = characterId;
        CharacterName = characterName;
        ModelName = modelName;
        DyeSources = dyeSources ?? Array.Empty<string>();
        DyeTargets = dyeTargets ?? Array.Empty<string>();
        HairStyle = hairStyle ?? "";
        HairColour = hairColour ?? "";
        OnChanged?.Invoke();
    }

    /// <summary>Put the character down but stay signed in — what choosing a different character is. The account
    /// and its token are left exactly as they are, so nothing has to be typed again; only the answer to "which
    /// character" is cleared, and that is the half <see cref="IsActive"/> reads, so the world comes down and the
    /// character list is the next thing that can happen.</summary>
    public static void LeaveCharacter()
    {
        CharacterId = CharacterName = ModelName = HairStyle = HairColour = null;
        DyeSources = DyeTargets = null;
        OnChanged?.Invoke();
    }

    public static void SignOut()
    {
        AccessToken = AccountId = CharacterId = CharacterName = ModelName = HairStyle = HairColour = null;
        DyeSources = DyeTargets = null;
        IsGm = false;   // signed out is nobody, and nobody is trusted with commands
        OnChanged?.Invoke();
    }
}
