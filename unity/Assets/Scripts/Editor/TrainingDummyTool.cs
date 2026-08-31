using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click combat test target. Spawns a <see cref="TrainingDummy"/>: a billboard mob built the
/// normal way (<see cref="RigBuilder25D.BuildMonster"/>) so it's click-targetable and takes hits,
/// then made completely inert — 10,000,000 HP, no movement, no aggro, no touch-damage — and set to
/// reset to full HP every 5 s. Stand next to it and swing (or cast Q) to read your damage / DPS off
/// the floating HP label before the regen wipes it. Re-runnable; replaces any prior dummy.
/// </summary>
public static class TrainingDummyTool
{
    const string Name = "Training_Dummy";

    [MenuItem("Tools/unwritten/Mobs & NPCs/Spawn Training Dummy", priority = 310)]
    static void Spawn()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Training Dummy", "Exit Play mode first, then re-run.", "OK"); return; }

        var tex = RigBuilder25D.MonsterSheet();
        if (tex == null)
        { EditorUtility.DisplayDialog("Training Dummy",
            "No Ninja Adventure monster sprite sheet found under Assets/1_Authored/Art/Billboards/Actor/Monster.", "OK"); return; }

        var prev = GameObject.Find(Name);
        if (prev != null) Object.DestroyImmediate(prev);

        Vector3 foot = ChooseSpot();
        var go = RigBuilder25D.BuildMonster(tex, foot, false);   // a normal mob: targetable + takes hits…
        go.name = Name;
        go.transform.localScale = Vector3.one * 1.0f;            // a touch bigger than mobs (0.7) so it reads as a fixture
        Undo.RegisterCreatedObjectUndo(go, "Spawn Training Dummy");

        // …then neuter it into an immobile punching bag — through the ONE description of what a dummy is, which the
        // server uses too. Written here once and it drifted the moment the server needed to make one.
        // The nameplate STAYS: the same bottom-anchored HP plate every other mob wears, reading out in the
        // target/hover panels the same way.
        TrainingDummy.MakeOne(go);

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        SceneView.lastActiveSceneView?.FrameSelected();

        Debug.Log($"[unwritten] Spawned '{Name}' at {foot} — {TrainingDummy.DummyHP:N0} HP, resets every 5 s.");
        EditorUtility.DisplayDialog("Training Dummy — spawned",
            $"Placed '{Name}' ({TrainingDummy.DummyHP:N0} HP).\n\n" +
            "Press Play, walk up to it and LEFT-click to attack (or Q to cast). Damage now ROLLS in a " +
            "min–max range each hit, and some swings show a red MISS — the dummy has FLEE 15, so low DEX " +
            "whiffs a fair bit and your accuracy (HIT = level + DEX) climbs as you add DEX. (Set its " +
            "Health ▸ Flee to 0 in the inspector for a clean, never-miss DPS read.) It reads out exactly like " +
            "any other mob — HP plate under its feet, full panel while you're engaged; it refills to full every 5 seconds.\n\n" +
            "Re-run to respawn it, or Tools ▸ unwritten ▸ Clear Training Dummy to remove it.", "OK");
    }

    [MenuItem("Tools/unwritten/Mobs & NPCs/Clear Training Dummy", priority = 311)]
    static void Clear()
    {
        var prev = GameObject.Find(Name);
        if (prev == null) { Debug.Log("[unwritten] No Training_Dummy to clear."); return; }
        Undo.DestroyObjectImmediate(prev);
        EditorSceneManager.MarkSceneDirty(prev.scene);
    }

    // Drop the dummy in front of the player if one's in the open scene, else on the ground under the
    // scene-view camera, else the origin — snapped to the Ground-layer terrain when we can ray it.
    static Vector3 ChooseSpot()
    {
        var player = Object.FindAnyObjectByType<Player3DController>();
        Vector3 guess = player != null ? player.transform.position + player.transform.forward * 0f + Vector3.forward * 3f
                                       : Vector3.zero;

        int groundLayer = LayerMask.NameToLayer("Ground");
        if (groundLayer >= 0)
        {
            Physics.SyncTransforms();
            int mask = 1 << groundLayer;
            if (Physics.Raycast(new Vector3(guess.x, 200f, guess.z), Vector3.down, out var hit, 400f,
                                mask, QueryTriggerInteraction.Ignore))
                return hit.point;
        }
        guess.y = 0f;
        return guess;
    }
}
