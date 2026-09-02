using System.Text.Json;

namespace CharServer;

/// <summary>
/// What may be stored as a character's save.
///
/// <para>The save document belongs to the GAME — its fields, its version, its meaning. This server stores it
/// and hands it back; it does not interpret it, and it must not grow a second opinion about what a
/// character's numbers are. What it DOES do is refuse something that cannot be a save at all: not an object,
/// or carrying no version. That is the boundary between "storing what I was given" and "trusting it".</para>
///
/// <para>Gameplay rules do NOT belong here. When the map server decides whether a number was earned, that
/// decision goes there; widening this check into gameplay would put the rules in two places.</para>
/// </summary>
public static class Progress
{
    /// <summary>The only keys a never-played character is allowed to carry — the look chosen at creation.</summary>
    static readonly HashSet<string> AppearanceKeys = new(StringComparer.Ordinal)
    {
        "modelName", "dyeSources", "dyeTargets",
    };

    public readonly record struct Verdict(bool Ok, string? Reason);

    public static Verdict Check(JsonElement progress)
    {
        if (progress.ValueKind != JsonValueKind.Object)
            return new Verdict(false, "progress must be a JSON object");

        if (HasWholeNumber(progress, "version", out var version) && version >= 1)
            return new Verdict(true, null);

        // No version. Legitimate for exactly one document: a character created but never played, which carries
        // only the look chosen at creation. Without this, ANY object at all would be accepted and stored as
        // somebody's character.
        foreach (var prop in progress.EnumerateObject())
            if (!AppearanceKeys.Contains(prop.Name))
                return new Verdict(false, "progress must carry a whole-number 'version'");

        return new Verdict(true, null);
    }

    /// <summary>The character's level as the save reports it, or null when it has never been played. READ out
    /// of the document rather than kept in a column beside it, so there is one owner of the number and a
    /// character list can never disagree with the character it describes.</summary>
    public static long? LevelOf(JsonElement progress)
        => HasWholeNumber(progress, "level", out var level) ? level : null;

    public readonly record struct Appearance(string ModelName, string[] DyeSources, string[] DyeTargets);

    public static Appearance AppearanceOf(JsonElement progress)
    {
        if (progress.ValueKind != JsonValueKind.Object)
            return new Appearance("", [], []);

        string model = progress.TryGetProperty("modelName", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString() ?? "" : "";
        return new Appearance(model, StringArray(progress, "dyeSources"), StringArray(progress, "dyeTargets"));
    }

    static string[] StringArray(JsonElement doc, string name)
    {
        if (!doc.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array) return [];
        var into = new List<string>();
        foreach (var item in arr.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String) into.Add(item.GetString()!);
        return [.. into];
    }

    // READ AS THE WIDEST WHOLE NUMBER THERE IS. Reading it as the ordinary one put a ceiling on it that the
    // login service does not have, so a number above about two billion was a refused save on one server and an
    // accepted one on the other — and a level above it read back as "never played".
    static bool HasWholeNumber(JsonElement doc, string name, out long value)
    {
        value = 0;
        return doc.ValueKind == JsonValueKind.Object
               && doc.TryGetProperty(name, out var v)
               && v.ValueKind == JsonValueKind.Number
               && v.TryGetInt64(out value);
    }
}
