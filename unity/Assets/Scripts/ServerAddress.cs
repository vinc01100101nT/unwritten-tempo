using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// THE one owner of "which machine is the server on" for a build that cannot be told by its environment.
///
/// <para>Desktop builds are aimed with environment variables — <c>ACCOUNTS_URL</c> for the account service and
/// <c>GAME_SERVER_HOST</c> for the world — which is why neither of those needs a rebuild to move. <b>Android has
/// no environment variables to set</b>, so a phone would read neither and fall back to localhost, which on a
/// phone means the phone itself: it would dial nothing and fail with no explanation.</para>
///
/// <para>So a phone is told once, by hand, on the sign-in screen, and the answer is remembered on the device.
/// Both halves of the connection read it from here rather than each keeping a copy, so the account service and
/// the world can never end up pointed at two different machines.</para>
///
/// <para>Empty on the desktop, always — nothing types into it there, so both env-var paths behave exactly as
/// they did before this existed.</para>
///
/// <para><b>And anything started WITHOUT A TERMINAL is told by a file</b> — see <see cref="Setting"/>. A copy
/// handed to a player who double-clicks it reads the addresses tools/build-game.sh wrote into the build; the
/// EDITOR opened from the Dock or Unity Hub reads them from the project's settings.env, the file the servers
/// start from. Neither receives the settings a shell would have exported, so both would otherwise have no
/// character server to ask and could sign in but never enter. The environment still wins where there is one, so
/// a build or a server started from a shell is unaffected.</para>
/// </summary>
public static class ServerAddress
{
    const string HostKey = "server_host";

    /// <summary>The file a handed-out copy carries its addresses in, sitting in the build's StreamingAssets
    /// folder. Lines are <c>NAME=value</c> using the SAME names as the environment settings, so there is one
    /// vocabulary for "where the servers are" rather than two.</summary>
    const string SettingsFileName = "server.txt";

    /// <summary>The port the account service listens on. Matches the default URL the desktop falls back to.</summary>
    public const int AccountsPort = 8080;

    static string typed;
    static bool loaded;

    static Dictionary<string, string> shipped;

    /// <summary>The machine typed on this device, or empty when nobody has said. Overrides the environment on
    /// the builds that have one, which only matters on a phone, where there is none to override.</summary>
    public static string TypedHost
    {
        get
        {
            if (!loaded) { typed = PlayerPrefs.GetString(HostKey, ""); loaded = true; }
            return typed;
        }
        set
        {
            typed = (value ?? "").Trim();
            loaded = true;
            PlayerPrefs.SetString(HostKey, typed);
            // Written through immediately, unlike the volume-style settings that deliberately do not: this is
            // typed once and a test build that is force-closed must not come back having forgotten its server.
            PlayerPrefs.Save();
        }
    }

    /// <summary>True when a host has been typed on this device.</summary>
    public static bool HasTypedHost => !string.IsNullOrEmpty(TypedHost);

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

        // ANDROID KEEPS ITS StreamingAssets INSIDE THE PACKAGE, where it is not a file and cannot be opened this
        // way. A phone is aimed by the address typed on its sign-in screen instead, which is the whole reason
        // TypedHost above exists, so there is nothing to fall back to here and nothing lost by not trying.
        if (Application.platform == RuntimePlatform.Android) return found;

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
