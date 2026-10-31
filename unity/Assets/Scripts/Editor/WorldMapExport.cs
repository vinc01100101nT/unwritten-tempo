using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes the wiki's top-down preview of a map from the world atlas (<see cref="WorldAtlas"/>): the game's own minimap
/// picture, which <see cref="MinimapBaker"/> hands over the moment it draws it (<see cref="WritePreview"/>), so the wiki
/// shows exactly what the in-game minimap shows.
///
/// <para><b>The world map itself is NOT written here.</b> <c>wiki/content/world.generated.json</c> and the world map
/// picture are drawn from the built seamless world by <c>tools/export-wiki-world-map.py</c>, their one writer.</para>
///
/// <para>Everything written lives in the wiki folder, outside the project's assets, so nothing Unity saves can change
/// because of it.</para>
/// </summary>
public static class WorldMapExport
{
    const string AtlasAssetPath = "Assets/3_Data/Resources/WorldAtlas.asset";
    const int PreviewJpgQuality = 80;

    static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
    static string WikiDir => Path.Combine(RepoRoot, "wiki");
    static string PreviewRel(string map) => "assets/minimaps/" + map + ".jpg";
    static string Abs(string rel) => Path.Combine(WikiDir, rel);

    static WorldAtlas Atlas => AssetDatabase.LoadAssetAtPath<WorldAtlas>(AtlasAssetPath);

    /// <summary>The wiki's preview of <paramref name="map"/>: its minimap picture (<paramref name="png"/>, as drawn),
    /// saved as a small JPEG. Called by <see cref="MinimapBaker"/> each time it draws a map.</summary>
    public static void WritePreview(string map, byte[] png)
    {
        // Only maps on the world map have a preview there; a test map would ship a picture nothing shows.
        var atlas = Atlas;
        if (atlas == null || atlas.Find(map) == null) return;
        var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
        if (tex.LoadImage(png)) WriteBytesIfChanged(Abs(PreviewRel(map)), tex.EncodeToJPG(PreviewJpgQuality));
        // editor-write-ok: destroys the throwaway picture made in memory to re-encode it; nothing saved is touched.
        UnityEngine.Object.DestroyImmediate(tex);
    }

    static void WriteBytesIfChanged(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        if (File.Exists(path))
        {
            var old = File.ReadAllBytes(path);
            if (old.Length == bytes.Length && old.AsSpan().SequenceEqual(bytes)) return;
        }
        File.WriteAllBytes(path, bytes);
    }
}
