using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The RO-style spell <b>cast bar</b> — a small bar that floats over a caster's head with the skill's name above
/// it and FILLS left→right as the spell winds up, then vanishes the instant the cast completes or is interrupted.
///
/// <para><b>ONE PER CASTER, not one in the game.</b> It used to be a single bar, on the reasoning that the player
/// casts one spell at a time — true of the player and false of the world, so nobody could see anybody else wind
/// up. Everybody in sight can be casting at once now, so a bar belongs to the BODY it floats over: ask for one by
/// naming that body and it is made, reused and recycled for you. Same shape as <see cref="SpeechBubble"/>, which
/// solved the identical problem for overhead speech.</para>
///
/// <para>Driven the same way from both sides, deliberately: whoever is showing a wind-up calls <see cref="Set"/>
/// every frame of it and <see cref="Hide"/> when it ends. At this keyboard that is the skill playing; for somebody
/// else it is their shell, following what the server said. One body of drawing code, so the two cannot drift.</para>
///
/// PERFORMANCE (see PERFORMANCE.md): one shared world-space <see cref="Canvas"/> for every bar, built once
/// (canvases are batch/rebuild boundaries — never one per cast), and bars are pooled rather than made and thrown
/// away. Per casting frame each one only writes transforms (follow + billboard) and the fill amount — no
/// allocation, no Find/GetComponent, no per-frame string work (the label is set only when it changes). Drawn with
/// <see cref="MobNameplate"/>'s shared draws-over-the-world material, so a bar is never hidden by terrain or scenery.
/// </summary>
[DisallowMultipleComponent]
public class CastBar : MonoBehaviour
{
    // Layout (UI pixels, scaled into the world by worldScale — matched to HoverPlate so the bar reads at
    // the same size as a unit's HP bar).
    const float WorldScale = 0.0075f;
    const float BarWidth = 60f, BarHeight = 8f;
    const int   LabelFontSize = 14;

    static readonly Color TrackColor = new Color(0.06f, 0.05f, 0.07f, 0.88f);

    static CastBar instance;          // the one host + canvas every bar lives under (Unity-null after a scene unload → rebuilt lazily)
    static bool quitting;

    Transform camT;

    // ---- one bar, belonging to one caster -------------------------------------
    class Bar
    {
        public Transform over;        // the body this bar floats above — also the name it is asked for by
        public RectTransform root;
        public Image fill;
        public TMP_Text label;
        public string shownLabel;     // last label written (so we only touch the Text when it changes)
        public int lastSetFrame;      // the frame its driver last refreshed it — see the sweep in LateUpdate
    }

    // The bars on screen right now, and the ones waiting to be used again. A LIST rather than a dictionary on
    // purpose: this holds the people casting within sight, which is a handful, and a reference walk over four
    // entries costs less than hashing one.
    static readonly List<Bar> live = new List<Bar>(4);
    static readonly Stack<Bar> pool = new Stack<Bar>();

    /// <summary>Show/refresh the cast bar over <paramref name="over"/>'s head, filled to
    /// <paramref name="progress01"/> (0 = just started, 1 = done). Cheap to call every frame, which is how it is
    /// meant to be called: a bar that stops being refreshed puts itself away.</summary>
    public static void Set(Transform over, float progress01, string text, Color tint)
    {
        if (over == null) return;
        var host = Ensure();
        if (host == null) return;
        host.Apply(over, Mathf.Clamp01(progress01), text, tint);
    }

    /// <summary>Hide the cast bar over <paramref name="over"/>'s head (cast finished or interrupted). Safe to call
    /// when there is no bar there.</summary>
    public static void Hide(Transform over)
    {
        if (over == null) return;
        for (int i = live.Count - 1; i >= 0; i--)
            if (ReferenceEquals(live[i].over, over)) { Recycle(i); return; }
    }

    static CastBar Ensure()
    {
        if (quitting) return null;
        if (instance != null) return instance;
        // A scene unload destroyed the old host and every bar under it — drop the stale entries with it, or the
        // next Set would refresh a bar that no longer exists.
        live.Clear();
        pool.Clear();
        var go = new GameObject("CastBarSystem") { hideFlags = HideFlags.DontSave };
        instance = go.AddComponent<CastBar>();
        return instance;
    }

    void OnApplicationQuit() => quitting = true;
    void OnDestroy() { if (instance == this) { instance = null; live.Clear(); pool.Clear(); } }

    void Apply(Transform over, float progress01, string text, Color tint)
    {
        if (camT == null) { var c = Camera.main; if (c == null) return; camT = c.transform; }

        Bar bar = null;
        for (int i = 0; i < live.Count; i++)
            if (ReferenceEquals(live[i].over, over)) { bar = live[i]; break; }
        if (bar == null)
        {
            bar = pool.Count > 0 ? pool.Pop() : Build();
            bar.over = over;
            bar.shownLabel = null;
            bar.root.gameObject.SetActive(true);
            live.Add(bar);
        }
        bar.lastSetFrame = Time.frameCount;

        // Ride the caster's head and yaw to face the camera (same as HoverPlate/DamagePopup).
        bar.root.position = UnitAnchor.Head(over);
        bar.root.rotation = camT.rotation;
        bar.root.localScale = Vector3.one * (WorldScale * UnitPlate.ShrinkAt(bar.root.position));

        bar.fill.fillAmount = progress01;
        bar.fill.color = tint;

        if (text != bar.shownLabel) { bar.shownLabel = text; bar.label.text = text; }   // only touch the Text on change
    }

    // A BAR NOBODY REFRESHED THIS FRAME IS OVER. Every bar is driven per frame by whoever is showing that wind-up,
    // and each of those drivers has an ending that hides it — but one of them lives on another machine and says so
    // over the network, and a message that never arrives would otherwise leave a bar hanging over somebody's head
    // for the rest of the session. This makes that impossible without the drivers having to be reliable.
    // The walk is over the people casting within sight, which is a handful, and skips instantly when there are none.
    void LateUpdate()
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var bar = live[i];
            if (bar.over == null || bar.lastSetFrame < Time.frameCount - 1) Recycle(i);
        }
    }

    static void Recycle(int index)
    {
        var bar = live[index];
        live.RemoveAt(index);
        bar.over = null;
        if (bar.root != null) bar.root.gameObject.SetActive(false);
        pool.Push(bar);
    }

    Bar Build()
    {
        // One shared world-space canvas in the FX band (above entities), like the mob nameplates.
        var canvas = gameObject.GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = DepthSortRuntime.FxPopup;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
        }

        var bar = new Bar();

        bar.root = NewRect("CastBar", transform);
        bar.root.localScale = Vector3.one * WorldScale;
        bar.root.pivot = new Vector2(0.5f, 0.5f);
        bar.root.sizeDelta = new Vector2(BarWidth, BarHeight);

        // Dark track.
        var bg = NewImage("Track", bar.root, TrackColor);
        var brt = bg.rectTransform;
        brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
        brt.offsetMin = Vector2.zero; brt.offsetMax = Vector2.zero;

        // Fill — inset inside the track, grows left→right as the spell winds up.
        bar.fill = NewImage("Fill", bg.rectTransform, Color.white);
        var frt = bar.fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one; frt.pivot = new Vector2(0.5f, 0.5f);
        frt.offsetMin = new Vector2(1.5f, 1.5f); frt.offsetMax = new Vector2(-1.5f, -1.5f);
        bar.fill.type = Image.Type.Filled;
        bar.fill.fillMethod = Image.FillMethod.Horizontal;
        bar.fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        bar.fill.fillAmount = 0f;

        // Skill name, just ABOVE the bar.
        var labelGO = new GameObject("Name", typeof(RectTransform));
        var lrt = (RectTransform)labelGO.transform;
        lrt.SetParent(bar.root, false);
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f); lrt.pivot = new Vector2(0.5f, 0f);
        lrt.anchoredPosition = new Vector2(0f, 2f);
        lrt.sizeDelta = new Vector2(BarWidth * 3f, LabelFontSize + 4);
        bar.label = labelGO.AddComponent<TextMeshProUGUI>();
        bar.label.font = UiKit.Font;
        bar.label.fontSharedMaterial = UiKit.OutlinedMaterial(UiKit.Font);   // dark edge + draws over terrain
        bar.label.fontSize = LabelFontSize;
        bar.label.fontStyle = FontStyles.Bold;
        bar.label.alignment = TextAlignmentOptions.Bottom;
        bar.label.textWrappingMode = TextWrappingModes.NoWrap;
        bar.label.raycastTarget = false;
        bar.label.color = new Color(0.95f, 0.95f, 0.99f);
        bar.shownLabel = null;
        return bar;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        // The ONE white sprite and the ONE draws-over-the-world material every floating unit widget shares.
        img.sprite = MobNameplate.SharedWhite;
        img.color = color;
        img.raycastTarget = false;
        var onTop = MobNameplate.SharedOnTop;
        if (onTop != null) img.material = onTop;
        return img;
    }
}
