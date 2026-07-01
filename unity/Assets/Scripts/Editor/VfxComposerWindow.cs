using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit-mode authoring workspace for composed visual effects (<see cref="VfxSequence"/> assets) — the single
/// window that REPLACES the in-Play F8 EffectTuner (single effects) and F9 SequenceTuner (compositions). It runs
/// entirely in edit mode: no Play required. You pick a composition from the left list (or make / duplicate /
/// delete one), lay its effects on a timeline (each with its own scale / xyz offset / speed / start time), and a
/// live preview re-bakes the WHOLE composition every frame via <see cref="ParticleSystem.Simulate"/> so any knob
/// updates the picture instantly — playing or paused. A human-height capsule + ground grid stand in the preview
/// as a true-world-scale ruler, so you size effects against the character, not guesswork.
///
/// <para>DATA MODEL: this edits the existing <see cref="VfxSequence"/> assets — the same inert data the runtime
/// <see cref="VfxDirector"/> plays. Every per-effect value (scale/offset/speed) lives ONLY in the asset; the
/// source library prefab is never touched, so one effect (a fireball) is reusable at many sizes across many
/// compositions. Skills/events/environment all just reference a composition by name.</para>
///
/// <para>PHASES: keep a composition to ONE beat (a travel, or an on-hit) and let code stitch them — code owns
/// timing (it knows when a skill lands). The runtime owns the kill switch + safety cap, not this window.</para>
/// </summary>
public class VfxComposerWindow : EditorWindow
{
    [MenuItem("Tools/unwritten/Art/Vfx Composer")]
    public static void Open()
    {
        var w = GetWindow<VfxComposerWindow>("Vfx Composer");
        w.minSize = new Vector2(880, 520);
        w.Show();
    }

    // ---- asset list ----
    readonly List<VfxSequence> all = new List<VfxSequence>();
    VfxSequence sel;
    Vector2 listScroll, stepScroll;
    string listFilter = "";

    // ---- layout ----
    float previewHeight = 260f;   // draggable via the splitter under the preview
    bool draggingSplitter;

    // ---- transport ----
    bool playing = true;
    float clock;                 // the single time everything renders at
    float previewDuration = 1f;
    double lastUpdate;

    // ---- preview ----
    PreviewRenderUtility pru;
    float camYaw = 35f, camPitch = 30f, camDist = 6f;
    Vector3 camPivot = new Vector3(0f, 1f, 0f);
    bool showRuler = true;
    float rulerHeight = 1.8f;    // human height in world units — the size reference
    GameObject ruler, capsule, ground;
    SpriteRenderer rulerSr;      // real baked-character billboard (falls back to the capsule if none found)
    float rulerAspect = 0.6f;    // sprite width / height, so the quad isn't stretched
    float rulerContentFrac = 1f; // fraction of the cell height the ART actually fills (rest is transparent padding)
    GameObject characterPrefab;  // source of the ruler sprite (a BakedBillboard8 prefab); auto-picked, user-overridable
    Texture2D gridTex;

    class PreviewInst
    {
        public int stepIndex;
        public EffectDef def;
        public GameObject go;
        public ParticleSystem[] roots;
        public float naturalLife;
    }
    readonly List<PreviewInst> preview = new List<PreviewInst>();
    string previewSig;
    float[] starts;
    readonly Dictionary<string, float> evTimes = new Dictionary<string, float>();

    // id picker cache
    string[] idOptions;

    void OnEnable()
    {
        previewHeight = EditorPrefs.GetFloat("VfxComposer.previewHeight", 260f);
        LoadAssets();
        RebuildIdOptions();
        lastUpdate = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }

    void OnDisable()
    {
        EditorApplication.update -= Tick;
        TeardownPreview();
        if (pru != null) { pru.Cleanup(); pru = null; }
        if (gridTex != null) { Object.DestroyImmediate(gridTex); gridTex = null; }
        rulerSr = null; ruler = capsule = ground = null;
    }

    // Advance the clock off the editor's own update loop (edit mode has no Time.deltaTime), then repaint so the
    // preview animates continuously — this is what lets an aura/loop play without entering Play.
    void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        float dt = (float)(now - lastUpdate);
        lastUpdate = now;
        if (sel == null) return;
        if (playing)
        {
            clock += dt;
            if (clock > previewDuration) clock = 0f;   // loop the composition
            Repaint();
        }
    }

    // ---------------------------------------------------------------- assets
    void LoadAssets()
    {
        all.Clear();
        foreach (var guid in AssetDatabase.FindAssets("t:VfxSequence"))
        {
            var a = AssetDatabase.LoadAssetAtPath<VfxSequence>(AssetDatabase.GUIDToAssetPath(guid));
            if (a != null) all.Add(a);
        }
        all.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));
        if (sel != null && !all.Contains(sel)) sel = null;
    }

    void RebuildIdOptions()
    {
        var defs = EffectCatalog.All;
        var list = new List<string> { "(none)" };
        for (int i = 0; i < defs.Count; i++)
            if (defs[i] != null && !string.IsNullOrEmpty(defs[i].id)) list.Add(defs[i].id);
        idOptions = list.ToArray();
    }

    void Select(VfxSequence s)
    {
        sel = s;
        clock = 0f;
        previewSig = null;   // force preview rebuild
    }

    const string Dir = "Assets/Resources/VfxSequences";

    void CreateNew()
    {
        EnsureFolder();
        string path = AssetDatabase.GenerateUniqueAssetPath($"{Dir}/composition.asset");
        var s = CreateInstance<VfxSequence>();
        AssetDatabase.CreateAsset(s, path);
        AssetDatabase.SaveAssets();
        LoadAssets();
        Select(s);
    }

    void Duplicate(VfxSequence s)
    {
        if (s == null) return;
        string src = AssetDatabase.GetAssetPath(s);
        string path = AssetDatabase.GenerateUniqueAssetPath(src);
        AssetDatabase.CopyAsset(src, path);
        AssetDatabase.SaveAssets();
        LoadAssets();
        Select(AssetDatabase.LoadAssetAtPath<VfxSequence>(path));
    }

    void DeleteAsset(VfxSequence s)
    {
        if (s == null) return;
        if (!EditorUtility.DisplayDialog("Delete composition",
            $"Delete '{s.name}'? This cannot be undone.", "Delete", "Cancel")) return;
        string path = AssetDatabase.GetAssetPath(s);
        if (sel == s) sel = null;
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.SaveAssets();
        LoadAssets();
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Dir))
            AssetDatabase.CreateFolder("Assets/Resources", "VfxSequences");
    }

    // ---------------------------------------------------------------- GUI
    void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();
        DrawAssetList();                 // left column
        DrawRight();                     // preview + step editor
        EditorGUILayout.EndHorizontal();
    }

    void DrawAssetList()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(220));
        EditorGUILayout.LabelField("Compositions", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        listFilter = EditorGUILayout.TextField(listFilter);
        if (GUILayout.Button("↻", GUILayout.Width(24))) { LoadAssets(); RebuildIdOptions(); }
        EditorGUILayout.EndHorizontal();

        listScroll = EditorGUILayout.BeginScrollView(listScroll);
        foreach (var s in all)
        {
            if (s == null) continue;
            if (listFilter.Length > 0 && s.name.IndexOf(listFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            bool isSel = s == sel;
            var style = isSel ? EditorStyles.whiteLabel : EditorStyles.label;
            var bg = GUI.backgroundColor;
            if (isSel) GUI.backgroundColor = new Color(0.3f, 0.5f, 0.9f);
            if (GUILayout.Button(s.name, style)) Select(s);
            GUI.backgroundColor = bg;
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("New")) CreateNew();
        using (new EditorGUI.DisabledScope(sel == null))
        {
            if (GUILayout.Button("Dup")) Duplicate(sel);
            if (GUILayout.Button("Del")) DeleteAsset(sel);
        }
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    void DrawRight()
    {
        EditorGUILayout.BeginVertical();
        if (sel == null)
        {
            EditorGUILayout.HelpBox("Pick a composition on the left, or press New.", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        // --- preview panel (height controlled by the splitter below it) ---
        var previewRect = GUILayoutUtility.GetRect(100, previewHeight, GUILayout.ExpandWidth(true), GUILayout.Height(previewHeight));
        DrawPreview(previewRect);
        DrawSplitter();

        // --- transport ---
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button(playing ? "⏸ Pause" : "▶ Play", EditorStyles.toolbarButton, GUILayout.Width(80)))
            playing = !playing;
        if (GUILayout.Button("⏮", EditorStyles.toolbarButton, GUILayout.Width(30))) clock = 0f;
        GUILayout.Label($"{clock:0.00} / {previewDuration:0.00}s", GUILayout.Width(110));
        clock = GUILayout.HorizontalSlider(clock, 0f, Mathf.Max(0.01f, previewDuration));
        GUILayout.Space(8);
        showRuler = GUILayout.Toggle(showRuler, "Ruler", EditorStyles.toolbarButton, GUILayout.Width(50));
        GUILayout.Label("h", GUILayout.Width(12));
        rulerHeight = EditorGUILayout.FloatField(rulerHeight, GUILayout.Width(40));
        EditorGUI.BeginChangeCheck();
        characterPrefab = (GameObject)EditorGUILayout.ObjectField(characterPrefab, typeof(GameObject), false, GUILayout.Width(140));
        if (EditorGUI.EndChangeCheck()) BuildRulerSprite();
        EditorGUILayout.EndHorizontal();

        // --- steps ---
        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField($"Effects ({sel.steps.Count})", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        stepScroll = EditorGUILayout.BeginScrollView(stepScroll);
        int remove = -1, moveUp = -1, moveDown = -1;
        for (int i = 0; i < sel.steps.Count; i++)
        {
            var st = sel.steps[i];
            if (st == null) continue;
            DrawStep(i, st, ref remove, ref moveUp, ref moveDown);
        }
        EditorGUILayout.EndScrollView();

        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(sel, "Edit composition");
            EditorUtility.SetDirty(sel);
        }

        if (remove >= 0) { Undo.RecordObject(sel, "Remove effect"); sel.steps.RemoveAt(remove); EditorUtility.SetDirty(sel); }
        if (moveUp > 0) { Undo.RecordObject(sel, "Reorder"); (sel.steps[moveUp], sel.steps[moveUp - 1]) = (sel.steps[moveUp - 1], sel.steps[moveUp]); EditorUtility.SetDirty(sel); }
        if (moveDown >= 0 && moveDown < sel.steps.Count - 1) { Undo.RecordObject(sel, "Reorder"); (sel.steps[moveDown], sel.steps[moveDown + 1]) = (sel.steps[moveDown + 1], sel.steps[moveDown]); EditorUtility.SetDirty(sel); }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("+ Add effect"))
        {
            Undo.RecordObject(sel, "Add effect");
            sel.steps.Add(new VfxStep { note = "new", kind = VfxStepKind.PlayAt });
            EditorUtility.SetDirty(sel);
        }
        GUILayout.FlexibleSpace();
        var bg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.6f, 1f, 0.6f);
        if (GUILayout.Button("SAVE", GUILayout.Width(120))) { AssetDatabase.SaveAssets(); }
        GUI.backgroundColor = bg;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    // Thin draggable bar under the preview: drag to trade height between the preview and the step editor.
    void DrawSplitter()
    {
        var r = GUILayoutUtility.GetRect(0, 6, GUILayout.ExpandWidth(true), GUILayout.Height(6));
        EditorGUI.DrawRect(r, new Color(0f, 0f, 0f, 0.35f));
        var mid = new Rect(r.x + r.width * 0.5f - 14, r.y + 2, 28, 2);
        EditorGUI.DrawRect(mid, new Color(1f, 1f, 1f, 0.25f));   // grip hint
        EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeVertical);

        var e = Event.current;
        if (e.type == EventType.MouseDown && r.Contains(e.mousePosition)) { draggingSplitter = true; e.Use(); }
        if (draggingSplitter)
        {
            if (e.type == EventType.MouseDrag)
            {
                previewHeight = Mathf.Clamp(previewHeight + e.delta.y, 120f, position.height - 180f);
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp)
            {
                draggingSplitter = false;
                EditorPrefs.SetFloat("VfxComposer.previewHeight", previewHeight);
                e.Use();
            }
        }
    }

    // Distinct, low-saturation tints cycled per effect so each block's parameters read as one group. Kept dark so
    // the default control skin stays legible on top.
    static readonly Color[] StepTints =
    {
        new Color(0.20f, 0.28f, 0.40f), // blue
        new Color(0.22f, 0.36f, 0.26f), // green
        new Color(0.38f, 0.28f, 0.20f), // amber
        new Color(0.34f, 0.24f, 0.38f), // purple
        new Color(0.40f, 0.22f, 0.24f), // red
        new Color(0.22f, 0.36f, 0.38f), // teal
    };

    void DrawStep(int i, VfxStep st, ref int remove, ref int moveUp, ref int moveDown)
    {
        var tint = StepTints[i % StepTints.Length];
        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = tint;
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUI.backgroundColor = prevBg;

        EditorGUILayout.BeginHorizontal();
        var chip = GUILayoutUtility.GetRect(26, 16, GUILayout.Width(26));
        EditorGUI.DrawRect(chip, tint * 1.6f);
        EditorGUI.LabelField(chip, $"#{i}", EditorStyles.whiteBoldLabel);
        st.note = EditorGUILayout.TextField(st.note ?? "", GUILayout.Width(140));
        st.kind = (VfxStepKind)EditorGUILayout.EnumPopup(st.kind, GUILayout.Width(90));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("↑", GUILayout.Width(24))) moveUp = i;
        if (GUILayout.Button("↓", GUILayout.Width(24))) moveDown = i;
        if (GUILayout.Button("✕", GUILayout.Width(24))) remove = i;
        EditorGUILayout.EndHorizontal();

        // schedule
        EditorGUILayout.BeginHorizontal();
        st.schedule = (VfxSchedule)EditorGUILayout.EnumPopup(st.schedule, GUILayout.Width(90));
        if (st.schedule == VfxSchedule.AtTime)
            st.delay = EditorGUILayout.Slider("start (s)", st.delay, 0f, 6f);
        else
            st.startEvent = EditorGUILayout.TextField("on event", st.startEvent ?? "");
        EditorGUILayout.EndHorizontal();

        // anchor + offset
        st.anchor = (VfxAnchor)EditorGUILayout.EnumPopup("anchor", st.anchor);
        st.offset = EditorGUILayout.Vector3Field("offset", st.offset);

        switch (st.kind)
        {
            case VfxStepKind.PlayAt:
            case VfxStepKind.Attach:
                st.effectId = IdPopup("effect", st.effectId);
                st.scale = EditorGUILayout.Slider("scale", st.scale <= 0f ? 1f : st.scale, 0.05f, 5f);
                if (st.kind == VfxStepKind.PlayAt)
                {
                    st.speed = EditorGUILayout.Slider("speed", st.speed <= 0f ? 1f : st.speed, 0.1f, 3f);
                    st.rotationEuler = EditorGUILayout.Vector3Field("rotation", st.rotationEuler);
                }
                break;
            case VfxStepKind.Carrier:
                st.effectId = IdPopup("effect", st.effectId);
                st.scale = EditorGUILayout.Slider("scale", st.scale <= 0f ? 1f : st.scale, 0.05f, 5f);
                st.carrierFrom = EditorGUILayout.Vector3Field("launch from", st.carrierFrom);
                st.carrierScatter = EditorGUILayout.Slider("scatter", st.carrierScatter, 0f, 3f);
                st.carrierSpeed = EditorGUILayout.Slider("fly speed", st.carrierSpeed, 1f, 40f);
                st.carrierMaxLife = EditorGUILayout.Slider("safety cap (s)", st.carrierMaxLife, 0.2f, 6f);
                break;
            case VfxStepKind.Shake:
                st.shakeStrength = EditorGUILayout.Slider("strength", st.shakeStrength, 0f, 2f);
                st.shakeDuration = EditorGUILayout.Slider("duration", st.shakeDuration, 0f, 1f);
                break;
            case VfxStepKind.Sound:
                if (st.sound == null) st.sound = new SfxSet();
                st.sound.volume = EditorGUILayout.Slider("volume", st.sound.volume, 0f, 1f);
                var clips = st.sound.clips ?? System.Array.Empty<AudioClip>();
                for (int c = 0; c < clips.Length; c++)
                    clips[c] = (AudioClip)EditorGUILayout.ObjectField($"clip {c}", clips[c], typeof(AudioClip), false);
                st.sound.clips = clips;
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+ clip"))
                {
                    var n = new AudioClip[clips.Length + 1];
                    System.Array.Copy(clips, n, clips.Length);
                    st.sound.clips = n;
                }
                using (new EditorGUI.DisabledScope(clips.Length == 0))
                    if (GUILayout.Button("- clip"))
                    {
                        var n = new AudioClip[clips.Length - 1];
                        System.Array.Copy(clips, n, n.Length);
                        st.sound.clips = n;
                    }
                EditorGUILayout.EndHorizontal();
                break;
        }

        st.emitEvent = EditorGUILayout.TextField("emit event", st.emitEvent ?? "");
        EditorGUILayout.EndVertical();
    }

    // Effect-id dropdown built from the EffectLibrary — pick a valid id (no typos → no silent no-op).
    string IdPopup(string label, string current)
    {
        if (idOptions == null || idOptions.Length == 0) RebuildIdOptions();
        int idx = 0;
        if (!string.IsNullOrEmpty(current))
        {
            idx = System.Array.IndexOf(idOptions, current);
            if (idx < 0) idx = 0;
        }
        int picked = EditorGUILayout.Popup(label, idx, idOptions);
        return picked <= 0 ? "" : idOptions[picked];
    }

    // ---------------------------------------------------------------- preview engine
    void EnsurePru()
    {
        if (pru != null) return;
        pru = new PreviewRenderUtility();
        pru.camera.clearFlags = CameraClearFlags.SolidColor;
        pru.camera.backgroundColor = new Color(0.13f, 0.13f, 0.16f);
        pru.camera.nearClipPlane = 0.05f;
        pru.camera.farClipPlane = 200f;
        pru.camera.fieldOfView = 45f;
        pru.lights[0].intensity = 1.1f;
        pru.lights[0].transform.rotation = Quaternion.Euler(35f, 40f, 0f);
        if (pru.lights.Length > 1) pru.lights[1].intensity = 0.4f;

        var sprite = Shader.Find("Sprites/Default");   // unlit + alpha + double-sided; URP-safe (used game-wide)

        // Ground: a 6x6 m grid so the floor reads as a floor and depth/scale is legible (1 m cells). MUST be an
        // OPAQUE, depth-writing material — a transparent (ZWrite-off) floor draws OVER the character/effect by
        // sort order instead of occluding by real depth (the "everything is behind the grid" bug).
        if (gridTex == null) gridTex = MakeGridTexture(512, 6, new Color(0.16f, 0.16f, 0.20f), new Color(0.32f, 0.34f, 0.40f));
        ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = Vector3.one * 0.6f;         // Plane primitive is 10 m at scale 1 → 6 m here
        Object.DestroyImmediate(ground.GetComponent<Collider>());
        var groundShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
        var gm = new Material(groundShader);
        gm.mainTexture = gridTex;
        if (gm.HasProperty("_BaseMap")) gm.SetTexture("_BaseMap", gridTex);   // URP/Unlit samples _BaseMap
        ground.GetComponent<Renderer>().sharedMaterial = gm;
        pru.AddSingleGO(ground);

        // Capsule fallback ruler (used only if no baked character is found/assigned).
        capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.DestroyImmediate(capsule.GetComponent<Collider>());
        capsule.GetComponent<Renderer>().sharedMaterial = new Material(sprite) { color = new Color(0.5f, 0.55f, 0.6f, 1f) };
        pru.AddSingleGO(capsule);

        // Real character billboard: a bottom-pivoted quad showing a baked character's South (camera-facing) frame.
        ruler = new GameObject("CharacterRuler");
        rulerSr = ruler.AddComponent<SpriteRenderer>();
        pru.AddSingleGO(ruler);
        BuildRulerSprite();
    }

    // Auto-pick a baked character (first prefab with a BakedBillboard8 that has usable strips) unless the user
    // assigned one. Slices its Idle→South frame 0 into a bottom-pivoted sprite so feet sit on the ground.
    void BuildRulerSprite()
    {
        if (rulerSr == null) return;
        var bb = ResolveCharacter();
        if (bb == null) { rulerSr.sprite = null; return; }

        BakedBillboard8.ActionSet set = null;
        foreach (var a in bb.actions)
        {
            if (a == null || !a.dirs.Any) continue;
            if (a.action == CharacterAction.Idle) { set = a; break; }
            set ??= a;
        }
        if (set == null) { rulerSr.sprite = null; return; }

        var strips = set.dirs.strips;
        Texture2D tex = (strips != null && strips.Length > 4) ? strips[4] : null;   // 4 = South (faces camera)
        if (tex == null) for (int i = 0; strips != null && i < strips.Length && tex == null; i++) tex = strips[i];
        if (tex == null) { rulerSr.sprite = null; return; }

        int cw = Mathf.Max(1, bb.cellW), ch = Mathf.Max(1, bb.cellH), ppu = bb.ppu > 0 ? bb.ppu : ch;
        cw = Mathf.Min(cw, tex.width);

        // Ground on the ART, not the padded cell: find the lowest/highest opaque rows in frame 0 and pivot at the
        // feet, scaling to the real content height. Uses a GPU readback (a temp RenderTexture) so it works even
        // when the baked strip isn't Read/Write-enabled.
        float pivotY = 0f; rulerContentFrac = 1f;
        if (TryOpaqueRows(tex, cw, ch, out int feet, out int head))
        {
            pivotY = feet / (float)ch;                        // transform origin at the feet row → feet at y=0
            rulerContentFrac = (head - feet + 1) / (float)ch; // scale the ART (not padding) to rulerHeight
        }
        rulerSr.sprite = Sprite.Create(tex, new Rect(0, 0, cw, ch), new Vector2(0.5f, pivotY), ppu);
        rulerAspect = (float)cw / ch;
    }

    BakedBillboard8 ResolveCharacter()
    {
        if (characterPrefab != null)
        {
            var c = characterPrefab.GetComponent<BakedBillboard8>();
            if (c != null) return c;
        }
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            var bb = go != null ? go.GetComponent<BakedBillboard8>() : null;
            if (bb == null) continue;
            foreach (var a in bb.actions) if (a != null && a.dirs.Any) { characterPrefab = go; return bb; }
        }
        return null;
    }

    // Lowest (feet) and highest (head) opaque pixel rows of the frame-0 block, via a GPU readback so it works on
    // non-readable baked strips. Rows counted from the bottom. False if the frame is fully transparent/failed.
    static bool TryOpaqueRows(Texture2D tex, int cw, int ch, out int feet, out int head)
    {
        feet = -1; head = -1;
        var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
        var prev = RenderTexture.active;
        try
        {
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var tmp = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
            tmp.ReadPixels(new Rect(0, 0, cw, ch), 0, 0);   // frame 0 sits at the strip's bottom-left
            tmp.Apply();
            var px = tmp.GetPixels();
            for (int y = 0; y < ch; y++)
                for (int x = 0; x < cw; x++)
                    if (px[y * cw + x].a > 0.15f) { if (feet < 0) feet = y; head = y; break; }
            Object.DestroyImmediate(tmp);
        }
        catch { }
        finally { RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); }
        return feet >= 0 && head >= feet;
    }

    // A grid image drawn once: dark cells, lighter 1-px lines on the shared edges (so N lines = N cells across).
    static Texture2D MakeGridTexture(int size, int cells, Color bg, Color line)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        int step = size / cells;
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool onLine = (x % step) < 2 || (y % step) < 2 || x >= size - 2 || y >= size - 2;
                px[y * size + x] = onLine ? line : bg;
            }
        t.SetPixels(px);
        t.Apply();
        return t;
    }

    void DrawPreview(Rect r)
    {
        EnsurePru();
        HandleCameraInput(r);

        // Place the camera first, so the character billboard can face it this frame.
        var camRot = Quaternion.Euler(camPitch, camYaw, 0f);
        pru.camera.transform.position = camPivot - (camRot * Vector3.forward) * camDist;
        pru.camera.transform.rotation = camRot;

        // Ruler at true human height. Prefer the real character billboard; fall back to the capsule if none.
        bool haveChar = rulerSr != null && rulerSr.sprite != null;
        if (ruler != null)
        {
            ruler.SetActive(showRuler && haveChar);
            if (showRuler && haveChar)
            {
                float nativeH = rulerSr.sprite.bounds.size.y * rulerContentFrac; // world height of the ART at 1× scale
                float s = nativeH > 1e-4f ? rulerHeight / nativeH : 1f;
                ruler.transform.localScale = Vector3.one * s;                 // bottom pivot → feet stay at y=0
                ruler.transform.position = Vector3.zero;
                Vector3 toCam = pru.camera.transform.position - ruler.transform.position; toCam.y = 0f;
                if (toCam.sqrMagnitude > 1e-6f) ruler.transform.rotation = Quaternion.LookRotation(toCam);
            }
        }
        if (capsule != null)
        {
            capsule.SetActive(showRuler && !haveChar);
            capsule.transform.localScale = new Vector3(rulerHeight * 0.28f, rulerHeight * 0.5f, rulerHeight * 0.28f);
            capsule.transform.position = new Vector3(0f, rulerHeight * 0.5f, 0f);
        }

        EnsurePreview();
        RenderPreviewAt(clock);

        pru.BeginPreview(r, GUIStyle.none);
        pru.camera.Render();
        var tex = pru.EndPreview();
        GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);
    }

    void HandleCameraInput(Rect r)
    {
        var e = Event.current;
        if (!r.Contains(e.mousePosition)) return;
        if (e.type == EventType.MouseDrag && e.button == 0)
        {
            camYaw += e.delta.x * 0.5f;
            camPitch = Mathf.Clamp(camPitch - e.delta.y * 0.5f, -20f, 80f);
            e.Use(); Repaint();
        }
        else if (e.type == EventType.ScrollWheel)
        {
            camDist = Mathf.Clamp(camDist + e.delta.y * 0.25f, 1.5f, 40f);
            e.Use(); Repaint();
        }
    }

    // Rebuild the preview instances only when structure (step count / kind / effect id) changes.
    string Signature()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(sel.name).Append('#').Append(sel.steps.Count).Append('|');
        for (int i = 0; i < sel.steps.Count; i++)
        {
            var st = sel.steps[i];
            if (st == null) { sb.Append("n;"); continue; }
            sb.Append((int)st.kind).Append(':').Append(st.effectId).Append(';');
        }
        return sb.ToString();
    }

    void EnsurePreview()
    {
        if (previewSig != Signature()) RebuildPreview();
    }

    void RebuildPreview()
    {
        TeardownPreview();
        for (int i = 0; i < sel.steps.Count; i++)
        {
            var st = sel.steps[i];
            if (st == null) continue;
            if (st.kind != VfxStepKind.PlayAt && st.kind != VfxStepKind.Attach && st.kind != VfxStepKind.Carrier) continue;
            var def = EffectCatalog.Get(st.effectId);
            if (def == null || def.prefab == null) continue;

            var go = Object.Instantiate(def.prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            var systems = go.GetComponentsInChildren<ParticleSystem>(true);
            var roots = new List<ParticleSystem>();
            float natural = def.lifetime > 0f ? def.lifetime : 0f;
            for (int j = 0; j < systems.Length; j++)
            {
                var ps = systems[j];
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.useAutoRandomSeed = false;          // deterministic: same T → same frame (no churn when frozen)
                ps.randomSeed = 0x5EED + (uint)j;
                var m = ps.main;
                m.scalingMode = ParticleSystemScalingMode.Hierarchy;   // root localScale must resize children
                if (def.lifetime <= 0f) natural = Mathf.Max(natural, m.duration + m.startLifetime.constantMax);
                if (!HasParentSystem(ps.transform, go.transform)) roots.Add(ps);
            }
            if (natural <= 0f) natural = 1f;

            pru.AddSingleGO(go);   // move into the preview scene
            preview.Add(new PreviewInst { stepIndex = i, def = def, go = go, roots = roots.ToArray(), naturalLife = natural });
        }
        previewSig = Signature();
    }

    static bool HasParentSystem(Transform t, Transform stop)
    {
        for (var p = t.parent; p != null && p != stop; p = p.parent)
            if (p.GetComponent<ParticleSystem>() != null) return true;
        return false;
    }

    void TeardownPreview()
    {
        for (int i = 0; i < preview.Count; i++)
            if (preview[i].go != null) Object.DestroyImmediate(preview[i].go);
        preview.Clear();
        previewSig = null;
    }

    static float CarrierFlight(VfxStep st)
    {
        float dist = st.carrierFrom.magnitude;
        return Mathf.Min(dist / Mathf.Max(0.1f, st.carrierSpeed), st.carrierMaxLife);
    }

    // Resolve each step's absolute start time (AtTime = delay; OnEvent = when its tag fires; a carrier emits on
    // arrival = start + flight). Iterated so event chains settle.
    void ResolveStarts(VfxSequence seq)
    {
        int n = seq.steps.Count;
        if (starts == null || starts.Length < n) starts = new float[n];
        for (int i = 0; i < n; i++) starts[i] = float.PositiveInfinity;
        evTimes.Clear();

        for (int pass = 0; pass < n + 2; pass++)
        {
            bool changed = false;
            for (int i = 0; i < n; i++)
            {
                var st = seq.steps[i];
                if (st == null) continue;
                float s;
                if (st.schedule == VfxSchedule.AtTime) s = Mathf.Max(0f, st.delay);
                else s = (!string.IsNullOrEmpty(st.startEvent) && evTimes.TryGetValue(st.startEvent, out var et))
                         ? et : float.PositiveInfinity;
                if (s < starts[i]) { starts[i] = s; changed = true; }
                if (!float.IsInfinity(s) && !string.IsNullOrEmpty(st.emitEvent))
                {
                    float ev = s + (st.kind == VfxStepKind.Carrier ? CarrierFlight(st) : 0f);
                    if (!evTimes.TryGetValue(st.emitEvent, out var cur) || ev < cur) { evTimes[st.emitEvent] = ev; changed = true; }
                }
            }
            if (!changed) break;
        }
    }

    // Render the whole composition frozen at absolute time T (re-baked every call, so edits show live). Anchor is
    // the origin (character feet at world 0); offset + def.offset position each effect against the ruler.
    void RenderPreviewAt(float T)
    {
        int n = sel.steps.Count;
        ResolveStarts(sel);

        float dur = 0f;
        for (int k = 0; k < preview.Count; k++)
        {
            var inst = preview[k];
            if (inst.stepIndex >= n) continue;
            var st = sel.steps[inst.stepIndex];
            if (st == null) continue;
            float s = starts[inst.stepIndex];
            if (float.IsInfinity(s)) continue;
            float window = st.kind == VfxStepKind.Carrier ? CarrierFlight(st) : inst.naturalLife;
            dur = Mathf.Max(dur, s + window);
        }
        previewDuration = Mathf.Max(0.01f, dur);
        T = Mathf.Clamp(T, 0f, previewDuration);

        for (int k = 0; k < preview.Count; k++)
        {
            var inst = preview[k];
            if (inst.go == null) continue;
            if (inst.stepIndex >= n) { if (inst.go.activeSelf) inst.go.SetActive(false); continue; }
            var st = sel.steps[inst.stepIndex];
            if (st == null) { if (inst.go.activeSelf) inst.go.SetActive(false); continue; }

            float s = starts[inst.stepIndex];
            float window = st.kind == VfxStepKind.Carrier ? CarrierFlight(st) : inst.naturalLife;
            float age = T - s;
            if (float.IsInfinity(s) || age < 0f || age > window)
            {
                if (inst.go.activeSelf) inst.go.SetActive(false);
                continue;
            }

            Vector3 anchorPt = st.offset + inst.def.offset;   // origin == target == world 0 in the composer
            float scale = inst.def.scale * (st.scale <= 0f ? 1f : st.scale);
            float speed = st.kind == VfxStepKind.PlayAt ? (st.speed <= 0f ? 1f : st.speed) : 1f;

            Vector3 pos = anchorPt;
            if (st.kind == VfxStepKind.Carrier)
            {
                float flight = Mathf.Max(0.0001f, window);
                Vector3 from = anchorPt + st.carrierFrom;
                pos = Vector3.Lerp(from, anchorPt, Mathf.Clamp01(age / flight));
            }

            if (!inst.go.activeSelf) inst.go.SetActive(true);
            inst.go.transform.position = pos;
            inst.go.transform.rotation = Quaternion.Euler(st.rotationEuler);
            inst.go.transform.localScale = Vector3.one * Mathf.Max(0.001f, scale);

            float simT = Mathf.Max(0.0001f, age * speed);
            for (int j = 0; j < inst.roots.Length; j++)
                if (inst.roots[j] != null) inst.roots[j].Simulate(simT, true, true, true);
        }
    }
}
