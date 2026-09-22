using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// The UI's view of the mouse, with the scroll wheel scaled by <see cref="GameSettings.ScrollSensitivity"/>.
/// Every scroll a window receives — each scrolling list, the character preview's zoom — reads its wheel
/// through the input module, so scaling it here reaches all of them at once and no window has to remember to
/// read the setting. Everything else (clicks, pointer position, touches, keys) passes through untouched.
/// </summary>
public class ScaledScrollInput : BaseInput
{
    public override Vector2 mouseScrollDelta => base.mouseScrollDelta * GameSettings.ScrollSensitivity;

    /// <summary>Route the scene's input module through this. Safe to call more than once.</summary>
    public static void Attach()
    {
        var module = Object.FindAnyObjectByType<BaseInputModule>();
        if (module == null || module.inputOverride is ScaledScrollInput) return;
        var input = module.GetComponent<ScaledScrollInput>();
        if (input == null) input = module.gameObject.AddComponent<ScaledScrollInput>();
        module.inputOverride = input;
    }
}
