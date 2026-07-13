using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The <b>Grimoire</b> (J) — a HUD window listing every skill authored in the <b>Skill Composer</b>
/// (<see cref="SkillCatalog"/>). Each composed <see cref="SkillDef"/> shows its icon, name, MP cost and
/// cooldown; drag a row onto a hotbar slot or click it to bind to the first empty slot (same binding path as
/// the Skills (K) menu — <see cref="SkillDragSource"/> / <see cref="HotbarBindings.BindToFirstEmpty"/>).
///
/// <para>This is the browse-and-bind surface for the data-driven skill pool that replaced the hand-written
/// Player3D* skill classes. Self-builds a <see cref="UiKit.Window"/> on the persistent GameUICanvas (added by
/// <see cref="Editor.UIShellBuilder"/>); the J toggle lives here in code so changing the key needs no re-bake.</para>
/// </summary>
public class SkillBookPanel : MonoBehaviour
{
    const KeyCode ToggleKey = KeyCode.J;

    static readonly Color RowBg    = new Color(0.16f, 0.16f, 0.20f, 0.95f);
    static readonly Color SubColor = new Color(0.75f, 0.8f, 0.95f, 0.85f);
    static readonly Color MpColor  = new Color(0.6f, 0.78f, 1f);

    GameObject root;                 // the window we show/hide on J
    RectTransform scrollContent;     // where rows are built
    bool built;

    void Update()
    {
        if (!built) { TryBuild(); return; }
        HandleHotkey();
    }

    void HandleHotkey()
    {
        if (root == null) return;
        if (Hotkeys.Down(ToggleKey))
        {
            bool open = UiAnim.Toggle(root);
            if (open) { Rebuild(); UISounds.PlayOpen(); } else UISounds.PlayClose();
        }
        else if (UiAnim.IsOpen(root) && Hotkeys.CancelDown())
        {
            UiAnim.Close(root);
            UISounds.PlayClose();
        }
    }

    void TryBuild()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        var content = UiKit.Window(canvas.transform, "Grimoire", new Vector2(360, 440), Vector2.zero, out root);
        scrollContent = EnsureScroll(content);

        var vlg = scrollContent.GetComponent<VerticalLayoutGroup>() ?? scrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6;
        vlg.padding = new RectOffset(2, 2, 2, 2);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;

        built = true;   // starts hidden (UiKit.Window inactivates the root); Rebuild runs on first open
    }

    // Turn the window's fixed "Content" node into a clipped vertical scroll and return the inner build target.
    RectTransform EnsureScroll(RectTransform viewport)
    {
        var existing = viewport.Find("ScrollContent") as RectTransform;
        if (existing != null) return existing;

        if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.GetComponent<Image>();
        if (vpImg != null) vpImg.raycastTarget = true;   // a graphic for the wheel to land on

        var scroll = viewport.GetComponent<ScrollRect>() ?? viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false; scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 22f;

        var sc = new GameObject("ScrollContent", typeof(RectTransform));
        var scrt = (RectTransform)sc.transform;
        scrt.SetParent(viewport, false);
        scrt.anchorMin = new Vector2(0, 1); scrt.anchorMax = new Vector2(1, 1); scrt.pivot = new Vector2(0.5f, 1);
        scrt.offsetMin = scrt.offsetMax = Vector2.zero;
        sc.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll.viewport = viewport;
        scroll.content = scrt;
        return scrt;
    }

    // ---- (re)build the list -------------------------------------------------

    void Rebuild()
    {
        if (scrollContent == null) return;
        for (int i = scrollContent.childCount - 1; i >= 0; i--) Destroy(scrollContent.GetChild(i).gameObject);

        var pool = SkillCatalog.OrderedIds();
        if (pool.Length == 0)
        {
            var empty = NewText(scrollContent, "No composed skills yet.\nMake one in the Skill Composer\n(Tools ▸ unwritten ▸ Skills).",
                                14, FontStyle.Italic, TextAnchor.MiddleCenter);
            empty.color = SubColor;
            SetHeight(empty.gameObject, 80);
            return;
        }

        var caption = NewText(scrollContent, "Drag a skill onto a hotbar slot, or click to bind:", 13, FontStyle.Italic, TextAnchor.MiddleCenter);
        caption.color = SubColor;
        SetHeight(caption.gameObject, 22);

        var combos = Character.Instance != null ? Character.Instance.Combos : null;
        for (int i = 0; i < pool.Length; i++)
        {
            var def = SkillCatalog.Get(pool[i]);
            // A skill committed to a combo chain has left solo use — don't offer it for hotbar binding here.
            if (def != null && (combos == null || !combos.IsCommitted(def.id))) BuildRow(def);
        }
    }

    void BuildRow(SkillDef def)
    {
        var row = NewImage(scrollContent, RowBg);
        row.raycastTarget = true;
        SetHeight(row.gameObject, 48);

        // Icon (left).
        if (def.icon != null)
        {
            var im = NewImage(row.transform, Color.white);
            im.sprite = def.icon; im.preserveAspect = true; im.raycastTarget = false;
            var irt = im.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(6f, 0f);
            irt.sizeDelta = new Vector2(38f, 38f);
        }

        // Name (top) + cost/cooldown sub-line (bottom).
        string name = string.IsNullOrEmpty(def.displayName) ? def.id : def.displayName;
        var title = NewText(row.transform, name, 14, FontStyle.Bold, TextAnchor.LowerLeft);
        title.color = def.tint == default ? Color.white : def.tint;
        title.raycastTarget = false;
        var trt = title.rectTransform;
        trt.anchorMin = new Vector2(0f, 0.5f); trt.anchorMax = new Vector2(1f, 1f);
        trt.offsetMin = new Vector2(def.icon != null ? 50f : 10f, 0f);
        trt.offsetMax = new Vector2(-8f, -4f);

        var sub = NewText(row.transform, $"<color=#99C7FF>MP {def.manaCost}</color>   ·   CD {def.cooldown:0.#}s", 11, FontStyle.Normal, TextAnchor.UpperLeft);
        sub.color = SubColor;
        sub.raycastTarget = false;
        var srt = sub.rectTransform;
        srt.anchorMin = new Vector2(0f, 0f); srt.anchorMax = new Vector2(1f, 0.5f);
        srt.offsetMin = new Vector2(def.icon != null ? 50f : 10f, 4f);
        srt.offsetMax = new Vector2(-8f, 0f);

        // Click = bind to first empty slot; drag = drop onto any slot (same path as the K menu).
        var btn = row.gameObject.AddComponent<Button>();
        btn.targetGraphic = row;
        var id = def.id;
        btn.onClick.AddListener(() => BindFirstEmpty(id));

        var drag = row.gameObject.AddComponent<SkillDragSource>();
        drag.skillId = id; drag.iconSprite = def.icon; drag.tint = def.tint == default ? Color.white : def.tint;
    }

    void BindFirstEmpty(string id)
    {
        var c = Character.Instance;
        if (c == null || c.Hotbar.BindToFirstEmpty(id) < 0) return;   // unknown, already bound, or bar full
        if (SaveManager.Instance != null) SaveManager.Instance.SaveNow();
        HotbarPanel.RebuildRequested?.Invoke();
        UISounds.PlayClick();
    }

    // ---- tiny UI helpers (match SkillDraftPanel's style) --------------------

    TMP_Text NewText(Transform parent, string str, int size, FontStyle style, TextAnchor anchor)
    {
        var t = UiKit.MakeText("Text", parent, str, size, style, anchor);
        t.color = Color.white;
        return t;
    }

    Image NewImage(Transform parent, Color color)
    {
        var go = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static void SetHeight(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = h;
    }
}
