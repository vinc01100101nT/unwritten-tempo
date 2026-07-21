using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Edit-mode authoring window for the <see cref="VfxCueLibrary"/> — the third stage of the pipeline:
/// <b>Vfx Composer</b> (author the look → a <see cref="VfxSequence"/>) → <b>Vfx Cue Composer</b> (bind a gameplay
/// event → that look) → in-game (events fire cues via <see cref="VfxCue"/>). It's the status/verb sibling of the
/// Skill Composer, but far simpler: a flat table of every <see cref="VfxCueId"/> with the sequence + anchor it
/// plays at — no tick→effect bindings, because a cue carries no gameplay (the event already happened).
///
/// Writes <c>Assets/3_Data/Resources/VfxCueLibrary.asset</c> (created on demand) and reloads the runtime
/// <see cref="VfxCueCatalog"/> on save so changes show without a domain reload.
/// </summary>
public class VfxCueComposerWindow : EditorWindow
{
    const string Dir = "Assets/3_Data/Resources";
    const string AssetPath = "Assets/3_Data/Resources/VfxCueLibrary.asset";

    VfxCueLibrary lib;
    Vector2 scroll;

    [MenuItem("Tools/unwritten/Composers/Vfx Cue Composer")]
    public static void Open()
    {
        var w = GetWindow<VfxCueComposerWindow>("Vfx Cue Composer");
        w.minSize = new Vector2(40, 400);
        w.LoadOrCreate();
    }

    void OnEnable() { if (lib == null) LoadOrCreate(); }

    void LoadOrCreate()
    {
        lib = AssetDatabase.LoadAssetAtPath<VfxCueLibrary>(AssetPath);
        if (lib == null)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/3_Data", "Resources");
            lib = CreateInstance<VfxCueLibrary>();
            AssetDatabase.CreateAsset(lib, AssetPath);
            AssetDatabase.SaveAssets();
        }
        Reconcile();
    }

    // Ensure exactly one binding row per enum id (add missing; the enum is append-only, so we never remove).
    void Reconcile()
    {
        foreach (VfxCueId id in Enum.GetValues(typeof(VfxCueId)))
            if (Find(id) == null)
                lib.cues.Add(new VfxCueBinding { id = id, scale = 1f });
    }

    VfxCueBinding Find(VfxCueId id)
    {
        for (int i = 0; i < lib.cues.Count; i++)
            if (lib.cues[i] != null && lib.cues[i].id == id) return lib.cues[i];
        return null;
    }

    void OnGUI()
    {
        if (lib == null) { if (GUILayout.Button("Create Vfx Cue Library")) LoadOrCreate(); return; }

        EditorGUILayout.HelpBox(
            "Bind each gameplay event to a composed VfxSequence (author looks in the Vfx Composer).\n" +
            "*Start cues should be an Attach/looping sequence — they run as a persistent aura for the ailment's " +
            "duration. Everything else plays once. Leave a cue's sequence empty for a silent no-op.",
            MessageType.Info);

        EditorGUILayout.Space(4);
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUI.BeginChangeCheck();

        // Passive Verbs = things that happen TO a unit: every cue here fires on the RECEIVER only (no attacker,
        // no direction). Active Verbs (attacker-side, e.g. OnAttack) aren't built yet — see VfxCueId.
        Section("Passive Verbs (receiver-only, one-shot)");
        Row(VfxCueId.OnHitByNormalAttack); Row(VfxCueId.OnHitByCriticalAttack); Row(VfxCueId.OnHitBySkill);
        Row(VfxCueId.OnReceiveHeal); Row(VfxCueId.OnLevelUp); Row(VfxCueId.OnDeath);

        Section("Damage-over-time (Start = aura, Tick = puff)");
        Row(VfxCueId.OnPoisonStart); Row(VfxCueId.OnPoisonTick);
        Row(VfxCueId.OnBleedStart);  Row(VfxCueId.OnBleedTick);
        Row(VfxCueId.OnBurnStart);   Row(VfxCueId.OnBurnTick);

        Section("Control / debuff (one-shot on apply)");
        Row(VfxCueId.OnStun); Row(VfxCueId.OnFreeze); Row(VfxCueId.OnSlow);
        Row(VfxCueId.OnHaste); Row(VfxCueId.OnSilence); Row(VfxCueId.OnBlind);

        Section("Skill lifecycle (one-shot)");
        Row(VfxCueId.OnCastStart);

        Section("Movement (actor-side, one-shot)");
        Row(VfxCueId.OnSprint);

        if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(lib);

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Save + Reload Catalog", GUILayout.Height(26)))
            {
                EditorUtility.SetDirty(lib);
                AssetDatabase.SaveAssets();
                VfxCueCatalog.Reload();
                Debug.Log("[unwritten] Saved VfxCueLibrary + reloaded VfxCueCatalog.");
            }
            if (GUILayout.Button("Ping Asset", GUILayout.Width(90), GUILayout.Height(26)))
                EditorGUIUtility.PingObject(lib);
        }
    }

    void Section(string title)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }

    void Row(VfxCueId id)
    {
        var b = Find(id);
        if (b == null) return;
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(id.ToString(), GUILayout.Width(120));
            b.vfx = (VfxSequence)EditorGUILayout.ObjectField(b.vfx, typeof(VfxSequence), false);
            EditorGUIUtility.labelWidth = 34;
            b.scale = EditorGUILayout.FloatField("×", b.scale, GUILayout.Width(70));
            EditorGUIUtility.labelWidth = 0;
        }
        b.offset = EditorGUILayout.Vector3Field(GUIContent.none, b.offset);
    }
}
