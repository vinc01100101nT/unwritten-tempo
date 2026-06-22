using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Bakes the <see cref="WorldAtlas"/> the world-map HUD shows: opens each generated map scene, snapshots it
/// straight down (same fixed-north top-down render the <see cref="MinimapBaker"/> uses), and composites every
/// thumbnail into ONE image arranged by grid cell. Writes the PNG + a <see cref="WorldAtlas"/> asset into
/// <c>Resources/</c> so <see cref="WorldMapHUD"/> can load it with no scene wiring.
///
/// Driven from the World Creator window (the "Bake World Map" button), which already holds the grid (each map's
/// scene name + cell + type). Each cell is rendered at a fixed plot extent (worldSize/2 around the origin) so all
/// thumbnails share one scale and tile seamlessly; empty cells stay dark.
/// </summary>
public static class WorldAtlasBaker
{
    public struct Cell { public string scene; public int col, row; public string type; }

    const string ResourcesDir = "Assets/Resources";
    const string AtlasAssetPath = "Assets/Resources/WorldAtlas.asset";
    const string AtlasPngPath = "Assets/Resources/WorldMap/atlas.png";
    const int CellPixels = 256;                                 // thumbnail edge per map

    public static void Bake(List<Cell> cells, float worldSize, bool interactive = true)
    {
        if (cells == null || cells.Count == 0)
        { if (interactive) EditorUtility.DisplayDialog("Bake World Map", "No maps to bake.", "OK"); return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { if (interactive) EditorUtility.DisplayDialog("Bake World Map", "Exit Play mode first, then re-run.", "OK"); return; }

        int minCol = int.MaxValue, minRow = int.MaxValue, maxCol = int.MinValue, maxRow = int.MinValue;
        foreach (var c in cells)
        {
            minCol = Mathf.Min(minCol, c.col); maxCol = Mathf.Max(maxCol, c.col);
            minRow = Mathf.Min(minRow, c.row); maxRow = Mathf.Max(maxRow, c.row);
        }
        int cols = maxCol - minCol + 1, rows = maxRow - minRow + 1;

        var atlas = new Texture2D(cols * CellPixels, rows * CellPixels, TextureFormat.RGB24, false);
        // Survive the UnloadUnusedAssets that OpenScene(Single) triggers each iteration — otherwise this
        // in-memory texture is destroyed mid-loop (a C# local doesn't protect a UnityEngine.Object from it).
        atlas.hideFlags = HideFlags.HideAndDontSave;
        FillDark(atlas);

        try
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                string path = $"Assets/Scenes/{c.scene}.unity";
                if (!File.Exists(path)) { Debug.LogWarning($"[unwritten] World atlas: scene '{path}' missing; skipped."); continue; }
                EditorUtility.DisplayProgressBar("Bake World Map", $"Snapshotting '{c.scene}' ({i + 1}/{cells.Count})", (float)i / cells.Count);

                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var thumb = SnapshotPlot(scene, worldSize, CellPixels);

                // Atlas pixel origin: column left→right from minCol; row TOP→bottom from minRow (north up).
                int px = (c.col - minCol) * CellPixels;
                int py = (rows - 1 - (c.row - minRow)) * CellPixels;     // Texture2D y=0 is bottom
                atlas.SetPixels(px, py, CellPixels, CellPixels, thumb.GetPixels());
                Object.DestroyImmediate(thumb);
            }
            atlas.Apply();
            EditorUtility.ClearProgressBar();
        }
        catch { EditorUtility.ClearProgressBar(); Object.DestroyImmediate(atlas); throw; }

        var texture = SaveAtlasPng(atlas);
        Object.DestroyImmediate(atlas);
        WriteAsset(texture, cells, cols, rows, minCol, minRow, worldSize);

        Debug.Log($"[unwritten] Baked world atlas: {cols}×{rows} cells → {AtlasPngPath}.");
        if (interactive)
            EditorUtility.DisplayDialog("Bake World Map — done",
                $"Composited {cells.Count} map snapshots into a {cols}×{rows} world atlas.\n\n"
                + "Press Play and open the World Map (default key: N) to see it.", "OK");
    }

    // Render one map straight down at a FIXED plot extent (worldSize/2 around origin) so every cell shares scale.
    // Mobs are hidden for the snapshot (billboards are edge-on from above). Mirrors MinimapBaker's camera setup.
    static Texture2D SnapshotPlot(Scene scene, float worldSize, int px)
    {
        // Hide mob billboards while we shoot.
        var hidden = new List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == "Mobs" && root.activeSelf) { root.SetActive(false); hidden.Add(root); }

        float extent = worldSize * 0.5f;
        var rt = new RenderTexture(px, px, 24, RenderTextureFormat.ARGB32) { name = "WorldAtlasRT" };
        rt.Create();

        var camGo = new GameObject("~WorldAtlasCam");
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = extent;
        cam.aspect = 1f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.08f, 0.10f, 0.09f, 1f);
        cam.cullingMask = ~((1 << LayerMask.NameToLayer("UI")) | (1 << LayerMask.NameToLayer("TransparentFX")));
        cam.allowMSAA = false;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 1000f;
        cam.transform.position = new Vector3(0f, 300f, 0f);
        cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);          // straight down, fixed-north (+Z up, +X right)
        cam.targetTexture = rt;

        var req = new RenderPipeline.StandardRequest();
        if (RenderPipeline.SupportsRenderRequest(cam, req)) { req.destination = rt; RenderPipeline.SubmitRenderRequest(cam, req); }
        else cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(px, px, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, px, px), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        cam.targetTexture = null;
        Object.DestroyImmediate(camGo);
        rt.Release();
        Object.DestroyImmediate(rt);

        foreach (var go in hidden) go.SetActive(true);
        return tex;
    }

    static void FillDark(Texture2D t)
    {
        var c = new Color(0.05f, 0.06f, 0.06f, 1f);
        var px = new Color[t.width * t.height];
        for (int i = 0; i < px.Length; i++) px[i] = c;
        t.SetPixels(px);
    }

    static Texture2D SaveAtlasPng(Texture2D atlas)
    {
        if (!AssetDatabase.IsValidFolder(ResourcesDir)) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/WorldMap")) AssetDatabase.CreateFolder(ResourcesDir, "WorldMap");

        File.WriteAllBytes(AtlasPngPath, atlas.EncodeToPNG());
        AssetDatabase.ImportAsset(AtlasPngPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        if (AssetImporter.GetAtPath(AtlasPngPath) is TextureImporter ti)
        {
            ti.textureType = TextureImporterType.Default;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = false;
            ti.maxTextureSize = 4096;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPngPath);
    }

    static void WriteAsset(Texture2D texture, List<Cell> cells, int cols, int rows, int minCol, int minRow, float worldSize)
    {
        var asset = AssetDatabase.LoadAssetAtPath<WorldAtlas>(AtlasAssetPath);
        bool isNew = asset == null;
        if (isNew) asset = ScriptableObject.CreateInstance<WorldAtlas>();

        asset.atlas = texture;
        asset.cols = cols; asset.rows = rows;
        asset.minCol = minCol; asset.minRow = minRow;
        asset.cellPixels = CellPixels;
        asset.worldSize = worldSize;
        asset.maps.Clear();
        foreach (var c in cells)
            asset.maps.Add(new WorldAtlas.Entry { scene = c.scene, col = c.col, row = c.row, type = c.type });

        if (isNew) AssetDatabase.CreateAsset(asset, AtlasAssetPath);
        else EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
    }
}
