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

    /// <summary>A save document larger than this is a bug or an attack, not a character.</summary>
    public required int MaxProgressBytes { get; init; }

    /// <summary>The addresses a BROWSER PAGE is allowed to read the public status from — the player wiki, which
    /// is served from its own address and is therefore a different site as far as a browser is concerned. Only
    /// the status route is opened to them; every other route is untouched. Several may be given, separated by
    /// commas. An address that is not listed is refused by the browser, silently, with nothing in its console —
    /// which is why this is a setting rather than something to discover.</summary>
    public required HashSet<string> WikiOrigins { get; init; }

    /// <summary>What the map servers prove they are with. NOT a player's token and never interchangeable with
    /// one: a person signing in must not be able to register a map server or ask for somebody's character, and
    /// a map server does not sign in. Set the same value on both.</summary>
    public required string ServerSecret { get; init; }

    public static Config FromEnvironment()
    {
        return new Config
        {
            DatabaseUrl = Required("DATABASE_URL"),
            JwtSecret = Required("JWT_SECRET"),
            Port = OptionalInt("CHAR_SERVER_PORT", 8081),
            MaxProgressBytes = OptionalInt("MAX_PROGRESS_BYTES", 256 * 1024),
            ServerSecret = Required("SERVER_SECRET"),
            WikiOrigins = Origins(Environment.GetEnvironmentVariable("WIKI_ORIGINS") ?? "http://127.0.0.1:8099"),
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

    /// <summary>Split a comma-separated list of addresses, dropping any trailing slash so that the value here
    /// and the value a browser sends cannot disagree over one character.</summary>
    static HashSet<string> Origins(string raw)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var one in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            set.Add(one.TrimEnd('/'));
        return set;
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
