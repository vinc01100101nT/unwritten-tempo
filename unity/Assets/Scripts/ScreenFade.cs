using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The whole screen fading to black and back, with one thing done at the darkest moment — a wing's trip, so the
/// player never watches the camera cross the map to where they landed. The frame it is clear again, a teleporting
/// player appears (<see cref="TeleportLook.ScreenCleared"/>).
///
/// <para>A trip into another map fades to black and HOLDS (<see cref="ToBlack"/>) while the loading screen is up over
/// it, and fades back once the new map is there (<see cref="FromBlack"/>) — the same fade either way.</para>
///
/// <para>Built the first time it is needed and switched off the rest of the time, so it draws nothing and ticks
/// nothing between trips. While it is up it takes every click, so nothing behind it — a window, the world — can be
/// used by a player who is away on a teleport.</para>
/// </summary>
public static class ScreenFade
{
    static CanvasGroup group;
    static ScreenFadeTicker ticker;
    static Action atBlack;
    static bool goingDark;
    // Fade straight back once black (a trip within one map), or hold black until FromBlack (a trip into another).
    static bool backAfterBlack;

    /// <summary>How long each half of every teleport's fade takes — to black, and back — in seconds.</summary>
    public const float FadeSeconds = 1f;

    /// <summary>Fade to black, run <paramref name="whileBlack"/> on the frame the screen is fully black, then fade
    /// back. Each half takes <see cref="FadeSeconds"/>. Asked again
    /// before the screen is black, the newer action replaces the older one — only the latest trip is where the
    /// server holds the player.</summary>
    public static void Through(Action whileBlack) => Darken(whileBlack, true);

    /// <summary>Fade to black, run <paramref name="atBlackDo"/> on the frame it is fully black, and stay black until
    /// <see cref="FromBlack"/>.</summary>
    public static void ToBlack(Action atBlackDo) => Darken(atBlackDo, false);

    /// <summary>Fade back from black — from wherever the fade is, and from fully black when none is running. An action
    /// still waiting for black is dropped: the trip it belonged to is over.</summary>
    public static void FromBlack()
    {
        Build();
        atBlack = null;
        goingDark = false;
        backAfterBlack = true;
        if (!group.gameObject.activeSelf)
        {
            group.alpha = 1f;
            group.gameObject.SetActive(true);
        }
        ticker.enabled = true;
    }

    /// <summary>Run <paramref name="action"/> once the screen is black: after whatever is already waiting for black
    /// while a fade to black is running, and at once otherwise (black already, or no fade at all). A move into another
    /// map waits here, so the old map is never seen going away.</summary>
    public static void AtBlack(Action action)
    {
        if (action == null) return;
        if (group != null && goingDark) { atBlack += action; return; }
        action();
    }

    static void Darken(Action whileBlack, bool back)
    {
        Build();
        atBlack = whileBlack;
        backAfterBlack = back;
        goingDark = true;
        group.gameObject.SetActive(true);
        ticker.enabled = true;
    }

    // One frame of the fade, driven by ScreenFadeTicker while it runs.
    internal static void Step(float seconds)
    {
        float step = seconds / FadeSeconds;
        if (goingDark)
        {
            group.alpha = Mathf.Min(1f, group.alpha + step);
            if (group.alpha < 1f) return;
            goingDark = false;
            var act = atBlack;
            atBlack = null;
            // Held black: nothing to tick until FromBlack.
            if (!backAfterBlack) ticker.enabled = false;
            act?.Invoke();
            return;
        }
        group.alpha = Mathf.Max(0f, group.alpha - step);
        if (group.alpha > 0f) return;
        ticker.enabled = false;
        group.gameObject.SetActive(false);
        TeleportLook.ScreenCleared();
    }

    static void Build()
    {
        if (group != null) return;

        var go = new GameObject("ScreenFade", typeof(Canvas), typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(ScreenFadeTicker));
        go.hideFlags = HideFlags.DontSaveInEditor;
        UnityEngine.Object.DontDestroyOnLoad(go);

        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Over the whole HUD, one below the loading curtain (short.MaxValue), so a door taken mid-fade still shows.
        canvas.sortingOrder = short.MaxValue - 1;

        var black = new GameObject("Black", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        var rt = (RectTransform)black.transform;
        rt.SetParent(go.transform, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var image = black.GetComponent<Image>();
        image.color = Color.black;
        image.raycastTarget = true;

        group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = true;
        group.interactable = false;

        ticker = go.GetComponent<ScreenFadeTicker>();
        ticker.enabled = false;
        go.SetActive(false);
    }
}

/// <summary>Ticks <see cref="ScreenFade"/>, and only while a fade is running.</summary>
public sealed class ScreenFadeTicker : MonoBehaviour
{
    // Unscaled: a fade is screen time, not game time.
    void Update() => ScreenFade.Step(Time.unscaledDeltaTime);
}
