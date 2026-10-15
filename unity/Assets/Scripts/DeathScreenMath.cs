using UnityEngine;

/// <summary>
/// HOW THE SCREEN GREYS WHEN THE PERSON AT THIS KEYBOARD DIES — only a picture on their screen, never a game number.
/// Numbers in, numbers out, so it is checked without standing the game up. <see cref="SystemWindow"/> is the caller
/// and <see cref="HandDrawnFX.DeathGrey"/> carries the answer to the screen.
/// </summary>
public static class DeathScreenMath
{
    /// <summary>How long, in seconds, the screen takes to fade to grey after the death.</summary>
    public const float GreyFadeSeconds = 1.5f;

    /// <summary>How grey the screen is <paramref name="secondsSinceDeath"/> after dying: 0 = full colour, 1 = grey,
    /// eased so it starts and ends gently.</summary>
    public static float GreyAt(float secondsSinceDeath)
    {
        float t = Mathf.Clamp01(secondsSinceDeath / GreyFadeSeconds);
        return t * t * (3f - 2f * t);
    }
}
