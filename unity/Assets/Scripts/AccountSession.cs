using System;
using UnityEngine;

/// <summary>
/// Who is signed in and which character they are playing. THE single answer to "whose data is this" — the
/// profile store asks it for the address and the token, and <see cref="SaveManager"/> asks it for the
/// character's appearance.
///
/// <para><b>Appearance lives HERE, not in the save.</b> Which body a character wears and how it is recoloured
/// are chosen at creation and are true before the character has ever been played — that is identity, not
/// progress. The save document carries progress only. Keeping appearance in both is two owners for one thing,
/// and the pair drifts the moment either is written alone.</para>
///
/// <para>Nothing is signed in until a sign-in flow fills this. Until then <see cref="IsActive"/> is false, and
/// the game has no character to load or save — which is the correct state for an online game sitting at its
/// sign-in screen, not an error.</para>
/// </summary>
public static class AccountSession
{
    /// <summary>Where the account service lives. Overridden by the ACCOUNTS_URL environment variable so a
    /// build can point at a real host without a rebuild.</summary>
    public static string ServerUrl =
        Environment.GetEnvironmentVariable("ACCOUNTS_URL") ?? "http://localhost:8080";

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

    /// <summary>Raised whenever the signed-in character changes. <see cref="GameRoot"/> listens for it: the
    /// world is not brought up until this says somebody is actually playing.</summary>
    public static event Action OnChanged;

    public static void SignIn(string accountId, string accessToken)
    {
        AccountId = accountId;
        AccessToken = accessToken;
        OnChanged?.Invoke();
    }

    /// <summary>Choose the character to play. The appearance comes from the character record the server
    /// returned — it is never read back out of the save.</summary>
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
        OnChanged?.Invoke();
    }
}
