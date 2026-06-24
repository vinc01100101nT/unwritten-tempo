using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Window for quick one-off 8-direction sprite bakes from a rigged model + its clips. The actual render
/// pipeline lives in <see cref="SpriteBakeCore"/> (shared with the <c>CharacterDef</c> one-click builder);
/// this is just the UI, the rotate/play preview, and a spawn-test-character helper.
///
/// Action names are picked from the <see cref="CharacterAction"/> enum (a closed contract set) rather than
/// typed, so a baked strip's name always matches what the runtime looks up. Output → <c>Assets/Art/BakedChars/&lt;name&gt;/</c>.
/// </summary>
public class SpriteBaker8 : EditorWindow
{
    // ---- inputs ----
    GameObject model;
    Texture2D albedo;
    string charName = "BakedChar";
    SpriteBakeCore.Settings settings = SpriteBakeCore.Settings.Default;

    [System.Serializable]
    class ActionDef { public CharacterAction action = CharacterAction.Idle; public AnimationClip clip; public int fps = 12; public int frames = 0; }
    List<ActionDef> actions = new List<ActionDef>
    {
        new ActionDef { action = CharacterAction.Idle, fps = 12 },
        new ActionDef { action = CharacterAction.Walk, fps = 18 },
    };

    Vector2 scroll;

    // ---- in-window preview (rotate + play, no Play mode); each frame pre-cut into its own texture ----
    Texture2D[][] previewFrames;  // [octant 0..7][frame]
    string[] bakedActions;
    int previewActionIdx;
    int previewDir = 4;           // start facing the camera (S)
    bool previewPlaying = true;
    float previewFps = 12f;
    float previewTime; double lastTick;

    [MenuItem("Tools/unwritten/Art/Bake 8-Dir Sprites", priority = 620)]
    static void Open() => GetWindow<SpriteBaker8>(true, "Bake 8-Dir Sprites");

    void OnEnable()
    {
        lastTick = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        RefreshBakedActions();
        LoadPreview();
    }

    void OnDisable() { EditorApplication.update -= Tick; DisposePreview(); }

    void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastTick);
        lastTick = now;
        if (previewPlaying && previewFrames != null) { previewTime += dt; Repaint(); }
    }

    string OutDir => "Assets/Art/BakedChars/" + SpriteBakeCore.Sanitize(charName);

    // Build the core's action list from the window rows that have a clip assigned.
    List<SpriteBakeCore.Action> CoreActions()
    {
        var list = new List<SpriteBakeCore.Action>();
        foreach (var a in actions)
            if (a.clip != null)
                list.Add(new SpriteBakeCore.Action { name = a.action.ToString(), clip = a.clip, fps = a.fps, frames = a.frames });
        return list;
    }

    // ----------------------------------------------------------------------------- GUI
    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            "Bake a rigged model (Quaternius/Synty/etc.) + its Mixamo clips into 8-direction sprite strips.\n" +
            "Tip: download Mixamo clips as FBX with 'In Place' ON, and use each model with the clip from its OWN FBX.",
            MessageType.Info);

        model = (GameObject)EditorGUILayout.ObjectField("Rigged model (FBX/prefab)", model, typeof(GameObject), false);
        albedo = (Texture2D)EditorGUILayout.ObjectField(
            new GUIContent("Albedo texture (optional)", "For models whose texture ships as a separate PNG " +
            "(e.g. Quaternius palettes). Auto-applied before baking. Leave empty if the model already shows its texture."),
            albedo, typeof(Texture2D), false);
        charName = EditorGUILayout.TextField("Character name", charName);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output look", EditorStyles.boldLabel);
        settings.pixelate = EditorGUILayout.ToggleLeft("Pixelate (crunchy RO pixel-art look)  —  off = clean hi-res billboard", settings.pixelate);
        settings.cellSize = Mathf.Clamp(EditorGUILayout.IntField("Cell size (px per frame)", settings.cellSize), 16, 512);
        using (new EditorGUI.DisabledScope(settings.pixelate))
            settings.superSample = Mathf.Clamp(EditorGUILayout.IntField("Hi-res supersample (×)", settings.superSample), 1, 8);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Camera / framing", EditorStyles.boldLabel);
        settings.cameraPitch = EditorGUILayout.Slider("Camera pitch (°)", settings.cameraPitch, 0f, 75f);
        settings.framePadding = EditorGUILayout.Slider("Frame padding", settings.framePadding, 1f, 2.5f);
        settings.yawOffset = EditorGUILayout.Slider("Yaw offset (°)", settings.yawOffset, 0f, 360f);
        settings.clockwise = EditorGUILayout.ToggleLeft("Clockwise octant mapping (flip if E/W look swapped)", settings.clockwise);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Animations (one strip set each)", EditorStyles.boldLabel);
        int remove = -1;
        for (int i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            a.action = (CharacterAction)EditorGUILayout.EnumPopup("Action", a.action);
            if (GUILayout.Button("✕", GUILayout.Width(24))) remove = i;
            EditorGUILayout.EndHorizontal();
            a.clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", a.clip, typeof(AnimationClip), false);
            a.fps = Mathf.Clamp(EditorGUILayout.IntField("FPS", a.fps), 4, 60);
            a.frames = Mathf.Max(0, EditorGUILayout.IntField("Frames (0 = auto from length × fps)", a.frames));
            EditorGUILayout.EndVertical();
        }
        if (remove >= 0) actions.RemoveAt(remove);
        if (GUILayout.Button("+ Add animation")) actions.Add(new ActionDef());

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Estimated images: {SpriteBakeCore.EstimateImages(CoreActions())}  " +
                                   $"({SpriteBakeCore.DirNames.Length} dirs × frames × actions)", EditorStyles.miniLabel);

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(model == null))
        {
            if (GUILayout.Button("Bake 8-Dir Sprites", GUILayout.Height(34))) Bake();
            if (GUILayout.Button("Bake + spawn test character in scene", GUILayout.Height(26)))
                if (Bake()) ApplyTestObject();
        }
        if (GUILayout.Button("Spawn test character from last bake")) ApplyTestObject();

        DrawPreview();
        EditorGUILayout.EndScrollView();
    }

    // ----------------------------------------------------------------------------- bake
    bool Bake()
    {
        if (model == null) { EditorUtility.DisplayDialog("Bake 8-Dir", "Assign a rigged model first.", "OK"); return false; }
        var core = CoreActions();
        if (core.Count == 0) { EditorUtility.DisplayDialog("Bake 8-Dir", "Add at least one animation clip.", "OK"); return false; }

        bool ok = SpriteBakeCore.Bake(model, albedo, settings, core, OutDir);
        if (ok)
        {
            RefreshBakedActions();
            LoadPreview();
            Debug.Log($"[unwritten] Baked 8-dir sprites → {OutDir} ({core.Count} actions). " +
                      "Rotate/play it in the Preview panel, or spawn a test character.");
        }
        return ok;
    }

    // ----------------------------------------------------------------------------- apply / test
    void ApplyTestObject()
    {
        if (!Directory.Exists(OutDir)) { EditorUtility.DisplayDialog("Bake 8-Dir", "Bake first.", "OK"); return; }

        var go = new GameObject(SpriteBakeCore.Sanitize(charName) + "_Test");
        var player = Object.FindFirstObjectByType<Player3DController>();
        go.transform.position = player != null ? player.transform.position + player.transform.forward * 2f : Vector3.zero;
        go.transform.localScale = Vector3.one * 1.7f;

        var bb = go.AddComponent<BakedBillboard8>();
        bb.cellW = settings.cellSize; bb.cellH = settings.cellSize; bb.ppu = settings.cellSize;
        bb.maxFrames = SpriteBakeCore.MaxFrames(CoreActions());
        bb.actions = new List<BakedBillboard8.ActionSet>();
        foreach (var a in actions)
            if (a.clip != null)
                bb.actions.Add(new BakedBillboard8.ActionSet
                {
                    action = a.action,
                    dirs = SpriteBakeCore.LoadDir8(OutDir, a.action.ToString()),
                    fps = a.fps,
                });

        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene);
        EditorUtility.DisplayDialog("Bake 8-Dir",
            "Spawned " + go.name + ".\n\nPress Play and move it — it picks Idle / Walk / Run and the 8-direction " +
            "frame automatically. If E/W look swapped, re-bake with 'Clockwise' flipped; if it shows its back, " +
            "nudge 'Yaw offset' by 180.", "OK");
    }

    // ----------------------------------------------------------------------------- preview (rotate + play)
    void DrawPreview()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

        if (bakedActions == null || bakedActions.Length == 0)
        {
            EditorGUILayout.HelpBox("Bake something to preview it here (rotate the 8 directions + play, no Play mode).", MessageType.None);
            if (GUILayout.Button("Refresh from disk")) { RefreshBakedActions(); LoadPreview(); }
            return;
        }

        EditorGUILayout.BeginHorizontal();
        int newIdx = EditorGUILayout.Popup("Action", previewActionIdx, bakedActions);
        if (newIdx != previewActionIdx) { previewActionIdx = newIdx; LoadPreview(); }
        if (GUILayout.Button("⟳", GUILayout.Width(28))) { RefreshBakedActions(); LoadPreview(); }
        EditorGUILayout.EndHorizontal();

        Rect box = GUILayoutUtility.GetRect(220, 220, GUIStyle.none, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(box, new Color(0.16f, 0.16f, 0.18f));
        var frames = (previewFrames != null && previewDir < previewFrames.Length) ? previewFrames[previewDir] : null;
        if (frames != null && frames.Length > 0)
        {
            int idx = previewPlaying ? Mathf.FloorToInt(previewTime * Mathf.Max(1f, previewFps)) % frames.Length : 0;
            var tex = frames[Mathf.Clamp(idx, 0, frames.Length - 1)];
            float sq = Mathf.Min(box.width, box.height);
            var dst = new Rect(box.x + (box.width - sq) * 0.5f, box.y, sq, sq);
            if (tex != null) GUI.DrawTexture(dst, tex, ScaleMode.ScaleToFit, true);
        }
        else EditorGUI.LabelField(box, "no frames for " + SpriteBakeCore.DirNames[previewDir], EditorStyles.centeredGreyMiniLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("◀ Rotate", GUILayout.Height(24))) previewDir = (previewDir + 7) % 8;
        GUILayout.Label(SpriteBakeCore.DirNames[previewDir], EditorStyles.boldLabel, GUILayout.Width(34));
        if (GUILayout.Button("Rotate ▶", GUILayout.Height(24))) previewDir = (previewDir + 1) % 8;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        for (int d = 0; d < SpriteBakeCore.DirNames.Length; d++)
        {
            bool on = d == previewDir;
            if (GUILayout.Toggle(on, SpriteBakeCore.DirNames[d], EditorStyles.miniButton) && !on) previewDir = d;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        previewPlaying = GUILayout.Toggle(previewPlaying, previewPlaying ? "⏸ Pause" : "▶ Play", EditorStyles.miniButton, GUILayout.Width(70));
        previewFps = EditorGUILayout.Slider("Preview FPS", previewFps, 4f, 60f);
        EditorGUILayout.EndHorizontal();
    }

    void RefreshBakedActions()
    {
        var found = new List<string>();
        if (Directory.Exists(OutDir))
            foreach (var f in Directory.GetFiles(OutDir, "*_N.png"))
            {
                string nm = Path.GetFileNameWithoutExtension(f);
                found.Add(nm.Substring(0, nm.Length - 2));   // strip the "_N" suffix
            }
        bakedActions = found.ToArray();
        previewActionIdx = Mathf.Clamp(previewActionIdx, 0, Mathf.Max(0, bakedActions.Length - 1));
    }

    void LoadPreview()
    {
        DisposePreview();
        if (bakedActions == null || bakedActions.Length == 0) return;
        string action = bakedActions[Mathf.Clamp(previewActionIdx, 0, bakedActions.Length - 1)];
        previewFrames = new Texture2D[8][];
        for (int i = 0; i < SpriteBakeCore.DirNames.Length; i++)
        {
            previewFrames[i] = System.Array.Empty<Texture2D>();
            string path = $"{OutDir}/{SpriteBakeCore.Sanitize(action)}_{SpriteBakeCore.DirNames[i]}.png";
            if (!File.Exists(path)) continue;

            var strip = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!strip.LoadImage(File.ReadAllBytes(path))) { Object.DestroyImmediate(strip); continue; }

            int cell = strip.height;
            int count = Mathf.Max(1, strip.width / Mathf.Max(1, cell));
            var cut = new Texture2D[count];
            for (int f = 0; f < count; f++)
            {
                var t = new Texture2D(cell, cell, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                t.SetPixels(strip.GetPixels(f * cell, 0, cell, cell));
                t.Apply();
                cut[f] = t;
            }
            previewFrames[i] = cut;
            Object.DestroyImmediate(strip);
        }
        previewTime = 0f;
    }

    void DisposePreview()
    {
        if (previewFrames == null) return;
        foreach (var dir in previewFrames)
            if (dir != null) foreach (var t in dir) if (t != null) Object.DestroyImmediate(t);
        previewFrames = null;
    }
}
