/// <summary>
/// THE PERSON AT THIS KEYBOARD'S side of drawing a weapon: what the server last said their weapon is doing, and the
/// asks this machine sends. It decides nothing — the server draws and puts away; this only asks, once per change,
/// and holds ONE skill press made with the weapon away until the server says it is in hand.
///
/// <para>The held press is the PRESS, not a resolved cast: it is replayed as a fresh press once the weapon is out, so
/// any target or point is picked after the draw, never aimed before it and fired somewhere stale. It is dropped the
/// moment the weapon is put away instead — a new life and a weapon swap arrive here as "sheathed" — and a press held
/// in one map is never run in another, so a door drops it too.</para>
/// </summary>
public static class WeaponDrawClient
{
    /// <summary>What the server last said this person's weapon is doing.</summary>
    public static DrawPhase MyPhase { get; private set; }

    /// <summary>Is the weapon in hand — the only phase a swing or a weapon skill is sent in.</summary>
    public static bool Ready => MyPhase == DrawPhase.Drawn;

    /// <summary>Is the weapon on the body or going there — the phases sprinting and peace belong to.</summary>
    public static bool Away => MyPhase == DrawPhase.Sheathed || MyPhase == DrawPhase.Sheathing;

    static bool askedThisPhase;
    static SkillRunner heldPress;
    static string heldInMap;

    /// <summary>The server's word, from this person's own shell.</summary>
    public static void Told(DrawPhase phase)
    {
        if (phase != MyPhase) askedThisPhase = false;
        MyPhase = phase;
        if (phase == DrawPhase.Drawn)
        {
            var press = heldPress; heldPress = null;
            if (press != null && heldInMap == MapManager.CurrentMap) press.Activate();
        }
        else if (phase == DrawPhase.Sheathed || phase == DrawPhase.Sheathing) heldPress = null;
    }

    /// <summary>Ask for the weapon — once until the server answers with a new phase.</summary>
    public static void AskToDraw()
    {
        if (askedThisPhase || MyPhase == DrawPhase.Drawn || MyPhase == DrawPhase.Drawing) return;
        askedThisPhase = true;
        GameNetwork.AskWeapon(true);
    }

    /// <summary>Ask to put the weapon away — once until the server answers with a new phase.</summary>
    public static void AskToSheathe()
    {
        if (askedThisPhase || Away) return;
        askedThisPhase = true;
        heldPress = null;
        GameNetwork.AskWeapon(false);
    }

    /// <summary>The toggle key: out if away, away if out.</summary>
    public static void Toggle() { if (Away) AskToDraw(); else AskToSheathe(); }

    /// <summary>A weapon skill pressed with the weapon away: draw, and press it again once it is in hand.</summary>
    public static void HoldPress(SkillRunner press)
    {
        heldPress = press;
        heldInMap = MapManager.CurrentMap;
        AskToDraw();
    }
}
