using System;
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

    [MenuItem("Tools/unwritten/Art/Stance Composer")]
    public static void Open()
    {
        var w = GetWindow<StanceComposerWindow>("Stance Composer");
        w.minSize = new Vector2(760, 420);
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
            if (GUILayout.Button("Save + Reload Catalog", GUILayout.Height(26)))
            {
                EditorUtility.SetDirty(lib);
                AssetDatabase.SaveAssets();
                StanceCatalog.Reload();
                Debug.Log("[unwritten] Saved StanceLibrary + reloaded StanceCatalog.");
            }
            if (GUILayout.Button("Ping Asset", GUILayout.Width(90), GUILayout.Height(26)))
                EditorGUIUtility.PingObject(lib);
        }
    }

    void Header()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            EditorGUILayout.LabelField("Stance", EditorStyles.miniBoldLabel, GUILayout.Width(80));
            EditorGUILayout.LabelField("Action", EditorStyles.miniBoldLabel, GUILayout.Width(100));
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
                b.priority = d.priority; b.playMode = d.playMode; b.fallPhase = d.fallPhase;
                GUI.changed = true;
            }
        }
    }
}
