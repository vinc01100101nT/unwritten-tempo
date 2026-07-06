using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Author-time WYSIWYG editor for a weapon's <see cref="WeaponGrip"/> — spawns a rigged character preview, mounts the
/// weapon prefab on the SAME hand bone the runtime <see cref="WeaponSocket"/> uses (via <see cref="HandSocketResolver"/>,
/// so tuner and game can't drift), and lets you drag the weapon into the palm with normal Scene handles. "Bake Grip"
/// copies the weapon's local transform back onto the prefab's <see cref="WeaponGrip"/> — the single source of truth the
/// game reads. Nothing here runs in Play; the grip is baked once at author time.
/// </summary>
public class WeaponGripTuner : EditorWindow
{
    GameObject weaponPrefab;
    GameObject previewRig;
    GameObject weaponInstance;
    Transform handBone;

    [MenuItem("Tools/unwritten/Weapons/Weapon Grip Tuner", priority = 660)]
    static void Open() => GetWindow<WeaponGripTuner>("Weapon Grip Tuner");

    void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "1. Assign a weapon prefab (a 3D mesh).\n" +
            "2. Spawn Preview — a rigged character appears with the weapon on its hand.\n" +
            "3. Move/rotate the selected weapon in the Scene view so the hilt sits in the palm.\n" +
            "4. Bake Grip → writes the offset onto the prefab's WeaponGrip. Assign the prefab to an Item's Equip Model.",
            MessageType.Info);

        weaponPrefab = (GameObject)EditorGUILayout.ObjectField("Weapon Prefab", weaponPrefab, typeof(GameObject), false);

        using (new EditorGUI.DisabledScope(weaponPrefab == null))
            if (GUILayout.Button("Spawn Preview")) SpawnPreview();

        using (new EditorGUI.DisabledScope(weaponInstance == null))
        {
            if (GUILayout.Button("Select Weapon (grab handles)")) Selection.activeGameObject = weaponInstance;
            if (GUILayout.Button("Bake Grip → Prefab")) BakeGrip();
        }

        if (GUILayout.Button("Clear Preview")) ClearPreview();
    }

    void SpawnPreview()
    {
        ClearPreview();

        string modelPath = Player3DVisualBuilder.DefaultModel;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null) { EditorUtility.DisplayDialog("Weapon Grip Tuner", "No preview rig at " + modelPath, "OK"); return; }

        previewRig = (GameObject)PrefabUtility.InstantiatePrefab(model);
        previewRig.name = "GripTuner Preview Rig";
        var anim = previewRig.GetComponent<Animator>() ?? previewRig.AddComponent<Animator>();
        var avatar = Character3DRigBuilder.LoadAvatar(modelPath);
        if (avatar != null) anim.avatar = avatar;

        var grip = weaponPrefab.GetComponent<WeaponGrip>();
        handBone = HandSocketResolver.Find(anim, grip != null ? grip.hand : WeaponHand.Right);
        if (handBone == null)
        {
            EditorUtility.DisplayDialog("Weapon Grip Tuner", "Could not find a hand bone on the preview rig.", "OK");
            ClearPreview();
            return;
        }

        weaponInstance = (GameObject)PrefabUtility.InstantiatePrefab(weaponPrefab);
        var t = weaponInstance.transform;
        t.SetParent(handBone, false);
        t.localPosition = grip != null ? grip.localPosition : Vector3.zero;
        t.localRotation = grip != null ? Quaternion.Euler(grip.localEuler) : Quaternion.identity;
        t.localScale    = grip != null ? grip.localScale : Vector3.one;

        Selection.activeGameObject = weaponInstance;
        SceneView.FrameLastActiveSceneView();
    }

    void BakeGrip()
    {
        if (weaponInstance == null || weaponPrefab == null) return;
        var t = weaponInstance.transform;
        var handEnum = weaponInstance.GetComponent<WeaponGrip>()?.hand ?? WeaponHand.Right;

        // Write ONLY the grip onto the prefab asset (leave its root transform untouched — the runtime socket
        // overwrites TRS from these grip values when it mounts).
        string assetPath = AssetDatabase.GetAssetPath(weaponPrefab);
        var root = PrefabUtility.LoadPrefabContents(assetPath);
        var grip = root.GetComponent<WeaponGrip>() ?? root.AddComponent<WeaponGrip>();
        grip.hand = handEnum;
        grip.localPosition = t.localPosition;
        grip.localEuler    = t.localEulerAngles;
        grip.localScale    = t.localScale;
        PrefabUtility.SaveAsPrefabAsset(root, assetPath);
        PrefabUtility.UnloadPrefabContents(root);
        Debug.Log($"[unwritten] Baked grip onto '{Path.GetFileName(assetPath)}': " +
                  $"pos {grip.localPosition}, euler {grip.localEuler}, scale {grip.localScale}.");
    }

    void ClearPreview()
    {
        if (previewRig != null) DestroyImmediate(previewRig);
        if (weaponInstance != null) DestroyImmediate(weaponInstance);
        previewRig = null;
        weaponInstance = null;
        handBone = null;
    }

    void OnDisable() => ClearPreview();
}
