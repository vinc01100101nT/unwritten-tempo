using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Pure file IO for the save slot: <see cref="SaveData"/> ⇄ a JSON file under
/// <see cref="Application.persistentDataPath"/>. No Character/game knowledge lives here — the
/// orchestration (when to save, applying data to the live character) is <see cref="SaveManager"/>'s job.
///
/// All operations are wrapped so a corrupt/locked file degrades to a warning + a fresh start rather
/// than throwing into gameplay.
/// </summary>
public static class SaveSystem
{
    const string FileName = "unwritten_save.json";

    /// <summary>Absolute path of the save file (platform-correct: AppData on Windows, etc.).</summary>
    public static string Path => System.IO.Path.Combine(Application.persistentDataPath, FileName);

    public static bool Exists() => File.Exists(Path);

    public static void Write(SaveData data)
    {
        if (data == null) return;
        try
        {
            File.WriteAllText(Path, JsonUtility.ToJson(data, prettyPrint: true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[unwritten] Save failed: {e.Message}");
        }
    }

    public static SaveData Read()
    {
        try
        {
            if (!Exists()) return null;
            var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(Path));
            // A future format bump would branch on data.version here.
            return data;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[unwritten] Load failed ({e.Message}); starting fresh.");
            return null;
        }
    }

    public static void Delete()
    {
        try { if (Exists()) File.Delete(Path); }
        catch (Exception e) { Debug.LogWarning($"[unwritten] Delete save failed: {e.Message}"); }
    }
}
