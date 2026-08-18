using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>The Skill Composer's operations that reach BEYOND the skill on screen — the ones that read or write EVERY
/// saved skill. They live apart from the window because that is exactly what makes them risky and worth finding: a
/// rename re-points other assets, a save can disarm another skill's gate, and the gate check reads the whole roster.
/// The window owns one skill's fields; these own the library.</summary>
public static class SkillLibrarySweeps
{
    static readonly List<StateId> grantedScratch = new List<StateId>(4);

    /// <summary>Rename a skill's id AND re-point everything that referenced the old one, in the same undo step — so an
    /// id is a label you can change, never a fragile string other assets silently depend on.
    ///
    /// <para>Sweeps every place a skill id is PERSISTED: a skill's <c>triggerSkillId</c> ("on cast complete of") and
    /// <c>requireSkillId</c> ("stacks from"), plus every Item's <c>grantedSkillIds</c> (a weapon hands its kit out by
    /// id, so a rename would otherwise strip that skill off the weapon). Refuses a blank id or one another skill
    /// already owns, since either would merge two skills into one reference.</para></summary>
    public static void RetargetSkillId(List<SkillDef> all, string oldId, string newId, SkillDef target)
    {
        if (target == null) return;
        newId = (newId ?? "").Trim();
        if (string.IsNullOrEmpty(newId))
        {
            EditorUtility.DisplayDialog("Rename skill id", "An id can't be empty — keeping the old one.", "OK");
            return;
        }
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && all[i] != target && all[i].id == newId)
            {
                EditorUtility.DisplayDialog("Rename skill id",
                    $"\"{newId}\" is already used by {all[i].displayName}. Ids must be unique — keeping the old one.", "OK");
                return;
            }

        Undo.RecordObject(target, "Rename skill id");
        target.id = newId;
        EditorUtility.SetDirty(target);
        SyncFileNameToId(target);
        if (string.IsNullOrEmpty(oldId)) return;   // brand-new skill: nothing can point at it yet

        int skillRefs = 0;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (s == null) continue;
            bool touched = false;
            // Both ids a skill can hold live PER TRIGGER now, so the sweep walks every entry — miss one and that
            // trigger silently stops matching (or stops finding its stack pile) after a rename.
            if (s.triggers != null)
                for (int t = 0; t < s.triggers.Count; t++)
                {
                    var e = s.triggers[t];
                    if (e == null) continue;
                    if (e.triggerSkillId == oldId) { Undo.RecordObject(s, "Rename skill id"); e.triggerSkillId = newId; touched = true; }
                    if (e.requireSkillId == oldId) { Undo.RecordObject(s, "Rename skill id"); e.requireSkillId = newId; touched = true; }
                }
            if (touched) { EditorUtility.SetDirty(s); skillRefs++; }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[Skill Composer] Renamed \"{oldId}\" → \"{newId}\"; re-pointed {skillRefs} skill reference(s). " +
                  "A weapon that already ROLLED the old id (in a save) simply drops it on load — kits are per-copy now.");
    }

    /// <summary>The file name ALWAYS follows the id — <c>skill.sword.parry</c> lives in <c>skill.sword.parry.asset</c>,
    /// no exceptions. One name, one meaning: browsing the folder tells you exactly what's authored, and no one has to
    /// open an asset to learn which skill it is. Every path that can change or mint an id calls this, so the two can't
    /// drift apart. Renaming through <see cref="AssetDatabase"/> carries the <c>.meta</c> along, so the GUID — and every
    /// scene/prefab/asset reference — survives untouched.</summary>
    public static void SyncFileNameToId(SkillDef s)
    {
        if (s == null || string.IsNullOrEmpty(s.id)) return;
        string path = AssetDatabase.GetAssetPath(s);
        if (string.IsNullOrEmpty(path) || System.IO.Path.GetFileNameWithoutExtension(path) == s.id) return;
        string err = AssetDatabase.RenameAsset(path, s.id);
        if (!string.IsNullOrEmpty(err))
            Debug.LogWarning($"[Skill Composer] Couldn't rename \"{path}\" to \"{s.id}.asset\": {err}");
    }

    /// <summary>Every pile the given skill actually hands out — one entry per STACKING effect that creates a state
    /// (<see cref="SkillEffect.TryStateFor"/>). Reused by the picker below and by the sweeps that keep consumers in
    /// step, so "what piles does this skill grant" is answered in exactly one place.
    ///
    /// <para>Non-stacking effects are excluded on purpose: a single-instance state has no pile to spend down, so
    /// gating on it could never mean "how many do I hold".</para></summary>
    public static void CollectGrantedStates(SkillDef granter, List<StateId> into)
    {
        into.Clear();
        if (granter?.bindings == null) return;
        for (int b = 0; b < granter.bindings.Count; b++)
        {
            var fx = granter.bindings[b]?.effects;
            if (fx == null) continue;
            for (int i = 0; i < fx.Count; i++)
            {
                var ef = fx[i];
                if (ef == null || !ef.stacking) continue;
                SkillEffect.CollectPiles(ef, into);
            }
        }
    }

    static readonly List<StateId> grantedBefore = new List<StateId>(4);
    static readonly List<StateId> grantedAfter = new List<StateId>(4);

    /// <summary>Keep every consumer of THIS skill's piles honest after its stacking effects were edited. A gate names
    /// a pair — whose pile, and which — so changing what this skill grants would otherwise leave other skills pointing
    /// at a pile that no longer exists, and they would silently stop firing.
    ///
    /// <para>Diffed against the last SAVED version, so the rule is simple and predictable: exactly one pile gone and
    /// exactly one new one is an EDIT of that pile (consumers follow it); anything else that removes a pile is a
    /// DELETION (consumers lose that gate rather than keep a reference that can never match). Run at Save, next to
    /// the other whole-library fixups, because it writes to OTHER assets.</para></summary>
    public static void SyncConsumersToGrantedStates(List<SkillDef> all, SkillDef granter, SkillDef before)
    {
        if (granter == null || before == null || string.IsNullOrEmpty(granter.id)) return;
        CollectGrantedStates(before, grantedBefore);
        CollectGrantedStates(granter, grantedAfter);

        StateId? renamedFrom = null, renamedTo = null;
        int gone = 0;
        for (int i = 0; i < grantedBefore.Count; i++)
            if (!grantedAfter.Contains(grantedBefore[i])) { gone++; renamedFrom = grantedBefore[i]; }
        int fresh = 0;
        for (int i = 0; i < grantedAfter.Count; i++)
            if (!grantedBefore.Contains(grantedAfter[i])) { fresh++; renamedTo = grantedAfter[i]; }
        if (gone == 0) return;                       // nothing lost — every existing gate still matches
        bool isEdit = gone == 1 && fresh == 1;

        int followed = 0, dropped = 0;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (s?.triggers == null || s == granter) continue;
            bool touched = false;
            for (int t = 0; t < s.triggers.Count; t++)
            {
                var e = s.triggers[t];
                if (e == null || !e.requiresState || e.requireSkillId != granter.id) continue;
                if (grantedAfter.Contains(e.requireStateId)) continue;   // its pile survived untouched
                Undo.RecordObject(s, "Sync stack gate");
                if (isEdit && e.requireStateId.Equals(renamedFrom.Value))
                {
                    e.requireStateId = renamedTo.Value; followed++;
                }
                else
                {
                    // The pile it was spending is gone. Disarm the gate rather than leave it pointing at nothing.
                    e.requiresState = false; e.requireSkillId = ""; dropped++;
                }
                touched = true;
            }
            if (touched) EditorUtility.SetDirty(s);
        }
        if (followed + dropped == 0) return;
        Debug.Log($"[Skill Composer] \"{granter.id}\" changed what it stacks — " +
                  $"{followed} gate(s) followed the change, {dropped} lost their gate (the pile they spent is gone).");
    }

    /// <summary>Read every saved skill's stack gates and report each one that can never match. A broken gate is
    /// SILENT at runtime — it counts nothing and spends nothing, which looks exactly like a skill that isn't scaling
    /// yet — so the whole roster is checked in one go rather than one play-test per skill. Reads only; nothing is
    /// repaired, because which pile a gate meant is the author's call, not a guess this can make.</summary>
    public static void CheckAllStackGates(List<SkillDef> all)
    {
        var report = new System.Text.StringBuilder();
        int broken = 0, gates = 0;
        for (int i = 0; i < all.Count; i++)
        {
            var s = all[i];
            if (s?.triggers == null) continue;
            for (int t = 0; t < s.triggers.Count; t++)
            {
                var e = s.triggers[t];
                if (e == null || !e.requiresState) continue;
                gates++;

                string fault = null;
                var granter = SkillCatalog.Get(e.requireSkillId);
                if (string.IsNullOrEmpty(e.requireSkillId)) fault = "reads a pile but names no skill to read it from";
                else if (granter == null) fault = $"reads \"{e.requireSkillId}\", which is not a saved skill";
                else
                {
                    CollectGrantedStates(granter, grantedScratch);
                    if (grantedScratch.Count == 0)
                        fault = $"reads \"{e.requireSkillId}\", which stacks nothing — there is no pile";
                    else if (!e.requireStateIdSet)
                        fault = $"never names WHICH of \"{e.requireSkillId}\"'s piles it reads";
                    else if (!grantedScratch.Contains(e.requireStateId))
                        fault = $"reads {e.requireStateId} from \"{e.requireSkillId}\", which that skill never stacks";
                }
                if (fault == null) continue;
                broken++;
                report.Append("\n • ").Append(s.displayName).Append(" (").Append(e.trigger).Append(") ").Append(fault);
            }

            // Modifier effects name a pile the same way a gate does, and fail the same silent way, so they are checked
            // in the same sweep — one broken-link answer for the whole roster, not two half-checks.
            if (s.bindings == null) continue;
            for (int b = 0; b < s.bindings.Count; b++)
            {
                var fx = s.bindings[b]?.effects;
                if (fx == null) continue;
                for (int f = 0; f < fx.Count; f++)
                {
                    var ef = fx[f];
                    if (ef == null || !SkillEffect.IsModifier(ef.kind)) continue;
                    gates++;

                    string fault = null;
                    var target = SkillCatalog.Get(ef.modifierSkillId);
                    if (string.IsNullOrEmpty(ef.modifierSkillId)) fault = "edits a pile but names no skill";
                    else if (target == null) fault = $"edits \"{ef.modifierSkillId}\", which is not a saved skill";
                    else
                    {
                        CollectGrantedStates(target, grantedScratch);
                        if (grantedScratch.Count == 0)
                            fault = $"edits \"{ef.modifierSkillId}\", which stacks nothing — there is no pile";
                        else if (!ef.modifierStateIdSet)
                            fault = $"never names WHICH of \"{ef.modifierSkillId}\"'s piles it edits";
                        else if (!grantedScratch.Contains(ef.modifierStateId))
                            fault = $"edits {ef.modifierStateId} of \"{ef.modifierSkillId}\", which that skill never stacks";
                    }
                    if (fault == null) continue;
                    broken++;
                    report.Append("\n • ").Append(s.displayName).Append(" (").Append(ef.kind).Append(") ").Append(fault);
                }
            }
        }
        string head = broken == 0
            ? $"[Skill Composer] All {gates} stack link(s) point at a real pile."
            : $"[Skill Composer] {broken} of {gates} stack link(s) can never match — each one silently counts and " +
              "spends nothing:";
        if (broken == 0) Debug.Log(head); else Debug.LogError(head + report);
    }
}
