// TEMPORARY DIAGNOSTIC — delete once the "Modified Scenes must be saved" cause is found (2026-10-09).
// Reads only: logs who marks a scene modified, so it survives the script reload that entering Play does.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
static class DirtySceneProbe
{
    const string Tag = "[dirty-probe] ";

    static DirtySceneProbe()
    {
        EditorSceneManager.sceneDirtied += s =>
            Debug.LogWarning(Tag + "SCENE BECAME DIRTY: " + s.name + " | play=" + EditorApplication.isPlayingOrWillChangePlaymode
                             + "\n" + System.Environment.StackTrace);
        Undo.postprocessModifications += mods =>
        {
            foreach (var m in mods)
            {
                var pm = m.currentValue;
                if (pm == null || pm.target == null) continue;
                if (pm.propertyPath.StartsWith("m_AnchoredPosition") || pm.target.name == "Content")
                    Debug.LogWarning(Tag + "UNDO-RECORDED WRITE: " + pm.target.name + "." + pm.propertyPath + " = " + pm.value
                                     + "\n" + System.Environment.StackTrace);
            }
            return mods;
        };
        EditorApplication.playModeStateChanged += st => Debug.Log(Tag + "play state " + st + " | " + DirtyList());
        EditorSceneManager.sceneSaved += s => Debug.Log(Tag + "saved " + s.name + " | " + DirtyList());
        Debug.Log(Tag + "armed after script reload | " + DirtyList());
    }

    static string DirtyList()
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
        {
            var s = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
            sb.Append(s.name).Append(s.isDirty ? "=DIRTY " : "=clean ");
        }
        return sb.ToString();
    }
}
