using UnityEditor;
using UnityEngine;

/// <summary>
/// Makes the default Inspector for a skill READ-ONLY, so the Skill Composer is the only way to change skill data.
///
/// <para><b>Why the lock is worth its cost.</b> Skill data is not just the game's — the player wiki and the design
/// docs are generated from it, and that generation happens on the Composer's Save. A value nudged in the plain
/// Inspector would change the game while the wiki and the docs went on quoting the old number, with nothing to say
/// which was right. The Composer isn't merely the nicest editor; it is the only one that publishes.</para>
///
/// <para>It is also where the rules live: the pricing bands, the gate wiring, the consumer sweep that re-points other
/// skills when an id changes, and the checks that run before a save lands. Editing the raw fields skips all of it.</para>
///
/// <para><b>There is deliberately no unlock toggle.</b> An escape hatch would be taken, and the moment it is taken the
/// guarantee is gone — a rule with an exception is a habit, not a rule. If the Composer ever cannot express something a
/// skill needs, the fix is to teach the Composer, which keeps every surface honest, rather than to reach around it.</para>
/// </summary>
[CustomEditor(typeof(SkillDef))]
public class SkillDefInspector : Editor
{
    public override void OnInspectorGUI()
    {
        var def = (SkillDef)target;

        EditorGUILayout.HelpBox(
            "Skill data is edited in the Skill Composer only.\n\n" +
            "Saving there also republishes the player wiki and the design docs from these values. " +
            "Editing here would change the game and leave both of those quoting the old numbers.",
            MessageType.Info);

        if (GUILayout.Button("Open in Skill Composer", GUILayout.Height(26)))
        {
            SkillComposerWindow.OpenOn(def);
            return;
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField(string.IsNullOrEmpty(def.displayName) ? def.id : def.displayName, EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Id", string.IsNullOrEmpty(def.id) ? "(unset)" : def.id);
        EditorGUILayout.LabelField("Cost", SkillTooltip.CostLine(def, SkillTooltip.Dialect.Plain));
        string when = SkillTooltip.TriggerLine(def, SkillTooltip.Dialect.Plain);
        if (!string.IsNullOrEmpty(when)) EditorGUILayout.LabelField("When", when);
        EditorGUILayout.LabelField("Published", def.published ? "yes — can roll onto a dropped weapon" : "no — not in drops yet");

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("What it does", EditorStyles.boldLabel);
        if (!string.IsNullOrEmpty(def.description))
            EditorGUILayout.LabelField(def.description, EditorStyles.wordWrappedMiniLabel);
        var lines = new System.Collections.Generic.List<string>();
        SkillTooltip.EffectLines(def, SkillTooltip.Dialect.Plain, lines);
        if (lines.Count == 0) EditorGUILayout.LabelField("(no effects authored)", EditorStyles.miniLabel);
        for (int i = 0; i < lines.Count; i++)
            EditorGUILayout.LabelField("• " + lines[i], EditorStyles.wordWrappedMiniLabel);

        // The real fields, shown so the data is still inspectable, but never editable from here.
        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.LabelField("Raw fields (read-only)", EditorStyles.miniBoldLabel);
            DrawDefaultInspector();
        }
    }
}
