using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A read-only reference view of the two skill-mutation systems: the ring's build-wide <b>voices</b>
/// (<see cref="RingVoices"/>) and the weapon-bound <b>affixes</b> (<see cref="WeaponAffixes"/>). One row per entry
/// with what it does, its scope, and (for voices) the stat that speaks it. Pure reference — the data is hardcoded in
/// C#, so nothing here is editable (unlike the Mob/Item tables, which edit ScriptableObjects). Reads straight from the
/// live catalogs via their public accessors, so it can never drift from the real balance data.
///
/// Open via <c>Tools ▸ unwritten ▸ Catalogs ▸ Affix &amp; Ring Voice Table</c>.
/// </summary>
public class AffixVoiceTableWindow : EditorWindow
{
    const string MenuPath = "Tools/unwritten/Catalogs/Affix & Ring Voice Table";
    const float RowHeight = 26f;

    [MenuItem(MenuPath)]
    static void Open()
    {
        var w = GetWindow<AffixVoiceTableWindow>("Affix & Voice");
        w.minSize = new Vector2(560, 360);
        w.Reload();
    }

    RingVoices.VoiceRow[] _voices;
    List<WeaponAffixes.AffixRow> _affixes;
    Vector2 _scroll;
    GUIStyle _effectStyle, _sectionStyle;

    void Reload()
    {
        _voices = RingVoices.AllVoices();
        _affixes = WeaponAffixes.AllAffixes();
        Repaint();
    }

    struct Col { public string label; public float w; public Col(string l, float wi) { label = l; w = wi; } }
    static readonly Col[] VoiceCols =
    {
        new Col("STAT",   90),
        new Col("Voice",  100),
        new Col("Scope",  190),
        new Col("Act %",  48),
        new Col("Effect", 380),
    };
    static readonly Col[] AffixCols =
    {
        new Col("id",     130),
        new Col("Name",   110),
        new Col("Kind",   90),
        new Col("Effect", 380),
    };

    void EnsureStyles()
    {
        if (_effectStyle == null)
            _effectStyle = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = false, alignment = TextAnchor.MiddleLeft };
        if (_sectionStyle == null)
            _sectionStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
    }

    void OnGUI()
    {
        if (_voices == null) Reload();
        EnsureStyles();

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            GUILayout.Label($"{_voices.Length} ring voices · {_affixes.Count} weapon affixes", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(60))) Reload();
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        GUILayout.Space(6);
        GUILayout.Label("Ring Voices — from stat investment, build-wide (worn ring required)", _sectionStyle);
        GUILayout.Label("Fire on your own casts; \"Outgoing\" ones only show on skills that hit mobs.", EditorStyles.miniLabel);
        GUILayout.Space(2);
        DrawHeader(VoiceCols);
        for (int i = 0; i < _voices.Length; i++)
        {
            var v = _voices[i];
            DrawRow(VoiceCols, i, v.Stat, v.Name, v.Scope, v.ActivatePercent.ToString("0") + "%", v.Effect);
        }

        GUILayout.Space(16);
        GUILayout.Label("Weapon Affixes — baked onto a weapon at loot, gone when unequipped", _sectionStyle);
        GUILayout.Label("Never duplicate a voice's job (no execute/repeat/expand affixes) — control + raw damage + utility only.", EditorStyles.miniLabel);
        GUILayout.Space(2);
        DrawHeader(AffixCols);
        for (int i = 0; i < _affixes.Count; i++)
        {
            var a = _affixes[i];
            DrawRow(AffixCols, i, a.Id, a.Name, a.Kind, null, a.Effect);
        }

        GUILayout.Space(8);
        EditorGUILayout.EndScrollView();
    }

    void DrawHeader(Col[] cols)
    {
        var bar = EditorGUILayout.GetControlRect(false, 18);
        float x = bar.x;
        foreach (var c in cols)
        {
            GUI.Label(new Rect(x, bar.y, c.w - 4, bar.height), c.label, EditorStyles.miniBoldLabel);
            x += c.w;
        }
    }

    void DrawRow(Col[] cols, int index, params string[] cells)
    {
        var rect = EditorGUILayout.GetControlRect(false, RowHeight);
        if (index % 2 == 0) EditorGUI.DrawRect(rect, new Color(1, 1, 1, 0.03f));

        float x = rect.x;
        for (int ci = 0; ci < cols.Length; ci++)
        {
            var r = new Rect(x, rect.y, cols[ci].w - 4, rect.height);
            x += cols[ci].w;
            string text = ci < cells.Length ? cells[ci] : null;
            if (text == null) continue;
            // Last column is the effect (rich-text); others are plain.
            GUI.Label(r, text, ci == cols.Length - 1 ? _effectStyle : EditorStyles.label);
        }
    }
}
