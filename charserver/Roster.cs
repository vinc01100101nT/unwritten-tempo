using System.Text.Json;

namespace CharServer;

/// <summary>
/// Which character models exist. Decided by the Unity project, not by this server — the models are what they
/// are, and a second hand-typed list here would drift the first time one is added. The list is exported from
/// the project into the login service's data file and only READ here, so this server can refuse an appearance
/// the game could never draw.
///
/// <para>It reads the SAME file the login service reads rather than keeping a copy, for the same reason: one
/// owner for one fact.</para>
/// </summary>
public sealed class Roster
{
    public IReadOnlyList<string> Models { get; }
    readonly Dictionary<string, string> byLowerName;

    Roster(IReadOnlyList<string> models)
    {
        Models = models;
        byLowerName = models.ToDictionary(m => m.ToLowerInvariant(), m => m);
    }

    public static Roster Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"No character-model roster at {path}. It is exported from the Unity project " +
                "(the login service's 'npm run sync-roster'), and this server reads that same file.", path);

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{path} has no 'models' array.");

        var list = new List<string>();
        foreach (var m in models.EnumerateArray())
            if (m.ValueKind == JsonValueKind.String) list.Add(m.GetString()!);

        if (list.Count == 0) throw new InvalidDataException($"{path} lists no models.");
        return new Roster(list);
    }

    /// <summary>The canonically-cased model name, or null when the roster has no such model.</summary>
    public string? Resolve(string? name)
        => name is not null && byLowerName.TryGetValue(name.Trim().ToLowerInvariant(), out var m) ? m : null;
}
