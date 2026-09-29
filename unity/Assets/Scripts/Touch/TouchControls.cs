#if UNITY_ANDROID
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// THE one place fingers are read on Android. Everything the touch HUD produces lands here, and every gameplay
/// script that wants to know what the player did reads it from here — the same shape <see cref="Hotkeys"/> uses
/// for keys and <see cref="PointerInput"/> uses for the mouse, so there is never a second place a gesture can
/// come from.
///
/// <para><b>Why legacy <c>Input.touches</c> and not the new Input System's <c>Touchscreen</c>:</b> the uGUI
/// EventSystem in this project runs <c>StandaloneInputModule</c>, which reads legacy touches. Finger ids must
/// MATCH the module's, because <see cref="EventSystem.IsPointerOverGameObject(int)"/> is asked per finger to
/// decide whether a touch belongs to the HUD or the world. Reading a different backend would hand us ids the
/// module never saw, and every touch would look like it missed the UI.</para>
///
/// <para>The gestures, all of them:
/// <list type="bullet">
/// <item>Finger on the move stick → <see cref="MoveAxis"/> (the stick writes it; it is UI, so it never reaches
///       the world gestures below).</item>
/// <item>Finger on a HUD button → a virtual key press (<see cref="PressVirtual"/>), which <see cref="Hotkeys"/>
///       answers as though the key were struck. This is what makes every window toggle, jump, dash, interact and
///       hotbar slot work without touching the twenty scripts that read those keys.</item>
/// <item>One finger on the world, pressed and lifted without travelling far → <see cref="WorldCommit"/> at
///       <see cref="WorldPointer"/>. That is a move order, an attack order, or an aim confirm.</item>
/// <item>One finger on the world, dragged past <see cref="DragSlop"/> → <see cref="OrbitDelta"/>, and the pending
///       commit is cancelled so spinning the camera never also orders a move.</item>
/// <item>Two fingers → <see cref="ZoomDir"/> from the pinch, one step per <see cref="PinchStep"/> of spread.</item>
/// </list></para>
///
/// <para><b>Aiming is the one exception to the drag rule.</b> While a skill is being aimed, dragging must slide
/// the reticle rather than spin the camera, and lifting confirms — so during aim the drag never becomes an orbit
/// and the commit survives any distance travelled. We ask the two targeters the same question the player
/// controller already asks them, rather than keeping our own copy of "are we aiming".</para>
/// </summary>
public static class TouchControls
{
    /// <summary>Screen-space travel, in inches, past which a world press stops being a tap and becomes a camera
    /// drag. Inches rather than pixels so it feels identical on a dense phone and a cheap one.</summary>
    const float DragSlop = 0.14f;

    /// <summary>Change in the gap between two fingers, in inches, that counts as one zoom notch — matching one
    /// mouse-wheel click on the desktop, so the camera's own zoom step is reused untouched.</summary>
    const float PinchStep = 0.22f;

    /// <summary>Fallback dots-per-inch when the device does not report one (rare, and 0 on some emulators).</summary>
    const float FallbackDpi = 400f;

    // ---- what the HUD writes ---------------------------------------------------------------------------

    /// <summary>Stick direction, -1..1 per axis, magnitude ≤ 1 so it is analog: a half-pushed stick walks at half
    /// speed. Written by the on-screen stick every frame it is held, cleared the frame it is released.</summary>
    public static Vector2 MoveAxis { get; set; }

    static readonly HashSet<KeyCode> virtualHeld = new HashSet<KeyCode>();

    /// <summary>Fire a one-shot key press from a HUD button — through <see cref="Hotkeys.PressOnScreen"/>, the one
    /// on-screen press every platform shares.</summary>
    public static void PressVirtual(KeyCode key) => Hotkeys.PressOnScreen(key);

    /// <summary>Start/stop a held key from a HUD button, for the reads that ask "is it down right now".</summary>
    public static void HoldVirtual(KeyCode key, bool held)
    {
        if (held) virtualHeld.Add(key); else virtualHeld.Remove(key);
    }

    public static bool VirtualHeld(KeyCode key) => virtualHeld.Contains(key) || Hotkeys.PressedOnScreen(key);

    // ---- what the fingers produce ----------------------------------------------------------------------

    /// <summary>True once a finger has touched the screen this session. Lets the shared pointer gate prefer the
    /// finger on a real phone while still answering with the mouse in the editor, where the build target can be
    /// Android but the Game view is driven by a mouse.</summary>
    public static bool HasTouch { get; private set; }

    /// <summary>Where on screen the world gesture is, in pixels. Holds its last value after the finger lifts, so
    /// the frame that reports <see cref="WorldCommit"/> still knows where the commit happened.</summary>
    public static Vector2 WorldPointer { get; private set; }

    /// <summary>True for the single frame a world press was released as a tap (or as an aim). This is the touch
    /// equivalent of a left click, and it deliberately fires on RELEASE, not on press: a finger that has just
    /// landed might still become a camera drag, and ordering a move on touch-down would fire before we know.</summary>
    public static bool WorldCommit { get; private set; }

    /// <summary>Screen-pixel movement of the orbit drag this frame, zero when not orbiting.</summary>
    public static Vector2 OrbitDelta { get; private set; }

    /// <summary>+1 / -1 / 0 for this frame's pinch, in the same units the camera's mouse-wheel zoom takes.</summary>
    public static int ZoomDir { get; private set; }

    // ---- gesture state ---------------------------------------------------------------------------------

    static int worldFinger = -1;         // finger id owning the current world gesture, -1 = none
    static Vector2 pressStart;
    static bool pendingCommit;           // the press is still a candidate tap (hasn't become a drag)
    static bool orbiting;
    static float lastPinchGap;
    static bool pinching;

    static float Dpi => Screen.dpi > 1f ? Screen.dpi : FallbackDpi;
    static float SlopPixels => DragSlop * Dpi;
    static float PinchStepPixels => PinchStep * Dpi;

    /// <summary>True while a skill is being aimed. Asked of the two targeters directly — the same pair the player
    /// controller consults before it treats a click as a move — so there is no second copy of "are we aiming"
    /// to fall out of step.</summary>
    static bool Aiming => GroundTargeter.Active || UnitTargeter.Active;

    /// <summary>Read the fingers and turn them into this frame's gestures. Called once, early, by
    /// <see cref="TouchDriver"/> — never from gameplay code.</summary>
    public static void Tick()
    {
        WorldCommit = false;
        OrbitDelta = Vector2.zero;
        ZoomDir = 0;

        int count = Input.touchCount;
        if (count > 0) HasTouch = true;
        if (count >= 2) { TickPinch(); return; }

        pinching = false;
        if (count == 0) { EndWorldGesture(); return; }

        TickOneFinger(Input.GetTouch(0));
    }

    // Two fingers: pinch to zoom, and no world gesture survives it (releasing out of a pinch must not fire a
    // stray move order, which is why the world finger is dropped without committing).
    static void TickPinch()
    {
        worldFinger = -1; pendingCommit = false; orbiting = false;

        Touch a = Input.GetTouch(0), b = Input.GetTouch(1);
        float gap = Vector2.Distance(a.position, b.position);
        if (!pinching) { pinching = true; lastPinchGap = gap; return; }

        float moved = gap - lastPinchGap;
        if (Mathf.Abs(moved) < PinchStepPixels) return;
        ZoomDir = moved > 0f ? 1 : -1;               // fingers apart = zoom in, matching every phone map
        lastPinchGap = gap;
    }

    static void TickOneFinger(Touch t)
    {
        switch (t.phase)
        {
            case TouchPhase.Began:
                // A finger that lands on the HUD belongs to the HUD — the stick, a button, a window. It must not
                // also drive the world, or every tap on the bar would order a move behind it.
                if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(t.fingerId))
                { worldFinger = -1; return; }
                worldFinger = t.fingerId;
                pressStart = t.position;
                WorldPointer = t.position;
                pendingCommit = true;
                orbiting = false;
                break;

            case TouchPhase.Moved:
            case TouchPhase.Stationary:
                if (t.fingerId != worldFinger) return;
                WorldPointer = t.position;
                if (Aiming)
                {
                    // Aiming: the drag slides the reticle and the lift confirms, so it never becomes an orbit and
                    // the commit is never cancelled no matter how far the finger travels.
                    pendingCommit = true;
                    return;
                }
                if (!orbiting && (t.position - pressStart).magnitude > SlopPixels)
                { orbiting = true; pendingCommit = false; }   // it's a camera drag, so it is no longer a tap
                if (orbiting) OrbitDelta = t.deltaPosition;
                break;

            case TouchPhase.Ended:
                if (t.fingerId != worldFinger) return;
                WorldPointer = t.position;
                if (pendingCommit) WorldCommit = true;
                EndWorldGesture();
                break;

            case TouchPhase.Canceled:
                if (t.fingerId == worldFinger) EndWorldGesture();
                break;
        }
    }

    static void EndWorldGesture()
    {
        worldFinger = -1; pendingCommit = false; orbiting = false;
    }
}
#endif
