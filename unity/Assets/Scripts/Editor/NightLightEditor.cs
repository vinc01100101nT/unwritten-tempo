using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene-view drag handle for <see cref="NightLight.localOffset"/>. Positioning a lamp by typing the Local
/// Offset numbers is miserable — they're in the prop's LOCAL space and multiplied by its scale, so on a
/// scale-4 building every "1" is 4 world units and you're solving math blind. This draws a real move handle
/// at the light's world position: grab it, drop it on the lamp, done. Works in edit mode (persists; no Play
/// needed) and shows the range as a wire sphere so you can see the reach while you place it.
/// </summary>
[CustomEditor(typeof(NightLight))]
public class NightLightEditor : Editor
{
    void OnSceneGUI()
    {
        var nl = (NightLight)target;
        var tr = nl.transform;

        Vector3 world = tr.TransformPoint(nl.localOffset);

        // Range ring (range is WORLD units in the component, so draw it in world space).
        Handles.color = new Color(nl.color.r, nl.color.g, nl.color.b, 0.9f);
        Handles.DrawWireDisc(world, Vector3.up, nl.range);
        Handles.DrawWireDisc(world, Vector3.right, nl.range);
        Handles.DrawWireDisc(world, Vector3.forward, nl.range);
        Handles.Label(world + Vector3.up * 0.3f, "NightLight");

        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.PositionHandle(world, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(nl, "Move Night Light");
            nl.localOffset = tr.InverseTransformPoint(moved);
            EditorUtility.SetDirty(nl);
        }
    }
}
