using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only sanity check for the active-skill MODIFIER system (Charter §12) — runs the pure logic
/// (<see cref="ModifierCatalog"/> / <see cref="SkillModBook"/> / <see cref="SkillModLoadout"/>) with no
/// scene or Play mode and logs the result. Proves: (1) tag eligibility filters the pool PER SKILL — a
/// melee Bash and a magic Fireball draw different subsets from one catalog; (2) a skill is born wearing
/// one random mod and gains one per even level until all its fillable phases are full; (3) the
/// support-only "Mending" mod can never roll onto an attack skill.
/// </summary>
public static class SkillModSelfTest
{
    static readonly string[] BashTags     = { SkillTag.Melee, SkillTag.Physical, SkillTag.Offense, SkillTag.SingleTarget };
    static readonly string[] FireballTags = { SkillTag.Magic, SkillTag.Ranged, SkillTag.Offense, SkillTag.SingleTarget };
    static readonly SkillPhase[] Phases   = (SkillPhase[])System.Enum.GetValues(typeof(SkillPhase));

    [MenuItem("Tools/unwritten/Debug/Print Modifier Catalog + Eligibility", priority = 802)]
    static void PrintCatalog()
    {
        var sb = new StringBuilder("[unwritten] Modifier catalog — eligibility per skill (Bash=melee/phys, Fireball=magic/ranged):\n");
        sb.Append($"  {"MODIFIER",-14} {"PHASE",-10} {"REQUIRES",-10} BASH  FIRE\n");
        foreach (var m in ModifierCatalog.All)
        {
            string req = m.Requires == null ? "-" : string.Join("+", m.Requires);
            sb.Append($"  {m.DisplayName,-14} {m.Phase,-10} {req,-10} {(m.EligibleFor(BashTags) ? " Y " : " . ")}  {(m.EligibleFor(FireballTags) ? " Y " : " . ")}\n");
        }
        sb.Append("\n(Long Reach/Cleaving/Bloodthirst = Bash-only; Far Cast/Siphon = Fireball-only; Mending = neither.)");
        Debug.Log(sb.ToString());
    }

    [MenuItem("Tools/unwritten/Debug/Simulate Modifier Rolls — Bash + Fireball (lvl 1->20)", priority = 803)]
    static void SimulateRolls()
    {
        var sb = new StringBuilder("[unwritten] Modifier roll simulation (seed 12345) — born with one, +1 each even level:\n");
        SimulateOne(sb, "Bash", "bash", BashTags);
        sb.Append('\n');
        SimulateOne(sb, "Fireball", "fireball", FireballTags);
        Debug.Log(sb.ToString());
    }

    static void SimulateOne(StringBuilder sb, string label, string id, string[] tags)
    {
        var book = new SkillModBook(seed: 12345);
        var loadout = book.Register(id, tags);
        sb.Append($"=== {label} ({string.Join(",", tags)}) ===\n");
        sb.Append($"  L1  born wearing: {Describe(loadout)}\n");
        for (int lvl = 2; lvl <= 20; lvl++)
        {
            book.OnLevel(lvl);
            if (lvl % 2 == 0)
                sb.Append($"  L{lvl,-2} -> {Describe(loadout)}{(loadout.Full ? "   [FULL]" : "")}\n");
        }

        sb.Append("  final loadout:\n");
        for (int i = 0; i < Phases.Length; i++)
        {
            var m = loadout.At(Phases[i]);
            sb.Append($"    {Phases[i],-10} {(m != null ? m.DisplayName : "(unfillable for this skill)")}\n");
        }

        bool mending = false;
        for (int i = 0; i < Phases.Length; i++)
            if (loadout.At(Phases[i]) != null && loadout.At(Phases[i]).Id == "mending") mending = true;
        sb.Append(mending
            ? "  !! FAIL: support-only 'Mending' rolled onto an attack skill — tag filter broken.\n"
            : "  PASS: support-only 'Mending' never rolled (tag filter works).\n");
    }

    static string Describe(SkillModLoadout l)
    {
        var parts = new StringBuilder();
        for (int i = 0; i < Phases.Length; i++)
        {
            var m = l.At(Phases[i]);
            if (m != null) { if (parts.Length > 0) parts.Append(", "); parts.Append(m.DisplayName); }
        }
        return parts.Length == 0 ? "(none)" : parts.ToString();
    }
}
