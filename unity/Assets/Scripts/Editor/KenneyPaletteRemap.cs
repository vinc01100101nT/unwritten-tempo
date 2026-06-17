using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click "skin the Kenney props to our pixel-art palette" (VIN-15). Each Kenney kit is flat-shaded
/// from a tiny <c>colormap.png</c> of solid swatches; this tool recolors every swatch to its NEAREST
/// colour in <b>Assets/Art/NinjaAdventure/Palette.png</b>, writes a sibling <c>colormap.ninja.png</c>
/// (layout untouched, so UVs still line up), and repoints the kit material at it. The terrain blocks are
/// already skinned by the projection shader — this covers the standing props (houses/trees/fountain/…).
///
/// Reversible: the original <c>colormap.png</c> is never overwritten. Delete the <c>colormap.ninja.png</c>
/// (and re-run Build Prefabs) to go back. <see cref="KenneyPrefabBaker"/> auto-prefers the ninja colormap
/// when present, so a re-bake keeps the recolour.
/// </summary>
public static class KenneyPaletteRemap
{
    const string PalettePath = "Assets/Art/NinjaAdventure/Palette.png";

    // (kit colormap, kit shared material) — the two palettes the town actually draws from.
    static readonly (string colormap, string material)[] Kits =
    {
        ("Assets/Art/KenneyFantasyTownKit/FBX format/Textures/colormap.png",
         "Assets/Art/KenneyFantasyTownKit/FBX format/KenneyFantasyTown.mat"),
        ("Assets/Art/KenneyPlatformerKit/FBX format/Textures/colormap.png",
         "Assets/Art/KenneyPlatformerKit/FBX format/KenneyPlatformer.mat"),
    };

    /// <summary>The recoloured sibling of a kit's colormap (e.g. .../colormap.ninja.png).</summary>
    public static string NinjaColormap(string colormap) =>
        colormap.Substring(0, colormap.Length - ".png".Length) + ".ninja.png";

    [MenuItem("Tools/unwritten/Recolor Props to Ninja Palette", priority = 23)]
    static void Run()
    {
        var palette = LoadColors(PalettePath);
        if (palette == null || palette.Count == 0)
        { Fail($"No palette colours found at {PalettePath}."); return; }

        int kitsDone = 0;
        var report = new System.Text.StringBuilder();
        foreach (var (colormap, material) in Kits)
        {
            if (!File.Exists(colormap)) { report.AppendLine($"• skipped (no colormap): {colormap}"); continue; }

            var src = LoadTexture(colormap);
            if (src == null) { report.AppendLine($"• skipped (unreadable): {colormap}"); continue; }

            int w = src.width, h = src.height;
            var pixels = src.GetPixels32();
            var cache = new Dictionary<int, Color32>();   // source rgb → mapped colour (flat swatches repeat a lot)
            int changed = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                if (c.a == 0) continue;                   // keep transparency
                int key = (c.r << 16) | (c.g << 8) | c.b;
                if (!cache.TryGetValue(key, out var mapped)) { mapped = Nearest(c, palette); cache[key] = mapped; }
                mapped.a = c.a;
                if (mapped.r != c.r || mapped.g != c.g || mapped.b != c.b) changed++;
                pixels[i] = mapped;
            }
            Object.DestroyImmediate(src);

            string outPath = NinjaColormap(colormap);
            WritePng(outPath, pixels, w, h);
            RepointMaterial(material, outPath);
            kitsDone++;
            report.AppendLine($"• {Path.GetFileName(material)} → {Path.GetFileName(outPath)} " +
                              $"({cache.Count} swatches, {changed} px recoloured)");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[unwritten] Recolor Props: {kitsDone} kit(s) remapped to {PalettePath} ({palette.Count} palette colours).\n{report}");
        EditorUtility.DisplayDialog("Recolor Props — done",
            $"Remapped {kitsDone} kit colormap(s) to the Ninja palette ({palette.Count} colours).\n\n{report}\n" +
            "Re-open / re-generate the 3D town to see the recoloured props. Originals are untouched " +
            "(delete the *.ninja.png to revert).", "OK");
    }

    // ---- colour helpers ------------------------------------------------------

    static Color32 Nearest(Color32 c, List<Color32> palette)
    {
        int best = 0; long bestD = long.MaxValue;
        for (int i = 0; i < palette.Count; i++)
        {
            var p = palette[i];
            long dr = c.r - p.r, dg = c.g - p.g, db = c.b - p.b;
            long d = dr * dr + dg * dg + db * db;     // squared RGB distance — cheap, good enough for flat swatches
            if (d < bestD) { bestD = d; best = i; }
        }
        return palette[best];
    }

    static List<Color32> LoadColors(string path)
    {
        var tex = LoadTexture(path);
        if (tex == null) return null;
        var seen = new HashSet<int>();
        var list = new List<Color32>();
        foreach (var c in tex.GetPixels32())
        {
            if (c.a == 0) continue;
            int key = (c.r << 16) | (c.g << 8) | c.b;
            if (seen.Add(key)) list.Add(c);
        }
        Object.DestroyImmediate(tex);
        return list;
    }

    // ---- texture IO (raw bytes → readable temp, so we never toggle import settings) ----

    static Texture2D LoadTexture(string path)
    {
        byte[] bytes;
        try { bytes = File.ReadAllBytes(path); } catch { return null; }
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes)) { Object.DestroyImmediate(tex); return null; }
        return tex;
    }

    static void WritePng(string path, Color32[] pixels, int w, int h)
    {
        var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        outTex.SetPixels32(pixels);
        outTex.Apply();
        File.WriteAllBytes(path, outTex.EncodeToPNG());
        Object.DestroyImmediate(outTex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

        // Crisp pixel-art: point filter, no compression. (Clamp wrap — colormaps don't tile.)
        if (AssetImporter.GetAtPath(path) is TextureImporter imp)
        {
            imp.filterMode = FilterMode.Point;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }
    }

    static void RepointMaterial(string matPath, string texPath)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (mat == null || tex == null) { Debug.LogWarning($"[unwritten] Recolor: can't repoint {matPath} → {texPath}"); return; }
        mat.SetTexture("_BaseMap", tex);
        mat.mainTexture = tex;
        EditorUtility.SetDirty(mat);
    }

    static void Fail(string msg)
    {
        EditorUtility.DisplayDialog("Recolor Props", msg, "OK");
        Debug.LogError("[unwritten] Recolor Props: " + msg);
    }
}
