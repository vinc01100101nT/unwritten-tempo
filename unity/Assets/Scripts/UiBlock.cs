using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// THE single source of truth for "is the pointer over UI right now?" — used by every world-input script so a
/// click on any UI never leaks to the 3D world (moving, targeting, attacking a mob behind the panel).
///
/// It folds together the two UI systems the game uses:
/// 1. <b>uGUI</b> (Canvas windows) — <see cref="EventSystem.IsPointerOverGameObject"/>. For this to be true the
///    window must have a raycast-target graphic under the cursor; <see cref="UiKit.ClickShield"/> guarantees
///    that across a whole window.
/// 2. <b>IMGUI</b> (OnGUI dev overlays like the Effect/Sequence Tuner) — the EventSystem CANNOT see
///    these, so each overlay calls <see cref="MarkImguiHot"/> in its OnGUI while the cursor is over its window;
///    that pulse is honored here for a frame (OnGUI runs after Update, so we accept last frame's mark too).
///
/// <para>IMGUI and uGUI are SEPARATE input systems: an IMGUI overlay drawn on top does NOT stop the uGUI
/// EventSystem from also delivering the same click to a Button behind it (the "clicked the dev panel but the
/// Calendar HUD opened" bug). <see cref="ImguiClickShield"/> fixes that globally — while an IMGUI overlay is hot
/// it raises a transparent full-screen uGUI raycast shield so those clicks are absorbed, not passed through.</para>
/// </summary>
public static class UiBlock
{
    static int hotFrame = -1;

    /// <summary>Call from an IMGUI overlay's OnGUI when the cursor is over its window this frame.</summary>
    public static void MarkImguiHot() => hotFrame = Time.frameCount;

    /// <summary>True if an IMGUI overlay flagged the cursor over it this frame or last (Update runs before
    /// OnGUI, so the mark for the current frame lands a frame late — accept both).</summary>
    public static bool ImguiHot => hotFrame >= Time.frameCount - 1;

    /// <summary>The gate every world-input script should check: true when the pointer is over ANY UI (uGUI
    /// window or IMGUI overlay), so the click should not also reach the world.</summary>
    public static bool PointerOverUI()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return true;
        return ImguiHot;
    }

    // Self-bootstrap the uGUI shield once per play session (survives scene loads). Cheap: the shield object is
    // inactive except on frames an IMGUI overlay is hot.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureShield()
    {
        var go = new GameObject("ImguiClickShield") { hideFlags = HideFlags.DontSaveInEditor };
        Object.DontDestroyOnLoad(go);
        go.AddComponent<ImguiClickShield>();
    }
}

/// <summary>A transparent full-screen uGUI raycast target that is enabled ONLY while an IMGUI dev overlay is hot,
/// on a top-most canvas — so a click over an IMGUI overlay is consumed by uGUI here instead of leaking to a HUD
/// button drawn behind the overlay. Built entirely in code; added via AddComponent (no scene wiring).</summary>
public class ImguiClickShield : MonoBehaviour
{
    GameObject shield;

    void Awake()
    {
        var canvasGo = new GameObject("Canvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue - 1;   // above every HUD/window canvas
        canvasGo.AddComponent<GraphicRaycaster>();

        var img = new GameObject("Shield").AddComponent<Image>();
        img.transform.SetParent(canvasGo.transform, false);
        img.color = new Color(0f, 0f, 0f, 0f);       // invisible, but a raycast target
        img.raycastTarget = true;
        var rt = img.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;

        shield = canvasGo;
        shield.SetActive(false);
    }

    void Update()
    {
        if (shield != null && shield.activeSelf != UiBlock.ImguiHot) shield.SetActive(UiBlock.ImguiHot);
    }
}
