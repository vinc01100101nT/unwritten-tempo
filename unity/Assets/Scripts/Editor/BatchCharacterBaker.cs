using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click batch baker for a whole folder of rigged characters that ship with EMBEDDED animations
/// (e.g. the Quaternius Ultimate Animated pack). For each FBX it reads the embedded clips, maps them by
/// name onto <see cref="CharacterAction"/>, bakes 8-direction sprite strips via the shared
/// <see cref="SpriteBakeCore"/>, writes a <see cref="BakedBillboard8"/> prefab, and registers it in a
/// <see cref="CharacterRoster"/>. Start with a small <b>first N</b> to sanity-check before committing to the
/// full (long) batch. Output → <c>Assets/1_Authored/Art/Characters/&lt;name&gt;/</c>.
/// </summary>
public class BatchCharacterBaker : EditorWindow
{
    string fbxFolder = "Assets/1_Authored/Art/Characters/Models";
    string outputRoot = "Assets/1_Authored/Art/Characters";
    // yawOffset 0 = correct for the Quaternius pack (faces the opposite way from Mixamo's +Z-forward, which needs 180).
    // framePadding 1.2 (NOT 1.0): the cell is framed around the UNION of all poses, and the jump apex is the
    // tallest — at padding 1.0 it sat flush with the cell's top edge and the head got CLIPPED. 1.2 leaves ~8.5%
    // (~5px @64) headroom so no pose's head is cut. worldScale is bumped in step (0.5→0.6 = ×1.2) so the on-screen
    // character size is unchanged — the extra headroom comes for free. Re-bake with "Skip already baked" OFF to apply.
    SpriteBakeCore.Settings settings = new SpriteBakeCore.Settings
    { cellSize = 64, pixelate = true, superSample = 4, cameraPitch = 30f, framePadding = 1.2f, yawOffset = 0f, clockwise = true };

    bool limitCount = true;
    int firstN = 3;
    bool skipAlreadyBaked = true;

    // Runtime params written onto each prefab's BakedBillboard8. feetNudge is ADDED to the auto-measured
    // grounding offset (0 = trust the measurement; +/- to fine-tune if a character reads slightly off).
    float feetNudge = 0f, worldScale = 0.6f, walkThreshold = 0.15f, runThreshold = 3.5f;   // 0.6 = 0.5 × 1.2, compensating the framePadding bump so on-screen size holds

    // Per-action animation FPS — doubles as the BAKED frame count (frames = clip length × fps) AND the playback
    // rate, so higher = smoother motion at the same real speed (costs more frames/VRAM). Editable in the window.
    int idleFps = 24, walkFps = 24, runFps = 24, otherFps = 24;

    Vector2 scroll;
    string[] fbxPaths = System.Array.Empty<string>();

    // Clip-name keyword → action. ORDER MATTERS: more specific keywords first (carry variants before plain
    // walk/run; specific attacks before generic). First keyword the clip name contains wins.
    static readonly (string kw, CharacterAction a)[] Map =
    {
        ("walk_carry", CharacterAction.WalkCarry), ("walkcarry", CharacterAction.WalkCarry),
        ("run_carry", CharacterAction.RunCarry), ("runcarry", CharacterAction.RunCarry),
        ("idle", CharacterAction.Idle),
        ("walk", CharacterAction.Walk),
        ("run", CharacterAction.Run),
        ("jump", CharacterAction.Jump),
        ("roll", CharacterAction.Roll),
        ("swordslash", CharacterAction.Attack), ("slash", CharacterAction.Attack),
        ("punch", CharacterAction.Punch),
        ("shoot", CharacterAction.Shoot),
        ("recievehit", CharacterAction.Hurt), ("receivehit", CharacterAction.Hurt), ("hit", CharacterAction.Hurt),
        ("death", CharacterAction.Die), ("die", CharacterAction.Die),
        ("defeat", CharacterAction.Defeat),
        ("victory", CharacterAction.Victory),
        ("pickup", CharacterAction.PickUp), ("pick_up", CharacterAction.PickUp),
        ("sitdown", CharacterAction.Sit), ("sit", CharacterAction.Sit),
        ("standup", CharacterAction.StandUp),
        ("cast", CharacterAction.Cast),
        ("dash", CharacterAction.Dash),
    };

    [MenuItem("Tools/unwritten/Art/Bake All Quaternius Characters", priority = 621)]
    static void Open() => GetWindow<BatchCharacterBaker>(true, "Bake All Characters");

    void OnEnable() => Rescan();

    void Rescan()
    {
        var list = new List<string>();
        if (AssetDatabase.IsValidFolder(fbxFolder))
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { fbxFolder }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                // Skip the loose Mixamo test FBX and anything not directly in the folder's model set.
                if (Path.GetFileNameWithoutExtension(p).Equals("Running", System.StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(p);
            }
        list.Sort();
        fbxPaths = list.ToArray();
    }

    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox("Bakes every character FBX (with embedded clips) in the folder into 8-dir sprite " +
            "prefabs + a CharacterRoster. Start with a small 'first N' to verify before the full batch.", MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        fbxFolder = EditorGUILayout.TextField("FBX folder", fbxFolder);
        if (GUILayout.Button("Rescan", GUILayout.Width(60))) Rescan();
        EditorGUILayout.EndHorizontal();
        outputRoot = EditorGUILayout.TextField("Output root", outputRoot);
        EditorGUILayout.LabelField($"Found {fbxPaths.Length} character FBX.", EditorStyles.miniLabel);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Test subset", EditorStyles.boldLabel);
        limitCount = EditorGUILayout.ToggleLeft("Bake only the first N (uncheck to bake ALL)", limitCount);
        using (new EditorGUI.DisabledScope(!limitCount))
            firstN = Mathf.Clamp(EditorGUILayout.IntField("First N characters", firstN), 1, Mathf.Max(1, fbxPaths.Length));
        skipAlreadyBaked = EditorGUILayout.ToggleLeft("Skip characters whose prefab already exists", skipAlreadyBaked);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Look", EditorStyles.boldLabel);
        settings.pixelate = EditorGUILayout.ToggleLeft("Pixelate", settings.pixelate);
        settings.cellSize = Mathf.Clamp(EditorGUILayout.IntField("Cell size (px)", settings.cellSize), 16, 256);
        using (new EditorGUI.DisabledScope(settings.pixelate))
            settings.superSample = Mathf.Clamp(EditorGUILayout.IntField("Hi-res supersample (×)", settings.superSample), 1, 8);
        settings.cameraPitch = EditorGUILayout.Slider("Camera pitch (°)", settings.cameraPitch, 0f, 75f);
        settings.framePadding = EditorGUILayout.Slider("Frame padding", settings.framePadding, 1f, 2.5f);
        settings.yawOffset = EditorGUILayout.Slider("Yaw offset (°)", settings.yawOffset, 0f, 360f);
        settings.clockwise = EditorGUILayout.ToggleLeft("Clockwise octant mapping", settings.clockwise);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Animation smoothness (FPS = baked frames)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("FPS is both the captured frame count (frames = clip length × fps) and the playback " +
            "rate — higher = smoother at the same speed, but more frames/VRAM. Re-bake to apply.", MessageType.None);
        idleFps  = Mathf.Clamp(EditorGUILayout.IntField("Idle FPS", idleFps), 4, 60);
        walkFps  = Mathf.Clamp(EditorGUILayout.IntField("Walk FPS", walkFps), 4, 60);
        runFps   = Mathf.Clamp(EditorGUILayout.IntField("Run FPS", runFps), 4, 60);
        otherFps = Mathf.Clamp(EditorGUILayout.IntField("Other actions FPS", otherFps), 4, 60);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime (per prefab)", EditorStyles.boldLabel);
        worldScale = EditorGUILayout.FloatField("World scale", worldScale);
        feetNudge = EditorGUILayout.FloatField(
            new GUIContent("Feet nudge", "Added to the auto-measured grounding offset. 0 = trust the measurement; " +
            "use a small +/- only if a character still floats/sinks a hair."), feetNudge);

        int count = limitCount ? Mathf.Min(firstN, fbxPaths.Length) : fbxPaths.Length;
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox($"Will process {count} character(s). Each = its embedded clips × 8 directions. " +
            "This can take a while; the progress bar is cancelable and re-runnable.", MessageType.None);

        using (new EditorGUI.DisabledScope(fbxPaths.Length == 0))
            if (GUILayout.Button(limitCount ? $"Bake first {count} characters" : $"Bake ALL {count} characters", GUILayout.Height(34)))
                Run(count);

        EditorGUILayout.EndScrollView();
    }

    void Run(int count)
    {
        var roster = LoadOrCreateRoster();
        int built = 0, skipped = 0;
        try
        {
            for (int i = 0; i < count; i++)
            {
                string fbxPath = fbxPaths[i];
                string name = Path.GetFileNameWithoutExtension(fbxPath);
                string dir = $"{outputRoot}/{name}";
                string spritesDir = $"{dir}/Sprites";
                string prefabPath = $"{dir}/{name}.prefab";

                if (skipAlreadyBaked && File.Exists(prefabPath))
                {
                    // still make sure it's in the roster
                    var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (existing != null) roster.Upsert(name, existing, AssetDatabase.LoadAssetAtPath<Texture2D>($"{spritesDir}/Idle_S.png"));
                    skipped++; continue;
                }

                if (EditorUtility.DisplayCancelableProgressBar("Baking characters",
                    $"{name} ({i + 1}/{count})", count > 0 ? (float)i / count : 0f)) break;

                if (BuildOne(fbxPath, name, dir, spritesDir, prefabPath, roster)) built++;
                else skipped++;
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        EditorUtility.SetDirty(roster);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[unwritten] Batch bake done: {built} built, {skipped} skipped. Roster has {roster.characters.Count} characters. " +
                  "Run Tools ▸ unwritten ▸ Create Character Sandbox Scene to walk them around.");
        EditorUtility.DisplayDialog("Batch bake", $"Built {built}, skipped {skipped}.\nRoster: {roster.characters.Count} characters.", "OK");
    }

    bool BuildOne(string fbxPath, string name, string dir, string spritesDir, string prefabPath, CharacterRoster roster)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (model == null) { Debug.LogWarning($"[unwritten] Skipped {name}: not a model."); return false; }

        // Read embedded clips and map them to actions (first matching keyword, first clip per action wins).
        var used = new HashSet<CharacterAction>();
        var actions = new List<SpriteBakeCore.Action>();
        var fpsByAction = new List<(CharacterAction action, float fps)>();
        foreach (var rep in AssetDatabase.LoadAllAssetRepresentationsAtPath(fbxPath))
        {
            if (!(rep is AnimationClip clip) || clip.name.StartsWith("__preview__")) continue;
            if (!TryMap(clip.name, out var act) || used.Contains(act)) continue;
            used.Add(act);
            int fps = DefaultFps(act);
            actions.Add(new SpriteBakeCore.Action { name = act.ToString(), clip = clip, fps = fps, frames = 0 });
            fpsByAction.Add((act, fps));
        }
        if (actions.Count == 0) { Debug.LogWarning($"[unwritten] Skipped {name}: no recognizable clips."); return false; }

        Directory.CreateDirectory(spritesDir);
        if (!SpriteBakeCore.Bake(model, null, settings, actions, spritesDir)) return false;   // cancelled

        // Build the prefab wired to the freshly baked strips.
        var go = new GameObject(name);
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, worldScale);
        var bb = go.AddComponent<BakedBillboard8>();
        bb.cellW = settings.cellSize; bb.cellH = settings.cellSize; bb.ppu = settings.cellSize;
        bb.maxFrames = SpriteBakeCore.MaxFrames(actions);
        // Auto-ground from the measured feet line (scale-independent), plus any manual nudge.
        bb.feetOffset = SpriteBakeCore.GroundingFeetOffset(spritesDir, actions) + feetNudge;
        bb.walkThreshold = walkThreshold; bb.runThreshold = runThreshold;
        bb.actions = new List<BakedBillboard8.ActionSet>();
        foreach (var (act, fps) in fpsByAction)
            bb.actions.Add(new BakedBillboard8.ActionSet { action = act, dirs = SpriteBakeCore.LoadDir8(spritesDir, act.ToString()), fps = fps });

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);

        AssetDatabase.ImportAsset(prefabPath);
        var previewStrip = AssetDatabase.LoadAssetAtPath<Texture2D>($"{spritesDir}/Idle_S.png");
        roster.Upsert(name, prefab, previewStrip);
        return prefab != null;
    }

    static bool TryMap(string clipName, out CharacterAction action)
    {
        string n = clipName.ToLowerInvariant();
        foreach (var (kw, a) in Map)
            if (n.Contains(kw)) { action = a; return true; }
        action = CharacterAction.Idle;
        return false;
    }

    // Maps an action to its window-set FPS (= baked frame count and playback rate). See the idle/walk/run/other
    // fields and the "Animation smoothness" section in OnGUI.
    int DefaultFps(CharacterAction a)
    {
        switch (a)
        {
            case CharacterAction.Idle: return idleFps;
            case CharacterAction.Walk: case CharacterAction.WalkCarry: return walkFps;
            case CharacterAction.Run: case CharacterAction.RunCarry: return runFps;
            default: return otherFps;
        }
    }

    CharacterRoster LoadOrCreateRoster()
    {
        string path = $"{outputRoot}/CharacterRoster.asset";
        if (!AssetDatabase.IsValidFolder(outputRoot)) Directory.CreateDirectory(outputRoot);
        var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(path);
        if (roster == null)
        {
            roster = ScriptableObject.CreateInstance<CharacterRoster>();
            AssetDatabase.CreateAsset(roster, path);
        }
        return roster;
    }
}
