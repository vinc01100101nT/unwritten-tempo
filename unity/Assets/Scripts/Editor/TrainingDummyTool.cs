using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click combat test target. Spawns a <see cref="TrainingDummy"/>: a billboard mob built the
/// normal way (<see cref="Build25DSpike.BuildMonster"/>) so it's click-targetable and takes hits,
/// then made completely inert — 10,000,000 HP, no movement, no aggro, no touch-damage — and set to
/// reset to full HP every 5 s. Stand next to it and swing (or cast Q) to read your damage / DPS off
/// the floating HP label before the regen wipes it. Re-runnable; replaces any prior dummy.
/// </summary>
public static class TrainingDummyTool
{
    const string Name = "Training_Dummy";
    const int    DummyHP = 10_000_000;

    [MenuItem("Tools/unwritten/Spawn Training Dummy", priority = 10)]
    static void Spawn()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorUtility.DisplayDialog("Training Dummy", "Exit Play mode first, then re-run.", "OK"); return; }

        var tex = Build25DSpike.MonsterSheet();
        if (tex == null)
        { EditorUtility.DisplayDialog("Training Dummy",
            "No Ninja Adventure monster sprite sheet found under Assets/Art/NinjaAdventure/Actor/Monster.", "OK"); return; }

        var prev = GameObject.Find(Name);
        if (prev != null) Object.DestroyImmediate(prev);

        Vector3 foot = ChooseSpot();
        var go = Build25DSpike.BuildMonster(tex, foot, false);   // a normal mob: targetable + takes hits…
        go.name = Name;
        go.transform.localScale = Vector3.one * 1.0f;            // a touch bigger than mobs (0.7) so it reads as a fixture
        Undo.RegisterCreatedObjectUndo(go, "Spawn Training Dummy");

        // …then neuter it into an immobile punching bag.
        var health = go.GetComponent<Health>();
        health.maxHealth = DummyHP;     // Health.Awake copies this into Current on Play
        health.defense = 0;             // measure raw weapon/skill output, not post-mitigation
        health.magicDefense = 0;
        health.flee = 15;               // RO FLEE: low DEX misses it a fair bit (red MISS), accuracy climbs as you add DEX
                                        // (set Health ▸ Flee to 0 in the inspector for a clean, never-miss DPS read)

        var ai = go.GetComponent<MonsterAI3D>();
        if (ai != null)
        {
            ai.moveSpeed = 0f;
            ai.aggroRadius = 0f;        // never chase
            ai.leashRadius = 0f;
            ai.wanderRadius = 0f;       // never wander — stays put as a fixed target
            ai.touchRange = 0f;         // never bite the player
            ai.attack = 0;
            ai.xpReward = 0;            // no free XP from the test rig
        }

        // It never dies (10M HP + regen), so the recycle/death FX would never fire — drop them so the
        // dummy is a clean, predictable fixture.
        StripIfPresent<MobRespawn>(go);
        StripIfPresent<DeathDissolve>(go);
        // (No MobNameplate to strip — MonsterAI3D.Awake skips the dummy, which shows its own "TEST DUMMY"
        // readout above its head.)

        var sr = go.GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = new Color(0.7f, 0.72f, 0.78f);   // slate-gray "training" tint (also the Revive base color)

        var dummy = go.AddComponent<TrainingDummy>();
        dummy.regenInterval = 5f;

        EditorSceneManager.MarkSceneDirty(go.scene);
        Selection.activeGameObject = go;
        SceneView.lastActiveSceneView?.FrameSelected();

        Debug.Log($"[unwritten] Spawned '{Name}' at {foot} — {DummyHP:N0} HP, resets every 5 s.");
        EditorUtility.DisplayDialog("Training Dummy — spawned",
            $"Placed '{Name}' ({DummyHP:N0} HP).\n\n" +
            "Press Play, walk up to it and LEFT-click to attack (or Q to cast). Damage now ROLLS in a " +
            "min–max range each hit, and some swings show a red MISS — the dummy has FLEE 15, so low DEX " +
            "whiffs a fair bit and your accuracy (HIT = level + DEX) climbs as you add DEX. (Set its " +
            "Health ▸ Flee to 0 in the inspector for a clean, never-miss DPS read.) Its floating HP label " +
            "shows the damage stacking up; it refills to full every 5 seconds.\n\n" +
            "Re-run to respawn it, or Tools ▸ unwritten ▸ Clear Training Dummy to remove it.", "OK");
    }

    [MenuItem("Tools/unwritten/Clear Training Dummy", priority = 11)]
    static void Clear()
    {
        var prev = GameObject.Find(Name);
        if (prev == null) { Debug.Log("[unwritten] No Training_Dummy to clear."); return; }
        Undo.DestroyObjectImmediate(prev);
        EditorSceneManager.MarkSceneDirty(prev.scene);
    }

    static void StripIfPresent<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c != null) Object.DestroyImmediate(c);
    }

    // Drop the dummy in front of the player if one's in the open scene, else on the ground under the
    // scene-view camera, else the origin — snapped to the Ground-layer terrain when we can ray it.
    static Vector3 ChooseSpot()
    {
        var player = Object.FindFirstObjectByType<Player3DController>();
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
