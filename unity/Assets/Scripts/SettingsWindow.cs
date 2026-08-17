using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The <b>Settings</b> window, opened from <see cref="SystemWindow"/>. Three tabs — Gameplay, Sound,
/// Graphics — each showing its own page; one page is visible at a time.
///
/// <para>Every option here reads and writes <see cref="GameSettings"/>, which owns the values and persists
/// them; this window only draws them.</para>
///
/// Self-builds a <see cref="UiKit.Window"/> on the persistent GameUICanvas (added by
/// <see cref="Editor.UIShellBuilder"/>). Escape is handled by <see cref="SystemWindow"/> so the two windows
/// close in order.
/// </summary>
public class SettingsWindow : MonoBehaviour
{
    static readonly Color TabIdle = new Color(UiKit.Gold.r, UiKit.Gold.g, UiKit.Gold.b, 0.35f);

    GameObject root;
    readonly List<Image> tabBorders = new List<Image>();
    readonly List<GameObject> pages = new List<GameObject>();

    public bool IsOpen => UiAnim.IsOpen(root);
    public void Open()  { if (root != null) { UiAnim.Open(root);  UISounds.PlayOpen(); } }
    public void Close() { if (root != null) { UiAnim.Close(root); UISounds.PlayClose(); } }

    void Start()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { enabled = false; return; }

        var content = UiKit.Window(canvas.transform, "Settings", new Vector2(460, 340), Vector2.zero, out root);

        var tabRow = UiKit.Img("Tabs", content, new Color(0, 0, 0, 0));
        tabRow.rectTransform.anchorMin = new Vector2(0, 1);
        tabRow.rectTransform.anchorMax = new Vector2(1, 1);
        tabRow.rectTransform.pivot = new Vector2(0.5f, 1);
        tabRow.rectTransform.sizeDelta = new Vector2(0, 28);
        tabRow.raycastTarget = false;
        var hl = tabRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 4;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = hl.childForceExpandHeight = true;

        var body = UiKit.Img("Pages", content, new Color(0, 0, 0, 0));
        body.raycastTarget = false;
        UiKit.Fill(body.rectTransform, Vector2.zero, new Vector2(0, -34));

        AddTab(tabRow.transform, "Gameplay", body.transform);
        AddTab(tabRow.transform, "Sound", body.transform);
        var graphics = AddTab(tabRow.transform, "Graphics", body.transform);

        var smooth = OptionRow(graphics, "Smooth camera follow", GameSettings.SmoothCameraFollow);
        smooth.onValueChanged.AddListener(v => GameSettings.SmoothCameraFollow = v);

        Select(0);
    }

    // A tab button plus the page it shows. Returns the page's build target.
    Transform AddTab(Transform tabRow, string label, Transform pageParent)
    {
        int index = pages.Count;
        var btn = UiKit.Btn(label, tabRow, () => Select(index));
        tabBorders.Add(btn.GetComponent<Image>());

        var page = UiKit.Img("Page_" + label, pageParent, new Color(0, 0, 0, 0));
        page.raycastTarget = false;
        UiKit.Fill(page.rectTransform, Vector2.zero, Vector2.zero);
        var vlg = page.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6;
        vlg.padding = new RectOffset(4, 4, 6, 4);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childAlignment = TextAnchor.UpperCenter;
        pages.Add(page.gameObject);
        return page.transform;
    }

    void Select(int index)
    {
        for (int i = 0; i < pages.Count; i++)
        {
            pages[i].SetActive(i == index);
            if (tabBorders[i] != null) tabBorders[i].color = i == index ? UiKit.Gold : TabIdle;
        }
    }

    // One option line: its name on the left, its tick box on the right.
    Toggle OptionRow(Transform page, string label, bool on)
    {
        var row = UiKit.Img("Row_" + label, page, new Color(0, 0, 0, 0));
        row.raycastTarget = false;
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;

        var text = UiKit.Label("Label", row.transform, label, 15, FontStyle.Normal, TextAnchor.MiddleLeft);
        UiKit.Fill(text.rectTransform, new Vector2(4, 0), new Vector2(-32, 0));
        text.raycastTarget = false;

        return UiKit.Checkbox(row.transform, on);
    }
}
