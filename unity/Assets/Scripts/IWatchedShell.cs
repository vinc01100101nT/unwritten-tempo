/// <summary>
/// What a watching machine is TOLD about a unit beyond its health — its level and what it wears, both exactly as the
/// server wrote them. A creature's shell (<see cref="NetMob"/>) and a person's (<see cref="NetPlayer"/>) both answer
/// it, so a readout that shows either one asks once instead of once per kind.
/// </summary>
public interface IWatchedShell
{
    /// <summary>The unit's level as the server has it; 0 when it has none to show.</summary>
    int Level { get; }

    /// <summary>What the unit wears, in the server's words (<see cref="WornList"/>).</summary>
    string WornText { get; }
}
