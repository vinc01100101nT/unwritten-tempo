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

        var gameplay = AddTab(tabRow.transform, "Gameplay", body.transform);
        var sound = AddTab(tabRow.transform, "Sound", body.transform);
        var graphics = AddTab(tabRow.transform, "Graphics", body.transform);

        // Tick boxes only need 20 points at the right edge, so their names get the wide column (a slider's
        // track would start at 45%).
        var quickSingle = UiKit.Checkbox(Row(gameplay, "Quick cast single targeting", 0.85f),
                                         GameSettings.QuickCastSingleTarget);
        quickSingle.onValueChanged.AddListener(v => GameSettings.QuickCastSingleTarget = v);

        var quickGround = UiKit.Checkbox(Row(gameplay, "Quick cast ground targeting", 0.85f),
                                         GameSettings.QuickCastGroundTarget);
        quickGround.onValueChanged.AddListener(v => GameSettings.QuickCastGroundTarget = v);

        var master = UiKit.MakeSlider(Row(sound, "Master volume"), GameSettings.MasterVolume);
        master.onValueChanged.AddListener(v => GameSettings.MasterVolume = v);

        var sfx = UiKit.MakeSlider(Row(sound, "Sound effects volume"), GameSettings.SfxVolume);
        sfx.onValueChanged.AddListener(v => GameSettings.SfxVolume = v);

        var musicVol = UiKit.MakeSlider(Row(sound, "Music volume"), GameSettings.MusicVolume);
        musicVol.onValueChanged.AddListener(v => GameSettings.MusicVolume = v);

        var smooth = UiKit.Checkbox(Row(graphics, "Smooth camera follow"), GameSettings.SmoothCameraFollow);
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

    // One option line: its name on the left, room for its control on the right. The caller drops the control
    // (tick box, slider) into the returned row, so every option reads at the same height and alignment.
    Transform Row(Transform page, string label, float labelWidth = 0.45f)
    {
        var row = UiKit.Img("Row_" + label, page, new Color(0, 0, 0, 0));
        row.raycastTarget = false;
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;

        var text = UiKit.Label("Label", row.transform, label, 15, FontStyle.Normal, TextAnchor.MiddleLeft);
        // Stop the name where its control starts — 45% by default (MakeSlider's track) — so a long label can
        // never run underneath its own control.
        text.rectTransform.anchorMin = new Vector2(0f, 0f);
        text.rectTransform.anchorMax = new Vector2(labelWidth, 1f);
        text.rectTransform.offsetMin = new Vector2(4f, 0f);
        text.rectTransform.offsetMax = new Vector2(-6f, 0f);
        text.raycastTarget = false;

        return row.transform;
    }
}
