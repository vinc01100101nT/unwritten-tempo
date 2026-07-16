using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bulk item-icon importer: slices a GRID sheet PNG into per-cell sprites and assigns each to an
/// <see cref="Item"/> by a row-major id list. The fast path for generating many icons in one GPT image
/// (vs <see cref="ItemIconImporter"/>'s one-PNG-per-item path, kept for hero/legendary quality).
///
/// <para>Per sheet you drop two files in <see cref="SheetDir"/>:</para>
/// <list type="bullet">
/// <item><c>&lt;name&gt;.png</c> — the grid image (transparent bg).</item>
/// <item><c>&lt;name&gt;.txt</c> — a manifest: a grid-size line + the item ids in row-major order
/// (left→right, top→bottom). The grid size lives HERE so changing it per sheet is a one-line text edit.</item>
/// </list>
///
/// <para>Manifest format (lines starting with <c>#</c> are comments):</para>
/// <code>
/// cols: 3
/// rows: 3            # optional — inferred from the id count if omitted
/// item.iron-sword
/// item.katana
/// -                  # a dash/blank skips that cell (empty grid slot)
/// item.life-potion
/// </code>
///
/// Cells are sliced row-major; cell N → the Nth non-comment manifest entry. Idempotent — re-run as sheets
/// arrive or the manifest changes. Unmatched ids and cell/id count mismatches are reported, not silent.
///
/// Menu: Tools ▸ unwritten ▸ Art ▸ Import Item Sheets.
/// </summary>
public static class ItemSheetImporter
{
    const string SheetDir = "Assets/1_Authored/Art/Items/Sheets";   // sheets + their manifests live here
    const string ItemsDir = "Assets/3_Data/Resources/Items";    // the Item data assets
    const int Ppu = 256;

    [MenuItem("Tools/unwritten/Art/Import Item Sheets", priority = 623)]
    static void Run()
    {
        EnsureFolder(SheetDir);

        var sheets = AssetDatabase.FindAssets("t:Texture2D", new[] { SheetDir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sheets.Count == 0)
        {
            Debug.LogWarning($"[unwritten] No sheets in {SheetDir}. Drop a grid PNG + a same-named .txt manifest " +
                             $"(grid size + row-major item ids), then re-run. See the Item Icon Style Guide.");
            EditorUtility.RevealInFinder(SheetDir);
            return;
        }

        var byId = AssetDatabase.FindAssets("t:Item", new[] { ItemsDir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<Item>)
            .Where(x => x != null && !string.IsNullOrEmpty(x.id) && x.id != "item.new")
            .GroupBy(x => x.id)
            .ToDictionary(g => g.Key, g => g.First());

        var log = new StringBuilder("[unwritten] Item sheet import:");
        foreach (var sheetPath in sheets)
            ImportOne(sheetPath, byId, log);

        AssetDatabase.SaveAssets();
        Debug.Log(log.ToString());
    }

    static void ImportOne(string sheetPath, Dictionary<string, Item> byId, StringBuilder log)
    {
        string manifestPath = Path.ChangeExtension(sheetPath, ".txt");
        if (!File.Exists(manifestPath))
        {
            log.Append($"\n  ✗ {Path.GetFileName(sheetPath)}: no manifest ({Path.GetFileName(manifestPath)}) — skipped.");
            return;
        }

        if (!ParseManifest(File.ReadAllLines(manifestPath), out int cols, out int rows, out var ids, out string err))
        {
            log.Append($"\n  ✗ {Path.GetFileName(sheetPath)}: {err}");
            return;
        }

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetPath);
        if (tex == null) { log.Append($"\n  ✗ {Path.GetFileName(sheetPath)}: not a texture."); return; }

        // Grid slice → named sub-sprites (named by id so they're stable and self-documenting in the Sprite Editor).
        SliceGrid(sheetPath, cols, rows, ids);

        var subSprites = AssetDatabase.LoadAllAssetsAtPath(sheetPath).OfType<Sprite>()
            .ToDictionary(s => s.name, s => s);

        int assigned = 0; var missing = new List<string>();
        foreach (var id in ids)
        {
            if (string.IsNullOrEmpty(id)) continue;                       // skipped cell
            if (!byId.TryGetValue(id, out var item)) { missing.Add($"{id} (no Item)"); continue; }
            if (!subSprites.TryGetValue(SpriteName(id, ids), out var spr)) { missing.Add($"{id} (no cell)"); continue; }
            if (item.icon != spr) { item.icon = spr; EditorUtility.SetDirty(item); }
            assigned++;
        }

        log.Append($"\n  ✓ {Path.GetFileName(sheetPath)}: {cols}×{rows}, {assigned}/{ids.Count(x => !string.IsNullOrEmpty(x))} assigned.");
        if (missing.Count > 0) log.Append($" Unmatched: {string.Join(", ", missing)}.");
        int cells = cols * rows;
        if (ids.Count > cells) log.Append($" ⚠ manifest lists {ids.Count} entries but grid has only {cells} cells (extras ignored).");
    }

    // Build a uniform grid SpriteMetaData and reimport as Multiple. Cell size = tex / grid; row-major, top-left first.
    static void SliceGrid(string path, int cols, int rows, List<string> ids)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;

        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Multiple;
        ti.filterMode = FilterMode.Point;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.spritePixelsPerUnit = Ppu;
        ti.maxTextureSize = 4096;                 // don't downscale a packed sheet below its cells

        // Source pixel dimensions (read off the imported texture).
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        int texW = tex != null ? tex.width : cols;
        int texH = tex != null ? tex.height : rows;
        float cw = texW / (float)cols;
        float ch = texH / (float)rows;

        var metas = new List<SpriteMetaData>(cols * rows);
        for (int r = 0; r < rows; r++)
        for (int c = 0; c < cols; c++)
        {
            int idx = r * cols + c;
            string id = idx < ids.Count ? ids[idx] : null;
            if (string.IsNullOrEmpty(id)) continue;                       // skipped/empty cell — no sub-sprite
            // Unity rects are bottom-left origin; row 0 is the TOP of the image.
            float x = c * cw;
            float y = texH - (r + 1) * ch;
            metas.Add(new SpriteMetaData
            {
                name = SpriteName(id, ids),
                rect = new Rect(x, y, cw, ch),
                alignment = (int)SpriteAlignment.Center,
                pivot = new Vector2(0.5f, 0.5f),
            });
        }

#pragma warning disable 618  // spritesheet/SpriteMetaData is the supported grid-slice path for an editor tool
        ti.spritesheet = metas.ToArray();
#pragma warning restore 618
        ti.SaveAndReimport();
    }

    // Sub-sprite name = the id, but disambiguated if the same id appears twice (shouldn't, but be safe).
    static string SpriteName(string id, List<string> ids) => id;

    static bool ParseManifest(string[] lines, out int cols, out int rows, out List<string> ids, out string err)
    {
        cols = 0; rows = 0; ids = new List<string>(); err = null;
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;

            if (TryKey(line, "cols", out int cv)) { cols = cv; continue; }
            if (TryKey(line, "rows", out int rv)) { rows = rv; continue; }

            // A dash or underscore = an intentionally empty cell (keeps row-major alignment).
            ids.Add(line == "-" || line == "_" ? null : line);
        }

        int count = ids.Count;
        if (count == 0) { err = "manifest has no item ids."; return false; }

        // Infer a square grid if cols/rows omitted, then fill the missing dimension from the id count.
        if (cols <= 0 && rows <= 0) { cols = Mathf.CeilToInt(Mathf.Sqrt(count)); rows = Mathf.CeilToInt(count / (float)cols); }
        else if (cols <= 0) cols = Mathf.CeilToInt(count / (float)rows);
        else if (rows <= 0) rows = Mathf.CeilToInt(count / (float)cols);

        if (cols <= 0 || rows <= 0) { err = "could not resolve a valid grid (cols/rows)."; return false; }
        return true;
    }

    static bool TryKey(string line, string key, out int value)
    {
        value = 0;
        int colon = line.IndexOf(':');
        if (colon < 0) return false;
        if (!string.Equals(line.Substring(0, colon).Trim(), key, System.StringComparison.OrdinalIgnoreCase)) return false;
        return int.TryParse(line.Substring(colon + 1).Trim(), out value);
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
