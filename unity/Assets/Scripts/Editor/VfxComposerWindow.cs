using System.Collections.Generic;
using System.Reflection;
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
    [MenuItem("Tools/unwritten/Composers/Vfx Composer")]
    public static void Open()
    {
        var w = GetWindow<VfxComposerWindow>("Vfx Composer");
        w.minSize = new Vector2(40, 520);
        w.Show();
    }

    // "Vfx Composer — Knob Reference" doc in Tempo Docs. If this URL scheme is wrong, replace it with the doc's
    // actual share link (docId: q177cgr85qm4kgaj3taq96yfzx89qcy9).
    const string GuideUrl = "https://app.tempo.new/docs/q177cgr85qm4kgaj3taq96yfzx89qcy9";

    // ---- asset list ----
    readonly List<VfxSequence> all = new List<VfxSequence>();
    VfxSequence sel;
    Vector2 listScroll, stepScroll;
    bool listCollapsed;
    string listFilter = "";
    float listWidth = 220f;
    bool draggingListSplitter;

    // ---- layout ----
    float previewHeight = 260f;   // draggable via the splitter under the preview
    bool draggingSplitter;

    // ---- transport ----
    bool playing = true;
    float clock;                 // the single time everything renders at
    float previewDuration = 1f;  // the loop length: where the clock resets (= loop-end marker, or autoDuration if unset)
    float autoDuration = 1f;     // full content length (all clips finished) — the timeline's x-scale
    bool draggingLoopMark;
    double lastUpdate;
    int loopSeed;                // bumped each loop → carrier scatter re-rolls per playback cycle

    // ---- preview ----
    PreviewRenderUtility pru;
    const float DefYaw = 35f, DefPitch = 30f, DefDist = 6f;
    static readonly Vector3 DefPivot = new Vector3(0f, 1f, 0f);
    float camYaw = DefYaw, camPitch = DefPitch, camDist = DefDist;
    Vector3 camPivot = DefPivot;
    GUIStyle compassStyle;
    bool showRuler = true;
    float rulerHeight = 0.3f;    // visible in-game character height in world units — the size reference
    bool showRadiusRing;
    // SINGLE SOURCE OF TRUTH: the AoE radius is owned by SkillTickBinding.radius (Skill Composer). This window does
    // NOT hold its own gameplay radius — it READS every authoritative radius of the SkillDefs that reference the
    // previewed sequence and draws a ring per value, so the artist sizes the VFX against the real number, never a
    // hand-typed copy. `manualRingR` is only a fallback ruler for a sequence no skill references yet.
    float manualRingR = 1.5f;
    readonly List<float> skillRadii = new List<float>();   // distinct radii read from referencing SkillDefs (read-only mirror)
    GameObject capsule, ground, radiusRing;
    readonly List<LineRenderer> radiusRingLrs = new List<LineRenderer>();
    Material ringMat;
    Texture2D gridTex;

    class PreviewInst
    {
        public int stepIndex;
        public int slot;          // 0 = this loop's spawn; k>0 = the occurrence spawned k loops ago (its still-playing tail)
        public EffectDef def;
        public GameObject go;
        public ParticleSystem[] roots;
        public float naturalLife;
    }
    const int PreviewSlots = 4;   // overlapping copies a PlayAt step can show across the loop-end marker
    readonly List<PreviewInst> preview = new List<PreviewInst>();
    bool previewHasMeshParticles;   // any previewed effect uses Mesh render mode (won't draw under Simulate) → show a hint
    string previewSig;
    float[] starts;
    readonly Dictionary<string, float> evTimes = new Dictionary<string, float>();

    // id picker cache
    string[] idOptions;

    // scratch reused by the emit-id dedup + "On Event" dropdown (editor-only; alloc here is fine)
    readonly List<string> emitIdScratch = new List<string>();
    readonly List<string> eventValScratch = new List<string>();
    readonly List<string> eventLabelScratch = new List<string>();

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
        StopPreviewSounds();
        TeardownPreview();
        if (pru != null) { pru.Cleanup(); pru = null; }
        if (gridTex != null) { Object.DestroyImmediate(gridTex); gridTex = null; }
        if (ringMat != null) { Object.DestroyImmediate(ringMat); ringMat = null; }
        capsule = ground = radiusRing = null;
        radiusRingLrs.Clear();
    }

    // Re-read the authoritative radii when returning to this window (they may have been edited in the Skill Composer).
    void OnFocus() => RefreshSkillRadii();

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
            float prev = clock;
            clock += dt;
            // Fire Sound steps whose start time we just crossed (edge-triggered), so you actually hear the sfx while
            // the preview plays. Only when playing forward — scrubbing the slider stays silent.
            if (clock > previewDuration) { TriggerPreviewSounds(prev, previewDuration); clock = 0f; loopSeed++; }
            else TriggerPreviewSounds(prev, clock);
            Repaint();
        }
    }

    // Play any Sound step whose scheduled start falls in (a, b]. Uses the editor's built-in audio preview so it
    // works in edit mode (no Play). A random clip from the set, mirroring the runtime's pick.
    void TriggerPreviewSounds(float a, float b)
    {
        if (sel == null) return;
        ResolveStarts(sel);
        for (int i = 0; i < sel.steps.Count && i < starts.Length; i++)
        {
            var st = sel.steps[i];
            if (st == null || st.kind != VfxStepKind.Sound) continue;
            if (st.sound == null || st.sound.clips == null || st.sound.clips.Length == 0) continue;
            float s = starts[i];
            if (float.IsInfinity(s) || s < a || s >= b) continue;   // half-open [a,b) so a step at t=0 fires
            var clips = st.sound.clips;
            PlayClipPreview(clips[Random.Range(0, clips.Length)]);
        }
    }

    // Reflection into UnityEditor.AudioUtil (internal) to audition a clip in edit mode. Method name differs across
    // Unity versions, so try the known ones.
    static MethodInfo audioPreview;
    static bool audioPreviewResolved;
    static void PlayClipPreview(AudioClip clip)
    {
        if (clip == null) return;
        if (!audioPreviewResolved)
        {
            audioPreviewResolved = true;
            var t = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
            audioPreview = t?.GetMethod("PlayPreviewClip", new[] { typeof(AudioClip), typeof(int), typeof(bool) })
                        ?? t?.GetMethod("PlayClip", new[] { typeof(AudioClip), typeof(int), typeof(bool) })
                        ?? t?.GetMethod("PlayClip", new[] { typeof(AudioClip) });
            // Version-agnostic fallback: any static Play* whose first parameter is an AudioClip.
            if (audioPreview == null && t != null)
                foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    var ps = m.GetParameters();
                    if (m.Name.StartsWith("Play") && ps.Length >= 1 && ps[0].ParameterType == typeof(AudioClip))
                    { audioPreview = m; break; }
                }
            if (audioPreview == null) Debug.LogWarning("VfxComposer: couldn't find UnityEditor.AudioUtil play method — sound preview disabled.");
        }
        if (audioPreview == null) return;
        var pars = audioPreview.GetParameters();
        var args = new object[pars.Length];
        args[0] = clip;
        for (int i = 1; i < pars.Length; i++)                      // fill remaining params with sane defaults
            args[i] = pars[i].ParameterType == typeof(bool) ? (object)false : System.Activator.CreateInstance(pars[i].ParameterType);
        audioPreview.Invoke(null, args);
    }

    // Edit-mode audio-preview transport, tied to the timeline (reflection into UnityEditor.AudioUtil).
    //   Pause/Resume  = true pause-from-position: the clip freezes and continues where it left off (⏸ / ▶).
    //   Stop          = cut everything (restart ⏮ / window close), so the next play starts clean.
    static MethodInfo audioPause, audioResume, audioStopAll;
    static bool audioTransportResolved;
    static void ResolveAudioTransport()
    {
        if (audioTransportResolved) return;
        audioTransportResolved = true;
        var t = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        audioPause = t?.GetMethod("PausePreviewClip", System.Type.EmptyTypes);
        audioResume = t?.GetMethod("ResumePreviewClip", System.Type.EmptyTypes);
        audioStopAll = t?.GetMethod("StopAllPreviewClips", System.Type.EmptyTypes)
                    ?? t?.GetMethod("StopAllClips", System.Type.EmptyTypes);
    }
    static void PausePreviewSounds() { ResolveAudioTransport(); audioPause?.Invoke(null, null); }
    static void ResumePreviewSounds() { ResolveAudioTransport(); audioResume?.Invoke(null, null); }
    static void StopPreviewSounds() { ResolveAudioTransport(); audioStopAll?.Invoke(null, null); }

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
        RefreshSkillRadii();
    }

    // Read the authoritative AoE radii (SkillTickBinding.radius) from every SkillDef that references the previewed
    // sequence. The Skill Composer OWNS this number; here we only mirror it, so the preview ring can never disagree
    // with the gameplay radius. Area-scope bindings only (Single ignores radius). Cheap editor-time asset scan; the
    // ring is a design aid, not a runtime path.
    void RefreshSkillRadii()
    {
        skillRadii.Clear();
        if (sel == null) return;
        foreach (var guid in AssetDatabase.FindAssets("t:SkillDef"))
        {
            var sd = AssetDatabase.LoadAssetAtPath<SkillDef>(AssetDatabase.GUIDToAssetPath(guid));
            if (sd == null || sd.vfx != sel || sd.bindings == null) continue;
            foreach (var b in sd.bindings)
            {
                if (b == null || b.scope != SkillScope.Area) continue;
                float rr = b.radius <= 0f ? 1.5f : b.radius;
                if (!skillRadii.Contains(rr)) skillRadii.Add(rr);
            }
        }
        skillRadii.Sort();
    }

    const string Dir = "Assets/3_Data/Resources/VfxSequences";

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

    // Rename the selected composition's asset file (the name IS the id skills/events reference it by).
    void RenameSelected(string newName)
    {
        if (sel == null) return;
        newName = newName?.Trim();
        if (string.IsNullOrEmpty(newName) || newName == sel.name) return;
        string err = AssetDatabase.RenameAsset(AssetDatabase.GetAssetPath(sel), newName);
        if (!string.IsNullOrEmpty(err)) { Debug.LogWarning($"Vfx Composer: rename failed — {err}"); return; }
        AssetDatabase.SaveAssets();
        LoadAssets();
        RebuildIdOptions();
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
        if (!AssetDatabase.IsValidFolder("Assets/3_Data/Resources"))
            AssetDatabase.CreateFolder("Assets/3_Data", "Resources");
        if (!AssetDatabase.IsValidFolder(Dir))
            AssetDatabase.CreateFolder("Assets/3_Data/Resources", "VfxSequences");
    }

    // ---------------------------------------------------------------- GUI
    void OnGUI()
    {
        EditorGUILayout.BeginHorizontal();
        DrawAssetList();                 // left column
        if (!listCollapsed) DrawListSplitter();
        DrawRight();                     // preview + step editor
        EditorGUILayout.EndHorizontal();
    }

    void DrawAssetList()
    {
        if (listCollapsed)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(20));
            if (GUILayout.Button("▶", GUILayout.Width(20), GUILayout.Height(40))) listCollapsed = false;
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.BeginVertical(GUILayout.Width(listWidth), GUILayout.MinWidth(0f), GUILayout.MaxWidth(listWidth));
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Compositions", EditorStyles.boldLabel, GUILayout.MinWidth(0f));
        if (GUILayout.Button("◀", GUILayout.Width(24))) listCollapsed = true;
        EditorGUILayout.EndHorizontal();

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
            if (GUILayout.Button(s.name, style, GUILayout.MinWidth(0f))) Select(s);
            GUI.backgroundColor = bg;
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("New", GUILayout.MinWidth(0f))) CreateNew();
        using (new EditorGUI.DisabledScope(sel == null))
        {
            if (GUILayout.Button("Dup", GUILayout.MinWidth(0f))) Duplicate(sel);
            if (GUILayout.Button("Del", GUILayout.MinWidth(0f))) DeleteAsset(sel);
        }
        if (GUILayout.Button("?", GUILayout.Width(24))) Application.OpenURL(GuideUrl);
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();
    }

    // Draggable divider between the list and the body — drag to resize, no minimum width.
    void DrawListSplitter()
    {
        float w = EditorLayoutFlow.VerticalSplitter(listWidth, ref draggingListSplitter);
        if (w != listWidth) { listWidth = w; Repaint(); }
    }

    void DrawRight()
    {
        EditorGUILayout.BeginVertical(GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
        if (sel == null)
        {
            EditorGUILayout.HelpBox("Pick a composition on the left, or press New.", MessageType.Info);
            EditorGUILayout.EndVertical();
            return;
        }

        // Scale the label column with the available content width so labelled rows shrink instead of the label
        // column holding a fixed ~150px floor.
        float contentWidth = position.width - (listCollapsed ? 20f : listWidth) - 5f;
        EditorLayoutFlow.ResponsiveLabelWidth(contentWidth);

        // --- name (rename the asset by typing here + Enter) ---
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("Name", GUILayout.MinWidth(0f), GUILayout.MaxWidth(40));
        EditorGUI.BeginChangeCheck();
        string typed = EditorGUILayout.DelayedTextField(sel.name);
        if (EditorGUI.EndChangeCheck()) RenameSelected(typed);
        EditorGUILayout.EndHorizontal();

        // --- preview panel (height controlled by the splitter below it) ---
        var previewRect = GUILayoutUtility.GetRect(100, previewHeight, GUILayout.ExpandWidth(true), GUILayout.Height(previewHeight));
        DrawPreview(previewRect);
        DrawSplitter();

        // --- transport (flex-wrap; see EditorLayoutFlow.Flow) ---
        using (var flow = new EditorLayoutFlow.Flow(contentWidth, EditorStyles.toolbar))
        {
            flow.Item(80);
            if (GUILayout.Button(playing ? "⏸ Pause" : "▶ Play", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                playing = !playing;
                if (playing) ResumePreviewSounds(); else PausePreviewSounds();   // clock and sound pause/resume together
            }
            flow.Item(30);
            if (GUILayout.Button("⏮", EditorStyles.toolbarButton, GUILayout.Width(30))) { clock = 0f; StopPreviewSounds(); }
            flow.Item(110);
            GUILayout.Label($"{clock:0.00} / {previewDuration:0.00}s", GUILayout.Width(110));
            flow.Item(140);
            clock = GUILayout.HorizontalSlider(clock, 0f, Mathf.Max(0.01f, previewDuration), GUILayout.Width(140));

            flow.Item(52 + 45);   // "loop end" label + field kept together
            GUILayout.Label("loop end", GUILayout.Width(52));
            EditorGUI.BeginChangeCheck();
            float loopField = EditorGUILayout.FloatField(sel.loopEnd <= 0f ? autoDuration : sel.loopEnd, GUILayout.Width(45));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(sel, "Set loop end");
                sel.loopEnd = loopField >= autoDuration - 1e-3f ? 0f : Mathf.Max(0f, loopField);
                EditorUtility.SetDirty(sel);
            }

            flow.Item(80);
            if (GUILayout.Button("Reset View", EditorStyles.toolbarButton, GUILayout.Width(80))) ResetView();
            flow.Item(50);
            showRuler = GUILayout.Toggle(showRuler, "Ruler", EditorStyles.toolbarButton, GUILayout.Width(50));
            flow.Item(12 + 40);   // "h" label + height field kept together
            GUILayout.Label("h", GUILayout.Width(12));
            rulerHeight = EditorGUILayout.FloatField(rulerHeight, GUILayout.Width(40));
            flow.Item(56);
            showRadiusRing = GUILayout.Toggle(showRadiusRing, "Radius", EditorStyles.toolbarButton, GUILayout.Width(56));
            if (skillRadii.Count > 0)
            {
                // Read-only mirror of the authoritative SkillTickBinding.radius values — edit them in the Skill Composer.
                flow.Item(120);
                GUILayout.Label("⟵ skill " + string.Join(", ", skillRadii.ConvertAll(r => r.ToString("0.##"))),
                                EditorStyles.miniLabel, GUILayout.Width(120));
            }
            else
            {
                // No skill references this sequence yet → a plain measuring ruler (NOT a gameplay value).
                flow.Item(32 + 40);   // "ruler" label + field kept together
                GUILayout.Label("ruler", EditorStyles.miniLabel, GUILayout.Width(32));
                manualRingR = EditorGUILayout.FloatField(manualRingR, GUILayout.Width(40));
            }
        }

        // --- timeline ---
        DrawTimeline();

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

    // A Gantt-style timeline under the transport: one colored bar per effect (tint matches its block) spanning its
    // start → end over [0, previewDuration], with a live playhead. Click/drag to scrub. Uses starts[]/previewDuration
    // computed by RenderPreviewAt earlier this frame.
    void DrawTimeline()
    {
        int n = sel.steps.Count;
        if (n == 0) return;
        const float rowH = 15f;
        var area = GUILayoutUtility.GetRect(10, n * rowH + 6, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(area, new Color(0f, 0f, 0f, 0.25f));

        float viewDur = Mathf.Max(0.01f, autoDuration);   // x-scale spans ALL content, so tails past the loop show
        float loopLen = Mathf.Clamp(previewDuration, 0.05f, viewDur);
        float x0 = area.x + 4f, w = area.width - 8f;

        // Shade the region past the loop-end marker: this is "next loop" time where overlapping tails live.
        float loopX = x0 + (loopLen / viewDur) * w;
        if (loopLen < viewDur - 1e-3f)
            EditorGUI.DrawRect(new Rect(loopX, area.y, x0 + w - loopX, area.height), new Color(1f, 0.5f, 0.1f, 0.07f));

        for (int i = 0; i < n; i++)
        {
            var st = sel.steps[i];
            if (st == null) continue;
            float rowY = area.y + 3f + i * rowH;
            var tint = StepTints[i % StepTints.Length];
            string label = string.IsNullOrEmpty(st.note) ? st.kind.ToString() : st.note;

            float s = (starts != null && i < starts.Length) ? starts[i] : 0f;
            if (float.IsInfinity(s))   // waits on an event that never fires
            {
                GUI.Label(new Rect(x0, rowY, w, rowH), $"{label}  (unscheduled)", EditorStyles.miniLabel);
                continue;
            }

            float len = StepWindow(i, st);
            float bx = x0 + (s / viewDur) * w;
            float bw = Mathf.Max(3f, (len / viewDur) * w);
            EditorGUI.DrawRect(new Rect(bx, rowY + 1f, bw, rowH - 3f), tint * 1.5f);
            GUI.Label(new Rect(bx + 3f, rowY - 1f, w, rowH), label, EditorStyles.miniLabel);
        }

        // playhead (sweeps 0..loopLen)
        float px = x0 + (Mathf.Clamp(clock, 0f, loopLen) / viewDur) * w;
        EditorGUI.DrawRect(new Rect(px, area.y, 1.5f, area.height), new Color(1f, 0.9f, 0.3f, 0.95f));

        // loop-end marker (draggable). Orange line + a grab handle spanning the row height.
        EditorGUI.DrawRect(new Rect(loopX - 1f, area.y, 2f, area.height), new Color(1f, 0.55f, 0.1f, 0.95f));
        var handle = new Rect(loopX - 4f, area.y, 8f, area.height);
        EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeHorizontal);

        var e = Event.current;
        if (e.type == EventType.MouseDown && handle.Contains(e.mousePosition)) { draggingLoopMark = true; e.Use(); }
        else if (draggingLoopMark)
        {
            if (e.type == EventType.MouseDrag)
            {
                float v = Mathf.Clamp(Mathf.Clamp01((e.mousePosition.x - x0) / w) * viewDur, 0.05f, viewDur);
                Undo.RecordObject(sel, "Set loop end");
                sel.loopEnd = v >= viewDur - 1e-3f ? 0f : v;   // snapping to the far end = auto (no early loop)
                EditorUtility.SetDirty(sel);
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp) { draggingLoopMark = false; e.Use(); }
        }
        // click / drag elsewhere to scrub (clamped to the loop range)
        else if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && area.Contains(e.mousePosition))
        {
            clock = Mathf.Min(Mathf.Clamp01((e.mousePosition.x - x0) / w) * viewDur, loopLen);
            e.Use(); Repaint();
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
        st.note = EditorGUILayout.TextField(st.note ?? "", GUILayout.MinWidth(0f), GUILayout.MaxWidth(140));
        st.kind = (VfxStepKind)EditorGUILayout.EnumPopup(st.kind, GUILayout.MinWidth(0f), GUILayout.MaxWidth(90));
        GUILayout.FlexibleSpace();
        float stepDur = StepWindow(i, st);   // this effect's own length in seconds (flight / clip / shake duration)
        GUILayout.Label(new GUIContent($"{stepDur:0.00}s", "This effect's duration in seconds"), EditorStyles.miniLabel, GUILayout.MinWidth(0f), GUILayout.MaxWidth(48));
        if (GUILayout.Button("↑", GUILayout.MinWidth(0f), GUILayout.MaxWidth(24))) moveUp = i;
        if (GUILayout.Button("↓", GUILayout.MinWidth(0f), GUILayout.MaxWidth(24))) moveDown = i;
        if (GUILayout.Button("✕", GUILayout.MinWidth(0f), GUILayout.MaxWidth(24))) remove = i;
        EditorGUILayout.EndHorizontal();

        // schedule
        EditorGUILayout.BeginHorizontal();
        st.schedule = (VfxSchedule)EditorGUILayout.EnumPopup(st.schedule, GUILayout.MinWidth(0f), GUILayout.MaxWidth(90));
        if (st.schedule == VfxSchedule.AtTime)
            st.delay = EditorGUILayout.Slider("start (s)", st.delay, 0f, 6f);
        else
            st.startEvent = EventPopup("on event", st.startEvent);
        EditorGUILayout.EndHorizontal();

        // OnEvent steps can snap to the exact point the emitter reported (e.g. a Carrier's scattered landing).
        if (st.schedule == VfxSchedule.OnEvent)
            st.snapToEventPoint = EditorGUILayout.Toggle(
                new GUIContent("snap to landing", "Anchor this step to the EXACT point the emitter reported " +
                    "(e.g. a Carrier's real scattered landing) instead of the plain anchor. Off = use anchor/offset."),
                st.snapToEventPoint);

        // anchor + offset
        st.anchor = (VfxAnchor)EditorGUILayout.EnumPopup("anchor", st.anchor);
        st.offset = EditorGUILayout.Vector3Field("offset", st.offset);

        switch (st.kind)
        {
            case VfxStepKind.PlayAt:
            case VfxStepKind.Attach:
                st.effectId = IdPopup("effect", st.effectId);
                st.scale = EditorGUILayout.Slider("scale", st.scale <= 0f ? 1f : st.scale, 0.05f, 5f);
                if (st.tint.a <= 0f) st.tint = Color.white;   // unset/legacy → no tint
                st.tint = EditorGUILayout.ColorField(new GUIContent("tint",
                    "Multiplies the effect's authored colour (white = original). Recolour a blue burst to red/green, etc."),
                    st.tint);
                st.scatter = EditorGUILayout.Toggle(new GUIContent("scatter",
                    "Jitter the spawn point by a random amount per axis (magnitude up to 0.2 units on x/y/z) each " +
                    "play, so repeats don't stack in the exact same spot. The composer preview stays deterministic."),
                    st.scatter);
                if (st.kind == VfxStepKind.PlayAt)
                {
                    st.speed = EditorGUILayout.Slider("speed", st.speed <= 0f ? 1f : st.speed, 0.1f, 3f);
                    float naturalReal = 1f;
                    for (int k = 0; k < preview.Count; k++)
                        if (preview[k].stepIndex == i) { naturalReal = preview[k].naturalLife / (st.speed <= 0f ? 1f : st.speed); break; }
                    st.trimEnd = EditorGUILayout.Slider("trim end (s)", Mathf.Clamp(st.trimEnd, 0f, naturalReal), 0f, Mathf.Max(0.05f, naturalReal));
                    st.clipStart = EditorGUILayout.Slider(new GUIContent("clip start (s)",
                        "Fast-forward into the clip on spawn — starts already mid-animation instead of frame 0."),
                        Mathf.Clamp(st.clipStart, 0f, naturalReal), 0f, Mathf.Max(0.05f, naturalReal));
                    st.rotationEuler = EditorGUILayout.Vector3Field("rotation", st.rotationEuler);
                }
                break;
            case VfxStepKind.Carrier:
                st.effectId = IdPopup("effect", st.effectId);
                st.scale = EditorGUILayout.Slider("scale", st.scale <= 0f ? 1f : st.scale, 0.05f, 5f);
                st.carrierFromCaster = EditorGUILayout.Toggle(new GUIContent("launch from caster",
                    "On: fly from the caster's live position to the target across the real (variable) gap — set anchor " +
                    "to Target. Off: fall-in from a fixed offset above where it lands (meteor/drop)."), st.carrierFromCaster);
                st.carrierFrom = EditorGUILayout.Vector3Field(new GUIContent(
                    st.carrierFromCaster ? "launch offset (from caster)" : "launch from (above target)",
                    st.carrierFromCaster ? "Offset from the caster, e.g. (0,1,0) leaves the hand 1 unit up."
                                         : "Offset from the landing, e.g. (0,9,0) falls from 9 units up."), st.carrierFrom);
                st.carrierFaceTravel = EditorGUILayout.Toggle("rotate to travel", st.carrierFaceTravel);
                st.carrierScatter = EditorGUILayout.Slider("scatter", st.carrierScatter, 0f, 3f);
                st.clipStart = EditorGUILayout.Slider(new GUIContent("clip start (s)",
                    "Fast-forward the travel effect into its clip on launch (skip the intro / desync a salvo)."),
                    st.clipStart, 0f, 3f);
                st.carrierSpeed = EditorGUILayout.Slider("fly speed", st.carrierSpeed, 1f, 40f);
                st.carrierMaxLife = EditorGUILayout.Slider("safety cap (s)", st.carrierMaxLife, 0.2f, 6f);
                if (st.tint.a <= 0f) st.tint = Color.white;   // unset/legacy → no tint
                st.tint = EditorGUILayout.ColorField(new GUIContent("tint",
                    "Multiplies the flying effect's authored colour (white = original). Recolour a fire bolt to green/purple, etc."),
                    st.tint);
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
                float clipLen = (st.sound.clips != null && st.sound.clips.Length > 0 && st.sound.clips[0] != null)
                    ? st.sound.clips[0].length : 0f;
                float durMax = Mathf.Max(0.1f, clipLen > 0f ? clipLen : 10f);
                st.soundDuration = EditorGUILayout.Slider(new GUIContent("duration (s)",
                    "Play length before fade/cut. 0 = full clip. Bounds a long/looping clip so it ends with the beat."),
                    Mathf.Clamp(st.soundDuration, 0f, durMax), 0f, durMax);
                st.soundFadeIn = EditorGUILayout.Slider(new GUIContent("ease in (s)",
                    "Ramp the sound up from silence at the start (smoothstep)."), st.soundFadeIn, 0f, durMax);
                st.soundFadeOut = EditorGUILayout.Slider(new GUIContent("ease out (s)",
                    "Ramp the sound down to silence at the end (smoothstep)."), st.soundFadeOut, 0f, durMax);
                break;
        }

        // Tick (gameplay HANDOFF) — expose a named beat the Skill Composer binds damage/heal/status onto. Only the
        // spawning kinds tick; this window never sets WHAT the tick does (that's the Skill Composer's job).
        if (st.kind == VfxStepKind.PlayAt || st.kind == VfxStepKind.Attach || st.kind == VfxStepKind.Carrier)
            DrawTick(st, StepWindow(i, st));

        st.emitEventOnStart = EmitField("emit event on start", st.emitEventOnStart, st, isEnd: false);
        st.emitEventOnEnd = EmitField(
            st.kind == VfxStepKind.Carrier ? "emit event on end (arrival)" : "emit event on end",
            st.emitEventOnEnd, st, isEnd: true);
        EditorGUILayout.EndVertical();
    }

    // The per-step "Tick (gameplay handoff)" block: name a beat and its cadence. This is the ONLY join between the
    // VFX and the Skill Composer — leave the id empty for a pure-visual step. WHAT the tick does (damage/element/
    // multiplier) is authored in the Skill Composer against this id, never here (separation of concerns).
    void DrawTick(VfxStep st, float stepDuration)
    {
        EditorGUILayout.Space(2);
        st.tickId = EditorGUILayout.TextField(new GUIContent("tick id",
            "Stable component name the Skill Composer binds gameplay onto (e.g. \"meteor_ball\", \"fire_pillar\"). " +
            "Empty = pure-visual step (no gameplay)."), st.tickId);
        if (string.IsNullOrEmpty(st.tickId)) return;

        st.spreadTicks = EditorGUILayout.Toggle(new GUIContent("  spread ticks",
            "Auto-fill ticks from this effect's duration: ticks = floor(duration / interval). " +
            "The component ticks for its whole visible lifetime; you only set the interval."), st.spreadTicks);

        if (st.spreadTicks)
        {
            st.tickInterval = EditorGUILayout.Slider(new GUIContent("  interval (s)",
                "Seconds between ticks. ticks = floor(duration / interval)."), st.tickInterval, 0.01f, 3f);
            st.tickCount = Mathf.Max(1, Mathf.FloorToInt(stepDuration / Mathf.Max(0.01f, st.tickInterval)));
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.IntField(new GUIContent("  ticks",
                    "Computed from duration ÷ interval."), st.tickCount);
        }
        else
        {
            st.tickCount = Mathf.Max(1, EditorGUILayout.IntField(new GUIContent("  ticks",
                "How many times this component fires its beat. 1 = one-shot (a meteor's land). N = a lingering " +
                "component (a fire pillar's repeated damage)."), st.tickCount <= 0 ? 1 : st.tickCount));
            using (new EditorGUI.DisabledScope(st.tickCount <= 1))
                st.tickInterval = EditorGUILayout.Slider(new GUIContent("  interval (s)",
                    "Seconds between ticks (ticks × interval = the active window). Only used when ticks > 1."),
                    st.tickInterval, 0f, 3f);
        }
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

    // An emit-event text field that keeps every event id UNIQUE across the composition: if you type a name that's
    // already emitted elsewhere, it auto-suffixes (impact → impact-2). This makes each id a single-source event, so
    // the "On Event" dropdown has no ambiguous entries.
    string EmitField(string label, string current, VfxStep owner, bool isEnd)
    {
        string typed = EditorGUILayout.TextField(label, current ?? "");
        if (typed == current) return current;                 // unchanged → don't churn
        string result = MakeUniqueEmit(typed, owner, isEnd);
        // Renaming an event id updates every listener that referenced it (single source of truth). Clearing the
        // field is treated as deletion, not a rename — listeners are left dangling (shown as "(missing)").
        if (!string.IsNullOrEmpty(current) && !string.IsNullOrEmpty(result) && result != current)
            foreach (var st in sel.steps)
                if (st != null && st.schedule == VfxSchedule.OnEvent && st.startEvent == current)
                    st.startEvent = result;
        return result;
    }

    string MakeUniqueEmit(string desired, VfxStep owner, bool isEnd)
    {
        if (string.IsNullOrEmpty(desired)) return "";
        // Collect every OTHER emit id in the composition (skip only the exact field being edited).
        emitIdScratch.Clear();
        foreach (var st in sel.steps)
        {
            if (st == null) continue;
            if (!(st == owner && !isEnd) && !string.IsNullOrEmpty(st.emitEventOnStart)) emitIdScratch.Add(st.emitEventOnStart);
            if (!(st == owner && isEnd) && !string.IsNullOrEmpty(st.emitEventOnEnd)) emitIdScratch.Add(st.emitEventOnEnd);
        }
        if (!emitIdScratch.Contains(desired)) return desired;
        for (int k = 2; ; k++)
        {
            string cand = $"{desired}-{k}";
            if (!emitIdScratch.Contains(cand)) return cand;
        }
    }

    // Every emit-event id declared anywhere in the composition (start + end), deduped — the vocabulary an
    // "On Event" step can listen for. Returns the shared scratch list; don't hold the reference across calls.
    List<string> CollectEmitIds()
    {
        emitIdScratch.Clear();
        foreach (var st in sel.steps)
        {
            if (st == null) continue;
            if (!string.IsNullOrEmpty(st.emitEventOnStart) && !emitIdScratch.Contains(st.emitEventOnStart)) emitIdScratch.Add(st.emitEventOnStart);
            if (!string.IsNullOrEmpty(st.emitEventOnEnd) && !emitIdScratch.Contains(st.emitEventOnEnd)) emitIdScratch.Add(st.emitEventOnEnd);
        }
        return emitIdScratch;
    }

    // Dropdown for the "On Event" (startEvent) field: pick from the emit ids declared in the composition, so a
    // listener can't reference a mistyped/nonexistent event. A stale value (its producer was renamed/removed) is
    // shown as "… (missing)" so it isn't silently lost.
    string EventPopup(string label, string current)
    {
        eventValScratch.Clear(); eventLabelScratch.Clear();
        eventValScratch.Add(""); eventLabelScratch.Add("(none)");
        foreach (var id in CollectEmitIds()) { eventValScratch.Add(id); eventLabelScratch.Add(id); }
        if (!string.IsNullOrEmpty(current) && !eventValScratch.Contains(current))
        {
            eventValScratch.Add(current);
            eventLabelScratch.Add(current + "  (missing)");
        }
        int idx = Mathf.Max(0, eventValScratch.IndexOf(current ?? ""));
        int picked = EditorGUILayout.Popup(label, idx, eventLabelScratch.ToArray());
        return eventValScratch[picked];
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
        // Built-in Unlit/Texture (opaque + depth-writing) over URP/Unlit: URP shaders can render magenta inside a
        // PreviewRenderUtility (their pipeline globals aren't set up in a preview scene), especially after a domain
        // reload. The built-in unlit is reliable here and still occludes by real depth.
        var groundShader = Shader.Find("Unlit/Texture") ?? Shader.Find("Universal Render Pipeline/Unlit");
        var gm = new Material(groundShader);
        gm.mainTexture = gridTex;
        if (gm.HasProperty("_BaseMap")) gm.SetTexture("_BaseMap", gridTex);   // URP/Unlit samples _BaseMap
        ground.GetComponent<Renderer>().sharedMaterial = gm;
        pru.AddSingleGO(ground);

        // World-unit AoE radius rings: flat circles on the floor at the REAL SkillTickBinding.radius values (read via
        // RefreshSkillRadii) — one ring per distinct authoritative radius, so the artist sizes the VFX to the number
        // the Skill Composer owns, never a hand-typed copy. Parent + a shared material; rings are pooled on demand.
        ringMat = new Material(sprite) { color = new Color(1f, 0.45f, 0.15f, 0.95f) };
        radiusRing = new GameObject("RadiusRings");
        pru.AddSingleGO(radiusRing);
        radiusRingLrs.Clear();
        for (int k = 0; k < 8; k++)   // fixed pool (registered with the preview scene up front); enabled per drawn radius
        {
            var go = new GameObject("RadiusRing" + k);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.widthMultiplier = 0.03f;
            lr.numCornerVertices = 0;
            lr.alignment = LineAlignment.View;
            lr.sharedMaterial = ringMat;
            lr.startColor = lr.endColor = new Color(1f, 0.45f, 0.15f, 0.95f);
            go.SetActive(false);
            pru.AddSingleGO(go);
            radiusRingLrs.Add(lr);
        }

        // Capsule ruler at true human height — the size reference effects are eyeballed against.
        capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.DestroyImmediate(capsule.GetComponent<Collider>());
        capsule.GetComponent<Renderer>().sharedMaterial = new Material(sprite) { color = new Color(0.5f, 0.55f, 0.6f, 1f) };
        pru.AddSingleGO(capsule);
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

    // Draw one ring per authoritative skill radius (or the manual fallback if no skill references this sequence).
    void UpdateRadiusRing()
    {
        const int seg = 64;
        for (int idx = 0; idx < radiusRingLrs.Count; idx++)
        {
            bool draw = skillRadii.Count > 0 ? idx < skillRadii.Count : idx == 0;
            var lr = radiusRingLrs[idx];
            lr.gameObject.SetActive(draw);
            if (!draw) continue;
            float rr = Mathf.Max(0.05f, skillRadii.Count > 0 ? skillRadii[idx] : manualRingR);
            if (lr.positionCount != seg) lr.positionCount = seg;
            for (int i = 0; i < seg; i++)
            {
                float a = (i / (float)seg) * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * rr, 0.02f, Mathf.Sin(a) * rr));
            }
        }
    }

    void DrawPreview(Rect r)
    {
        EnsurePru();
        HandleCameraInput(r);

        // Place the camera first, so the character billboard can face it this frame.
        var camRot = Quaternion.Euler(camPitch, camYaw, 0f);
        pru.camera.transform.position = camPivot - (camRot * Vector3.forward) * camDist;
        pru.camera.transform.rotation = camRot;

        // Ruler at true human height — a plain capsule sized to rulerHeight (user-overridable).
        if (capsule != null)
        {
            capsule.SetActive(showRuler);
            capsule.transform.localScale = new Vector3(rulerHeight * 0.28f, rulerHeight * 0.5f, rulerHeight * 0.28f);
            capsule.transform.position = new Vector3(0f, rulerHeight * 0.5f, 0f);
        }

        if (radiusRing != null)
        {
            radiusRing.SetActive(showRadiusRing);
            if (showRadiusRing) UpdateRadiusRing();
            else for (int i = 0; i < radiusRingLrs.Count; i++) radiusRingLrs[i].gameObject.SetActive(false);
        }

        EnsurePreview();
        RenderPreviewAt(clock);

        // Apply any active Shake step as a decaying camera jitter, so a shake beat is visible in the preview
        // (mirrors CameraFollow3D: linear decay, strongest active shake wins, screen-plane offset).
        Vector3 shake = PreviewShakeOffset(clock, camRot);
        if (shake != Vector3.zero) pru.camera.transform.position += shake;

        pru.BeginPreview(r, GUIStyle.none);
        pru.camera.Render();
        var tex = pru.EndPreview();
        GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, false);

        DrawCompass(r);

        // Mesh render-mode particles don't draw under the Simulate()-based preview (they DO in-game). Warn so a
        // missing ring/torus in the preview reads as a known tool limitation, not a broken effect.
        if (previewHasMeshParticles)
        {
            var note = new GUIContent("⚠ Mesh-particle parts (e.g. rings) don't show in preview — they render in-game.");
            var style = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(1f, 0.85f, 0.4f) } };
            var size = style.CalcSize(note);
            EditorGUI.DrawRect(new Rect(r.x + 6f, r.yMax - size.y - 8f, size.x + 8f, size.y + 4f), new Color(0f, 0f, 0f, 0.55f));
            GUI.Label(new Rect(r.x + 10f, r.yMax - size.y - 6f, size.x, size.y), note, style);
        }
    }

    // Reset the orbit camera to its default framing.
    void ResetView()
    {
        camYaw = DefYaw; camPitch = DefPitch; camDist = DefDist; camPivot = DefPivot;
        Repaint();
    }

    // Overlay N/S/E/W labels at the ground's edges by projecting those world points through the preview camera —
    // so orientation is legible no matter how the view is orbited. +Z = North, +X = East (grid is 6 m → edge ≈3 m).
    void DrawCompass(Rect r)
    {
        if (pru == null) return;
        if (compassStyle == null)
            compassStyle = new GUIStyle(EditorStyles.boldLabel)
            { alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.85f, 0.35f) } };
        DrawCompassLabel(r, new Vector3(0f, 0f, 3f), "N");
        DrawCompassLabel(r, new Vector3(0f, 0f, -3f), "S");
        DrawCompassLabel(r, new Vector3(3f, 0f, 0f), "E");
        DrawCompassLabel(r, new Vector3(-3f, 0f, 0f), "W");
    }

    void DrawCompassLabel(Rect r, Vector3 world, string s)
    {
        Vector3 vp = pru.camera.WorldToViewportPoint(world);
        if (vp.z <= 0f) return;                                   // behind the camera
        float x = r.x + vp.x * r.width;
        float y = r.y + (1f - vp.y) * r.height;
        GUI.Label(new Rect(x - 9f, y - 9f, 18f, 18f), s, compassStyle);
    }

    void HandleCameraInput(Rect r)
    {
        var e = Event.current;
        if (!r.Contains(e.mousePosition)) return;
        if (e.type == EventType.MouseDrag && e.button == 0)
        {
            camYaw += e.delta.x * 0.5f;
            camPitch = Mathf.Clamp(camPitch + e.delta.y * 0.5f, -20f, 80f);
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
            sb.Append((int)st.kind).Append(':').Append(st.effectId).Append(':').Append(ColorUtility.ToHtmlStringRGBA(st.tint)).Append(';');
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
        previewHasMeshParticles = false;
        for (int i = 0; i < sel.steps.Count; i++)
        {
            var st = sel.steps[i];
            if (st == null) continue;
            if (st.kind != VfxStepKind.PlayAt && st.kind != VfxStepKind.Attach && st.kind != VfxStepKind.Carrier) continue;
            var def = EffectCatalog.Get(st.effectId);
            if (def == null || def.prefab == null) continue;

            // Only a one-shot PlayAt can spill past the loop-end marker and overlap its next spawn, so it gets a small
            // pool of instances (one per still-playing copy). Attach (continuous aura) / Carrier (short flight) need one.
            int slots = st.kind == VfxStepKind.PlayAt ? PreviewSlots : 1;
            for (int slot = 0; slot < slots; slot++) AddPreviewInst(i, st, def, slot);
        }
        previewSig = Signature();
    }

    // Multiply a particle system's startColor by a tint (mirrors Vfx.Multiply so the preview matches the runtime recolour).
    static ParticleSystem.MinMaxGradient MultiplyTint(ParticleSystem.MinMaxGradient g, Color tint)
    {
        switch (g.mode)
        {
            case ParticleSystemGradientMode.Color:
                return new ParticleSystem.MinMaxGradient(g.color * tint);
            case ParticleSystemGradientMode.TwoColors:
                return new ParticleSystem.MinMaxGradient(g.colorMin * tint, g.colorMax * tint);
            default:
                return g;
        }
    }

    void AddPreviewInst(int i, VfxStep st, EffectDef def, int slot)
    {
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
            // Identical seed across ALL slots: as a clip ages it is handed slot0→slot1→… on each loop restart, and
            // that handoff is only seamless if every slot renders the SAME pattern at the SAME age (else it pops/cuts).
            ps.randomSeed = 0x5EED + (uint)j;
            var psr = ps.GetComponent<ParticleSystemRenderer>();
            // Mesh render-mode particles don't draw under the editor's Simulate()-based preview (they DO in-game via
            // real Play). Flag it so the preview can warn instead of silently hiding e.g. a flat ring/torus mesh.
            if (psr != null && psr.renderMode == ParticleSystemRenderMode.Mesh) previewHasMeshParticles = true;
            var m = ps.main;
            m.scalingMode = ParticleSystemScalingMode.Hierarchy;   // root localScale must resize children
            // PlayAt is a one-shot: kill looping so it emits a single clip (matches the runtime one-shot). Attach
            // (auras) and Carrier (trails) keep their authored looping.
            if (st.kind == VfxStepKind.PlayAt) { var mm = ps.main; mm.loop = false; }
            if (st.tint.a > 0f && st.tint != Color.white) { var mc = ps.main; mc.startColor = MultiplyTint(mc.startColor, st.tint); }
            if (def.lifetime <= 0f) natural = Mathf.Max(natural, m.duration + m.startLifetime.constantMax);
            if (!HasParentSystem(ps.transform, go.transform)) roots.Add(ps);
        }
        if (natural <= 0f) natural = 1f;

        pru.AddSingleGO(go);   // move into the preview scene
        preview.Add(new PreviewInst { stepIndex = i, slot = slot, def = def, go = go, roots = roots.ToArray(), naturalLife = natural });
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

    // Preview-only stand-in for the caster: the composer collapses origin==target==world 0, so a "launch from
    // caster" carrier would have zero flight. Place the caster this far from the impact so its flight is visible.
    // Runtime uses the real (variable) ctx.origin — this is purely to give the editor something to animate.
    static readonly Vector3 PreviewCasterOffset = new Vector3(-6f, 0f, 0f);

    static float CarrierFlight(VfxStep st)
    {
        float dist = (st.carrierFromCaster ? PreviewCasterOffset + st.carrierFrom : st.carrierFrom).magnitude;
        return Mathf.Min(dist / Mathf.Max(0.1f, st.carrierSpeed), st.carrierMaxLife);
    }

    // Deterministic pseudo-random in [0,1) from two ints (loop seed + step channel) — a stable, alloc-free stand-in
    // for Random so the preview's carrier scatter is repeatable within a loop.
    static float Hash01(int a, int b)
    {
        unchecked
        {
            int h = a * 73856093 ^ b * 19349663;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h >> 8) & 0xFFFF) / 65535f;
        }
    }

    // On-screen length of a preview instance: Carrier = flight time; PlayAt = clip / playback speed (faster → shorter,
    // always one full clip); Attach/others = the raw clip lifetime.
    // The (scattered) landing of the Carrier that raises `tag`, as of loop `seed`. Mirrors the carrier render math
    // so a snap-to-landing PlayAt sits exactly where that loop's carrier landed. Passing an earlier seed (loopSeed -
    // slot) pins a still-playing tail to its own birth loop instead of the current one.
    bool TryCarrierLanding(string tag, int seed, out Vector3 land)
    {
        land = default;
        if (string.IsNullOrEmpty(tag)) return false;
        for (int k = 0; k < preview.Count; k++)
        {
            var ci = preview[k];
            if (ci.go == null || ci.stepIndex >= sel.steps.Count) continue;
            var cst = sel.steps[ci.stepIndex];
            if (cst == null || cst.kind != VfxStepKind.Carrier || cst.emitEventOnEnd != tag) continue;
            land = cst.offset + ci.def.offset;
            if (cst.carrierScatter > 0f)
            {
                float sc = cst.carrierScatter;
                land.x += (Hash01(seed, ci.stepIndex * 4 + 0) * 2f - 1f) * sc;
                land.z += (Hash01(seed, ci.stepIndex * 4 + 1) * 2f - 1f) * sc;
            }
            return true;
        }
        return false;
    }

    static float InstWindow(VfxStep st, PreviewInst inst)
    {
        if (st.kind == VfxStepKind.Carrier) return CarrierFlight(st);
        if (st.kind == VfxStepKind.PlayAt)
            return Mathf.Max(0.05f, inst.naturalLife / (st.speed <= 0f ? 1f : st.speed) - st.trimEnd);
        return inst.naturalLife;
    }

    // Record an event tag's earliest fire time (min-wins). Returns true if it changed the table (drives the
    // settle loop in ResolveStarts). No-op for an empty tag.
    bool RegisterEvent(string tag, float time)
    {
        if (string.IsNullOrEmpty(tag)) return false;
        if (evTimes.TryGetValue(tag, out var cur) && time >= cur) return false;
        evTimes[tag] = time;
        return true;
    }

    // How long after its start a step's "end" event fires: a Carrier on arrival (flight), a Shake after its
    // duration, a Sound ≈ instantly, a PlayAt/Attach after the effect's natural lifetime (from the preview).
    float StepWindow(int stepIndex, VfxStep st)
    {
        if (st.kind == VfxStepKind.Carrier) return CarrierFlight(st);
        if (st.kind == VfxStepKind.Shake) return st.shakeDuration;
        if (st.kind == VfxStepKind.Sound) return 0f;
        for (int k = 0; k < preview.Count; k++)
            if (preview[k].stepIndex == stepIndex) return InstWindow(st, preview[k]);
        return 0f;
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
                if (!float.IsInfinity(s))
                {
                    if (RegisterEvent(st.emitEventOnStart, s)) changed = true;
                    if (RegisterEvent(st.emitEventOnEnd, s + StepWindow(i, st))) changed = true;
                }
            }
            if (!changed) break;
        }
    }

    // Camera jitter for any Shake step active at time T. Strongest active shake wins (bounded, like the runtime),
    // decaying linearly to 0 over its duration. Noise is a deterministic function of T so scrubbing is stable
    // (not random per repaint). Offset lives in the camera's screen plane. Needs starts[] (set by ResolveStarts).
    Vector3 PreviewShakeOffset(float T, Quaternion camRot)
    {
        if (sel == null || starts == null) return Vector3.zero;
        float amp = 0f;
        for (int i = 0; i < sel.steps.Count && i < starts.Length; i++)
        {
            var st = sel.steps[i];
            if (st == null || st.kind != VfxStepKind.Shake) continue;
            float s = starts[i];
            if (float.IsInfinity(s)) continue;
            float dur = Mathf.Max(1e-4f, st.shakeDuration);
            float age = T - s;
            if (age < 0f || age > dur) continue;
            amp = Mathf.Max(amp, st.shakeStrength * (1f - age / dur));
        }
        if (amp <= 1e-5f) return Vector3.zero;
        float nx = Mathf.PerlinNoise(T * 40f, 0.3f) * 2f - 1f;
        float ny = Mathf.PerlinNoise(0.7f, T * 40f) * 2f - 1f;
        return (camRot * Vector3.right * nx + camRot * Vector3.up * ny) * amp;
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
            if (inst.slot != 0 || inst.stepIndex >= n) continue;   // slot>0 is the same clip a loop later — no new length
            var st = sel.steps[inst.stepIndex];
            if (st == null) continue;
            float s = starts[inst.stepIndex];
            if (float.IsInfinity(s)) continue;
            dur = Mathf.Max(dur, s + InstWindow(st, inst));
        }
        // Non-particle steps (Shake) aren't in the preview list but still occupy time — count them so the clip
        // length (and scrub range) covers them.
        for (int i = 0; i < n; i++)
        {
            var st = sel.steps[i];
            if (st == null || st.kind != VfxStepKind.Shake) continue;
            float s = starts[i];
            if (!float.IsInfinity(s)) dur = Mathf.Max(dur, s + st.shakeDuration);
        }
        autoDuration = Mathf.Max(0.01f, dur);
        // The loop restarts at the user's marker (clamped within the content), else at the full length. Clips still
        // playing past this point keep rendering via the extra slots below.
        float loopLen = sel.loopEnd > 0.001f ? Mathf.Clamp(sel.loopEnd, 0.05f, autoDuration) : autoDuration;
        previewDuration = loopLen;
        T = Mathf.Clamp(T, 0f, previewDuration);

        for (int k = 0; k < preview.Count; k++)
        {
            var inst = preview[k];
            if (inst.go == null) continue;
            if (inst.stepIndex >= n) { if (inst.go.activeSelf) inst.go.SetActive(false); continue; }
            var st = sel.steps[inst.stepIndex];
            if (st == null) { if (inst.go.activeSelf) inst.go.SetActive(false); continue; }

            float s = starts[inst.stepIndex];
            float window = InstWindow(st, inst);
            // slot k = this clip as spawned k loops ago, so its tail keeps playing past the loop-end marker and
            // overlaps the fresh spawn (slot 0). previewDuration is the loop length.
            float age = (T - s) + inst.slot * previewDuration;
            if (float.IsInfinity(s) || age < 0f || age > window)
            {
                if (inst.go.activeSelf) inst.go.SetActive(false);
                continue;
            }

            Vector3 anchorPt = st.offset + inst.def.offset;   // origin == target == world 0 in the composer
            // Snap-to-landing: rebase onto the emitter carrier's landing. A still-playing tail (slot>0) keeps the
            // landing from the loop it was BORN in (seed = loopSeed - slot) — so it stays put instead of jumping to
            // the new loop's scatter. Slot 0 uses the current loop's landing.
            if (st.schedule == VfxSchedule.OnEvent && st.snapToEventPoint
                && TryCarrierLanding(st.startEvent, loopSeed - inst.slot, out var landPt))
                anchorPt = landPt + st.offset + inst.def.offset;
            float scale = inst.def.scale * (st.scale <= 0f ? 1f : st.scale);
            float speed = st.kind == VfxStepKind.PlayAt ? (st.speed <= 0f ? 1f : st.speed) : 1f;

            Vector3 pos = anchorPt;
            Quaternion rot = Quaternion.Euler(st.rotationEuler);
            if (st.kind == VfxStepKind.Carrier)
            {
                float flight = Mathf.Max(0.0001f, window);
                // Mirror the runtime: scatter both the landing (spread around the anchor) and the launch, independently.
                // Deterministic per (loop, step) — stable while scrubbing a loop, re-rolls each cycle so you SEE it.
                Vector3 land = anchorPt;
                Vector3 fromBase = st.carrierFromCaster ? anchorPt + PreviewCasterOffset : anchorPt;
                Vector3 from = fromBase + st.carrierFrom;
                if (st.carrierScatter > 0f)
                {
                    float sc = st.carrierScatter;
                    land.x += (Hash01(loopSeed, inst.stepIndex * 4 + 0) * 2f - 1f) * sc;
                    land.z += (Hash01(loopSeed, inst.stepIndex * 4 + 1) * 2f - 1f) * sc;
                    Vector3 fb = st.carrierFromCaster ? fromBase : land;
                    from = fb + st.carrierFrom;
                    from.x += (Hash01(loopSeed, inst.stepIndex * 4 + 2) * 2f - 1f) * sc;
                    from.z += (Hash01(loopSeed, inst.stepIndex * 4 + 3) * 2f - 1f) * sc;
                }
                pos = Vector3.Lerp(from, land, Mathf.Clamp01(age / flight));
                // Face the actual travel direction (down → dir, so a pure vertical drop stays upright).
                if (st.carrierFaceTravel)
                {
                    Vector3 dir = land - from;
                    if (dir.sqrMagnitude > 1e-6f) rot = Quaternion.FromToRotation(Vector3.down, dir.normalized);
                }
            }

            if (!inst.go.activeSelf) inst.go.SetActive(true);
            inst.go.transform.position = pos;
            inst.go.transform.rotation = rot;
            inst.go.transform.localScale = Vector3.one * Mathf.Max(0.001f, scale);

            float simT = Mathf.Max(0.0001f, age * speed + Mathf.Max(0f, st.clipStart));
            for (int j = 0; j < inst.roots.Length; j++)
                if (inst.roots[j] != null) inst.roots[j].Simulate(simT, true, true, true);
        }
    }
}
