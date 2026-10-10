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
    /// <summary>The hairstyle id of no hair at all — the game's HeadPieces.NoHair.</summary>
    public const string NoHair = "none";

    public IReadOnlyList<string> Models { get; }
    readonly Dictionary<string, string> byLowerName;

    /// <summary>Every hairstyle the game can draw, exported from the same project beside the models.</summary>
    public IReadOnlyList<string> Hairstyles { get; }
    readonly HashSet<string> noHairModels;

    Roster(IReadOnlyList<string> models, IReadOnlyList<string> hairstyles, IEnumerable<string> noHairModels)
    {
        Models = models;
        byLowerName = models.ToDictionary(m => m.ToLowerInvariant(), m => m);
        Hairstyles = hairstyles;
        this.noHairModels = new HashSet<string>(noHairModels, StringComparer.Ordinal);
    }

    /// <summary>May <paramref name="model"/> be created wearing <paramref name="style"/>? No hair always; any listed
    /// hairstyle, unless the model's head takes no hair (an animal's) — the same list the creation screen offers.</summary>
    public bool AllowsHair(string model, string style)
        => style == NoHair || (!noHairModels.Contains(model) && Hairstyles.Contains(style));

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

        // THE HAIRSTYLES, required: a file without them was exported before hairstyles existed, and a server reading
        // it would refuse every new character. Said plainly rather than started half-blind.
        if (!doc.RootElement.TryGetProperty("hairstyles", out var hairs) || hairs.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{path} has no 'hairstyles' array — re-run accounts/scripts/sync-roster.mjs.");
        var styles = new List<string>();
        foreach (var h in hairs.EnumerateArray())
            if (h.ValueKind == JsonValueKind.String) styles.Add(h.GetString()!);
        var bald = new List<string>();
        if (doc.RootElement.TryGetProperty("noHairModels", out var nh) && nh.ValueKind == JsonValueKind.Array)
            foreach (var m in nh.EnumerateArray())
                if (m.ValueKind == JsonValueKind.String) bald.Add(m.GetString()!);
        return new Roster(list, styles, bald);
    }

    /// <summary>The canonically-cased model name, or null when the roster has no such model.</summary>
    public string? Resolve(string? name)
        => name is not null && byLowerName.TryGetValue(name.Trim().ToLowerInvariant(), out var m) ? m : null;
}
