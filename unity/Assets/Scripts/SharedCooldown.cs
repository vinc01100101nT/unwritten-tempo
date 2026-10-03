/// <summary>
/// THE SHARED COOLDOWN — every ultimate a unit has, of any weapon, waits on one moment after any of them is cast:
/// casting one starts <see cref="UltimateSeconds"/> on all of them, on top of each one's own cooldown, so a unit may
/// carry several ultimates but must choose which to spend. The same rule for a person and a creature (decided with the
/// user 2026-10-03; the name "shared cooldown" is the one players are told).
///
/// <para>The moment lives on the unit's skill list (<see cref="UnitSkills.SharedUltimateDueAtUtc"/>); the server
/// starts it and tells the owner, whose machine only compares the moment it was sent.</para>
/// </summary>
public static class SharedCooldown
{
    /// <summary>How long every ultimate waits after any ultimate is cast.</summary>
    public const float UltimateSeconds = 40f;

    /// <summary>The name the owner is told the shared moment under, in the same message a skill's own cooldown
    /// travels in. Not a skill id: no skill can be called this.</summary>
    public const string UltimateKey = "shared-cooldown:ultimates";
}
