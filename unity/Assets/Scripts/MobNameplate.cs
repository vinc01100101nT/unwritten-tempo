using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A creature's DISPLAY NAME, carried on the creature for every readout that names it (the hover plate, the target
/// panel, the text client) — and the owner of the ONE world-space canvas every floating unit widget hangs under.
///
/// <para>It draws nothing of its own. Every unit in the game — creature, elite, shadow, person, villager — reads
/// through the same <see cref="HoverPlate"/> when pointed at, so there is one health bar and one behaviour for all of
/// them, with no per-kind plate beside it.</para>
///
/// PERFORMANCE (see PERFORMANCE.md): there is exactly ONE shared world-space <see cref="Canvas"/> for every floating
/// unit widget — never a Canvas per unit (canvases are batch/rebuild boundaries). A shared ZTest-Always material keeps
/// a widget from being swallowed by the terrain behind it.
/// </summary>
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public class MobNameplate : MonoBehaviour
{
    [Tooltip("The creature's name as every readout shows it. Set by the mob builder; falls back to the GameObject name.")]
    public string displayName;

    // ---- shared across ALL floating widgets: one canvas, one white sprite, one on-top material ----
    static Transform host;
    static Sprite whiteSprite;
    static Material onTopMat;     // UI/Default with ZTest Always, so a widget isn't hidden by terrain

    void Awake()
    {
        if (string.IsNullOrEmpty(displayName))
            displayName = gameObject.name.StartsWith("Monster_") ? gameObject.name.Substring("Monster_".Length)
                                                                 : gameObject.name;
    }

    // ---- shared resources --------------------------------------------------------------------------

    /// <summary>The ONE world-space canvas every floating unit widget hangs under, and the two shared drawing
    /// resources that go with it. Opened up so a second world widget cannot quietly start a second canvas, a
    /// second material and a second white sprite of its own.</summary>
    internal static Transform SharedHost { get { EnsureHost(); return host; } }

    internal static Material SharedOnTop => OnTop;

    internal static Sprite SharedWhite => White;

    static void EnsureHost()
    {
        if (host != null) return;                           // alive (Unity-null check); recreated after a scene unload
        var go = new GameObject("MobNameplateSystem") { hideFlags = HideFlags.DontSaveInEditor };   // runtime-only pool host — never serialize into a scene
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = DepthSortRuntime.FxPopup;     // FX band: above all entities + overhead
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        host = go.transform;
    }

    // One shared UI material that draws over the world (UiKit.DrawOverTheWorld), so a widget is never hidden by
    // the terrain or scenery in front of it.
    static Material OnTop
    {
        get
        {
            if (onTopMat != null) return onTopMat;
            var sh = Shader.Find("UI/Default");
            if (sh == null) return null;
            onTopMat = new Material(sh) { name = "MobNameplate (on top)" };
            UiKit.DrawOverTheWorld(onTopMat);
            return onTopMat;
        }
    }

    static Sprite White => whiteSprite != null
        ? whiteSprite
        : (whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f)));
}
