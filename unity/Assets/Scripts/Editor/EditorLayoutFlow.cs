using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared IMGUI layout helpers for editor windows so they behave like responsive web UI:
/// a CSS-like <b>flex-wrap</b> toolbar, a <b>responsive label column</b> that shrinks with the
/// window, and a <b>no-minimum draggable splitter</b>. Centralized here (one choke point) so every
/// composer/tool gets the same behavior instead of each re-implementing it.
/// </summary>
public static class EditorLayoutFlow
{
    /// <summary>
    /// A toolbar row that wraps: controls keep their readable fixed widths, and when the next item
    /// (or a kept-together label+field pair) won't fit on the current line, the row is closed and a new
    /// one opens below — exactly CSS <c>flex-wrap</c>, hand-rolled for IMGUI.
    /// <code>
    /// using (var flow = new EditorLayoutFlow.Flow(contentWidth, EditorStyles.toolbar))
    /// {
    ///     flow.Item(80); if (GUILayout.Button("Play", GUILayout.Width(80))) { ... }
    ///     flow.Item(52 + 45); GUILayout.Label("loop end", GUILayout.Width(52)); field(GUILayout.Width(45));
    /// }
    /// </code>
    /// Call <see cref="Item"/> with a control's (or grouped pair's) pixel width BEFORE drawing it.
    /// </summary>
    public class Flow : IDisposable
    {
        readonly float avail;
        readonly GUIStyle rowStyle;
        float used;

        public Flow(float availableWidth, GUIStyle rowStyle = null)
        {
            avail = availableWidth - 8f;   // padding / scrollbar gutter
            this.rowStyle = rowStyle;
            BeginRow();
        }

        void BeginRow()
        {
            if (rowStyle != null) EditorGUILayout.BeginHorizontal(rowStyle);
            else EditorGUILayout.BeginHorizontal();
            used = 0f;
        }

        public void Item(float width)
        {
            if (used > 0.01f && used + width > avail)
            {
                EditorGUILayout.EndHorizontal();
                BeginRow();
            }
            used += width + 2f;   // + inter-item spacing
        }

        public void Dispose() => EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// Shrink the label column with the available content width so labelled rows collapse instead of
    /// the label column holding a fixed ~150px floor. Never exceeds <paramref name="max"/>.
    /// </summary>
    public static void ResponsiveLabelWidth(float availableWidth, float max = 150f, float fraction = 0.4f)
        => EditorGUIUtility.labelWidth = Mathf.Clamp(availableWidth * fraction, 0f, max);

    /// <summary>
    /// A vertical divider the user drags to resize a side panel — no minimum (drags to 0). Returns the
    /// new panel width; the caller stores it and calls <see cref="EditorWindow.Repaint"/> while dragging.
    /// </summary>
    public static float VerticalSplitter(float currentWidth, ref bool dragging)
    {
        var rect = GUILayoutUtility.GetRect(5f, 5f, GUILayout.Width(5f), GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.25f));
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

        var e = Event.current;
        if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
        { dragging = true; e.Use(); }
        else if (e.type == EventType.MouseUp)
            dragging = false;
        else if (e.type == EventType.MouseDrag && dragging)
        {
            currentWidth = Mathf.Max(0f, e.mousePosition.x - rect.width * 0.5f);
            e.Use();
        }
        return currentWidth;
    }
}
