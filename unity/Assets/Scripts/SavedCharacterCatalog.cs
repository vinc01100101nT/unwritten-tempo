using System.Collections.Generic;
using UnityEngine;

/// <summary>Every <see cref="SavedCharacter"/> in <c>Resources/SavedCharacters</c>, found by id, by the elite that always
/// becomes it, or picked at random for a normal. Loaded once, like <see cref="MonsterCatalog"/>.</summary>
public static class SavedCharacterCatalog
{
    const string ResourceDir = "SavedCharacters";

    static SavedCharacter[] all;
    static Dictionary<string, SavedCharacter> byId;
    static Dictionary<string, SavedCharacter> byElite;
    static List<SavedCharacter> forNormals;

    static void EnsureLoaded()
    {
        if (all != null) return;
        all = Resources.LoadAll<SavedCharacter>(ResourceDir) ?? System.Array.Empty<SavedCharacter>();
        byId = new Dictionary<string, SavedCharacter>(all.Length, System.StringComparer.OrdinalIgnoreCase);
        byElite = new Dictionary<string, SavedCharacter>(System.StringComparer.OrdinalIgnoreCase);
        forNormals = new List<SavedCharacter>(all.Length);
        foreach (var c in all)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.id)) continue;
            byId[c.id] = c;
            if (string.IsNullOrWhiteSpace(c.eliteId)) forNormals.Add(c);
            else byElite[c.eliteId] = c;
        }
    }

    /// <summary>Drop the cache so the next access reloads (after creating or editing assets).</summary>
    public static void Reload() { all = null; byId = null; byElite = null; forNormals = null; }

    public static IReadOnlyList<SavedCharacter> All { get { EnsureLoaded(); return all; } }

    /// <summary>The character with <paramref name="id"/>, or null.</summary>
    public static SavedCharacter Get(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        EnsureLoaded();
        return byId.TryGetValue(id, out var c) ? c : null;
    }

    /// <summary>The character the elite <paramref name="eliteId"/> always turns into, or null when none is linked.</summary>
    public static SavedCharacter ForElite(string eliteId)
    {
        if (string.IsNullOrEmpty(eliteId)) return null;
        EnsureLoaded();
        return byElite.TryGetValue(eliteId, out var c) ? c : null;
    }

    /// <summary>A character a normal may turn into — every character no elite owns, equally likely. Null when there is
    /// none. The die is the caller's, because it decides a result.</summary>
    public static SavedCharacter ForNormal(int roll)
    {
        EnsureLoaded();
        if (forNormals.Count == 0) return null;
        int n = forNormals.Count;
        return forNormals[((roll % n) + n) % n];
    }

    /// <summary>How many characters a normal may turn into.</summary>
    public static int NormalCount { get { EnsureLoaded(); return forNormals.Count; } }
}
