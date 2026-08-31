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
/// </summary>
public static class ServerAddress
{
    const string HostKey = "server_host";

    /// <summary>The port the account service listens on. Matches the default URL the desktop falls back to.</summary>
    public const int AccountsPort = 8080;

    static string typed;
    static bool loaded;

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
}
