using UnityEngine;

/// <summary>
/// How a body that is only WATCHED catches up with where the server last said it was. One function, used by every
/// kind of watched thing — a person (<see cref="NetPlayer"/>) and a creature (<see cref="NetMob"/>) — because
/// "glide toward the last known place" is one behaviour, not two, and two copies of it drift.
///
/// <para><b>Frame-rate independent.</b> Blending by <c>rate x deltaTime</c> reads as a fraction of the remaining gap
/// per frame, so the same body converges FASTER on a machine drawing more frames — two players watching the same
/// creature would see it in two different places. The exponential form below closes the same fraction per SECOND
/// however many frames that second was cut into.</para>
///
/// <para><b>An arrived body is left alone.</b> Most things are standing still most of the time, and the rotation
/// blend is the expensive half. Below the thresholds there is nothing anybody could see, so nothing is paid.</para>
/// </summary>
public static class WatchedBody
{
    // How much of the remaining gap is closed each second. The same number both kinds of body used before this was
    // one function, kept as-is so nothing about the feel changes.
    const float CatchUpRate = 12f;

    // Close enough to count as arrived: half a centimetre, and half a degree.
    const float SettledDistance = 0.005f;
    const float SettledAngle = 0.5f;

    // How much of the gap this FRAME closes. One answer for the whole frame, worked out once and handed to every
    // body in it: the frame length is the same for all of them, and the exponential above is the only expensive
    // part of this. A field full of watched creatures would otherwise pay for the identical sum once each.
    static int workedOutOnFrame = -1;
    static float caughtUpThisFrame;

    static float CaughtUpThisFrame()
    {
        if (workedOutOnFrame != Time.frameCount)
        {
            workedOutOnFrame = Time.frameCount;
            caughtUpThisFrame = 1f - Mathf.Exp(-CatchUpRate * Time.deltaTime);
        }
        return caughtUpThisFrame;
    }

    /// <summary>Move <paramref name="t"/> a frame's worth toward where the server says it is — or, while
    /// <paramref name="move"/> is drawing a move this body was said to have begun, a frame's worth of that move
    /// instead. A hop is over in a few hundredths of a second and a blink in none, so following the ten-a-second
    /// position reports would draw either as a slow slide.</summary>
    public static void Follow(Transform t, Vector3 wherePosition, Quaternion whichWay, WatchedMove move)
    {
        if (t == null) return;
        if (move != null && move.Step(t, wherePosition)) { Face(t, whichWay); return; }
        Follow(t, wherePosition, whichWay);
    }

    /// <summary>Move <paramref name="t"/> a frame's worth toward where the server says it is.</summary>
    public static void Follow(Transform t, Vector3 wherePosition, Quaternion whichWay)
    {
        if (t == null) return;

        Vector3 here = t.position;
        if ((here - wherePosition).sqrMagnitude > SettledDistance * SettledDistance)
            t.position = Vector3.Lerp(here, wherePosition, CaughtUpThisFrame());
        Face(t, whichWay);
    }

    static void Face(Transform t, Quaternion whichWay)
    {
        // THE DIRECTION IS TAKEN WHOLE, NOT EASED INTO. A body that draws a real character turns its own model toward
        // whatever it is told, at the character turn speed — the same single easing the person at this keyboard gets.
        // Easing here as well put a SECOND, much slower easing in front of that one, so a watched character crawled
        // round to face things while the person beside it snapped round: two smoothings in series, and the outer one
        // decided the speed. Handing over the real answer leaves exactly one, and it is the character's own.
        //
        // Nothing that draws a flat sprite is affected: a sprite is swung to face the camera every frame regardless
        // of what this says.
        if (Quaternion.Angle(t.rotation, whichWay) > SettledAngle)
            t.rotation = whichWay;
    }
}

/// <summary>A SCRIPTED MOVE SAID ONCE, as it begins — a glide (a Dash hop, or a person's shove, which runs on the same
/// glide), a blink, or a creature's knockback — by the machine that moves that body: the owner's machine for a person,
/// the server for a creature. Held as one synced value on the unit's shell, the same shape a person's jump travels in
/// and for the same reason: the move is scripted from start to end, so every screen can draw the identical move
/// instead of easing toward position reports that arrive too rarely to show it. It carries WHICH effect moved the body
/// (<see cref="look"/>), never what that looks like — each screen reads the look out of its own copy of the skill.</summary>
public struct MoveSaid
{
    public const byte Glide = 0, Blink = 1, Knockback = 2;

    /// <summary>When it began, on the clock every machine shares, stamped by the server. 0 = none said yet.</summary>
    public double at;
    /// <summary><see cref="Glide"/>, <see cref="Blink"/> or <see cref="Knockback"/>.</summary>
    public byte kind;
    /// <summary>Glide / blink: where it ends, in the same coordinates as the unit's synced position.</summary>
    public Vector3 to;
    /// <summary>Knockback: the flat direction the body is pushed.</summary>
    public Vector3 direction;
    /// <summary>Glide / knockback: starting travel speed, in world units per second.</summary>
    public float speed;
    public bool easeIn, easeOut;
    /// <summary>Glide: the ease window, in seconds of full-speed travel.</summary>
    public float easeSeconds;
    /// <summary>Glide: the mover's own safety window. Knockback: how long the push lasts. Blink: 0.</summary>
    public float window;
    /// <summary>The authored effect behind the move, or <see cref="RelocationLook.None"/>.</summary>
    public RelocationLook look;
}

/// <summary>One watched body's scripted move, drawn on this machine with the SAME rule the mover's own legs use
/// (<see cref="GlideEase"/>), plus the afterimage and see-through fade its effect authored, read off this machine's
/// copy of the skill. One per watched shell; nothing allocates per move.
///
/// <para><b>It waits at the end for the reports to catch up.</b> The drawn move finishes before the first position
/// report sent from after it has arrived, and following the older report would drag the body back toward where it
/// started and forward again. So it stands at the end until a report lands nearer the end than the start, or
/// <see cref="ConfirmWait"/> passes.</para></summary>
public sealed class WatchedMove
{
    // Close enough to count as arrived — the same snap the person's own glide uses.
    const float ArriveRange = 0.05f;
    // The longest a drawn move stands at its end waiting for the position reports to agree with it. Ten report
    // intervals: far longer than a report takes to cross the wire, so it only ever runs out when the mover was
    // stopped short (a wall) and the reports are never going to arrive at the drawn end.
    const float ConfirmWait = 1f;

    enum Phase { Idle, Moving, Waiting }

    double drawingFor;
    Phase phase;
    byte kind;
    Vector3 start, end, velocity;
    float speed, total, easeSeconds, slowPerSecond, until;
    bool easeIn, easeOut;
    Transform faded;

    /// <summary>Has a move been said that this machine has not started drawing yet? Asked every frame, so the
    /// coordinate conversion for a new move is paid once per move rather than once per frame.</summary>
    public bool IsNew(in MoveSaid said) => said.at > 0d && said.at != drawingFor;

    /// <summary>Start drawing the latest move said for this unit, from wherever this machine is drawing the body right
    /// now. A move already over by the time it arrives (somebody who started watching late) is never started.</summary>
    public void Tell(in MoveSaid said, Vector3 toInWorld, Transform body)
    {
        if (!IsNew(said) || body == null) return;
        drawingFor = said.at;
        StopFade();
        float age = (float)(Mirror.NetworkTime.time - said.at);
        if (age > said.window + ConfirmWait) { phase = Phase.Idle; return; }
        float left = Mathf.Max(0f, said.window - age);

        kind = said.kind;
        start = body.position;
        // THE LOOK, from this machine's own copy of the skill — snapped where the body stands now, before it moves,
        // because the copy belongs to the spot being left.
        if (said.look.TryRead(out float afterimageSeconds, out Color tint, out float opacity))
        {
            Afterimage.Capture(body, afterimageSeconds, tint);
            if (opacity < 0.999f && kind == MoveSaid.Glide && left > 0f)
            {
                // Timed as well as released on arrival, so a body dropped mid-hop can never stay see-through.
                CharacterFade.HoldFor(body, this, opacity, left);
                faded = body;
            }
        }

        switch (kind)
        {
            case MoveSaid.Blink:
                body.position = toInWorld;
                end = toInWorld;
                Wait();
                return;
            case MoveSaid.Knockback:
                if (said.speed <= 0f || left <= 0f) { phase = Phase.Idle; return; }
                velocity = new Vector3(said.direction.x, 0f, said.direction.z).normalized * said.speed;
                slowPerSecond = said.speed / said.window;
                until = Time.time + left;
                phase = Phase.Moving;
                return;
            default:
                Vector3 flat = toInWorld - start; flat.y = 0f;
                total = flat.magnitude;
                end = toInWorld;
                if (total <= ArriveRange || said.speed <= 0f || left <= 0f) { phase = Phase.Idle; return; }
                speed = said.speed;
                easeIn = said.easeIn; easeOut = said.easeOut;
                easeSeconds = Mathf.Max(0.01f, said.easeSeconds);
                until = Time.time + left;
                phase = Phase.Moving;
                return;
        }
    }

    /// <summary>Move <paramref name="t"/> one frame of the move, or hold it at the end while the reports catch up.
    /// FALSE when nothing is being drawn, so the caller follows <paramref name="reported"/> instead.</summary>
    public bool Step(Transform t, Vector3 reported)
    {
        if (phase == Phase.Idle) return false;

        if (phase == Phase.Waiting)
        {
            if (Time.time >= until || (reported - end).sqrMagnitude < (reported - start).sqrMagnitude)
            {
                phase = Phase.Idle;
                return false;
            }
            return true;
        }

        if (Time.time >= until) { end = t.position; Wait(); return true; }

        Vector3 here = t.position;
        if (kind == MoveSaid.Knockback)
        {
            t.position = here + velocity * Time.deltaTime;
            velocity = GlideEase.KnockbackSlowed(velocity, slowPerSecond, Time.deltaTime);
            return true;
        }

        Vector3 to = end - here; to.y = 0f;
        float dist = to.magnitude;
        float step = GlideEase.Speed(speed, dist, total, easeIn, easeOut, easeSeconds) * Time.deltaTime;
        if (step >= dist - ArriveRange)
        {
            t.position = end;
            Wait();
            return true;
        }
        t.position = Vector3.Lerp(here, end, step / dist);
        return true;
    }

    /// <summary>Drop whatever is being drawn — the body is going away (out of view, out of the map).</summary>
    public void Stop()
    {
        StopFade();
        phase = Phase.Idle;
    }

    void Wait()
    {
        StopFade();
        phase = Phase.Waiting;
        until = Time.time + ConfirmWait;
    }

    void StopFade()
    {
        if (faded != null) CharacterFade.Release(faded, this);
        faded = null;
    }
}
