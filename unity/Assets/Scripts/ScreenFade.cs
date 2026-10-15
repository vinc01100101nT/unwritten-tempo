using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The whole screen fading to black and back, with one thing done at the darkest moment — a wing's trip, so the
/// player never watches the camera cross the map to where they landed.
///
/// <para>Built the first time it is needed and switched off the rest of the time, so it draws nothing and ticks
/// nothing between trips. It never blocks a click.</para>
/// </summary>
public static class ScreenFade
{
    static CanvasGroup group;
    static ScreenFadeTicker ticker;
    static Action atBlack;
    static bool goingDark;

    /// <summary>Fade to black, run <paramref name="whileBlack"/> on the frame the screen is fully black, then fade
    /// back. Each half takes <see cref="LoadingScreen.FadeInSeconds"/>, the same as a door's fade in. Asked again
    /// before the screen is black, the newer action replaces the older one — only the latest trip is where the
    /// server holds the player.</summary>
    public static void Through(Action whileBlack)
    {
        Build();
        atBlack = whileBlack;
        goingDark = true;
        group.gameObject.SetActive(true);
        ticker.enabled = true;
    }

    // One frame of the fade, driven by ScreenFadeTicker while it runs.
    internal static void Step(float seconds)
    {
        float step = seconds / LoadingScreen.FadeInSeconds;
        if (goingDark)
        {
            group.alpha = Mathf.Min(1f, group.alpha + step);
            if (group.alpha < 1f) return;
            goingDark = false;
            var act = atBlack;
            atBlack = null;
            act?.Invoke();
            return;
        }
        group.alpha = Mathf.Max(0f, group.alpha - step);
        if (group.alpha > 0f) return;
        ticker.enabled = false;
        group.gameObject.SetActive(false);
    }

    static void Build()
    {
        if (group != null) return;

        var go = new GameObject("ScreenFade", typeof(Canvas), typeof(CanvasGroup), typeof(ScreenFadeTicker));
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
        image.raycastTarget = false;

        group = go.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
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
