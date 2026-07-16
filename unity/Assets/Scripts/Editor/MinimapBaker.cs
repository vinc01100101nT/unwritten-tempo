using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Author-time tool that snapshots the ACTIVE map scene from straight above into a flat PNG, then drops
/// (or updates) a <see cref="MinimapData"/> on a scene root so the HUD <see cref="Minimap"/> shows that
/// chart instead of running a live second camera. Re-run any time the map's layout changes.
///
/// Flow: encapsulate every renderer's bounds → square them up (+margin) → fly a temporary orthographic
/// camera over the centre looking down → render to a RenderTexture (URP render-request) → read back to a
/// Texture2D → save <c>Assets/2_Generated/Minimaps/&lt;Scene&gt;.png</c> → import it (clamp, no mips) → write the
/// image + world rect onto the scene's <see cref="MinimapData"/> → save the scene.
///
/// The map's own player isn't in the scene at author-time (it lives in Systems), so the snapshot is clean.
/// </summary>
public static class MinimapBaker
{
    const string OutDir = "Assets/2_Generated/Minimaps";
    const int Resolution = 1024;        // baked PNG edge (px)
    const float MarginFrac = 0.04f;     // breathing room around the map bounds

    [MenuItem("Tools/unwritten/World/Bake Minimap (active map)", priority = 130)]
    public static void BakeActiveScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Bake Minimap", "Exit Play mode first, then re-run.", "OK"); return; }

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || string.IsNullOrEmpty(scene.path))
        { EditorUtility.DisplayDialog("Bake Minimap", "Open and select a saved map scene first.", "OK"); return; }
        if (scene.name == MapManager.SystemsScene)
        { EditorUtility.DisplayDialog("Bake Minimap", "The active scene is Systems — select a content MAP scene instead.", "OK"); return; }

        if (!TryComputeBounds(scene, out Bounds b))
        { EditorUtility.DisplayDialog("Bake Minimap", "No renderers found in this scene to snapshot.", "OK"); return; }

        // Square footprint (+margin) so X and Z share one scale → fixed-north UV mapping is trivial.
        float extent = Mathf.Max(b.size.x, b.size.z) * 0.5f;
        extent *= 1f + MarginFrac;
        float worldSize = extent * 2f;
        var worldMin = new Vector2(b.center.x - extent, b.center.z - extent);

        var tex = Snapshot(b, extent);
        string path = SaveTexture(tex, scene.name);
        Object.DestroyImmediate(tex);

        var image = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        ApplyData(scene, image, worldMin, worldSize);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[unwritten] Baked minimap for '{scene.name}' → {path} " +
                  $"(covers {worldSize:0}×{worldSize:0} world units). Press Play to see it in the HUD corner.");
        EditorUtility.DisplayDialog("Bake Minimap — done",
            $"Snapshotted '{scene.name}' to a {Resolution}² chart and wired a MinimapData onto the scene.\n\n" +
            "The HUD minimap now uses this image (no live camera) for this map. Re-bake whenever the layout changes.",
            "OK");
    }

    // Encapsulate every renderer's world bounds in the scene's roots.
    static bool TryComputeBounds(Scene scene, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (r is ParticleSystemRenderer) continue;   // FX shouldn't define the map's footprint
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
        return any;
    }

    // Render the map straight-down into a Texture2D.
    static Texture2D Snapshot(Bounds b, float extent)
    {
        var rt = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32) { name = "MinimapBakeRT" };
        rt.Create();

        var camGo = new GameObject("~MinimapBakeCam");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = extent;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.10f, 0.09f, 1f);   // dark where there's no terrain
        // Skip UI + the weather overlay quads (rain / wet-puddles / snow all live on TransparentFX) for a clean chart.
        cam.cullingMask = ~((1 << LayerMask.NameToLayer("UI")) | (1 << LayerMask.NameToLayer("TransparentFX")));
        cam.allowMSAA = false;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = b.size.y + 400f;
        cam.transform.position = new Vector3(b.center.x, b.max.y + 50f, b.center.z);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);     // straight down, fixed-north
        cam.targetTexture = rt;

        var req = new RenderPipeline.StandardRequest();
        if (RenderPipeline.SupportsRenderRequest(cam, req)) { req.destination = rt; RenderPipeline.SubmitRenderRequest(cam, req); }
        else cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(Resolution, Resolution, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        cam.targetTexture = null;
        Object.DestroyImmediate(camGo);
        rt.Release();
        Object.DestroyImmediate(rt);
        return tex;
    }

    static string SaveTexture(Texture2D tex, string sceneName)
    {
        if (!AssetDatabase.IsValidFolder("Assets/2_Generated")) AssetDatabase.CreateFolder("Assets", "2_Generated");
        if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/2_Generated", "Minimaps");

        string path = $"{OutDir}/{sceneName}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        // A plain clamped texture (RawImage wants a Texture, not a Sprite).
        if (AssetImporter.GetAtPath(path) is TextureImporter ti)
        {
            ti.textureType = TextureImporterType.Default;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = false;
            ti.SaveAndReimport();
        }
        return path;
    }

    static void ApplyData(Scene scene, Texture2D image, Vector2 worldMin, float worldSize)
    {
        // Reuse an existing MinimapData if the map already has one; else make a dedicated root object.
        MinimapData data = null;
        foreach (var root in scene.GetRootGameObjects())
        {
            data = root.GetComponentInChildren<MinimapData>(true);
            if (data != null) break;
        }
        if (data == null)
        {
            var go = new GameObject("MinimapData");
            SceneManager.MoveGameObjectToScene(go, scene);
            data = go.AddComponent<MinimapData>();
        }
        data.image = image;
        data.worldMin = worldMin;
        data.worldSize = worldSize;
        EditorUtility.SetDirty(data);
    }
}
