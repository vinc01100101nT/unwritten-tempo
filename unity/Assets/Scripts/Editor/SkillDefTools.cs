using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Wires a <see cref="SkillRunner"/> onto the player for every skill in the catalog, so a newly authored skill can be
/// play-tested without adding its component by hand.
///
/// <para>This file used to also BUILD a default skill — Meteor, authored in code as the proof that the data-driven
/// pipeline worked. That proof is long since made: every skill is authored in the Skill Composer now. The builder was
/// removed on 2026-08-23 because it had become actively harmful: it wrote its skill under the pre-rename id, so
/// running it would resurrect a duplicate Meteor beside the real one.</para>
/// </summary>
public static class SkillDefTools
{
    [MenuItem("Tools/unwritten/Skills/Attach Data Skills To Player (Systems scene)")]
    public static void AttachToPlayer()
    {
        SkillCatalog.Reload();
        var host = ResolvePlayer();
        if (host == null) { Debug.LogWarning("[unwritten] No player found in the open scene (need a Player3DController)."); return; }

        var existing = new HashSet<SkillDef>();
        foreach (var r in host.GetComponents<SkillRunner>()) if (r.def != null) existing.Add(r.def);

        int added = 0;
        foreach (var def in SkillCatalog.All)
        {
            if (def == null || existing.Contains(def)) continue;
            var runner = host.AddComponent<SkillRunner>();
            runner.def = def;
            added++;
        }
        EditorUtility.SetDirty(host);
        Debug.Log($"[unwritten] Attached {added} data skill(s) to {host.name}. Re-bake the UI shell if the hotbar doesn't refresh.");
    }

    static GameObject ResolvePlayer()
    {
        var ctrl = Object.FindAnyObjectByType<Player3DController>();
        return ctrl != null ? ctrl.gameObject : null;
    }
}
