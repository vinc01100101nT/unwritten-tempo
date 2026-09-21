using Npgsql;

namespace CharServer;

/// <summary>
/// The one way this server reaches the database. Everything that touches a character goes through here, so
/// the connection settings, the timeouts and the error translation are written once.
///
/// <para>The login service points at this same database. That is deliberate and is the shape Ragnarok has
/// always had: ONE store, ownership split by TABLE. The login service owns accounts and tokens; this server
/// owns characters and nothing else touches them.</para>
/// </summary>
public sealed class Db
{
    readonly NpgsqlDataSource source;

    Db(NpgsqlDataSource source) => this.source = source;

    /// <summary>Open the pool. The URL is the same one the login service reads, in the same form
    /// (postgres://user:pass@host/db), which Npgsql does not take directly — so it is translated here rather
    /// than asking whoever runs this to keep a second spelling of one setting.</summary>
    public static Db Open(string databaseUrl)
    {
        var builder = new NpgsqlDataSourceBuilder(ToConnectionString(databaseUrl));
        return new Db(builder.Build());
    }

    internal static string ToConnectionString(string databaseUrl)
    {
        // Already in key=value form — hand it straight through, so a connection string that Npgsql understands
        // and a URL that Node understands are both accepted rather than one of them being an error.
        if (!databaseUrl.Contains("://", StringComparison.Ordinal)) return databaseUrl;

        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':', 2);
        var b = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.TrimStart('/'),
            // BOUNDED ON PURPOSE: with no command timeout a query that hangs holds its pooled connection for
            // good, and enough of those wedge every door on this server (the pool is 100 by default). Five
            // seconds is a ceiling no read here comes near. Auto-prepare: the same handful of statements run
            // all day, and re-planning the leaderboard read cost more than running it (2.5 ms against 1.5).
            CommandTimeout = 5,
            MaxAutoPrepare = 25,
            AutoPrepareMinUsages = 2,
        };
        if (userInfo.Length > 0 && userInfo[0].Length > 0) b.Username = Uri.UnescapeDataString(userInfo[0]);
        if (userInfo.Length > 1 && userInfo[1].Length > 0) b.Password = Uri.UnescapeDataString(userInfo[1]);

        // WHATEVER ELSE THE URL SAYS, carried across rather than dropped. A hosted database puts its security
        // settings here — how to encrypt the connection, whether to insist on it — and the login service reads
        // the URL whole, so throwing this half away would have the two servers connecting to one database on
        // different terms, from one setting they are told to share. Anything Npgsql does not recognise is left
        // out rather than refused, so a setting meant for the other server's driver cannot stop this one.
        var query = uri.Query.TrimStart('?');
        if (query.Length > 0)
        {
            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length != 2) continue;
                var key = Uri.UnescapeDataString(kv[0]);
                var value = Uri.UnescapeDataString(kv[1]);
                if (b.ContainsKey(key)) b[key] = value;
            }
        }

        return b.ConnectionString;
    }

    public async Task<NpgsqlConnection> ConnectAsync(CancellationToken ct = default)
        => await source.OpenConnectionAsync(ct);

    /// <summary>Prove the database is actually reachable, at start-up, rather than finding out on the first
    /// player. A server that is listening but cannot read a character is the failure that looks like success.</summary>
    public async Task CheckReachableAsync(CancellationToken ct = default)
    {
        await using var conn = await ConnectAsync(ct);
        await using var cmd = new NpgsqlCommand("select 1", conn);
        await cmd.ExecuteScalarAsync(ct);
    }

    /// <summary>Postgres's "you broke a unique index" code. The one database error this server has an actual
    /// answer for — a name somebody else already took — rather than a five-hundred.</summary>
    public static bool IsUniqueViolation(Exception e)
        => e is PostgresException p && p.SqlState == "23505";
}
