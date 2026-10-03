using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// THE one owner of "which machine is the server on" for a build that cannot be told by its environment.
///
/// <para>Builds are aimed with environment variables — <c>ACCOUNTS_URL</c> for the account service and
/// <c>GAME_SERVER_HOST</c> for the world — which is why neither of those needs a rebuild to move.</para>
///
/// <para><b>Anything started WITHOUT A TERMINAL is told by a file</b> — see <see cref="Setting"/>. A copy
/// handed to a player who double-clicks it reads the addresses tools/build-game.sh wrote into the build; the
/// EDITOR opened from the Dock or Unity Hub reads them from the project's settings.env, the file the servers
/// start from. Neither receives the settings a shell would have exported, so both would otherwise have no
/// character server to ask and could sign in but never enter. The environment still wins where there is one, so
/// a build or a server started from a shell is unaffected.</para>
/// </summary>
public static class ServerAddress
{
    /// <summary>The file a handed-out copy carries its addresses in, sitting in the build's StreamingAssets
    /// folder. Lines are <c>NAME=value</c> using the SAME names as the environment settings, so there is one
    /// vocabulary for "where the servers are" rather than two.</summary>
    const string SettingsFileName = "server.txt";

    /// <summary>The port the account service listens on — the one a build with no address of its own dials.</summary>
    public const int AccountsPort = 8080;

    static Dictionary<string, string> shipped;

    /// <summary>What this copy was told for <paramref name="name"/>, or null when nobody has said.
    ///
    /// <para>The ENVIRONMENT is asked first, so every way of aiming a build that worked before still wins and a
    /// server started from a terminal is unaffected. Only when the environment is silent — which is the normal
    /// state of a game somebody double-clicked — is the file the build carries consulted.</para>
    ///
    /// <para>Read from disk once and remembered, because sign-in asks for this several times in a row.</para></summary>
    public static string Setting(string name)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrEmpty(fromEnvironment)) return fromEnvironment;

        if (shipped == null) shipped = ReadShippedSettings();
        return shipped.TryGetValue(name, out var value) && value.Length > 0 ? value : null;
    }

    static Dictionary<string, string> ReadShippedSettings()
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            var path = Path.Combine(Application.streamingAssetsPath, SettingsFileName);
            foreach (var raw in File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>())
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var split = line.IndexOf('=');
                if (split <= 0) continue;

                found[line.Substring(0, split).Trim()] = line.Substring(split + 1).Trim();
            }
        }
        catch (Exception e)
        {
            // A copy that cannot read its own settings must say so and carry on with the built-in defaults —
            // never throw at sign-in, where the player has no way to act on an exception.
            GameLog.Warn(LogCat.Client, "server address: could not read " + SettingsFileName + ": " + e.Message);
        }

#if UNITY_EDITOR
        ReadProjectSettings(found);
#endif
        return found;
    }

#if UNITY_EDITOR
    /// <summary>The addresses THIS machine's servers answer on, which the editor reads straight from the project's
    /// one settings file (settings.env, at the top of the project) — the same file the servers themselves start
    /// from, so the editor can never be pointed somewhere different from them. A build carries these in its own
    /// copy of <see cref="SettingsFileName"/> instead, written there by tools/build-game.sh.
    ///
    /// <para><b>Only the two addresses are taken, by name.</b> That file also holds every secret the servers have,
    /// and none of those may ever reach anything in the game — so nothing but these two names is read out of it,
    /// and this code does not exist outside the editor.</para></summary>
    static void ReadProjectSettings(Dictionary<string, string> found)
    {
        try
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "settings.env"));
            if (!File.Exists(path)) return;

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.StartsWith("export ", StringComparison.Ordinal)) line = line.Substring(7).Trim();
                var split = line.IndexOf('=');
                if (split <= 0) continue;

                var name = line.Substring(0, split).Trim();
                if (name != "ACCOUNTS_URL" && name != "CHAR_SERVER_URL") continue;
                if (found.ContainsKey(name)) continue;

                var value = line.Substring(split + 1).Trim().Trim('"');
                if (value.Length > 0) found[name] = value;
            }
        }
        catch (Exception e)
        {
            GameLog.Warn(LogCat.Client, "server address: could not read settings.env: " + e.Message);
        }
    }
#endif
}
