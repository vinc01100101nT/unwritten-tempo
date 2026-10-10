/// <summary>Where a unit's weapon is: on its body (peace), coming out, in its hands (combat), or going back.</summary>
public enum DrawPhase : byte { Sheathed, Drawing, Drawn, Sheathing }

/// <summary>A unit's draw state as it crosses the wire: the phase, when it began on the clock every machine shares,
/// and how long it lasts. Finished values — a watching machine only works out how far into the move to draw.</summary>
public struct DrawSaid
{
    public byte phase;
    public double at;
    public float window;

    public DrawPhase Phase => (DrawPhase)phase;

    /// <summary>How far through the move it is at <paramref name="now"/> (0..1); 1 for the resting phases.</summary>
    public float Progress(double now)
        => window <= 0f ? 1f : (float)System.Math.Max(0.0, System.Math.Min(1.0, (now - at) / window));
}

/// <summary>
/// ONE unit's combat-or-peace state, decided on the server only — every other machine is told it and draws it. Owned
/// by the unit's <see cref="StatusController"/> and ticked there with the unit's other timers. A request that arrives
/// while the weapon is moving is remembered (the newest wins) and carried out when the move ends, so nothing pressed
/// mid-draw is lost or doubled. <see cref="Version"/> goes up on every change, which is what the network shells watch.
/// </summary>
public sealed class WeaponDraw
{
    public DrawPhase Phase { get; private set; }
    /// <summary>When the current phase began (game time) and how long it lasts — 0 for the two resting phases.</summary>
    public float PhaseStart { get; private set; }
    public float Window { get; private set; }
    public int Version { get; private set; }

    enum Pending : byte { None, Draw, Sheathe }
    Pending pending;
    float pendingSeconds;

    public bool IsDrawn => Phase == DrawPhase.Drawn;
    public bool IsSheathed => Phase == DrawPhase.Sheathed;

    /// <summary>Take the weapon out over <paramref name="seconds"/>. Mid-sheathe it is remembered for afterwards.</summary>
    public void RequestDraw(float seconds, float now)
    {
        switch (Phase)
        {
            case DrawPhase.Sheathed: Begin(DrawPhase.Drawing, now, seconds); pending = Pending.None; break;
            case DrawPhase.Sheathing: pending = Pending.Draw; pendingSeconds = seconds; break;
            default: pending = Pending.None; break;   // already out, or coming out: a sheathe asked for meanwhile is dropped
        }
    }

    /// <summary>Put the weapon away over <paramref name="seconds"/>. Mid-draw it is remembered for afterwards.</summary>
    public void RequestSheathe(float seconds, float now)
    {
        switch (Phase)
        {
            case DrawPhase.Drawn: Begin(DrawPhase.Sheathing, now, seconds); pending = Pending.None; break;
            case DrawPhase.Drawing: pending = Pending.Sheathe; pendingSeconds = seconds; break;
            default: pending = Pending.None; break;
        }
    }

    /// <summary>Advance the clock. True on the tick a draw finished — the "weapon drawn" moment.</summary>
    public bool Tick(float now)
    {
        if (Phase != DrawPhase.Drawing && Phase != DrawPhase.Sheathing) return false;
        if (now < PhaseStart + Window) return false;
        bool drew = Phase == DrawPhase.Drawing;
        Begin(drew ? DrawPhase.Drawn : DrawPhase.Sheathed, now, 0f);
        var next = pending; pending = Pending.None;
        if (drew && next == Pending.Sheathe) Begin(DrawPhase.Sheathing, now, pendingSeconds);
        else if (!drew && next == Pending.Draw) Begin(DrawPhase.Drawing, now, pendingSeconds);
        return drew;
    }

    /// <summary>Weapon on the body at once, nothing pending — a new life, a weapon swapped, a body arriving.</summary>
    public void Reset(float now)
    {
        pending = Pending.None;
        Begin(DrawPhase.Sheathed, now, 0f);
    }

    void Begin(DrawPhase phase, float now, float window)
    {
        Phase = phase; PhaseStart = now; Window = window; Version++;
    }
}
