/// <summary>
/// A PLAYER'S MACHINE: the kill combo and the per-hour rates exactly as the server last sent them
/// (<see cref="GameNetwork.KillComboShown"/>). A copy and nothing more — no sum is done here; <see cref="KillComboHud"/>
/// draws it, and compares the sent end moment with the clock only to know when to stop drawing the count.
///
/// <para>Forgotten on every new connection: a door or a reconnect starts a new run and a new meter on the server.</para>
/// </summary>
public static class KillComboReadout
{
    /// <summary>True once a message has arrived on this connection.</summary>
    public static bool Has { get; private set; }

    /// <summary>Goes up by one with every message (and every forgetting), so a drawer can tell something changed with
    /// one compare.</summary>
    public static int Version { get; private set; }

    public static int Combo { get; private set; }
    public static int BonusPercent { get; private set; }
    public static double EndsAtEpochSeconds { get; private set; }
    public static float WindowSeconds { get; private set; }
    public static long ExperiencePerHour { get; private set; }
    public static long ZenyPerHour { get; private set; }

    public static void Take(int combo, int bonusPercent, double endsAtEpochSeconds, float windowSeconds,
                            long experiencePerHour, long zenyPerHour)
    {
        Combo = combo;
        BonusPercent = bonusPercent;
        EndsAtEpochSeconds = endsAtEpochSeconds;
        WindowSeconds = windowSeconds;
        ExperiencePerHour = experiencePerHour;
        ZenyPerHour = zenyPerHour;
        Has = true;
        Version++;
    }

    public static void Forget()
    {
        Has = false;
        Combo = 0;
        BonusPercent = 0;
        EndsAtEpochSeconds = 0;
        ExperiencePerHour = 0;
        ZenyPerHour = 0;
        Version++;
    }
}
