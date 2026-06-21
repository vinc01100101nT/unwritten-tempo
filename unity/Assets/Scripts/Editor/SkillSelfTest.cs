using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only sanity checks for the Trio-Ring passive draft — runs the pure logic
/// (<see cref="SkillRing"/> / <see cref="PassiveRanks"/> / <see cref="DraftService"/>)
/// with no scene or Play mode, and logs the result. Lets us verify the maths and the
/// steering rules without pressing Play.
/// </summary>
public static class SkillSelfTest
{
    [MenuItem("Tools/unwritten/Debug/Print Skill Ring + Soft-Cap Table", priority = 800)]
    static void PrintRing()
    {
        var sb = new StringBuilder("[unwritten] Trio-Ring layout (clockwise):\n");
        foreach (var s in SkillRing.Ring)
            sb.Append($"  {SkillRing.ShortName(s),-6}  Trio = [{SkillRing.ShortName(SkillRing.LeftOf(s))}, " +
                      $"{SkillRing.ShortName(s)}, {SkillRing.ShortName(SkillRing.RightOf(s))}]\n");

        sb.Append("\nSoft-cap curve  passive% = Cap·R/(R+K)  (ATK: Cap 80% K 40 · CRIT: Cap 50%):\n");
        foreach (int r in new[] { 1, 5, 10, 20, 40, 80, 160 })
            sb.Append($"  R={r,-4} ATK +{SkillRing.Percent(StatId.Atk, r) * 100,5:0.0}%   " +
                      $"CRIT +{SkillRing.Percent(StatId.CritChance, r) * 100,5:0.0}%\n");
        Debug.Log(sb.ToString());
    }

    [MenuItem("Tools/unwritten/Debug/Simulate Skill Draft Walk (20 levels)", priority = 801)]
    static void SimulateWalk()
    {
        var p = new PassiveRanks();
        var rng = new System.Random(12345);   // fixed seed → reproducible log
        var sb = new StringBuilder("[unwritten] Simulated 20-pick walk (auto-steers RIGHT, takes wildcard if it lands on the magic pole):\n");

        for (int i = 0; i < 20; i++)
        {
            // top up a point each iteration to mimic leveling
            if (p.BankedPoints == 0) p.AddPoint();
            var draft = DraftService.BuildDraft(p, rng);

            DraftService.Option choice;
            if (draft.isOpening)
            {
                choice = draft.options[0];   // start on ATK
            }
            else
            {
                // strategy: grab the wildcard only when it's MAtk (a deliberate pivot), else drift right
                int wild = draft.options.FindIndex(o => o.isWildcard && o.stat == StatId.MAtk);
                choice = wild >= 0 ? draft.options[wild] : draft.options[2];   // [2] = right neighbor
            }

            string opts = "";
            foreach (var o in draft.options)
                opts += $"{(o.isLeader ? "*" : o.isWildcard ? "?" : " ")}{SkillRing.ShortName(o.stat)} ";
            p.Pick(choice.stat);
            sb.Append($"  L{i + 1,-2} draft[{opts.Trim()}]  ->  {SkillRing.ShortName(choice.stat)}" +
                      $"   leader now {SkillRing.ShortName(p.Leader)}\n");
        }

        sb.Append("\nFinal ranks / %s:\n");
        foreach (var s in SkillRing.Ring)
        {
            int r = p.RanksIn(s);
            if (r > 0) sb.Append($"  {SkillRing.ShortName(s),-6} ×{r}  +{SkillRing.Percent(s, r) * 100:0.0}%\n");
        }
        Debug.Log(sb.ToString());
    }
}
