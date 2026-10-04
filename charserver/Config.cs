namespace CharServer;

/// <summary>
/// Every environment-dependent value this server needs, read once and checked at start-up. A missing secret
/// is a start-up failure on purpose: a server that boots with a default signing key is worse than one that
/// refuses to boot, because nothing looks wrong until somebody forges a token.
///
/// <para>The names match the login service's exactly. They are two programs reading ONE setting each — the
/// same database, the same signing secret — and a second spelling for either is how they would come to
/// disagree about which database holds the characters, or whose tokens to believe.</para>
/// </summary>
public sealed class Config
{
    public required string DatabaseUrl { get; init; }
    public required string JwtSecret { get; init; }
    public required int Port { get; init; }

    /// <summary>Which addresses this answers on (BIND_HOST). 0.0.0.0 answers on all of them, which is what a machine
    /// on your own desk needs. On a hosted machine set BIND_HOST to 127.0.0.1: the encrypting proxy sits on the same
    /// machine and reaches it there, and nothing on the internet can then reach this server unencrypted — a
    /// character's save sent to the open port would travel readable, and a certificate on the proxy does not
    /// prevent that while a second, plain door stands open beside it.</summary>
    public required string BindHost { get; init; }

    /// <summary>A save document larger than this is a bug or an attack, not a character.</summary>
    public required int MaxProgressBytes { get; init; }

    /// <summary>The addresses a BROWSER PAGE is allowed to read the public status from — the website, which
    /// is served from its own address and is therefore a different site as far as a browser is concerned. Only
    /// the status route is opened to them; every other route is untouched. Several may be given, separated by
    /// commas. An address that is not listed is refused by the browser, silently, with nothing in its console —
    /// which is why this is a setting rather than something to discover.</summary>
    public required HashSet<string> WikiOrigins { get; init; }

    /// <summary>What the map servers prove they are with. NOT a player's token and never interchangeable with
    /// one: a person signing in must not be able to register a map server or ask for somebody's character, and
    /// a map server does not sign in. Set the same value on both.</summary>
    public required string ServerSecret { get; init; }

    /// <summary>The folder the launcher downloads updates from, when this machine hands them out. Its
    /// <c>version.txt</c> is the version every player's copy must match — see <see cref="GameVersion"/>. Unset
    /// means no copy is ever turned away for being out of date.</summary>
    public string? PatchDir { get; init; }

    public static Config FromEnvironment()
    {
        return new Config
        {
            DatabaseUrl = Required("DATABASE_URL"),
            JwtSecret = Required("JWT_SECRET"),
            // Addresses and ports have no default here: settings.env is the one place they are written, so a value
            // typed in this file could only ever disagree with it.
            Port = RequiredInt("CHAR_SERVER_PORT"),
            BindHost = Required("BIND_HOST"),
            MaxProgressBytes = OptionalInt("MAX_PROGRESS_BYTES", 256 * 1024),
            ServerSecret = Required("SERVER_SECRET"),
            PatchDir = Environment.GetEnvironmentVariable("PATCH_DIR"),
            WikiOrigins = Origins(Environment.GetEnvironmentVariable("WIKI_ORIGINS")
                                  ?? WebsiteOrigins(Environment.GetEnvironmentVariable("PATCH_URL"))),
        };
    }

    static string Required(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v))
            throw new InvalidOperationException(
                $"Missing required environment variable {name}. It must be the SAME value the login service uses.");
        return v;
    }

    /// <summary>Where the website is served from when nobody says otherwise: the download server, which serves the
    /// site from the same folder as the updates. Its address is PATCH_URL; this computer's own two names for that
    /// port are added because a browser on this computer sends whichever one was typed. With no PATCH_URL there is
    /// no website, and no page is let in.</summary>
    static string WebsiteOrigins(string? patchUrl)
    {
        if (!Uri.TryCreate(patchUrl, UriKind.Absolute, out var uri)) return "";
        return string.Join(',', uri.GetLeftPart(UriPartial.Authority),
                           $"http://127.0.0.1:{uri.Port}", $"http://localhost:{uri.Port}");
    }

    /// <summary>Split a comma-separated list of addresses, dropping any trailing slash so that the value here
    /// and the value a browser sends cannot disagree over one character.</summary>
    static HashSet<string> Origins(string raw)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var one in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            set.Add(one.TrimEnd('/'));
        return set;
    }

    static int RequiredInt(string name)
    {
        var v = Required(name);
        if (!int.TryParse(v, out var n))
            throw new InvalidOperationException($"{name} must be a whole number, got \"{v}\".");
        return n;
    }

    static int OptionalInt(string name, int fallback)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v)) return fallback;
        if (!int.TryParse(v, out var n))
            throw new InvalidOperationException($"{name} must be a whole number, got \"{v}\".");
        return n;
    }
}
