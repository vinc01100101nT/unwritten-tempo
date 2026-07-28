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
    GameObject seededFrom;   // prefab the hand popup was last seeded from (re-seed whenever this changes)
    WeaponHand hand = WeaponHand.Right;
    GameObject previewRig;
    GameObject weaponInstance;
    Transform handBone;

    // ---- batch grip ----
    const string WeaponPrefabDir = "Assets/1_Authored/Prefabs/Weapons";
    string batchPrefix = "";
    [Range(0f, 0.3f)] float gripInset = 0.06f;   // grab this fraction of the length up from the pommel end

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
        // Re-seed the hand popup from the prefab's authored grip whenever the assigned prefab changes (covers first
        // assign, window reopen, and domain reload) — so it reflects the SAVED hand, never a stale default.
        if (weaponPrefab != seededFrom)
        {
            seededFrom = weaponPrefab;
            var g = weaponPrefab != null ? weaponPrefab.GetComponent<WeaponGrip>() : null;
            if (g != null) hand = g.hand;
        }

        hand = (WeaponHand)EditorGUILayout.EnumPopup("Hand", hand);

        using (new EditorGUI.DisabledScope(weaponPrefab == null))
            if (GUILayout.Button("Spawn Preview")) SpawnPreview();

        using (new EditorGUI.DisabledScope(weaponInstance == null))
        {
            if (GUILayout.Button("Select Weapon (grab handles)")) Selection.activeGameObject = weaponInstance;
            if (GUILayout.Button("Bake Grip → Prefab")) BakeGrip();
        }

        if (GUILayout.Button("Clear Preview")) ClearPreview();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Batch — copy a tuned grip to many weapons", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Tune ONE weapon of a kind (e.g. a sword) and Bake Grip. Then, with that same tuned prefab in " +
            "'Weapon Prefab' above, type its name prefix (e.g. \"Sword\") and press the button below. Every " +
            "matching prefab gets a grip that reuses the tuned rotation, is auto-scaled to the seed's length, and " +
            "seats its handle-end in the same palm spot. Works for same-shaped weapons (all swords, all axes, …). " +
            "Spot-check a few after — outliers can be re-tuned individually.",
            MessageType.None);
        batchPrefix = EditorGUILayout.TextField("Name Prefix", batchPrefix);
        gripInset = EditorGUILayout.Slider("Grip Inset (up from pommel)", gripInset, 0f, 0.3f);
        using (new EditorGUI.DisabledScope(weaponPrefab == null || string.IsNullOrEmpty(batchPrefix)))
            if (GUILayout.Button("Copy Grip → all \"" + batchPrefix + "*\" prefabs")) BakeGripToMatching();
    }

    // Mesh-local bounds of a weapon prefab asset (its single mesh, at the mesh's own local space).
    static bool TryMeshBounds(GameObject prefab, out Bounds b)
    {
        b = default;
        var mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>(true) : null;
        var mesh = mf != null ? mf.sharedMesh : null;
        if (mesh == null) return false;
        b = mesh.bounds;
        return true;
    }

    // Copy the seed prefab's tuned grip onto every Weapons/<prefix>*.prefab: same hand + rotation, uniform scale
    // normalized to the seed's rendered length, and a position that seats each weapon's handle-end (pommel) at the
    // SAME hand-local point the seed's handle-end sits at. Anchoring to the pommel (Y-min of the mesh) — not the
    // mesh origin, which sits at different spots per weapon — is what makes one tuned grip transfer correctly.
    void BakeGripToMatching()
    {
        var seedGrip = weaponPrefab.GetComponent<WeaponGrip>();
        if (seedGrip == null)
        {
            EditorUtility.DisplayDialog("Weapon Grip Tuner",
                "The assigned 'Weapon Prefab' has no baked WeaponGrip yet. Tune + Bake Grip on it first, then batch.", "OK");
            return;
        }
        if (!TryMeshBounds(weaponPrefab, out var seedB))
        {
            EditorUtility.DisplayDialog("Weapon Grip Tuner", "Seed prefab has no mesh to measure.", "OK");
            return;
        }

        var R = Quaternion.Euler(seedGrip.localEuler);
        var sSeed = seedGrip.localScale;
        float seedLen = seedB.size.y * sSeed.y;                                  // the seed's rendered length
        Vector3 seedAnchorMesh = new Vector3(seedB.center.x, seedB.min.y + gripInset * seedB.size.y, seedB.center.z);
        Vector3 palm = seedGrip.localPosition + R * Vector3.Scale(sSeed, seedAnchorMesh);   // hand-local pommel spot

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { WeaponPrefabDir });
        int done = 0, skipped = 0;
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string fname = System.IO.Path.GetFileNameWithoutExtension(path);
            if (!fname.StartsWith(batchPrefix, System.StringComparison.OrdinalIgnoreCase)) continue;
            if (path == AssetDatabase.GetAssetPath(weaponPrefab)) continue;      // don't overwrite the seed itself

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!TryMeshBounds(asset, out var b) || b.size.y < 1e-4f) { skipped++; continue; }

            float uni = seedLen / b.size.y;                                       // normalize length to the seed
            Vector3 s = new Vector3(uni, uni, uni);
            Vector3 anchorMesh = new Vector3(b.center.x, b.min.y + gripInset * b.size.y, b.center.z);
            Vector3 pos = palm - R * Vector3.Scale(s, anchorMesh);

            var root = PrefabUtility.LoadPrefabContents(path);
            var grip = root.GetComponent<WeaponGrip>() ?? root.AddComponent<WeaponGrip>();
            grip.hand = seedGrip.hand;
            grip.localEuler = seedGrip.localEuler;
            grip.localScale = s;
            grip.localPosition = pos;
            PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
            done++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[unwritten] Batch grip from '{weaponPrefab.name}': wrote {done} prefab(s) matching '{batchPrefix}*', " +
                  $"skipped {skipped} (no mesh).");
        EditorUtility.DisplayDialog("Weapon Grip Tuner",
            $"Copied grip to {done} '{batchPrefix}*' prefab(s). Spot-check a few in the tuner; re-bake any outliers.", "OK");
    }

    void SpawnPreview()
    {
        ClearPreview();

        string modelPath = Player3DVisualBuilder.DefaultModel;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (model == null) { EditorUtility.DisplayDialog("Weapon Grip Tuner", "No preview rig at " + modelPath, "OK"); return; }

        previewRig = (GameObject)PrefabUtility.InstantiatePrefab(model);
        previewRig.name = "GripTuner Preview Rig";
        // Normalize to the SAME height the runtime player uses (Player3DVisualBuilder.Attach) so the preview matches
        // the in-game rig instead of showing the model at its huge native FBX size.
        float nativeH = Character3DRigBuilder.MeasureHeight(previewRig);
        previewRig.transform.localScale = Vector3.one * (nativeH > 1e-3f ? Character3DRigBuilder.TargetHeight / nativeH : 1f);
        var anim = previewRig.GetComponent<Animator>() ?? previewRig.AddComponent<Animator>();
        var avatar = Character3DRigBuilder.LoadAvatar(modelPath);
        if (avatar != null) anim.avatar = avatar;

        var grip = weaponPrefab.GetComponent<WeaponGrip>();
        handBone = HandSocketResolver.Find(anim, hand);
        if (handBone == null)
        {
            EditorUtility.DisplayDialog("Weapon Grip Tuner", "Could not find a hand bone on the preview rig.", "OK");
            ClearPreview();
            return;
        }

        weaponInstance = (GameObject)PrefabUtility.InstantiatePrefab(weaponPrefab);
        // Stamp the chosen hand on the instance so Bake writes it back (the runtime socket reads grip.hand to pick the bone).
        var instGrip = weaponInstance.GetComponent<WeaponGrip>() ?? weaponInstance.AddComponent<WeaponGrip>();
        instGrip.hand = hand;
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
