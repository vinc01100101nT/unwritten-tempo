using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes the wiki's world map — <c>wiki/content/world.generated.json</c>, the world map picture, and one top-down
/// preview per map — from the SAME world map the game's World Map window loads (<see cref="WorldAtlas"/>), and
/// nothing else ever writes them.
///
/// <para><b>Where each value comes from.</b> Every map's place and type is the world atlas's own entry, turned into a
/// share of the picture exactly the way the World Map window turns a world point into one. The picture is the
/// atlas's own texture file, copied byte for byte. Names come from the world layout, the one home of them. Which
/// creatures live on a map is NOT written here: the wiki reads it from the creature export, so there is one copy.</para>
///
/// <para><b>The previews</b> are the game's own minimap pictures: <see cref="MinimapBaker"/> hands each one over the
/// moment it draws it (<see cref="WritePreview"/>), so the wiki shows exactly what the in-game minimap shows.</para>
///
/// <para><b>When it runs.</b> The map data and picture: whenever <see cref="WikiExport"/> runs, like every Database page.
/// A preview: whenever its map is drawn. Everything written lives in the wiki folder, outside the project's assets,
/// so nothing Unity saves can change because of it.</para>
/// </summary>
public static class WorldMapExport
{
    const string AtlasAssetPath = "Assets/3_Data/Resources/WorldAtlas.asset";
    const int PreviewJpgQuality = 80;

    static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
    static string WikiDir => Path.Combine(RepoRoot, "wiki");
    public static string DataPath => Path.Combine(WikiDir, "content", "world.generated.json");
    const string PictureRel = "assets/world-map.png";
    static string PreviewRel(string map) => "assets/minimaps/" + map + ".jpg";
    static string Abs(string rel) => Path.Combine(WikiDir, rel);

    [Serializable] class Export { public string note, picture; public int width, height; public Zone[] maps; }

    [Serializable] class Zone
    {
        public string id, name, type, preview;
        public float left, top, width, height;
    }

    static WorldAtlas Atlas => AssetDatabase.LoadAssetAtPath<WorldAtlas>(AtlasAssetPath);

    /// <summary>Regenerate the map data and copy the picture. Returns how many maps were written.</summary>
    public static int Write()
    {
        var atlas = Atlas;
        if (atlas == null || atlas.atlas == null) return 0;

        var names = MobExport.MapNames();
        float w = Mathf.Max(1f, atlas.worldW), h = Mathf.Max(1f, atlas.worldH);
        var zones = new List<Zone>(atlas.maps.Count);
        foreach (var e in atlas.maps)
        {
            if (e == null || string.IsNullOrEmpty(e.scene)) continue;
            string preview = PreviewRel(e.scene);
            zones.Add(new Zone
            {
                id = e.scene,
                name = names.TryGetValue(e.scene, out var n) ? n : e.scene,
                type = e.type,
                preview = File.Exists(Abs(preview)) ? preview : "",
                left = (e.x - atlas.minX) / w,
                top = (e.y - atlas.minY) / h,
                width = e.w / w,
                height = e.h / h,
            });
        }

        var picturePath = AssetDatabase.GetAssetPath(atlas.atlas);
        if (!string.IsNullOrEmpty(picturePath))
            WriteBytesIfChanged(Abs(PictureRel), File.ReadAllBytes(Path.Combine(RepoRoot, "unity", picturePath)));

        var export = new Export
        {
            note = "GENERATED from the game's world map (Resources/WorldAtlas) and the world layout's names by the editor " +
                   "(WikiExport), after any change to the game's data or code. Do NOT hand-edit: the next change " +
                   "overwrites this file.",
            picture = PictureRel,
            width = atlas.atlas.width,
            height = atlas.atlas.height,
            maps = zones.ToArray(),
        };
        SkillExport.WriteIfChanged(DataPath, JsonUtility.ToJson(export, true) + "\n");
        return zones.Count;
    }

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
