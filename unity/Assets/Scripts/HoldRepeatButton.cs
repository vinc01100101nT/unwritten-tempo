using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Press-and-hold auto-repeat for a runtime-built uGUI <see cref="Button"/>. Behaviour:
///   • press        → fire once immediately,
///   • keep holding → after <see cref="initialDelay"/>, fire every <see cref="repeatInterval"/>,
///   • release/exit → stop.
/// Each fire calls the action set via <see cref="SetAction"/>; if it returns <c>false</c> (e.g. there's
/// nothing left to spend) the hold stops early. Sits alongside a Button so interactable/raycast
/// handling comes for free. Uses unscaled time so it keeps repeating even if the game is paused while
/// a menu is open. Replaces a plain <c>onClick</c> when you want click-and-hold to keep firing.
/// </summary>
[RequireComponent(typeof(Button))]
public class HoldRepeatButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
    [Tooltip("Seconds to hold after the first fire before auto-repeat kicks in.")]
    public float initialDelay = 0.5f;
    [Tooltip("Seconds between fires once auto-repeat has started.")]
    public float repeatInterval = 0.08f;

    Func<bool> action;
    Button button;
    bool holding;
    float nextFire;

    void Awake() => button = GetComponent<Button>();

    /// <summary>The per-fire action. Return <c>false</c> to end the current hold (out of points, etc.).</summary>
    public void SetAction(Func<bool> a) => action = a;

    public void OnPointerDown(PointerEventData e)
    {
        if (button == null || !button.IsInteractable() || action == null) return;
        holding = true;
        Fire();                                          // add 1 right away
        nextFire = Time.unscaledTime + initialDelay;     // …then the 0.5s pause before continuous adds
    }

    public void OnPointerUp(PointerEventData e)   => holding = false;
    public void OnPointerExit(PointerEventData e) => holding = false;

    void Update()
    {
        if (!holding) return;
        if (button == null || !button.IsInteractable()) { holding = false; return; }
        if (Time.unscaledTime < nextFire) return;
        Fire();
        nextFire = Time.unscaledTime + repeatInterval;
    }

    void Fire()
    {
        if (action == null || !action()) holding = false;   // stop once the action reports nothing happened
    }
}
