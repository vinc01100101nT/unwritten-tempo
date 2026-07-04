using System;
using System.Collections.Generic;
using System.IO;
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

    [MenuItem("Tools/unwritten/Art/Stance Composer")]
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

        // Legacy sources kept for the caster experiment: the Quaternius body's embedded takes + the Mixamo dir.
        if (File.Exists(CasterRig.CasterBodyPath))
            foreach (var k in Build3DCharacterSpike.LoadClips(CasterRig.CasterBodyPath).Keys) names.Add(k);
        string mixDir = Path.GetDirectoryName(CasterRig.CasterBodyPath);
        if (AssetDatabase.IsValidFolder(mixDir))
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { mixDir }))
            {
                string file = Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
                if (!file.StartsWith("Caster_")) names.Add(file);
            }

        var opts = new List<string> { "" };
        opts.AddRange(names);
        clipPalette = opts.ToArray();
        clipLabels = new GUIContent[clipPalette.Length];
        for (int i = 0; i < clipPalette.Length; i++)
            clipLabels[i] = new GUIContent(clipPalette[i] == "" ? "(none — stands)" : clipPalette[i]);
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
            "(no hardcoded map). Empty = the character just stands. Idle/Walk/Run clips feed the locomotion blend; " +
            "every other stance's clip becomes its own state. Action (2D) still selects the baked sprite set for " +
            "billboard characters. After adding/importing model clips, hit 'Refresh Clips'.\n\n" +
            "Speed source = where the LIVE fps multiplier is read from (the stance never stores the stat): AttackSpeed/" +
            "CastSpeed read Character.Instance; MoveGait reads move speed; FitWindow fits the clip to a runtime window " +
            "(Attack = swing time, Roll = dash length); Baked = the action's authored fps. × mult is a constant on top.\n\n" +
            "Play mode: Normal loops (Die auto-holds its last frame); FallTail loops the clip's tail as a free-fall pose.",
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
            EditorGUILayout.LabelField("Action (2D)", EditorStyles.miniBoldLabel, GUILayout.Width(100));
            EditorGUILayout.LabelField("Speed source", EditorStyles.miniBoldLabel, GUILayout.Width(110));
            EditorGUILayout.LabelField("× mult", EditorStyles.miniBoldLabel, GUILayout.Width(55));
            EditorGUILayout.LabelField("Priority", EditorStyles.miniBoldLabel, GUILayout.Width(55));
            EditorGUILayout.LabelField("Play mode", EditorStyles.miniBoldLabel, GUILayout.Width(90));
            EditorGUILayout.LabelField("Fall phase", EditorStyles.miniBoldLabel, GUILayout.Width(90));
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
            b.action = (CharacterAction)EditorGUILayout.EnumPopup(b.action, GUILayout.Width(100));
            b.speedSource = (StanceSpeedSource)EditorGUILayout.EnumPopup(b.speedSource, GUILayout.Width(110));
            b.speedMult = EditorGUILayout.FloatField(b.speedMult, GUILayout.Width(55));
            b.priority = EditorGUILayout.IntField(b.priority, GUILayout.Width(55));
            b.playMode = (StancePlayMode)EditorGUILayout.EnumPopup(b.playMode, GUILayout.Width(90));
            using (new EditorGUI.DisabledScope(b.playMode != StancePlayMode.FallTail))
                b.fallPhase = EditorGUILayout.Slider(b.fallPhase, 0f, 1f, GUILayout.Width(90));
            // Reset this row to its built-in default (StanceDefaults) — for rows saved before the defaults existed.
            if (GUILayout.Button(new GUIContent("↺", "Reset this stance to its default"), GUILayout.Width(24)))
            {
                var d = StanceDefaults.For(id);
                b.action = d.action; b.speedSource = d.speedSource; b.speedMult = d.speedMult;
                b.priority = d.priority; b.playMode = d.playMode; b.fallPhase = d.fallPhase; b.clip = d.clip;
                GUI.changed = true;
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
