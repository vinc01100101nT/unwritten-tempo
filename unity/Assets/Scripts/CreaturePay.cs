/// <summary>
/// WHAT A CREATURE'S GEAR MULTIPLIES ITS PAY BY — plain arithmetic, so it can be checked by calling it. Everything a
/// kill is worth is multiplied by it: the kill's experience, what the creature's own materials sell for, and the gold a
/// saved character pays. Design: docs/3-world/what-a-kill-pays.md.
///
/// <para>A creature's level sets its body; the grade and the folds of its gear are the only difficulty dial, and
/// they are two separate terms: <b>pay = what the level and kind give × the grade's percent × the folds' percent</b>.
/// Raw gear with no folds is exactly 100% of both. Nothing is measured and nothing is capped.</para>
/// </summary>
public static class CreaturePay
{
    /// <summary>How much more each grade above Raw pays, in percent.</summary>
    public const int PercentPerGrade = 25;

    /// <summary>How much more each fold pays, in percent.</summary>
    public const int PercentPerFold = 5;

    /// <summary>What the grade alone pays, in percent: 100 at Raw, 250 at A.</summary>
    public static int GradePercent(Quality grade) => 100 + PercentPerGrade * (CraftingMath.Band(grade) - 1);

    /// <summary>What the folds alone pay, in percent: 100 with none. Folds past the grade's cap are not worn, so they
    /// are not paid.</summary>
    public static int FoldPercent(Quality grade, int folds) =>
        100 + PercentPerFold * System.Math.Min(System.Math.Max(0, folds), CraftingMath.FoldCap(grade));

    /// <summary>An amount multiplied by both percents, rounded half up in whole numbers — the one rounding the price
    /// writer, the server and the offline check all use.</summary>
    public static long Scaled(long amount, Quality grade, int folds)
    {
        if (amount <= 0) return 0;
        return (amount * GradePercent(grade) * FoldPercent(grade, folds) + 5000) / 10000;
    }
}
