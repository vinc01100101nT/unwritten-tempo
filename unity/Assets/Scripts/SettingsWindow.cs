using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The <b>Settings</b> window, opened from <see cref="SystemWindow"/>. Four tabs — Gameplay, Sound,
/// Graphics, Keys — each showing its own page; one page is visible at a time. Keys is where the hotbar's third row
/// gets its keys: each slot's button opens a list of the keys the game does not already use (<see cref="GameKeys"/>).
/// Graphics' Resolution button opens the same list, filled with the sizes the monitor offers.
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
    GameObject root;
    readonly List<Button> tabs = new List<Button>();
    readonly List<GameObject> pages = new List<GameObject>();

    public bool IsOpen => UiAnim.IsOpen(root);
    public void Open()
    {
        if (root == null) return;
        // Alt+Enter and the window's own frame change the screen without this window, so it re-reads it on every open.
        resolutionLabel.text = SizeName(Screen.width, Screen.height);
        fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen);
        UiAnim.Open(root);
        UISounds.PlayOpen();
    }
    public void Close() { if (root != null) { UiAnim.Close(root); UISounds.PlayClose(); } }

    void Start()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { enabled = false; return; }

        var content = UiKit.Window(canvas.transform, "Settings", new Vector2(460, 340), Vector2.zero, out root);

        var tabRow = UiKit.TabRow(content);

        var body = UiKit.Img("Pages", content, new Color(0, 0, 0, 0));
        body.raycastTarget = false;
        UiKit.Fill(body.rectTransform, Vector2.zero, new Vector2(0, -34));

        var gameplay = AddTab(tabRow.transform, "Gameplay", body.transform);
        var sound = AddTab(tabRow.transform, "Sound", body.transform);
        var graphics = AddTab(tabRow.transform, "Graphics", body.transform);
        var keys = AddTab(tabRow.transform, "Keys", body.transform);

        // Tick boxes only need 20 points at the right edge, so their names get the wide column (a slider's
        // track would start at 45%).
        var quickSingle = UiKit.Checkbox(Row(gameplay, "Quick cast single targeting", 0.85f),
                                         GameSettings.QuickCastSingleTarget);
        quickSingle.onValueChanged.AddListener(v => GameSettings.QuickCastSingleTarget = v);

        var quickGround = UiKit.Checkbox(Row(gameplay, "Quick cast ground targeting", 0.85f),
                                         GameSettings.QuickCastGroundTarget);
        quickGround.onValueChanged.AddListener(v => GameSettings.QuickCastGroundTarget = v);

        var hideCalls = UiKit.Checkbox(Row(gameplay, "Hide other players' skill calls", 0.85f),
                                       GameSettings.HideOtherPlayersSkillCalls);
        hideCalls.onValueChanged.AddListener(v => GameSettings.HideOtherPlayersSkillCalls = v);

        var scrollSens = UiKit.MakeSlider(Row(gameplay, "Scroll sensitivity"), 0f);
        scrollSens.minValue = GameSettings.ScrollSensitivityMin;
        scrollSens.maxValue = GameSettings.ScrollSensitivityMax;
        scrollSens.value = GameSettings.ScrollSensitivity;
        scrollSens.onValueChanged.AddListener(v => GameSettings.ScrollSensitivity = v);

        var master = UiKit.MakeSlider(Row(sound, "Master volume"), GameSettings.MasterVolume);
        master.onValueChanged.AddListener(v => GameSettings.MasterVolume = v);

        var sfx = UiKit.MakeSlider(Row(sound, "Sound effects volume"), GameSettings.SfxVolume);
        sfx.onValueChanged.AddListener(v => GameSettings.SfxVolume = v);

        var musicVol = UiKit.MakeSlider(Row(sound, "Music volume"), GameSettings.MusicVolume);
        musicVol.onValueChanged.AddListener(v => GameSettings.MusicVolume = v);

        var dark = UiKit.Checkbox(Row(graphics, "Dark mode"), GameSettings.DarkMode);
        dark.onValueChanged.AddListener(v => GameSettings.DarkMode = v);

        var smooth = UiKit.Checkbox(Row(graphics, "Smooth camera follow"), GameSettings.SmoothCameraFollow);
        smooth.onValueChanged.AddListener(v => GameSettings.SmoothCameraFollow = v);

        var resolutionBtn = ChoiceButton(Row(graphics, "Resolution", 0.6f), SizeName(Screen.width, Screen.height),
                                         OpenResolutionPicker);
        resolutionLabel = resolutionBtn.GetComponentInChildren<TMPro.TMP_Text>();

        fullscreenToggle = UiKit.Checkbox(Row(graphics, "Fullscreen", 0.85f), GameSettings.Fullscreen);
        fullscreenToggle.onValueChanged.AddListener(v => GameSettings.Fullscreen = v);

        BuildKeysPage(keys);
        BuildPicker(body.transform);

        Select(0);
    }

    // ---- resolution ---------------------------------------------------------------------------------------------

    TMPro.TMP_Text resolutionLabel;
    Toggle fullscreenToggle;
    readonly List<Vector2Int> sizes = new List<Vector2Int>();

    static string SizeName(int width, int height) => width + " × " + height;

    void OpenResolutionPicker()
    {
        OpenPicker(110f);
        GameSettings.Resolutions(sizes);
        for (int i = 0; i < sizes.Count; i++)
        {
            var size = sizes[i];
            PickerChoice(SizeName(size.x, size.y), () =>
            {
                GameSettings.SetResolution(size.x, size.y);
                // Screen.width still reads the old size until the next frame, so the label shows the pick itself.
                resolutionLabel.text = SizeName(size.x, size.y);
            });
        }
    }

    // ---- the hotbar's third row ---------------------------------------------------------------------------------

    readonly TMPro.TMP_Text[] keyLabels = new TMPro.TMP_Text[HotbarBindings.PerRow];
    readonly List<KeyCode> freeKeys = new List<KeyCode>();

    void BuildKeysPage(Transform page)
    {
        var vlg = page.GetComponent<VerticalLayoutGroup>();
        if (vlg != null) vlg.spacing = 3;
        for (int c = 0; c < HotbarBindings.PerRow; c++)
        {
            int column = c;
            var row = Row(page, "Row 3 · Slot " + (c + 1), 0.6f);
            row.GetComponent<LayoutElement>().preferredHeight = 24;
            var btn = ChoiceButton(row, KeyName(column), () => OpenKeyPicker(column));
            keyLabels[column] = btn.GetComponentInChildren<TMPro.TMP_Text>();
        }
    }

    static string KeyName(int column)
    {
        var key = GameSettings.HotbarRowThreeKey(column);
        return key == KeyCode.None ? "None" : GameKeys.Label(key);
    }

    // Fill the list with "None" and every key this slot may take right now.
    void OpenKeyPicker(int column)
    {
        OpenPicker(64f);
        AddKeyChoice(column, KeyCode.None);
        GameKeys.FreeKeys(column, freeKeys);
        for (int i = 0; i < freeKeys.Count; i++) AddKeyChoice(column, freeKeys[i]);
    }

    void AddKeyChoice(int column, KeyCode key)
    {
        PickerChoice(key == KeyCode.None ? "None" : GameKeys.Label(key), () =>
        {
            if (GameSettings.SetHotbarRowThreeKey(column, key))
            {
                keyLabels[column].text = KeyName(column);
                HotbarPanel.RebuildRequested?.Invoke();
            }
        });
    }

    // ---- the choice list ----------------------------------------------------------------------------------------
    // One list laid over the pages, opened by any option that picks from several (a key, a resolution); built empty,
    // refilled each time it opens.

    GameObject picker;
    RectTransform pickerList;
    GridLayoutGroup pickerGrid;

    void BuildPicker(Transform pageParent)
    {
        var panel = UiKit.Img("Picker", pageParent, UiKit.PanelRaise);
        UiKit.Fill(panel.rectTransform, Vector2.zero, Vector2.zero);
        picker = panel.gameObject;
        var view = UiKit.Img("List", panel.transform, new Color(0, 0, 0, 0)).rectTransform;
        UiKit.Fill(view, new Vector2(4, 36), new Vector2(-4, -4));
        pickerList = UiKit.ScrollColumn(view);
        pickerGrid = pickerList.gameObject.AddComponent<GridLayoutGroup>();
        pickerGrid.cellSize = new Vector2(64, 26);
        pickerGrid.spacing = new Vector2(4, 4);
        var cancel = UiKit.Btn("Cancel", panel.transform, () => picker.SetActive(false));
        var crt = (RectTransform)cancel.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 0f); crt.pivot = new Vector2(1f, 0f);
        crt.sizeDelta = new Vector2(90, 28); crt.anchoredPosition = new Vector2(-4, 4);
        picker.SetActive(false);
    }

    // Empty the list, size its buttons to fit their names, and show it; the caller then adds its choices.
    void OpenPicker(float choiceWidth)
    {
        for (int i = pickerList.childCount - 1; i >= 0; i--) Destroy(pickerList.GetChild(i).gameObject);
        pickerGrid.cellSize = new Vector2(choiceWidth, 26);
        picker.SetActive(true);
        picker.transform.SetAsLastSibling();
    }

    // One choice in the open list: clicking it does `pick` and closes the list.
    void PickerChoice(string label, System.Action pick)
    {
        UiKit.Btn(label, pickerList, () => { pick(); picker.SetActive(false); });
    }

    // The button on an option's row that shows the current choice and opens the list.
    static Button ChoiceButton(Transform row, string label, System.Action open)
    {
        var btn = UiKit.Btn(label, row, open);
        var rt = (RectTransform)btn.transform;
        rt.anchorMin = new Vector2(0.6f, 0f); rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = new Vector2(-4f, 0f);
        return btn;
    }

    // A tab button plus the page it shows. Returns the page's build target.
    Transform AddTab(Transform tabRow, string label, Transform pageParent)
    {
        int index = pages.Count;
        var btn = UiKit.Btn(label, tabRow, () => Select(index));
        tabs.Add(btn);

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
        if (picker != null) picker.SetActive(false);
        for (int i = 0; i < pages.Count; i++)
        {
            pages[i].SetActive(i == index);
            UiKit.MarkTab(tabs[i], i == index);
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
