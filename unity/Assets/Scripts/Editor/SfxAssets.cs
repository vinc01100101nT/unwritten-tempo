using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor helper: builds <see cref="SfxSet"/>s from the TomMusic WAV pack and forces those clips to
/// import as <c>Decompress On Load</c> (right for short, latency-sensitive SFX). Used by the player /
/// character / item builders so sound wiring is one line and the import settings are correct without
/// hand-clicking each file.
/// </summary>
public static class SfxAssets
{
    public const string Root = "Assets/Audio/TomMusic/WAV Files/SFX/";
    public const string SwordDir = "Attacks/Sword Attacks Hits and Blocks";

    /// <summary>A set from <paramref name="folder"/> (under <see cref="Root"/>) given bare clip names
    /// (no extension). Missing clips are skipped; an all-missing set warns once.</summary>
    public static SfxSet Set(float volume, string folder, params string[] names)
    {
        var clips = names.Select(n => Load($"{Root}{folder}/{n}.wav")).Where(c => c != null).ToArray();
        if (clips.Length == 0)
            Debug.LogWarning($"[unwritten] No SFX found in '{Root}{folder}'. Is the TomMusic pack imported there?");
        return new SfxSet { clips = clips, volume = volume };
    }

    static AudioClip Load(string path)
    {
        if (!File.Exists(path)) return null;
        ConfigureImport(path);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static void ConfigureImport(string path)
    {
        if (!(AssetImporter.GetAtPath(path) is AudioImporter ai)) return;
        var s = ai.defaultSampleSettings;
        if (s.loadType != AudioClipLoadType.DecompressOnLoad)
        {
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            ai.defaultSampleSettings = s;
            ai.SaveAndReimport();
        }
    }
}
