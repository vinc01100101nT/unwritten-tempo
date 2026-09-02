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

    static int OptionalInt(string name, int fallback)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v)) return fallback;
        if (!int.TryParse(v, out var n))
            throw new InvalidOperationException($"{name} must be a whole number, got \"{v}\".");
        return n;
    }
}
