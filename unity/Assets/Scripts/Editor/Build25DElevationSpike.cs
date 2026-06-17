using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click proof that the 2.5D world supports MULTIPLE GROUND LEVELS (terraces / elevation), not
/// just a flat field. Builds <b>Assets/Scenes/Elevation25D.unity</b>: a base ground, a raised plateau,
/// and a ramp between them (all on a dedicated <c>Ground</c> layer), plus the standard spike rig.
///
/// It turns on <see cref="Pathfinder3D.HeightAware"/> via a <see cref="Pathfinder3DConfig"/>, so routing
/// samples per-cell ground height and only steps where the rise is small. The proof to look for on Play:
///   • walk UP the ramp onto the plateau (click on top of it — the path goes via the ramp),
///   • you CANNOT path straight up the cliff faces (click the plateau from the cliff side → it routes
///     around to the ramp, or reports no route),
///   • the player/mobs stand correctly on whichever tier they're on (gravity + ground colliders).
///
/// Standalone, like Build 2.5D Spike — just open it and press Play. Throwaway / branch-only.
/// </summary>
public static class Build25DElevationSpike
{
    const string ScenePath = "Assets/Scenes/Elevation25D.unity";

    // Plateau: a solid block centred at (8,0,8), half-extents (6,2,6) → top surface at y=+2, footprint
    // X[2,14] Z[2,14]. Ramp climbs its -Z edge.
    static readonly Vector3 PlateauCenter = new Vector3(8f, 0f, 8f);
    static readonly Vector3 PlateauScale  = new Vector3(12f, 4f, 12f);   // top at y = scale.y/2 = 2
    const float TierHeight = 2f;
    const float RampRun = 4f;   // horizontal length of the ramp (rise = TierHeight)

    static LayerMask groundMask;

    [MenuItem("Tools/unwritten/Build Elevation Spike", priority = 5)]
    static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Elevation Spike", "Exit Play mode first, then re-run.", "OK"); return; }

        int rendererIndex = Build25DSpike.EnsureUniversalRenderer();
        if (rendererIndex < 0) return;

        int groundLayer = EnsureGroundLayer();
        if (groundLayer < 0)
        { EditorUtility.DisplayDialog("Elevation Spike", "No free layer slot to create a 'Ground' layer. Free one in Project Settings ▸ Tags and Layers, then re-run.", "OK"); return; }
        groundMask = 1 << groundLayer;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);

        Build25DSpike.BuildLight();
        BuildTiers(groundLayer);                       // base + plateau + ramp (the multi-level ground)
        BuildPathfinderConfig();                       // turn on height-aware routing for this scene

        // Player starts on the BASE, clear of the plateau footprint.
        var player = Build25DSpike.BuildPlayer(new Vector3(0f, 0f, -6f));
        Build25DSpike.BuildCamera(rendererIndex, player);
        Build25DSpike.BuildMonsters(PlateauCenter, 2f);   // mobs spawn ON the plateau → they must use the ramp to chase
        Build25DSpike.BuildCharacter();
        Build25DSpike.BuildHUD();
        if (player != null) Build25DSpike.BuildCursor(player);

        // The flat-spike rig sets a tiny slopeLimit (anti prop-mount). Ramps need a real one — and let
        // the controllers step the small seam at the ramp foot. Then drop everything onto its tier.
        Physics.SyncTransforms();   // make the freshly-created tier colliders queryable for the snap rays
        foreach (var cc in Object.FindObjectsByType<CharacterController>(FindObjectsSortMode.None))
        {
            cc.slopeLimit = 45f;
            cc.stepOffset = 0.3f;
            SnapToGround(cc.transform);
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.playModeStartScene = null;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("Elevation Spike — built",
            $"Built {ScenePath}\n\n" +
            "Base ground + raised plateau + ramp (height-aware pathfinding ON).\n\n" +
            "NEXT: press Play.\n" +
            "• Click on TOP of the plateau → the player routes UP the ramp.\n" +
            "• Click the plateau from a cliff side → it routes around to the ramp (can't climb the cliff).\n" +
            "• Mobs on the plateau chase you down the ramp.\n" +
            "Camera: scroll = zoom; Q/E or right-drag = orbit.",
            "OK");
        Debug.Log($"[unwritten] Elevation Spike built → {ScenePath} (Ground layer {groundLayer}). Open & press Play.");
    }

    // ---- the multi-level ground ----------------------------------------------

    static void BuildTiers(int groundLayer)
    {
        // Base: 40×40 flat ground at y=0.
        var baseGo = GameObject.CreatePrimitive(PrimitiveType.Plane);
        baseGo.name = "Tier_Base";
        baseGo.transform.localScale = new Vector3(4f, 1f, 4f);
        Paint(baseGo, new Color(0.36f, 0.55f, 0.27f), groundLayer);

        // Plateau: a solid block; its top face is the upper tier, its sides are cliffs.
        var plateau = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plateau.name = "Tier_Plateau";
        plateau.transform.position = PlateauCenter;
        plateau.transform.localScale = PlateauScale;
        Paint(plateau, new Color(0.42f, 0.50f, 0.30f), groundLayer);

        // Ramp: a tilted slab from the base up to the plateau's -Z edge (x centred on the plateau).
        float angle = Mathf.Atan2(TierHeight, RampRun) * Mathf.Rad2Deg;       // ~26.6° (< slopeLimit 45)
        float length = Mathf.Sqrt(TierHeight * TierHeight + RampRun * RampRun);
        float edgeZ = PlateauCenter.z - PlateauScale.z * 0.5f;                // plateau -Z edge (z=2)
        var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ramp.name = "Tier_Ramp";
        ramp.transform.localScale = new Vector3(4f, 0.5f, length);
        ramp.transform.rotation = Quaternion.Euler(-angle, 0f, 0f);           // tilt so +Z end rises
        // Midpoint between base end (edgeZ - run, y=0) and plateau end (edgeZ, y=TierHeight).
        ramp.transform.position = new Vector3(PlateauCenter.x, TierHeight * 0.5f, edgeZ - RampRun * 0.5f);
        Paint(ramp, new Color(0.55f, 0.45f, 0.30f), groundLayer);
    }

    static void Paint(GameObject go, Color color, int layer)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
        m.SetFloat("_Smoothness", 0f);
        go.GetComponent<MeshRenderer>().sharedMaterial = m;
        go.layer = layer;
    }

    static void BuildPathfinderConfig()
    {
        var cfg = new GameObject("Pathfinder3DConfig").AddComponent<Pathfinder3DConfig>();
        cfg.heightAware = true;
        cfg.groundMask = groundMask;
        cfg.maxStep = 0.6f;
    }

    // ---- helpers --------------------------------------------------------------

    static void SnapToGround(Transform t)
    {
        Vector3 p = t.position;
        if (Physics.Raycast(new Vector3(p.x, 60f, p.z), Vector3.down, out var hit, 120f, groundMask, QueryTriggerInteraction.Ignore))
            t.position = new Vector3(p.x, hit.point.y + Build25DSpike.FootOffsetY, p.z);
    }

    // Find or create a user layer named "Ground" (user layers are slots 8..31). Returns its index, or -1.
    public static int EnsureGroundLayer()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        if (assets == null || assets.Length == 0) return -1;
        var so = new SerializedObject(assets[0]);
        var layers = so.FindProperty("layers");
        if (layers == null) return -1;

        for (int i = 8; i < layers.arraySize; i++)
            if (layers.GetArrayElementAtIndex(i).stringValue == "Ground") return i;
        for (int i = 8; i < layers.arraySize; i++)
        {
            var p = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(p.stringValue)) { p.stringValue = "Ground"; so.ApplyModifiedProperties(); return i; }
        }
        return -1;
    }
}
