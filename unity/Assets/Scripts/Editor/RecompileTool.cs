using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

public static class RecompileTool
{
    // %#r = Ctrl/Cmd + Shift + R
    [MenuItem("Tools/unwritten/Debug/Recompile Scripts %#r", priority = 810)]
    public static void Recompile()
    {
        AssetDatabase.Refresh();
        CompilationPipeline.RequestScriptCompilation();
        Debug.Log("[unwritten] Script recompilation requested.");
    }
}
