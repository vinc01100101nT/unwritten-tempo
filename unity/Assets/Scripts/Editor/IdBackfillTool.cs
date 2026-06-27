using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// QUICKFIX 2026-06-27 — one-shot: backfill the new stable kebab `id` on PRE-EXISTING Item + MapRecipe
// assets that serialized before the field existed (they load with id="" since Unity doesn't apply C#
// field defaults to old assets). Delete after every Item/MapRecipe asset has a non-empty id.
public static class IdBackfillTool
{
    [MenuItem("Tools/unwritten/Quick Fixes/Backfill Item + Map IDs", priority = 2001)]
    static void Backfill()
    {
        var sb = new StringBuilder();
        var seen = new HashSet<string>();
        int filled = 0, ok = 0;

        filled += Pass<Item>("item", i => i.id, (i, v) => i.id = v, i => i.displayName, seen, sb, ref ok);
        filled += Pass<MapRecipe>("map", m => m.id, (m, v) => m.id = v, m => m.mapName, seen, sb, ref ok);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[unwritten] ID backfill: {filled} filled, {ok} already had ids.\n{sb}");
        EditorUtility.DisplayDialog("Backfill Item + Map IDs",
            $"{filled} asset(s) got a fresh stable id.\n{ok} already had one (left untouched).\n\n" +
            "Quests/loot/recipes can now reference these by id. See Console for the full list.", "OK");
    }

    static int Pass<T>(string prefix, System.Func<T, string> getId, System.Action<T, string> setId,
        System.Func<T, string> getName, HashSet<string> seen, StringBuilder log, ref int ok) where T : Object
    {
        int filled = 0;
        foreach (var guid in AssetDatabase.FindAssets($"t:{typeof(T).Name}"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) continue;

            var cur = getId(asset);
            bool needs = string.IsNullOrWhiteSpace(cur) || cur == $"{prefix}.new";
            if (!needs)
            {
                if (!seen.Add(cur)) log.AppendLine($"  ⚠ DUPLICATE id '{cur}' → {path}");
                ok++;
                continue;
            }

            var id = Unique($"{prefix}.{Kebab(getName(asset))}", seen);
            setId(asset, id);
            EditorUtility.SetDirty(asset);
            log.AppendLine($"  • {id} → {path}");
            filled++;
        }
        return filled;
    }

    static string Unique(string baseId, HashSet<string> seen)
    {
        var id = baseId;
        int n = 2;
        while (!seen.Add(id)) id = $"{baseId}-{n++}";
        return id;
    }

    static string Kebab(string s)
    {
        if (string.IsNullOrEmpty(s)) return "unnamed";
        var sb = new StringBuilder(s.Length);
        bool lastDash = false;
        foreach (char c in s.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(c); lastDash = false; }
            else if (!lastDash) { sb.Append('-'); lastDash = true; }
        }
        return sb.ToString().Trim('-');
    }
}
