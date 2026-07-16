using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Wires hand-/AI-authored item icon PNGs onto their <see cref="Item"/> assets by ID — the art half of the
/// item pipeline (stats/slot/rarity live in the <see cref="Item"/> ScriptableObject; this only supplies the
/// picture). Drop a PNG named after an item's <see cref="Item.id"/> (e.g. <c>item.iron-sword.png</c>) into
/// <see cref="SourceDir"/>; this applies icon-friendly import settings (Sprite/Single, point filter, no
/// compression, alpha transparency) and assigns the sliced sprite to the matching item's <see cref="Item.icon"/>.
///
/// <para>Matching order per file: exact <see cref="Item.id"/> → kebab(displayName) → asset file name. Idempotent
/// and incremental — re-run as new icons arrive; unmatched files and items with the placeholder id are reported,
/// not silently dropped. The icon source is independent of where the art came from (functional name, not vendor).</para>
///
/// Menu: Tools ▸ unwritten ▸ Art ▸ Import Item Icons.
/// </summary>
public static class ItemIconImporter
{
    // Source PNGs go here (named by item id). Kept OUT of Resources — only the Item assets need to ship.
    const string SourceDir = "Assets/1_Authored/Art/Items";
    // Where the Item assets live (BagSeeder loads them at runtime).
    const string ItemsDir = "Assets/3_Data/Resources/Items";
    const int Ppu = 256;

    [MenuItem("Tools/unwritten/Art/Import Item Icons", priority = 622)]
    static void Run()
    {
        EnsureFolder(SourceDir);

        var pngs = AssetDatabase.FindAssets("t:Texture2D", new[] { SourceDir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pngs.Count == 0)
        {
            Debug.LogWarning($"[unwritten] No PNGs in {SourceDir}. Drop item icons there, named by item id " +
                             $"(e.g. 'item.iron-sword.png'), then re-run. (See the Item Icon Style Guide.)");
            EditorUtility.RevealInFinder(SourceDir);
            return;
        }

        var items = AssetDatabase.FindAssets("t:Item", new[] { ItemsDir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Item>)
            .Where(x => x != null)
            .ToList();

        // Lookup tables (first wins; ids are meant to be unique).
        var byId   = new Dictionary<string, Item>();
        var byName = new Dictionary<string, Item>();
        foreach (var it in items)
        {
            if (!string.IsNullOrEmpty(it.id) && it.id != "item.new" && !byId.ContainsKey(it.id)) byId[it.id] = it;
            string k = Kebab(it.displayName);
            if (!string.IsNullOrEmpty(k) && !byName.ContainsKey(k)) byName[k] = it;
        }

        int assigned = 0;
        var unmatched = new List<string>();

        foreach (var path in pngs)
        {
            ApplyIconImportSettings(path);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) { unmatched.Add($"{Path.GetFileName(path)} (no sprite — bad import?)"); continue; }

            string key = Path.GetFileNameWithoutExtension(path);
            Item target =
                byId.TryGetValue(key, out var a) ? a :
                byName.TryGetValue(Kebab(key), out var b) ? b :
                items.FirstOrDefault(it => string.Equals(it.name, key, System.StringComparison.OrdinalIgnoreCase));

            if (target == null) { unmatched.Add($"{Path.GetFileName(path)} (no Item with id/name '{key}')"); continue; }

            if (target.icon != sprite)
            {
                target.icon = sprite;
                EditorUtility.SetDirty(target);
            }
            assigned++;
        }

        AssetDatabase.SaveAssets();

        var sb = new StringBuilder();
        sb.Append($"[unwritten] Item icons imported: {assigned}/{pngs.Count} assigned.");
        if (unmatched.Count > 0) sb.Append($"\n  Unmatched ({unmatched.Count}): ").Append(string.Join(", ", unmatched));
        var missingId = items.Where(i => string.IsNullOrEmpty(i.id) || i.id == "item.new").Select(i => i.name).ToList();
        if (missingId.Count > 0)
            sb.Append($"\n  ⚠ Items with no stable id (match only by name): {string.Join(", ", missingId)}");
        Debug.Log(sb.ToString());
    }

    // Icon-friendly settings: crisp UI/world sprite, no mips, no compression artefacts, full alpha.
    static void ApplyIconImportSettings(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        bool changed = false;

        if (ti.textureType != TextureImporterType.Sprite) { ti.textureType = TextureImporterType.Sprite; changed = true; }
        if (ti.spriteImportMode != SpriteImportMode.Single) { ti.spriteImportMode = SpriteImportMode.Single; changed = true; }
        if (ti.filterMode != FilterMode.Point) { ti.filterMode = FilterMode.Point; changed = true; }
        if (ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
        if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
        if (ti.textureCompression != TextureImporterCompression.Uncompressed)
        { ti.textureCompression = TextureImporterCompression.Uncompressed; changed = true; }
        if (!Mathf.Approximately(ti.spritePixelsPerUnit, Ppu)) { ti.spritePixelsPerUnit = Ppu; changed = true; }

        if (changed) ti.SaveAndReimport();
    }

    static string Kebab(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.Trim().ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        // collapse runs of '-'
        var parts = sb.ToString().Split(new[] { '-' }, System.StringSplitOptions.RemoveEmptyEntries);
        return string.Join("-", parts);
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
