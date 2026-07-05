using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// One-click registration for the hand-drawn colour grade. <see cref="HandDrawnGradeFeature"/> only runs once it's
/// added to the project's URP renderer(s); doing that by hand in the inspector is easy to forget, so this menu finds
/// every <see cref="UniversalRendererData"/> in the project and adds the feature as a sub-asset if it isn't there
/// yet. Idempotent — safe to re-run; it skips renderers that already have it. After this, the "Saturation"/"Contrast"
/// knobs in DevSettings ▸ Hand-Drawn take effect.
/// </summary>
public static class SetupHandDrawnGrade
{
    [MenuItem("Tools/unwritten/Scene Setup/Setup Hand-Drawn Grade", priority = 122)]
    public static void Setup()
    {
        var guids = AssetDatabase.FindAssets("t:UniversalRendererData");
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("Hand-Drawn Grade",
                "No URP UniversalRendererData found in the project.", "OK");
            return;
        }

        int added = 0, already = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (data == null) continue;

            if (HasFeature(data)) { already++; continue; }

            var feature = ScriptableObject.CreateInstance<HandDrawnGradeFeature>();
            feature.name = "HandDrawnGradeFeature";
            feature.SetActive(true);
            AssetDatabase.AddObjectToAsset(feature, data);

            // Wire it into the renderer's serialized feature list + the parallel id map URP keeps.
            var so   = new SerializedObject(data);
            var list = so.FindProperty("m_RendererFeatures");
            var map  = so.FindProperty("m_RendererFeatureMap");
            int i = list.arraySize;
            list.InsertArrayElementAtIndex(i);
            list.GetArrayElementAtIndex(i).objectReferenceValue = feature;
            map.InsertArrayElementAtIndex(i);
            // The map stores each feature's local file id; fetch it now that the sub-asset exists.
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
            map.GetArrayElementAtIndex(i).longValue = localId;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(data);
            added++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[HandDrawnGrade] Added to {added} renderer(s); {already} already had it. " +
                  "Press Play and drag DevSettings ▸ Hand-Drawn ▸ Saturation.");
    }

    static bool HasFeature(UniversalRendererData data)
    {
        if (data.rendererFeatures == null) return false;
        foreach (var f in data.rendererFeatures)
            if (f is HandDrawnGradeFeature) return true;
        return false;
    }
}
