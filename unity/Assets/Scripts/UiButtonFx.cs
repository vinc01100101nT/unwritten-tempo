using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Hover/press feel for HUD buttons, added ONCE inside <see cref="UiKit.Btn"/> so every button in the game
/// gets the same motion for free — the button sibling of <see cref="UiAnim"/>. Pointer-enter scales up a
/// touch, pointer-down presses in; both ease back on exit/up. Tunes live here, not per button.
///
/// Perf: a transition coroutine runs only while the scale is settling (a few frames on enter/exit/press),
/// never every frame; the RectTransform is cached. No allocation in any steady-state or combat path.
/// </summary>
[DisallowMultipleComponent]
public class UiButtonFx : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    const float Hover = 1.05f;
    const float Press = 0.95f;
    const float Speed = 14f;

    RectTransform rt;
    Coroutine anim;
    bool hovering, pressing;

    void Awake() => rt = (RectTransform)transform;

    public void OnPointerEnter(PointerEventData e) { hovering = true;  Retarget(); }
    public void OnPointerExit(PointerEventData e)  { hovering = false; Retarget(); }
    public void OnPointerDown(PointerEventData e)  { pressing = true;  Retarget(); }
    public void OnPointerUp(PointerEventData e)    { pressing = false; Retarget(); }

    float Target => pressing ? Press : hovering ? Hover : 1f;

    void Retarget()
    {
        if (!isActiveAndEnabled) return;
        if (anim != null) StopCoroutine(anim);
        anim = StartCoroutine(Settle());
    }

    IEnumerator Settle()
    {
        while (true)
        {
            float cur = rt.localScale.x;
            float next = Mathf.MoveTowards(cur, Target, Speed * Time.unscaledDeltaTime * Mathf.Max(0.01f, Mathf.Abs(Target - cur)));
            rt.localScale = Vector3.one * next;
            if (Mathf.Approximately(next, Target)) break;
            yield return null;
        }
        rt.localScale = Vector3.one * Target;
        anim = null;
    }

    void OnDisable()
    {
        // reset so a button that gets hidden mid-hover doesn't reappear scaled
        hovering = pressing = false;
        if (rt != null) rt.localScale = Vector3.one;
        anim = null;
    }
}
