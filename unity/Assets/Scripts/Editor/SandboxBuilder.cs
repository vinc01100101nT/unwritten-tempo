using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the standalone character-select sandbox scene: a ground plane, a light, a main camera, and a
/// <see cref="CharacterSelectSandbox"/> wired to the baked <see cref="CharacterRoster"/>. Open it and press
/// Play to pick + walk any baked character — no touching the real game.
/// </summary>
public static class SandboxBuilder
{
    const string ScenePath = "Assets/Scenes/CharacterSandbox.unity";
    const string RosterPath = "Assets/1_Authored/Art/Characters/CharacterRoster.asset";

    [MenuItem("Tools/unwritten/Scene Setup/Create Character Sandbox Scene", priority = 622)]
    static void Create()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Ground (checkered so you can SEE the character move — a flat colour hides motion)
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(8f, 1f, 8f);   // 80×80 units
        ApplyCheckerFloor(ground.GetComponent<MeshRenderer>());

        // Light
        var lightGO = new GameObject("Directional Light");
        var light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // Camera (the sandbox drives it each frame; just needs to exist + be MainCamera)
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.backgroundColor = new Color(0.18f, 0.20f, 0.24f);
        camGO.AddComponent<AudioListener>();
        camGO.transform.position = new Vector3(0f, 6f, -9f);
        camGO.transform.rotation = Quaternion.Euler(35f, 0f, 0f);

        // Sandbox controller + roster
        var sbGO = new GameObject("CharacterSandbox");
        var sb = sbGO.AddComponent<CharacterSelectSandbox>();
        var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
        sb.roster = roster;
        if (roster == null)
            Debug.LogWarning("[unwritten] No CharacterRoster yet — bake some characters first " +
                             "(Tools ▸ unwritten ▸ Bake All Quaternius Characters), then re-open this scene or assign the roster.");

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorUtility.DisplayDialog("Character Sandbox",
            roster != null
                ? "Sandbox scene created. Press Play, click a character from the list, and walk it with WASD (Shift = run)."
                : "Sandbox scene created, but no roster found yet. Bake characters first, then re-run this or drag the " +
                  "CharacterRoster onto the CharacterSandbox object.",
            "OK");
    }

    // A 2×2 checker tiled across the plane so the character's movement is visible against a reference grid.
    static void ApplyCheckerFloor(MeshRenderer mr)
    {
        if (mr == null) return;
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        var a = new Color(0.30f, 0.32f, 0.36f); var b = new Color(0.38f, 0.40f, 0.45f);
        tex.SetPixels(new[] { a, b, b, a });
        tex.Apply();

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(shader);
        if (mat.HasProperty("_BaseMap")) { mat.SetTexture("_BaseMap", tex); mat.SetTextureScale("_BaseMap", new Vector2(40f, 40f)); }
        if (mat.HasProperty("_MainTex")) { mat.SetTexture("_MainTex", tex); mat.SetTextureScale("_MainTex", new Vector2(40f, 40f)); }
        mr.sharedMaterial = mat;
    }
}
