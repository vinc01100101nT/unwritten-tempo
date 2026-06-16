using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Theme picker for the UI shell. Lists every Ninja Adventure UI theme that has a
/// nine_path_panel, shows a live preview of the selected frame, lets you choose a
/// frame thickness, and rebuilds the in-scene UI shell with one click. Choice is
/// saved in EditorPrefs, so plain "Build UI Shell" reuses it.
///
/// Menu: Tools ▸ unwritten ▸ UI Shell Theme…
/// </summary>
public class UIShellWindow : EditorWindow
{
    static readonly string[] ScaleLabels = { "1× (thin)", "2× (chunky)", "3× (bold)" };
    static readonly int[] ScaleValues = { 16, 32, 48 };   // -> CanvasScaler.referencePixelsPerUnit

    List<string> themes;
    int themeIndex;
    int scaleIndex = 1;

    [MenuItem("Tools/unwritten/UI Shell Theme…")]
    static void Open()
    {
        var w = GetWindow<UIShellWindow>(true, "unwritten — UI Shell");
        w.minSize = new Vector2(360, 360);
        w.Refresh();
    }

    void Refresh()
    {
        themes = UIShellBuilder.DiscoverThemes();
        string saved = EditorPrefs.GetString(UIShellBuilder.PrefTheme, UIShellBuilder.DefaultTheme);
        themeIndex = Mathf.Max(0, themes.IndexOf(saved));

        int savedScale = EditorPrefs.GetInt(UIShellBuilder.PrefScale, UIShellBuilder.DefaultScale);
        scaleIndex = System.Array.IndexOf(ScaleValues, savedScale);
        if (scaleIndex < 0) scaleIndex = 1;
    }

    void OnGUI()
    {
        if (themes == null) Refresh();

        EditorGUILayout.LabelField("UI Theme", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Pick a skin and rebuild the in-scene UI shell. Only \"Theme Wood\" is a full kit; " +
            "other themes reuse Wood's slots/health-bar and just reskin the window frame.",
            MessageType.Info);

        var names = themes.ConvertAll(UIShellBuilder.ShortName).ToArray();
        themeIndex = EditorGUILayout.Popup("Theme", Mathf.Clamp(themeIndex, 0, names.Length - 1), names);
        scaleIndex = EditorGUILayout.Popup("Frame thickness", scaleIndex, ScaleLabels);

        // Live preview of the chosen frame sprite.
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(themes[themeIndex] + "/nine_path_panel.png");
        if (tex != null)
        {
            var r = GUILayoutUtility.GetRect(128, 128, GUILayout.ExpandWidth(true));
            r.width = r.height = 128;
            EditorGUI.DrawTextureTransparent(r, tex, ScaleMode.ScaleToFit);
        }

        GUILayout.Space(8);
        using (new EditorGUI.DisabledScope(themes.Count == 0))
        {
            if (GUILayout.Button("Build / Rebuild UI Shell", GUILayout.Height(34)))
            {
                EditorPrefs.SetString(UIShellBuilder.PrefTheme, themes[themeIndex]);
                EditorPrefs.SetInt(UIShellBuilder.PrefScale, ScaleValues[scaleIndex]);
                UIShellBuilder.Build(themes[themeIndex], ScaleValues[scaleIndex]);
            }
        }

        if (GUILayout.Button("Rescan themes")) Refresh();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("In-game keys", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField("I = Bag   ·   C = Character   ·   K = Skills   ·   Esc = close", EditorStyles.miniLabel);
    }
}
