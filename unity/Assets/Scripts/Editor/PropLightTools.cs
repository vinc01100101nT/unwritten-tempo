using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click lamp authoring: drops a warm <see cref="PropLight"/> on each selected prop (door, torch,
/// brazier, portal) so nearby billboard sprites brighten and take the light's colour. The light is a
/// child at a small upward offset (idempotent — re-running reuses the existing "PropLight" child rather
/// than stacking duplicates). Use the inspector on the new light to retint, set range/intensity, or
/// enable Flicker for a torch.
/// </summary>
public static class PropLightTools
{
    [MenuItem("Tools/unwritten/World/Add Prop Light to Selection", priority = 233)]
    public static void AddToSelection()
    {
        var sel = Selection.gameObjects;
        if (sel == null || sel.Length == 0)
        {
            EditorUtility.DisplayDialog("Add Prop Light",
                "Select one or more props in the scene (the door, a torch, a portal) first, then run this.\n\n" +
                "A warm point light is added as a child of each — tweak colour/range on it afterwards.", "OK");
            return;
        }

        Color warm = new Color(1f, 0.72f, 0.36f);
        foreach (var go in sel)
        {
            var pl = PropLight.Attach(go.transform, new Vector3(0f, 1.2f, 0f), warm, 4.5f, 3f);
            EditorUtility.SetDirty(pl);
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        Debug.Log($"[unwritten] Added a PropLight to {sel.Length} prop(s). Select the new 'PropLight' child to " +
                  $"retint, set range/intensity, or enable Flicker (torch). Press Play to see sprites pick it up.");
    }
}
