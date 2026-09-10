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

    /// <summary>Move <paramref name="t"/> a frame's worth toward where the server says it is.</summary>
    public static void Follow(Transform t, Vector3 wherePosition, Quaternion whichWay)
    {
        if (t == null) return;

        Vector3 here = t.position;
        if ((here - wherePosition).sqrMagnitude > SettledDistance * SettledDistance)
            t.position = Vector3.Lerp(here, wherePosition, CaughtUpThisFrame());

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
