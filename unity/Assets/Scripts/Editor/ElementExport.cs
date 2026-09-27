using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes the wiki's element chart — <c>wiki/content/elements.generated.json</c> — by asking
/// <see cref="Elements.Modifier"/> for every attack element against every defending element at every level, so the
/// wiki prints exactly the chart the game hits with, and nothing else ever writes it.
///
/// <para><b>When it runs</b> is decided in one place for every Database page — <see cref="WikiExport"/>, which includes
/// every recompile, since the chart lives in code. It writes one file in the wiki folder and touches nothing Unity
/// saves.</para>
/// </summary>
public static class ElementExport
{
    static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
    public static string WikiPath => Path.Combine(RepoRoot, "wiki", "content", "elements.generated.json");

    [Serializable] class Export { public string note; public string[] elements; public Level[] levels; }
    [Serializable] class Level { public int level; public Row[] rows; }
    // percent[i] = this attack element against elements[i], in whole percent; below zero heals the defender.
    [Serializable] class Row { public string attack; public int[] percent; }

    public static void Write()
    {
        var names = new string[Elements.Count];
        for (int e = 0; e < Elements.Count; e++) names[e] = Elements.Name((Element)e);

        var levels = new Level[Elements.MaxLevel];
        for (int lv = 1; lv <= Elements.MaxLevel; lv++)
        {
            var rows = new Row[Elements.Count];
            for (int a = 0; a < Elements.Count; a++)
            {
                var pct = new int[Elements.Count];
                for (int d = 0; d < Elements.Count; d++)
                    pct[d] = Mathf.RoundToInt(Elements.Modifier((Element)a, (Element)d, lv) * 100f);
                rows[a] = new Row { attack = names[a], percent = pct };
            }
            levels[lv - 1] = new Level { level = lv, rows = rows };
        }

        var export = new Export
        {
            note = "GENERATED from the game's element chart (Elements.Modifier) by the editor (WikiExport), after every " +
                   "recompile. Do NOT hand-edit: the next recompile overwrites this file.",
            elements = names,
            levels = levels,
        };
        SkillExport.WriteIfChanged(WikiPath, JsonUtility.ToJson(export, true) + "\n");
    }
}
