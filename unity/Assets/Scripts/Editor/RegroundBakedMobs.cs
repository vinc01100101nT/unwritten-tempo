using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

// QUICKFIX 2026-07-03 — one-time migration of mobs baked BEFORE the feet-pivot convention (UnitAnchor):
// their transform sat at ground + half-height (centre) with CharacterController center=(0,SpriteFoot,0).
// This drops each such mob's pivot onto the ground and stacks its capsule up from the feet. Idempotent
// (skips mobs already on the new convention). Delete after every map has been re-baked or migrated.
public static class RegroundBakedMobs
{
    [MenuItem("Tools/unwritten/Quick Fixes/Reground Baked Mobs", priority = 900)]
    public static void Reground()
    {
        var mobs = Object.FindObjectsByType<MonsterAI3D>();
        int moved = 0, skipped = 0;
        foreach (var mob in mobs)
        {
            var t = mob.transform;
            var cc = mob.GetComponent<CharacterController>();
            // Signature of the NEW convention: capsule already stacks up from the feet (center.y ≈ height/2).
            // Such a mob is already grounded — leave it (keeps this re-runnable and safe on mixed scenes).
            if (cc != null && Mathf.Abs(cc.center.y - cc.height * 0.5f) < 0.01f) { skipped++; continue; }

            Undo.RecordObject(t, "Reground Mob");
            var p = t.position;
            p.y -= UnitAnchor.HalfHeight(t);   // stale Y = ground + half-height; drop the pivot onto the ground
            t.position = p;

            if (cc != null)
            {
                Undo.RecordObject(cc, "Reground Mob Capsule");
                var c = cc.center; c.y = cc.height * 0.5f; cc.center = c;   // capsule bottom at the feet pivot
            }
            EditorUtility.SetDirty(mob);
            moved++;
        }
        if (moved > 0) EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[unwritten] Reground Baked Mobs: dropped {moved} mob(s) onto the ground, skipped {skipped} " +
                  "already on the feet-pivot convention. Save the scene to persist.");
    }
}
