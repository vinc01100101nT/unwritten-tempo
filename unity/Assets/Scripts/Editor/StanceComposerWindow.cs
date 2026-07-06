using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit-mode authoring window for the <see cref="StanceLibrary"/> — the stance sibling of the
/// <see cref="VfxCueComposerWindow"/>. A table binding every <see cref="StanceId"/> to the baked
/// <see cref="CharacterAction"/> it plays, plus how the pose's speed is modulated (a live source + a hand-tuned
/// multiplier), how it ranks against other held stances, and how it plays back (loop / fall-tail).
///
/// Writes <c>Assets/Resources/StanceLibrary.asset</c> (created on demand) and reloads the runtime
/// <see cref="StanceCatalog"/> on save. Drive stances at runtime on a <see cref="StanceController"/>.
/// </summary>
public class StanceComposerWindow : EditorWindow
{
    const string Dir = "Assets/Resources";
    const string AssetPath = "Assets/Resources/StanceLibrary.asset";

    StanceLibrary lib;
    Vector2 scroll;

    // The clip palette for the Clip dropdown — the live-3D model's animation names (Quaternius embedded takes +
    // Mixamo clips). Cached; rebuilt on load or via the Refresh button. Index 0 is "" = no clip (the char stands).
    string[] clipPalette = { "" };
    GUIContent[] clipLabels = { new GUIContent("(none — stands)") };

    // Resolves a composer clip name → its Humanoid AnimationClip, so a row can read the clip's real frame count
    // (length × frameRate) for frame-based authoring. Cached; rebuilt with the palette (each build rescans the folder).
    Func<string, Motion> clipResolver;

    [MenuItem("Tools/unwritten/Composers/Stance Composer")]
    public static void Open()
    {
        var w = GetWindow<StanceComposerWindow>("Stance Composer");
        w.minSize = new Vector2(980, 420);
        w.LoadOrCreate();
    }

    void OnEnable() { if (lib == null) LoadOrCreate(); }

    void LoadOrCreate()
    {
        lib = AssetDatabase.LoadAssetAtPath<StanceLibrary>(AssetPath);
        if (lib == null)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "Resources");
            lib = CreateInstance<StanceLibrary>();
            AssetDatabase.CreateAsset(lib, AssetPath);
            AssetDatabase.SaveAssets();
        }
        Reconcile();
        BuildClipPalette();
    }

    // Gather the animation names a live-3D character can play: the Quaternius body's embedded takes (clean suffix)
    // plus the Mixamo clips (file name = the clip string). This is the dropdown of the model's clips — pick one per
    // stance. Editor-only; runs on load / Refresh, not per-frame.
    void BuildClipPalette()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        // The committed animation source: shared Humanoid clips (Assets/Art/Quat/Anim/Humanoid) that retarget onto
        // every rigged avatar. These are what the live-3D player/NPCs actually play.
        foreach (var n in HumanoidClipResolver.ClipNames()) names.Add(n);

        var opts = new List<string> { "" };
        opts.AddRange(names);
        clipPalette = opts.ToArray();
        clipLabels = new GUIContent[clipPalette.Length];
        for (int i = 0; i < clipPalette.Length; i++)
            clipLabels[i] = new GUIContent(clipPalette[i] == "" ? "(none — stands)" : clipPalette[i]);

        clipResolver = HumanoidClipResolver.Resolver();
    }

    // The playing clip's frame count = length × authored fps, resolved from its name. 0 when there's no clip or the
    // name doesn't match a file in the Humanoid folder (then the row falls back to normalized 0–1 authoring).
    int ClipFrameCount(string clip)
    {
        var c = ResolveClip(clip);
        return c != null ? Mathf.Max(1, Mathf.RoundToInt(c.length * c.frameRate)) : 0;
    }

    // The playing clip's authored frame rate (fps), for converting a seconds timestamp ↔ a frame index. 0 when the clip
    // can't be resolved (no clip assigned / name doesn't match a file).
    float ClipFps(string clip)
    {
        var c = ResolveClip(clip);
        return c != null ? c.frameRate : 0f;
    }

    // Resolve a composer clip name → its AnimationClip. The resolver is a non-serialized delegate, so it's null after a
    // domain reload (recompile) even though the palette survived — lazily rebuild it here so callers never silently see
    // "no clip" (that mis-fed frame edits as seconds before). Null when the name genuinely doesn't match a file.
    AnimationClip ResolveClip(string clip)
    {
        if (string.IsNullOrEmpty(clip)) return null;
        if (clipResolver == null) clipResolver = HumanoidClipResolver.Resolver();
        return clipResolver(clip) as AnimationClip;
    }

    // Ensure one binding row per enum id (add missing with a sensible default; the enum is append-only so we never
    // remove). Existing rows are left untouched — the asset is the single owner of a stance once authored.
    void Reconcile()
    {
        foreach (StanceId id in Enum.GetValues(typeof(StanceId)))
            if (Find(id) == null)
                lib.stances.Add(StanceDefaults.For(id));
    }

    StanceBinding Find(StanceId id)
    {
        for (int i = 0; i < lib.stances.Count; i++)
            if (lib.stances[i] != null && lib.stances[i].id == id) return lib.stances[i];
        return null;
    }

    void OnGUI()
    {
        if (lib == null) { if (GUILayout.Button("Create Stance Library")) LoadOrCreate(); return; }

        EditorGUILayout.HelpBox(
            "Every character pose is a stance. HELD stances (Combat/Guard/Casting/Sit) are pushed/popped on a " +
            "StanceController and the highest priority wins. ACTION stances (locomotion + one-shots) are driven one " +
            "at a time by the gameplay brain and beat held stances.\n\n" +
            "Clip (3D) = the animation a live-3D character plays for this stance — the SINGLE owner of that choice " +
            "(no hardcoded map). Empty = the character just stands. Idle/Walk/Run clips feed the locomotion blend " +
            "(the driven clip alone picks Idle/Walk/Run — no 2D action involved); every other stance's clip becomes " +
            "its own state. After adding/importing model clips, hit 'Refresh Clips'.\n\n" +
            "Speed source = where the LIVE fps multiplier is read from (the stance never stores the stat): AttackSpeed/" +
            "CastSpeed read Character.Instance; MoveGait reads move speed; FitWindow fits the clip to a runtime window " +
            "(Attack = swing time, Roll = dash length); Baked = the action's authored fps. × mult is a constant on top.\n\n" +
            "Jump timing (the button on the Jump row): crouchEnd / airborneFreeze, authored in clip FRAMES. 0→crouchEnd the player is " +
            "rooted (crouch/push-off); crouchEnd→airborneFreeze plays then FREEZES for the whole airborne time; on landing " +
            "it plays airborneFreeze→end (rooted) as the landing tail. The Fall stance (ledge walk-off) reuses airborneFreeze.",
            MessageType.Info);

        EditorGUILayout.Space(4);
        Header();
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUI.BeginChangeCheck();

        Section("Held stances (pushed/popped; priority-resolved)");
        Row(StanceId.Combat); Row(StanceId.Guard); Row(StanceId.Casting); Row(StanceId.Sit);

        Section("Locomotion (driven by the player brain)");
        Row(StanceId.Idle); Row(StanceId.Walk); Row(StanceId.Run);

        Section("Action one-shots (driven by the player brain)");
        Row(StanceId.Attack); Row(StanceId.Jump); Row(StanceId.Roll);
        Row(StanceId.Hurt); Row(StanceId.PickUp); Row(StanceId.Die); Row(StanceId.Fall);

        Section("Skill-release cues (a SkillDef's 'skill type' — plays once at release)");
        foreach (var id in SkillReleaseStances.All) Row(id);

        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(lib);

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Save + Apply to 3D", GUILayout.Height(26)))
            {
                EditorUtility.SetDirty(lib);
                AssetDatabase.SaveAssets();
                StanceCatalog.Reload();
                // Rebuild the live-3D visual's controller from these clips + reassign it, so the change applies now
                // (no manual Use-3D-Character/Caster). No-op with a friendly note if the player is a billboard.
                string applied = Character3DControllerBuilder.RebuildActiveFromLibrary();
                Debug.Log(applied != null
                    ? $"[unwritten] Saved StanceLibrary, reloaded catalog, rebuilt + applied to '{applied}'. Press Play (or it's already live)."
                    : "[unwritten] Saved StanceLibrary + reloaded catalog. No live-3D player in the open scenes to rebuild — open the Systems scene (it owns the player), then click Save + Apply to 3D again.");
            }
            if (GUILayout.Button("Refresh Clips", GUILayout.Width(110), GUILayout.Height(26)))
                BuildClipPalette();
            if (GUILayout.Button("Ping Asset", GUILayout.Width(90), GUILayout.Height(26)))
                EditorGUIUtility.PingObject(lib);
        }
    }

    void Header()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            EditorGUILayout.LabelField("Stance", EditorStyles.miniBoldLabel, GUILayout.Width(80));
            EditorGUILayout.LabelField("Clip (3D)", EditorStyles.miniBoldLabel, GUILayout.Width(200));
            EditorGUILayout.LabelField("Speed source", EditorStyles.miniBoldLabel, GUILayout.Width(110));
            EditorGUILayout.LabelField("× mult", EditorStyles.miniBoldLabel, GUILayout.Width(55));
            EditorGUILayout.LabelField("Priority", EditorStyles.miniBoldLabel, GUILayout.Width(55));
        }
    }

    void Section(string title)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    void Row(StanceId id)
    {
        var b = Find(id);
        if (b == null) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(id.ToString(), GUILayout.Width(80));
            ClipField(b);
            // Jump owns two clip timestamps (crouchEnd / airborneFreeze); Roll owns one (rootFromFrame) — each edited via a
            // small button beside the 3D clip dropdown. A same-width blank keeps every other row's columns aligned.
            if (id == StanceId.Jump) JumpTimingButton(b);
            else if (id == StanceId.Roll) RollRootButton(b);
            else GUILayout.Space(94);
            b.speedSource = (StanceSpeedSource)EditorGUILayout.EnumPopup(b.speedSource, GUILayout.Width(110));
            b.speedMult = EditorGUILayout.FloatField(b.speedMult, GUILayout.Width(55));
            b.priority = EditorGUILayout.IntField(b.priority, GUILayout.Width(55));
            // Reset this row to its built-in default (StanceDefaults) — for rows saved before the defaults existed.
            if (GUILayout.Button(new GUIContent("↺", "Reset this stance to its default"), GUILayout.Width(24)))
            {
                var d = StanceDefaults.For(id);
                b.action = d.action; b.speedSource = d.speedSource; b.speedMult = d.speedMult;
                b.priority = d.priority; b.crouchEnd = d.crouchEnd; b.airborneFreeze = d.airborneFreeze;
                b.rootFromFrame = d.rootFromFrame; b.clip = d.clip;
                GUI.changed = true;
            }
        }
    }

    // The Jump row's timing button: label shows the two timestamps as FRAMES out of the clip's frame count (e.g.
    // "8/45/60") — crouchEnd / airborneFreeze / total. Jump is Baked (no FitWindow), so seconds↔frames is a stable
    // map via the clip's fps; the timestamps stay STORED as seconds (the runtime contract). Falls back to seconds
    // (e.g. "0.25/1.50") when the clip can't be resolved to a frame rate.
    void JumpTimingButton(StanceBinding b)
    {
        float fps = ClipFps(b.clip);
        int total = ClipFrameCount(b.clip);
        string text = fps > 0f
            ? $"{Mathf.RoundToInt(b.crouchEnd * fps)}/{Mathf.RoundToInt(b.airborneFreeze * fps)}/{total}"
            : $"{b.crouchEnd:0.##}/{b.airborneFreeze:0.##}";
        var label = new GUIContent(text, "Jump clip timing: crouchEnd / airborneFreeze (frames, of total). Click to edit.");
        var rect = GUILayoutUtility.GetRect(label, EditorStyles.miniButton, GUILayout.Width(90));
        if (GUI.Button(rect, label, EditorStyles.miniButton))
            PopupWindow.Show(rect, new JumpTimingPopup(b, fps, total, () => { EditorUtility.SetDirty(lib); Repaint(); }));
    }

    // The Roll row's timing button: label shows the root-start FRAME (e.g. "f31/60" when the clip's frame count is
    // known, else just "f31"); from that frame until the clip ends the player is rooted. Always frame-authored — no
    // dependency on resolving the clip (unlike Jump, which needs fps to convert its stored seconds).
    void RollRootButton(StanceBinding b)
    {
        int total = ClipFrameCount(b.clip);
        string text = total > 0 ? $"f{b.rootFromFrame}/{total}" : $"f{b.rootFromFrame}";
        var label = new GUIContent(text, "Roll root start: rooted from this frame until the clip ends. Click to edit.");
        var rect = GUILayoutUtility.GetRect(label, EditorStyles.miniButton, GUILayout.Width(90));
        if (GUI.Button(rect, label, EditorStyles.miniButton))
            PopupWindow.Show(rect, new RollRootPopup(b, total, () => { EditorUtility.SetDirty(lib); Repaint(); }));
    }

    // Attached popup for the Roll root-start frame — a plain frame IntField. When the clip's frame count is known it
    // clamps to it and shows the total as a hint; otherwise it still accepts any frame. Mutates the binding in place.
    class RollRootPopup : PopupWindowContent
    {
        readonly StanceBinding b;
        readonly int total;
        readonly Action onChange;
        public RollRootPopup(StanceBinding binding, int totalFrames, Action changed) { b = binding; total = totalFrames; onChange = changed; }

        public override Vector2 GetWindowSize() => new Vector2(260, total > 0 ? 92 : 72);

        public override void OnGUI(Rect r)
        {
            EditorGUILayout.LabelField("Roll root start", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            int frame = EditorGUILayout.IntField(new GUIContent("Root from frame", "Rooted from this frame until the clip ends (the roll's recovery). 0 = no rooting (moves the whole roll)."), b.rootFromFrame);
            if (total > 0) EditorGUILayout.LabelField(" ", $"of {total} frames");
            if (EditorGUI.EndChangeCheck())
            {
                frame = Mathf.Max(0, frame);
                if (total > 0) frame = Mathf.Min(frame, total);
                b.rootFromFrame = frame;
                onChange?.Invoke();
            }
        }
    }

    // Attached popup for the Jump row's two timestamps. Authors them as FRAMES when the clip's fps is known (converting
    // to/from the seconds the binding STORES), else as raw seconds. Clamps airborneFreeze ≥ crouchEnd so the phases
    // can't invert. Mutates the binding in place; onChange marks the asset dirty + repaints the table.
    class JumpTimingPopup : PopupWindowContent
    {
        readonly StanceBinding b;
        readonly float fps;
        readonly int total;
        readonly Action onChange;
        public JumpTimingPopup(StanceBinding binding, float clipFps, int totalFrames, Action changed)
        { b = binding; fps = clipFps; total = totalFrames; onChange = changed; }

        public override Vector2 GetWindowSize() => new Vector2(260, fps > 0f ? 112 : 84);

        public override void OnGUI(Rect r)
        {
            if (fps > 0f) { FrameFields(); return; }
            // No fps → don't silently accept seconds (that's how frame edits got stored as seconds). Say what's missing.
            EditorGUILayout.LabelField("Jump clip timing (frames)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Assign the Jump row's Clip (3D) to edit timing in frames.", EditorStyles.wordWrappedMiniLabel);
        }

        void FrameFields()
        {
            EditorGUILayout.LabelField("Jump clip timing (frames)", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            int crouch = EditorGUILayout.IntField(new GUIContent("Crouch end frame", "Rooted from frame 0 to here (crouch/push-off), then launch."), Mathf.RoundToInt(b.crouchEnd * fps));
            int freeze = EditorGUILayout.IntField(new GUIContent("Freeze at frame", "Airborne pose freezes here; landing tail plays here→clip end."), Mathf.RoundToInt(b.airborneFreeze * fps));
            EditorGUILayout.LabelField(" ", $"of {total} frames  ({fps:0.#} fps)");
            if (EditorGUI.EndChangeCheck())
            {
                crouch = Mathf.Max(0, crouch);
                freeze = Mathf.Max(crouch, freeze);   // freeze can't precede the crouch/launch
                b.crouchEnd = crouch / fps;
                b.airborneFreeze = freeze / fps;
                onChange?.Invoke();
            }
        }
    }

    // The Clip dropdown: the single owner of a stance's 3D animation. Options are the model's clips (BuildClipPalette)
    // plus "" = none (stands). If a stance already names a clip that isn't in the palette (authored for another model,
    // or the file was removed), it's shown appended with a "(?)" so it's never silently lost.
    void ClipField(StanceBinding b)
    {
        string cur = b.clip ?? "";
        string[] opts = clipPalette;
        GUIContent[] labels = clipLabels;
        int idx = Array.IndexOf(opts, cur);
        if (idx < 0)
        {
            opts = new string[clipPalette.Length + 1];
            clipPalette.CopyTo(opts, 0);
            opts[opts.Length - 1] = cur;
            labels = new GUIContent[opts.Length];
            Array.Copy(clipLabels, labels, clipLabels.Length);
            labels[labels.Length - 1] = new GUIContent(cur + "  (?)");
            idx = opts.Length - 1;
        }
        int sel = EditorGUILayout.Popup(idx, labels, GUILayout.Width(200));
        b.clip = opts[Mathf.Clamp(sel, 0, opts.Length - 1)];
    }
}
