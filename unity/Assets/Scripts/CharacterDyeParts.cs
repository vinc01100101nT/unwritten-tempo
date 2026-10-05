using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHICH PARTS OF EACH CHARACTER MODEL CAN BE RECOLOURED — the distinct cloth colours a model actually wears, skin and
/// eyes left out — worked out once in the editor and saved here (<c>CharacterDyePartsBaker.BakeAll</c>).
///
/// <para>Finding them reads the model's whole texture back from the graphics card, which froze the game for 18.7 ms
/// (measured 2026-10-05) the first time each model appeared. A model never changes while the game runs, so the answer
/// is worked out when the model is authored, not when somebody walks into view.</para>
///
/// <para>A model missing from this list has no recolourable parts in game and is reported once in the log; the asset
/// rules check fails any character model that has no entry, so a new model cannot ship without one.</para>
/// </summary>
public class CharacterDyeParts : ScriptableObject
{
    public const string ResourcePath = "CharacterDyeParts";

    [Serializable]
    public class Entry
    {
        /// <summary>The model file the parts were read from, without its extension — what the asset check matches
        /// against the model folder.</summary>
        public string modelFile;
        /// <summary>The names of the textures the model is drawn with, sorted and joined by "|" — the key a body is
        /// looked up by (<see cref="CharacterDye3D.SignatureOf"/>).</summary>
        public string textures;
        /// <summary>The recolourable colours, authored sRGB, most-worn first.</summary>
        public Color[] parts;
    }

    public List<Entry> entries = new List<Entry>();

    static Dictionary<string, Color[]> byTextures;
    static readonly HashSet<string> reportedMissing = new HashSet<string>();

    /// <summary>The recolourable parts of the model drawn with <paramref name="textures"/>; empty when none were saved.</summary>
    public static IReadOnlyList<Color> For(string textures)
    {
        if (string.IsNullOrEmpty(textures)) return Array.Empty<Color>();
        if (byTextures == null) Load();
        if (byTextures.TryGetValue(textures, out var parts)) return parts;
        if (reportedMissing.Add(textures))
            GameLog.Warn(LogCat.Client, "no saved outfit parts for the model drawn with '" + textures + "' — its "
                             + "colours cannot be changed. Run CharacterDyePartsBaker.BakeAll in the editor.");
        return Array.Empty<Color>();
    }

    static void Load()
    {
        byTextures = new Dictionary<string, Color[]>(StringComparer.Ordinal);
        var table = Resources.Load<CharacterDyeParts>(ResourcePath);
        if (table == null) return;
        foreach (var e in table.entries)
            if (e != null && !string.IsNullOrEmpty(e.textures) && e.parts != null) byTextures[e.textures] = e.parts;
    }

#if UNITY_EDITOR
    /// <summary>Editor only: forget the loaded table so a fresh bake is read on the next lookup.</summary>
    public static void Forget() { byTextures = null; reportedMissing.Clear(); }
#endif
}
