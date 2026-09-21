using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// THE way a HUD element reacts to the cursor arriving and leaving — and nothing else. Every hover card (skills,
/// buffs, states) goes through it.
///
/// <para>It replaced Unity's EventTrigger, which answers EVERY pointer event — the mouse wheel and drags included —
/// even when only enter and exit were wanted. Answering the wheel stopped it there: a row with a hover card inside a
/// scrolling list swallowed every wheel turn, so the list never scrolled. This listens to enter and exit only, so the
/// wheel and drags carry on to the scrolling list around it.</para>
/// </summary>
public sealed class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    Action onEnter, onExit;

    /// <summary>Make <paramref name="go"/> call <paramref name="enter"/> / <paramref name="exit"/> as the cursor
    /// arrives and leaves. Calling it again REPLACES the pair, so a rebuilt cell never fires twice.</summary>
    public static void Set(GameObject go, Action enter, Action exit)
    {
        if (go == null) return;
        var relay = go.GetComponent<HoverRelay>();
        if (relay == null) relay = go.AddComponent<HoverRelay>();
        relay.onEnter = enter;
        relay.onExit = exit;
    }

    public void OnPointerEnter(PointerEventData e) => onEnter?.Invoke();
    public void OnPointerExit(PointerEventData e) => onExit?.Invoke();
}
