using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Assembles a single <b>house prefab</b> from the Kenney Fantasy Town Kit's modular wall/roof FBX
/// (the kit has no pre-made house — only pieces). Walls tile around a footprint with a door + windows,
/// a flat roof caps the top, everything is re-skinned with the kit colormap and dropped to y=0, then
/// saved as <c>Assets/Prefabs/KenneyFantasyTown/house-small.prefab</c> for the 3D town baker to place.
///
/// ASSEMBLED BLIND (pivot/rotation conventions can't be seen here). Tuning knobs are the consts at
/// the top — after a first build, screenshot it and we adjust facings / add corner pieces / swap the
/// flat roof for a gable. Re-runnable (overwrites the prefab in place).
/// </summary>
public static class HouseAssembler
{
    const string KitDir   = "Assets/Art/KenneyFantasyTownKit/FBX format";
    const string MatPath  = KitDir + "/KenneyFantasyTown.mat";
    const string OutPath  = "Assets/Prefabs/KenneyFantasyTown/house-small.prefab";

    // ---- tuning knobs --------------------------------------------------------
    const int   Nx = 3, Nz = 3;     // wall segments per side (footprint)
    const int   Rows = 2;           // wall rows tall (door lives on the bottom row)
    const string PWall   = "wall";              // plain wall segment
    const string PDoor   = "wall-door";         // wall with a doorway (front-center, bottom row)
    const string PWindow = "wall-window-shutters";
    const string PRoof   = "roof-flat";         // flat roof tile

    [MenuItem("Tools/unwritten/Build Prefabs/Assemble House (Fantasy Town)", priority = 23)]
    static void Build()
    {
        var wall = Measure(PWall);
        if (wall.fbx == null) { Fail($"'{PWall}.fbx' not found in {KitDir}."); return; }

        float L = Mathf.Max(wall.size.x, wall.size.z);   // segment length (long horizontal axis)
        float H = wall.size.y;                           // one row height
        bool longAxisX = wall.size.x >= wall.size.z;     // which way the segment runs at rotation 0
        float W = Nx * L, D = Nz * L;
        var mat = Material();

        var house = new GameObject("house-small");

        // Perimeter walls (butt-jointed corners for v1 — no separate corner piece yet).
        for (int row = 0; row < Rows; row++)
        {
            float y = row * H;
            for (int i = 0; i < Nx; i++)
            {
                float x = -W / 2f + L * (i + 0.5f);
                PlaceWall(house, EdgePiece(Edge.Front, i, Nx, row), mat, longAxisX,
                          new Vector3(x, y, -D / 2f), 0f);                 // front (−Z)
                PlaceWall(house, EdgePiece(Edge.Back, i, Nx, row), mat, longAxisX,
                          new Vector3(x, y, D / 2f), 180f);                // back (+Z)
            }
            for (int j = 0; j < Nz; j++)
            {
                float z = -D / 2f + L * (j + 0.5f);
                PlaceWall(house, EdgePiece(Edge.Left, j, Nz, row), mat, longAxisX,
                          new Vector3(-W / 2f, y, z), 90f);                // left (−X)
                PlaceWall(house, EdgePiece(Edge.Right, j, Nz, row), mat, longAxisX,
                          new Vector3(W / 2f, y, z), 270f);               // right (+X)
            }
        }

        // Flat roof: tile roof pieces across the top opening.
        var roof = Measure(PRoof);
        if (roof.fbx != null)
        {
            float rl = Mathf.Max(roof.size.x, roof.size.z, 0.01f);
            int rnx = Mathf.Max(1, Mathf.RoundToInt(W / rl));
            int rnz = Mathf.Max(1, Mathf.RoundToInt(D / rl));
            float roofY = Rows * H;
            for (int i = 0; i < rnx; i++)
                for (int k = 0; k < rnz; k++)
                {
                    float x = -W / 2f + (W / rnx) * (i + 0.5f);
                    float z = -D / 2f + (D / rnz) * (k + 0.5f);
                    Place(house, roof.fbx, mat, new Vector3(x, roofY, z), Quaternion.identity);
                }
        }

        // Drop so the lowest point sits on y=0 (handles whatever vertical pivot the FBX use).
        DropToGround(house);

        // Save as a single prefab.
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs")) AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/KenneyFantasyTown"))
            AssetDatabase.CreateFolder("Assets/Prefabs", "KenneyFantasyTown");
        var saved = PrefabUtility.SaveAsPrefabAsset(house, OutPath);
        Object.DestroyImmediate(house);
        AssetDatabase.SaveAssets();

        Selection.activeObject = saved;
        EditorGUIUtility.PingObject(saved);
        Debug.Log($"[unwritten] Assembled {OutPath} ({Nx}×{Nz}, {Rows} rows, L={L:F2} H={H:F2}). " +
                  "Drag it into a scene + screenshot so we can tune facings / corners / roof.");
        EditorUtility.DisplayDialog("House assembled",
            $"Built {OutPath}\n({Nx}×{Nz} footprint, {Rows} rows tall, flat roof).\n\n" +
            "Drag it into the Stage25D scene and screenshot it — assembled blind, so the door/window " +
            "facings or roof may need a tweak (knobs are at the top of HouseAssembler.cs).", "OK");
    }

    enum Edge { Front, Back, Left, Right }

    // Door on the front-center bottom segment; windows on the center of every edge above the door row.
    static string EdgePiece(Edge e, int i, int n, int row)
    {
        int center = n / 2;
        if (e == Edge.Front && row == 0 && i == center) return PDoor;
        if (row >= 1 && i == center) return PWindow;
        return PWall;
    }

    // ---- placement helpers ---------------------------------------------------

    static void PlaceWall(GameObject parent, string name, Material mat, bool longAxisX,
                          Vector3 pos, float yaw)
    {
        var p = Measure(name);
        if (p.fbx == null) p = Measure(PWall);   // fall back to plain wall if a variant is missing
        if (p.fbx == null) return;
        // If the segment's long axis is Z (not X), add 90° so "yaw 0" always means "runs along X".
        float baseYaw = longAxisX ? 0f : 90f;
        Place(parent, p.fbx, mat, pos, Quaternion.Euler(0f, yaw + baseYaw, 0f));
    }

    static void Place(GameObject parent, GameObject fbx, Material mat, Vector3 pos, Quaternion rot)
    {
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        inst.transform.SetParent(parent.transform, false);
        inst.transform.SetPositionAndRotation(pos, rot);
        foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
        {
            var mats = mr.sharedMaterials;
            for (int k = 0; k < mats.Length; k++) mats[k] = mat;
            mr.sharedMaterials = mats;
        }
    }

    static void DropToGround(GameObject root)
    {
        var rends = root.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return;
        var b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
        root.transform.position -= new Vector3(0f, b.min.y, 0f);
    }

    // ---- asset helpers -------------------------------------------------------

    struct Piece { public GameObject fbx; public Vector3 size; }
    static readonly Dictionary<string, Piece> _cache = new Dictionary<string, Piece>();

    static Piece Measure(string name)
    {
        if (_cache.TryGetValue(name, out var c)) return c;
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{KitDir}/{name}.fbx");
        var piece = new Piece { fbx = fbx, size = Vector3.one };
        if (fbx != null)
        {
            var tmp = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            tmp.transform.position = Vector3.zero;
            tmp.transform.rotation = Quaternion.identity;
            var rends = tmp.GetComponentsInChildren<Renderer>();
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
                piece.size = b.size;
            }
            Object.DestroyImmediate(tmp);
        }
        _cache[name] = piece;
        return piece;
    }

    static Material Material()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(KitDir + "/Textures/colormap.png");
        if (tex != null) { mat.SetTexture("_BaseMap", tex); mat.mainTexture = tex; }
        mat.SetFloat("_Smoothness", 0f);
        return mat;
    }

    static void Fail(string msg)
    {
        EditorUtility.DisplayDialog("House assembler", msg, "OK");
        Debug.LogError("[unwritten] House assembler: " + msg);
    }
}
