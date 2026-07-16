using System.IO;
using UnityEditor;
using UnityEngine;
using Preset = WeatherDirector.Preset;

/// <summary>
/// One-click bridge for the "code now, assets later" plan: turns the hard-coded timelines in
/// <see cref="WeatherSequences"/> into editable <see cref="WeatherSequenceAsset"/>s plus a
/// <see cref="WeatherSequenceLibrary"/> at <c>Resources/WeatherSequenceLibrary</c>. Each asset starts as an EXACT copy
/// of its <see cref="WeatherSequences.FromCode"/> timeline, so nothing about how the weather looks changes the moment
/// you run this — you've just moved the numbers into the Inspector, ready to tune live. After this, the engine resolves
/// every preset from the assets (and the code timelines become the safety fallback for any preset you delete).
///
/// <para>Idempotent: re-running updates existing assets in place (and re-links the library) rather than duplicating.
/// It does NOT overwrite hand-tuned keys silently — it only (re)writes assets it created and any the library lists.
/// This is a keeper authoring tool, not a quick-fix.</para>
/// </summary>
public static class WeatherSequenceAssetBaker
{
    // The library MUST live directly under a Resources folder so WeatherSequenceLibrary.Active() can load it by name.
    const string Dir = "Assets/3_Data/Resources/Weather";
    const string LibPath = "Assets/3_Data/Resources/" + WeatherSequenceLibrary.ResourcePath + ".asset";

    [MenuItem("Tools/unwritten/Weather/Generate Sequence Assets from Code", priority = 200)]
    static void Generate()
    {
        EnsureFolder(Dir);

        var presets = (Preset[])System.Enum.GetValues(typeof(Preset));
        var assets = new WeatherSequenceAsset[presets.Length];

        for (int i = 0; i < presets.Length; i++)
        {
            var p = presets[i];
            string path = $"{Dir}/Weather_{p}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<WeatherSequenceAsset>(path);
            bool isNew = asset == null;
            if (isNew) asset = ScriptableObject.CreateInstance<WeatherSequenceAsset>();

            var seq = WeatherSequences.FromCode(p);
            asset.preset = p;
            asset.displayName = seq.name;
            asset.keys = ToData(seq.keys);

            if (isNew) AssetDatabase.CreateAsset(asset, path);
            else EditorUtility.SetDirty(asset);
            assets[i] = asset;
        }

        // Create or update the single library and point it at every per-preset asset.
        var lib = AssetDatabase.LoadAssetAtPath<WeatherSequenceLibrary>(LibPath);
        bool libIsNew = lib == null;
        if (libIsNew) lib = ScriptableObject.CreateInstance<WeatherSequenceLibrary>();
        lib.sequences = assets;
        if (libIsNew) AssetDatabase.CreateAsset(lib, LibPath);
        else EditorUtility.SetDirty(lib);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        WeatherSequenceLibrary.InvalidateCache();

        Debug.Log($"[unwritten] Weather sequence assets ready: {assets.Length} timelines in {Dir} + library at " +
                  $"{LibPath}. Tune them in the Inspector — the director now resolves weather from these (code is the fallback).");
        Selection.activeObject = lib;
    }

    static WeatherKeyData[] ToData(WeatherKey[] keys)
    {
        var data = new WeatherKeyData[keys.Length];
        for (int i = 0; i < keys.Length; i++)
            data[i] = new WeatherKeyData(keys[i].at, keys[i].channel, keys[i].target, keys[i].ramp);
        return data;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        var leaf = Path.GetFileName(path);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
