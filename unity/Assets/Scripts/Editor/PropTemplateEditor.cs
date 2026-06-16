using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Inspector for <see cref="PropTemplate"/>. Adds a clickable footprint grid for editing the prop's
/// collision: click cells to toggle what blocks (turns the prop Custom), or use the mode dropdown
/// (Auto / Custom / None). None keeps your painted cells so you can switch back. Collision is pre-baked
/// into a merged collider at generate/stamp time — never generated at Play.
/// </summary>
[CustomEditor(typeof(PropTemplate))]
public class PropTemplateEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var p = (PropTemplate)target;

        // Everything except the raw cell lists (drawn as the grid below / hidden as noise).
        serializedObject.Update();
        DrawPropertiesExcluding(serializedObject, "m_Script", "cells", "collisionCells", "collisionMode");
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.LabelField("Art footprint", $"{p.cells.Count} cells", EditorStyles.miniLabel);

        DrawCollisionSection(p);
    }

    void DrawCollisionSection(PropTemplate p)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Collision", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        var newMode = (PropCollision)EditorGUILayout.EnumPopup(
            new GUIContent("Mode", "Auto = derived (buildings full, trees' bottom row) · " +
                                   "Custom = your painted/clicked cells · None = off (cells kept)"),
            p.collisionMode);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(p, "Change Collision Mode");
            p.collisionMode = newMode;
            EditorUtility.SetDirty(p);
        }

        if (p.size.x <= 0 || p.size.y <= 0)
        {
            EditorGUILayout.HelpBox("Capture the prop first to edit collision.", MessageType.Info);
            return;
        }

        var art = new HashSet<Vector2Int>();
        foreach (var c in p.cells) art.Add(c.pos);
        var effective = new HashSet<Vector2Int>(p.CollisionFootprint());

        EditorGUILayout.HelpBox(
            p.collisionMode == PropCollision.Auto
                ? "Auto: buildings block their whole footprint; trees block only their bottom row. " +
                  "Click a half-cell to start a Custom shape."
            : p.collisionMode == PropCollision.Custom
                ? "Custom: click half-cells to toggle what blocks. Red = blocks; grey = art with no collision."
                : "None: nothing blocks. Your painted cells are kept — switch to Custom to use them again.",
            MessageType.None);

        int gw = p.size.x * 2, gh = p.size.y * 2;   // half-cell resolution: 2× the prop's footprint
        EditorGUILayout.LabelField($"Grid = {gw}×{gh} half-cells (each ½ a world cell)", EditorStyles.miniLabel);

        if (gw * gh > 2048)
            EditorGUILayout.HelpBox($"Footprint {p.size.x}×{p.size.y} ({gw}×{gh} half-cells) is too large to show the grid.", MessageType.Warning);
        else
            DrawGrid(p, art, effective);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear → Auto"))
        {
            Undo.RecordObject(p, "Clear Prop Collision");
            p.collisionCells = new List<Vector2Int>();
            p.collisionMode = PropCollision.Auto;
            EditorUtility.SetDirty(p);
        }
        if (GUILayout.Button("Fill footprint → Custom"))
        {
            Undo.RecordObject(p, "Fill Prop Collision");
            var fill = new HashSet<Vector2Int>();
            foreach (var a in art) foreach (var h in PropTemplate.HalfCellsOf(a)) fill.Add(h);
            p.collisionCells = new List<Vector2Int>(fill);
            p.collisionMode = PropCollision.Custom;
            EditorUtility.SetDirty(p);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField($"Blocking cells: {effective.Count}", EditorStyles.miniLabel);
    }

    void DrawGrid(PropTemplate p, HashSet<Vector2Int> art, HashSet<Vector2Int> effective)
    {
        const float sz = 13f;   // smaller cells since the half-cell grid is 2× the footprint
        bool greyed = p.collisionMode == PropCollision.None;
        int gw = p.size.x * 2, gh = p.size.y * 2;

        for (int hy = gh - 1; hy >= 0; hy--)   // top row first = world orientation
        {
            EditorGUILayout.BeginHorizontal();
            for (int hx = 0; hx < gw; hx++)
            {
                var cell = new Vector2Int(hx, hy);                       // half-cell coord
                bool blocks = effective.Contains(cell);
                bool hasArt = art.Contains(new Vector2Int(hx / 2, hy / 2));   // art is full-cell

                Color bg = blocks ? (greyed ? new Color(0.5f, 0.3f, 0.3f) : new Color(0.85f, 0.25f, 0.25f))
                         : hasArt ? new Color(0.55f, 0.55f, 0.55f)
                                  : new Color(0.22f, 0.22f, 0.22f);

                var prev = GUI.backgroundColor;
                GUI.backgroundColor = bg;
                if (GUILayout.Button(GUIContent.none, GUILayout.Width(sz), GUILayout.Height(sz)))
                    ToggleCell(p, effective, cell);
                GUI.backgroundColor = prev;
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    void ToggleCell(PropTemplate p, HashSet<Vector2Int> effective, Vector2Int cell)
    {
        Undo.RecordObject(p, "Edit Prop Collision");
        if (p.collisionMode != PropCollision.Custom)
        {
            // Painting collision turns the prop Custom; seed from what's currently shown so you can
            // start from the Auto guess (or the kept cells) and tweak.
            p.collisionCells = new List<Vector2Int>(effective);
            p.collisionMode = PropCollision.Custom;
        }
        if (p.collisionCells.Contains(cell)) p.collisionCells.Remove(cell);
        else p.collisionCells.Add(cell);
        EditorUtility.SetDirty(p);
    }
}
