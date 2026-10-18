/// <summary>
/// WHOM A CREATURE FIGHTS, as plain functions over plain numbers. A creature keeps its target until that target has
/// been outside its aggro area for <see cref="GiveUpSeconds"/> in a row, and turns to a different attacker only when
/// that attacker has taken more health off it than its current target has — so two people trading blows cannot bounce
/// it between them.
/// </summary>
public static class AggroMath
{
    /// <summary>How long the target may stay outside the aggro area before the creature stops chasing: 5 seconds.</summary>
    public const float GiveUpSeconds = 5f;

    /// <summary>True once the target has been outside the aggro area for <see cref="GiveUpSeconds"/> or longer.</summary>
    public static bool GivesUp(float outsideSince, float now) => now - outsideSince >= GiveUpSeconds;

    /// <summary>True when an attacker who has taken <paramref name="attackerDealt"/> health off the creature should
    /// take it from its current target, who has taken <paramref name="targetDealt"/>.</summary>
    public static bool TakesAggro(float attackerDealt, float targetDealt) => attackerDealt > targetDealt;
}
