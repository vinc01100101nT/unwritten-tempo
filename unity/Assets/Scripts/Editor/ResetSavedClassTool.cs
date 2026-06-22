using UnityEditor;
using UnityEngine;

/// <summary>
/// Dev convenience: forget the saved player class (the <c>unwritten.class</c> PlayerPref) so the game
/// boots as if it were a fresh install — falling back to <see cref="ClassState.Default"/> (Mage). Use this
/// to verify the default-class behaviour on a machine where you've already picked a class in a past session
/// (that saved choice otherwise wins over the default). Also works in Play mode (takes effect next boot).
/// </summary>
public static class ResetSavedClassTool
{
    [MenuItem("Tools/unwritten/Reset Saved Class", priority = 400)]
    static void Reset()
    {
        ClassState.Reset();
        Debug.Log($"[unwritten] Saved class cleared — next boot starts as the default ({ClassState.Default}).");
        EditorUtility.DisplayDialog(
            "Reset Saved Class",
            $"Cleared the saved class.\n\nThe game will now start as the default: {ClassState.Default}.\n\n" +
            "Press Play (or relaunch a build) to see it.",
            "OK");
    }
}
