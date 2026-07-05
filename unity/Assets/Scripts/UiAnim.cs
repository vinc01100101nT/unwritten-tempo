using System.Collections;
using UnityEngine;

/// <summary>
/// THE single choke point for HUD window open/close motion. Every panel that shows/hides itself routes
/// through <see cref="Open"/>/<see cref="Close"/>/<see cref="Toggle"/> here instead of calling
/// <c>GameObject.SetActive</c> directly — so the game's window feel (a subtle fade + scale pop) is defined
/// ONCE and can't drift per panel. Adds a new motion → edit this file, not N call sites.
///
/// JS/TS analogy: a tiny transition controller you mount on a modal; open() fades+scales it in, close()
/// reverses then unmounts. State lives here so a toggle can't get out of sync with a mid-flight animation.
///
/// Perf: motion runs only during the ~0.12s open/close via one coroutine, with the CanvasGroup/RectTransform
/// cached — zero steady-state cost and nothing in any combat hot path (see PERFORMANCE.md).
/// </summary>
[DisallowMultipleComponent]
public class UiAnim : MonoBehaviour
{
    const float Duration = 0.12f;
    const float FromScale = 0.96f;

    CanvasGroup group;
    RectTransform rt;
    Coroutine anim;
    // intended state — the source of truth a toggle reads, NOT activeSelf (which lags during a close anim)
    bool open;
    bool wired;

    void Wire()
    {
        if (wired) return;
        rt = (RectTransform)transform;
        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        wired = true;
    }

    // ---- static entry points (all HUD panels call these) ----------------------

    /// <summary>Fade + scale a panel in. No-op if already open.</summary>
    public static void Open(GameObject go)  { if (go != null) For(go).OpenInternal(); }

    /// <summary>Fade + scale a panel out, then deactivate it (so WindowFrame still saves position).</summary>
    public static void Close(GameObject go) { if (go != null) For(go).CloseInternal(); }

    /// <summary>Flip a panel's open state. Returns the NEW intended state (use this instead of activeSelf).</summary>
    public static bool Toggle(GameObject go)
    {
        if (go == null) return false;
        var a = For(go);
        if (a.open) { a.CloseInternal(); return false; }
        a.OpenInternal(); return true;
    }

    public static void SetOpen(GameObject go, bool value) { if (value) Open(go); else Close(go); }

    /// <summary>Intended open state — true from the moment Open starts until Close starts (survives the anim).</summary>
    public static bool IsOpen(GameObject go)
    {
        if (go == null) return false;
        var a = go.GetComponent<UiAnim>();
        return a != null ? a.open : go.activeSelf;
    }

    static UiAnim For(GameObject go)
    {
        var a = go.GetComponent<UiAnim>();
        if (a == null) a = go.AddComponent<UiAnim>();
        a.Wire();
        return a;
    }

    // ---- instance ------------------------------------------------------------

    void OpenInternal()
    {
        Wire();
        if (open && gameObject.activeSelf) return;
        open = true;
        // showing from hidden: force alpha 0 so the fade always plays (a fresh CanvasGroup defaults to 1)
        if (!gameObject.activeSelf) { group.alpha = 0f; gameObject.SetActive(true); }
        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(Play(true));
    }

    void CloseInternal()
    {
        Wire();
        open = false;
        // already hidden
        if (!gameObject.activeSelf) return;
        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(Play(false));
    }

    IEnumerator Play(bool opening)
    {
        // clicks pass through immediately on close
        group.blocksRaycasts = opening;
        float t = 0f;
        float a0 = group.alpha;
        float a1 = opening ? 1f : 0f;
        float s0 = opening ? FromScale : 1f;
        float s1 = opening ? 1f : FromScale;
        // starting fully hidden: snap the from-scale so the pop is visible from the first frame
        if (opening && a0 <= 0.01f) rt.localScale = Vector3.one * FromScale;

        while (t < Duration)
        {
            // HUD ignores timeScale (a death screen may pause the game)
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / Duration);
            // ease-out on open, ease-in on close
            float e = opening ? 1f - (1f - k) * (1f - k) : k * k;
            group.alpha = Mathf.Lerp(a0, a1, e);
            rt.localScale = Vector3.one * Mathf.Lerp(s0, s1, e);
            yield return null;
        }

        group.alpha = a1;
        rt.localScale = Vector3.one;
        anim = null;
        // now WindowFrame.OnDisable runs and saves the window position
        if (!opening) gameObject.SetActive(false);
        else group.blocksRaycasts = true;
    }
}
