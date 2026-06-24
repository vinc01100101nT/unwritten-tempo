using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom inspector for <see cref="CharacterDef"/> — adds the one-click <b>Bake &amp; Build Prefab</b> button.
/// It bakes every action's 8-direction strips into a <c>Sprites/</c> folder beside the asset (via the shared
/// <see cref="SpriteBakeCore"/>), then (re)writes a <see cref="BakedBillboard8"/> prefab next to it, fully wired.
/// Guards against the two contract mistakes: duplicate actions and a missing required Idle.
/// </summary>
[CustomEditor(typeof(CharacterDef))]
public class CharacterBuilder : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var def = (CharacterDef)target;

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);

        // ---- validation ----
        var problems = new List<string>();
        if (def.model == null) problems.Add("Assign a rigged model.");

        var dups = FindDuplicateActions(def);
        if (dups.Count > 0) problems.Add("Duplicate action(s): " + string.Join(", ", dups) + " — each action must be unique.");

        bool hasIdle = false; int validClips = 0;
        foreach (var a in def.actions)
        {
            if (a == null || a.clip == null) continue;
            validClips++;
            if (a.action == CharacterAction.Idle) hasIdle = true;
        }
        if (validClips == 0) problems.Add("Add at least one action with a clip.");
        if (validClips > 0 && !hasIdle) problems.Add("A character needs an Idle action (with a clip).");

        string assetPath = AssetDatabase.GetAssetPath(def);
        if (string.IsNullOrEmpty(assetPath)) problems.Add("Save this CharacterDef as an asset first (it bakes beside itself).");

        if (problems.Count > 0)
            EditorGUILayout.HelpBox("• " + string.Join("\n• ", problems), MessageType.Warning);
        else
        {
            string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            EditorGUILayout.HelpBox($"Bakes → {dir}/Sprites/ , prefab → {dir}/{def.name}.prefab\n" +
                                    $"{validClips} action(s) × 8 directions = {SpriteBakeCore.EstimateImages(CoreActions(def))} images.",
                                    MessageType.Info);
        }

        using (new EditorGUI.DisabledScope(problems.Count > 0))
            if (GUILayout.Button("Bake & Build Prefab", GUILayout.Height(34)))
                Build(def, assetPath);
    }

    static List<string> FindDuplicateActions(CharacterDef def)
    {
        var seen = new HashSet<CharacterAction>();
        var dups = new List<string>();
        foreach (var a in def.actions)
        {
            if (a == null || a.clip == null) continue;
            if (!seen.Add(a.action) && !dups.Contains(a.action.ToString())) dups.Add(a.action.ToString());
        }
        return dups;
    }

    static List<SpriteBakeCore.Action> CoreActions(CharacterDef def)
    {
        var list = new List<SpriteBakeCore.Action>();
        foreach (var a in def.actions)
            if (a != null && a.clip != null)
                list.Add(new SpriteBakeCore.Action { name = a.action.ToString(), clip = a.clip, fps = a.fps, frames = a.frames });
        return list;
    }

    static void Build(CharacterDef def, string assetPath)
    {
        string dir = Path.GetDirectoryName(assetPath).Replace('\\', '/');
        string spritesDir = dir + "/Sprites";
        string prefabPath = dir + "/" + def.name + ".prefab";

        var settings = new SpriteBakeCore.Settings
        {
            cellSize = def.cellSize,
            pixelate = def.pixelate,
            superSample = def.superSample,
            cameraPitch = def.cameraPitch,
            framePadding = def.framePadding,
            yawOffset = def.yawOffset,
            clockwise = def.clockwise,
        };
        var coreActions = CoreActions(def);

        if (!SpriteBakeCore.Bake(def.model, def.albedo, settings, coreActions, spritesDir))
            return;   // cancelled or failed; SpriteBakeCore already cleaned up

        // ---- build / overwrite the prefab, fully wired to the freshly baked strips ----
        var go = new GameObject(def.name);
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, def.worldScale);

        var bb = go.AddComponent<BakedBillboard8>();
        bb.cellW = def.cellSize; bb.cellH = def.cellSize; bb.ppu = def.cellSize;
        bb.maxFrames = SpriteBakeCore.MaxFrames(coreActions);
        bb.feetOffset = def.feetOffset;
        bb.walkThreshold = def.walkThreshold;
        bb.runThreshold = def.runThreshold;
        bb.actions = new List<BakedBillboard8.ActionSet>();
        foreach (var a in def.actions)
            if (a != null && a.clip != null)
                bb.actions.Add(new BakedBillboard8.ActionSet
                {
                    action = a.action,
                    dirs = SpriteBakeCore.LoadDir8(spritesDir, a.action.ToString()),
                    fps = a.fps,
                });

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
        AssetDatabase.Refresh();

        if (prefab != null)
        {
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"[unwritten] Built character '{def.name}' → {prefabPath} ({coreActions.Count} actions, strips in {spritesDir}).");
        }
    }
}
