using UnityEngine;

/// <summary>
/// Ending this process, said in one place. There are two ways to stop and which one is right depends on where
/// the game is running, not on who asked it to: in the editor there is no application to quit, only Play mode to
/// switch off, and in a built game it is the other way round. Every route out — a player pressing <b>Quit
/// game</b>, a server refusing to start — comes through here, so neither can be written with only half of that.
/// </summary>
public static class GameExit
{
    /// <summary>Stop now. <paramref name="exitCode"/> is what a built game hands back to whatever started it:
    /// 0 for an ordinary ending, non-zero for a failure. It means nothing in the editor, which has no exit code
    /// to give.</summary>
    public static void Quit(int exitCode = 0)
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit(exitCode);
#endif
    }
}
