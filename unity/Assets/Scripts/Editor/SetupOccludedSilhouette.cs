using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// One-click registration for the x-ray occlusion silhouette. <see cref="OccludedSilhouetteFeature"/> only runs
/// once it's added to the project's URP renderer(s); this menu finds every <see cref="UniversalRendererData"/> and
/// adds the feature as a sub-asset if it isn't there yet, and makes sure the silhouette shader survives a player
/// build. Idempotent — safe to re-run. (Mirrors <c>SetupHandDrawnGrade</c>.)
/// </summary>
public static class SetupOccludedSilhouette
{
    [MenuItem("Tools/unwritten/Scene Setup/Setup Occluded Silhouette", priority = 123)]
    public static void Setup()
    {
        var sh = Shader.Find("Unwritten/OccludedSilhouette");
        if (sh != null) EnsureAlwaysIncluded(sh);

        var guids = AssetDatabase.FindAssets("t:UniversalRendererData");
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("Occluded Silhouette",
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

            var feature = ScriptableObject.CreateInstance<OccludedSilhouetteFeature>();
            feature.name = "OccludedSilhouetteFeature";
            feature.SetActive(true);
            AssetDatabase.AddObjectToAsset(feature, data);

            var so   = new SerializedObject(data);
            var list = so.FindProperty("m_RendererFeatures");
            var map  = so.FindProperty("m_RendererFeatureMap");
            int i = list.arraySize;
            list.InsertArrayElementAtIndex(i);
            list.GetArrayElementAtIndex(i).objectReferenceValue = feature;
            map.InsertArrayElementAtIndex(i);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);
            map.GetArrayElementAtIndex(i).longValue = localId;
            so.ApplyModifiedProperties();

            EditorUtility.SetDirty(data);
            added++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Occluded Silhouette",
            $"Added to {added} renderer(s); {already} already had it.\n\n"
            + "The player needs an OccludedSilhouetteTarget component (Build Player adds it; "
            + "'Add Occluded Silhouette to Player' tags an existing one). Press Play and walk behind a building.",
            "OK");
    }

    [MenuItem("Tools/unwritten/Player/Add Occluded Silhouette to Player", priority = 210)]
    public static void AddToPlayer()
    {
        // The visible model is the Character3DVisual object (a SEPARATE object riding the controller); the player
        // ROOT only holds the hidden sprite, so the marker must go on the visual or it flags nothing you can see.
        var visual = Object.FindFirstObjectByType<Character3DVisual>();
        var host = visual != null ? visual.gameObject
                 : Object.FindFirstObjectByType<Player3DController>()?.gameObject;
        if (host == null)
        {
            EditorUtility.DisplayDialog("Occluded Silhouette",
                "No player visual found in the open scene(s). Open the scene with the player (Systems) first.", "OK");
            return;
        }
        if (host.GetComponent<OccludedSilhouetteTarget>() == null)
        {
            Undo.AddComponent<OccludedSilhouetteTarget>(host);
            EditorUtility.SetDirty(host);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(host.scene);
        }
        EditorUtility.DisplayDialog("Occluded Silhouette",
            $"'{host.name}' is tagged. Save the scene (Ctrl+S) to keep it, then Play and walk behind a building.", "OK");
    }

    // Register a shader in Project Settings ▸ Graphics ▸ Always Included Shaders (idempotent) so a shader the
    // renderer only references via a runtime-created material isn't stripped from a player build.
    static void EnsureAlwaysIncluded(Shader shader)
    {
        var gs = GraphicsSettings.GetGraphicsSettings();
        var so = new SerializedObject(gs);
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        for (int i = 0; i < arr.arraySize; i++)
            if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
        int idx = arr.arraySize;
        arr.InsertArrayElementAtIndex(idx);
        arr.GetArrayElementAtIndex(idx).objectReferenceValue = shader;
        so.ApplyModifiedProperties();
    }

    static bool HasFeature(UniversalRendererData data)
    {
        if (data.rendererFeatures == null) return false;
        foreach (var f in data.rendererFeatures)
            if (f is OccludedSilhouetteFeature) return true;
        return false;
    }
}
