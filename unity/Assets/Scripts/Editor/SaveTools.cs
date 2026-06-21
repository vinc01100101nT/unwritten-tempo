using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor helpers for the M1 save slice — for testing the load/continue flow without hunting through
/// the OS file system. "Delete Save File" gives you a clean "new game"; "Open Save Folder" reveals the
/// JSON; "Print Save Path" logs the absolute path to the Console.
/// </summary>
public static class SaveTools
{
    [MenuItem("Tools/unwritten/Save/Delete Save File (new game)", priority = 900)]
    static void DeleteSave()
    {
        if (!SaveSystem.Exists())
        {
            EditorUtility.DisplayDialog("unwritten", "No save file to delete — already a fresh start.", "OK");
            return;
        }
        if (EditorUtility.DisplayDialog("Delete save?",
                $"Permanently delete the character save?\n\n{SaveSystem.Path}", "Delete", "Cancel"))
        {
            SaveSystem.Delete();
            Debug.Log("[unwritten] Save deleted — next Play starts a fresh level-1 character.");
        }
    }

    [MenuItem("Tools/unwritten/Save/Open Save Folder", priority = 901)]
    static void OpenFolder() => EditorUtility.RevealInFinder(SaveSystem.Path);

    [MenuItem("Tools/unwritten/Save/Print Save Path", priority = 902)]
    static void PrintPath()
        => Debug.Log($"[unwritten] Save path: {SaveSystem.Path}  (exists: {SaveSystem.Exists()})");
}
