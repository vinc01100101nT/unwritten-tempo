using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scans the particle-effect prefab folders and (re)builds <c>Resources/EffectLibrary.asset</c> — the table
/// <see cref="EffectCatalog"/>/<see cref="Vfx"/> resolve ids against. Each prefab becomes an <see cref="EffectDef"/>
/// whose id is a category prefix + kebab-cased name (e.g. <c>Hits and explosions/Explosion</c> →
/// <c>hit.explosion</c>, <c>Procedural fire/…/Explosion</c> → <c>fire.explosion</c>). Idempotent and
/// NON-destructive: an existing entry (matched by prefab reference) keeps its id + hand-tuned scale/lifetime, so
/// re-running only ADDS new prefabs and reports entries whose prefab was deleted. Menu:
/// <b>Tools ▸ unwritten ▸ Art ▸ Rebuild Effect Library</b>.
/// </summary>
public static class EffectLibraryBuilder
{
    // A prefab folder to scan. Prefix modes (checked in this order):
    //   FixedPrefix != null           → every prefab under it gets that id prefix (a flat vendor pack).
    //   ByFirstSubfolder == true       → the id prefix is the FIRST subfolder under Path (the drop-in CONVENTION root:
    //                                     Assets/Art/VFX/<prefix>/… → "<prefix>.*", any depth below that). No code edit
    //                                     per pack — just drop prefabs into a prefix-named folder and Rebuild.
    //   otherwise                      → the prefix is each prefab's IMMEDIATE parent folder via CategoryPrefix (a
    //                                     vendor pack pre-nested by category, e.g. Hovl).
    // The vendor roots are source-specific by design — add one per pack imported. The convention root below means
    // most new packs need NO code change at all.
    struct ScanRoot
    {
        public string Path;
        public string FixedPrefix;
        public string[] ExtraDrop;   // extra kebab tokens to strip from generated ids (source-specific naming noise, e.g. "fx")
        public bool ByFirstSubfolder; // prefix = first subfolder under Path (the drop-in convention root)
        public ScanRoot(string path, string fixedPrefix = null, string[] extraDrop = null, bool byFirstSubfolder = false)
        { Path = path; FixedPrefix = fixedPrefix; ExtraDrop = extraDrop; ByFirstSubfolder = byFirstSubfolder; }
    }

    // Universal naming noise stripped from generated ids everywhere (art tools name files "FX_…"/"VFX_…").
    static readonly string[] NoiseTokens = { "fx", "vfx" };

    static readonly ScanRoot[] Roots =
    {
        // ── The drop-in convention root: NO code edit needed for new packs. ─────────────────────────────
        // Make a subfolder named for the id prefix you want (e.g. Assets/Art/VFX/circle/), drop the pack's
        // prefabs in (any depth below it), then run Rebuild Effect Library → ids read "circle.<name>".
        new ScanRoot("Assets/Art/VFX", byFirstSubfolder: true, extraDrop: NoiseTokens),

        // ── Vendor packs left in their imported location (one line per pack). ──────────────────────────
        new ScanRoot("Assets/Art/Hovl Studio/Magic effects pack/Prefabs"),          // nested by category
        new ScanRoot("Assets/Art/Hovl Studio/Procedural fire/Prefabs", "fire"),     // flat → all "fire.*"
        // Eric VFX ground circles — flat pack; drop the "FX_"/"MagicCircle" naming noise so ids read "circle.icearrow01".
        new ScanRoot("Assets/Eric VFX Studio/Game VFX - Magic Circle(Free)/Prefabs", "circle", new[] { "fx", "magiccircle" }),
        // NamuFX stylized water — flat pack, all water FX → "water.*" (e.g. Water_Splash_A → water.splash-a, the
        // puddle step splash WaterStep resolves by default).
        new ScanRoot("Assets/NamuFX/StylizedWaterEffects/Prefabs", "water"),
    };

    const string LibraryPath = "Assets/Resources/EffectLibrary.asset";

    // Category subfolder → id prefix (for nested roots). A folder not listed falls back to the kebab of its name.
    static readonly Dictionary<string, string> CategoryPrefix = new Dictionary<string, string>
    {
        { "AoE effects", "aoe" },
        { "Character auras", "aura" },
        { "Environment", "env" },
        { "Hits and explosions", "hit" },
        { "Magic circles", "circle" },
        { "Magic shields", "shield" },
        { "Portals", "portal" },
        { "Slash effects", "slash" },
        { "Smoke effects", "smoke" },
        { "Sparks", "spark" },
    };

    [MenuItem("Tools/unwritten/Art/Rebuild Effect Library", priority = 624)]
    public static void Rebuild()
    {
        string summary = RebuildCore();
        EditorUtility.DisplayDialog("Rebuild Effect Library", summary, "OK");
        var built = AssetDatabase.LoadAssetAtPath<EffectLibrary>(LibraryPath);
        if (built != null) Selection.activeObject = built;
    }

    // The scan/build itself, dialog-free so it can run headless (bridge/automation). Returns a text summary.
    public static string RebuildCore()
    {
        var missing = new List<string>();
        foreach (var r in Roots) if (!Directory.Exists(r.Path)) missing.Add(r.Path);
        if (missing.Count == Roots.Length)
            return "No prefab folders found:\n" + string.Join("\n", missing);

        var lib = AssetDatabase.LoadAssetAtPath<EffectLibrary>(LibraryPath);
        if (lib == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath));
            lib = ScriptableObject.CreateInstance<EffectLibrary>();
            AssetDatabase.CreateAsset(lib, LibraryPath);
        }

        // Keep the existing entries so hand-tuned scale/lifetime + renamed ids survive a rebuild. Index them by
        // the prefab they point at (the stable identity) and by id (to dedupe generated ids).
        var byPrefab = new Dictionary<GameObject, EffectDef>();
        var usedIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var kept = new List<EffectDef>();
        var dropped = new List<string>();
        foreach (var e in lib.effects)
        {
            if (e == null) continue;
            if (e.prefab == null) { dropped.Add(string.IsNullOrEmpty(e.id) ? "(unnamed)" : e.id); continue; }
            byPrefab[e.prefab] = e;
            if (!string.IsNullOrWhiteSpace(e.id)) usedIds.Add(e.id);
            kept.Add(e);
        }

        int added = 0;
        var addedIds = new List<string>();
        foreach (var root in Roots)
        {
            if (!Directory.Exists(root.Path)) continue;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { root.Path }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || byPrefab.ContainsKey(prefab)) continue;

                string id = Unique(MakeId(path, root), usedIds);
                usedIds.Add(id);
                byPrefab[prefab] = null;   // guard against the same prefab matching two overlapping roots
                kept.Add(new EffectDef { id = id, prefab = prefab, scale = 1f, lifetime = 0f });
                addedIds.Add(id);
                added++;
            }
        }

        kept.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        lib.effects = kept;
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        EffectCatalog.Reload();

        var sb = new StringBuilder();
        sb.AppendLine($"Effect library: {kept.Count} effects total.");
        sb.AppendLine($"Added {added} new; kept {kept.Count - added} existing (ids/tweaks preserved).");
        if (dropped.Count > 0) sb.AppendLine($"Dropped {dropped.Count} with a missing prefab: {string.Join(", ", dropped)}");
        if (added > 0) sb.AppendLine($"\nNew ids:\n  {string.Join("\n  ", addedIds)}");
        Debug.Log("[EffectLibraryBuilder] " + sb);
        return sb.ToString();
    }

    // "…/Hits and explosions/Explosion.prefab" → "hit.explosion". Prefix is the root's FixedPrefix (flat pack)
    // or, for a nested root, the immediate parent folder mapped via CategoryPrefix. The name is kebab-cased with
    // any token equal to the prefix stripped (so "AoE slash blue" → "aoe.slash-blue").
    static string MakeId(string assetPath, ScanRoot root)
    {
        string prefix;
        if (!string.IsNullOrEmpty(root.FixedPrefix)) prefix = root.FixedPrefix;
        else if (root.ByFirstSubfolder)
        {
            // Prefix = the first path segment under the convention root. A prefab dropped straight into the root
            // with no prefix folder falls back to "vfx" (still valid; just un-namespaced).
            string rel = assetPath.Substring(root.Path.Length).TrimStart('/');
            int slash = rel.IndexOf('/');
            string sub = slash > 0 ? rel.Substring(0, slash) : null;
            prefix = string.IsNullOrEmpty(sub) ? "vfx"
                   : CategoryPrefix.TryGetValue(sub, out var sp) ? sp : Kebab(sub);
        }
        else
        {
            string category = Path.GetFileName(Path.GetDirectoryName(assetPath));
            prefix = CategoryPrefix.TryGetValue(category, out var p) ? p : Kebab(category);
        }
        // Drop the prefix token (so "aoe slash" → "aoe.slash") plus any source-specific naming noise for this root.
        var drop = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase) { prefix };
        if (root.ExtraDrop != null) foreach (var d in root.ExtraDrop) drop.Add(d);
        string name = Path.GetFileNameWithoutExtension(assetPath);
        string body = Kebab(name, drop);
        return string.IsNullOrEmpty(body) ? prefix : $"{prefix}.{body}";
    }

    // Lowercase, split on non-alphanumerics, drop any token in `drop`, join with '-'.
    static string Kebab(string s, HashSet<string> drop = null)
    {
        var tokens = new List<string>();
        var cur = new StringBuilder();
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c)) cur.Append(char.ToLowerInvariant(c));
            else if (cur.Length > 0) { Flush(cur, tokens, drop); }
        }
        Flush(cur, tokens, drop);
        return string.Join("-", tokens);
    }

    static void Flush(StringBuilder cur, List<string> tokens, HashSet<string> drop)
    {
        if (cur.Length == 0) return;
        string t = cur.ToString();
        cur.Clear();
        if (drop == null || !drop.Contains(t)) tokens.Add(t);
    }

    static string Unique(string id, HashSet<string> used)
    {
        if (!used.Contains(id)) return id;
        for (int n = 2; ; n++)
        {
            string candidate = $"{id}-{n}";
            if (!used.Contains(candidate)) return candidate;
        }
    }
}
