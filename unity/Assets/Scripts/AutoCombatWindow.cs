using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The <b>Auto-Combat</b> window, opened from <see cref="SystemWindow"/>. Three tabs — Hunt, Loot, Potions — over the
/// settings the server keeps for this character (<see cref="AutoCombatSettings"/>), plus the button that asks to start
/// or stops it.
///
/// <para><b>It only draws and asks.</b> Every change is sent to the server as the whole settings document a moment after
/// the player stops moving a control, and the window then shows whatever the server kept — a skill it would not take
/// comes back empty, a number comes back inside its range. Nothing here decides anything.</para>
///
/// <para>Built from the same pieces as the Settings window (tabs, rows, tick boxes, sliders, the choice list), on the
/// persistent GameUICanvas; added at runtime by <see cref="HotbarPanel"/>, so it needs no shell rebuild.</para>
/// </summary>
public class AutoCombatWindow : MonoBehaviour
{
    static AutoCombatWindow instance;

    GameObject root;
    readonly List<Button> tabs = new List<Button>();
    readonly List<GameObject> pages = new List<GameObject>();

    // The document being edited: the server's last word plus whatever the player has changed since and not yet sent.
    AutoCombatSettings draft = new AutoCombatSettings();
    int drawnVersion = -1;
    bool dirty;
    float sendAt;
    // Long enough that dragging a slider is one request, short enough that it feels saved.
    const float QuietSecondsBeforeSending = 0.4f;

    TMPro.TMP_Text onOffLabel, stateLabel;
    readonly Button[] huntButtons = new Button[3];
    readonly TMPro.TMP_Text[] slotLabels = new TMPro.TMP_Text[AutoCombatSettings.SkillSlots];
    Slider lootRate, healthBelow, manaBelow;
    TMPro.TMP_Text lootRateLabel, healthBelowLabel, manaBelowLabel;
    Toggle lootGears, lootConsumables, lootMaterials, lootAll;

    public static void Open()
    {
        if (instance == null || instance.root == null) return;
        instance.drawnVersion = -1;
        UiAnim.Open(instance.root);
        UISounds.PlayOpen();
    }

    void Start()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) { enabled = false; return; }
        instance = this;

        var content = UiKit.Window(canvas.transform, "Auto-Combat", new Vector2(440, 320), Vector2.zero, out root);
        var tabRow = UiKit.TabRow(content);
        var body = UiKit.Img("Pages", content, new Color(0, 0, 0, 0));
        body.raycastTarget = false;
        UiKit.Fill(body.rectTransform, Vector2.zero, new Vector2(0, -34));

        var hunt = AddTab(tabRow.transform, "Hunt", body.transform);
        var loot = AddTab(tabRow.transform, "Loot", body.transform);
        var potions = AddTab(tabRow.transform, "Potions", body.transform);

        // ---- Hunt ----
        var onOffRow = Row(hunt, "Auto-combat", 0.6f);
        var onOff = ChoiceButton(onOffRow, "Start", () => AutoCombatClient.Toggle());
        onOffLabel = onOff.GetComponentInChildren<TMPro.TMP_Text>();
        stateLabel = UiKit.Label("State", hunt, "", 12, FontStyle.Italic, TextAnchor.MiddleLeft);
        stateLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 18;
        stateLabel.color = UiKit.TextDim;
        stateLabel.raycastTarget = false;

        var modeRow = Row(hunt, "Hunt", 0.25f);
        string[] modeNames = { "Standing", "Radius", "Free Roam" };
        for (int m = 0; m < huntButtons.Length; m++)
        {
            int mode = m;
            var b = UiKit.Btn(modeNames[m], modeRow, () => { draft.huntMode = mode; Changed(); DrawHunt(); });
            var rt = (RectTransform)b.transform;
            rt.anchorMin = new Vector2(0.25f + 0.25f * m, 0f); rt.anchorMax = new Vector2(0.25f + 0.25f * (m + 1), 1f);
            rt.offsetMin = new Vector2(2f, 0f); rt.offsetMax = new Vector2(-2f, 0f);
            huntButtons[m] = b;
        }

        for (int i = 0; i < slotLabels.Length; i++)
        {
            int slot = i;
            var row = Row(hunt, "Skill " + (i + 1), 0.6f);
            row.GetComponent<LayoutElement>().preferredHeight = 24;
            var btn = ChoiceButton(row, "None", () => OpenSkillPicker(slot));
            slotLabels[i] = btn.GetComponentInChildren<TMPro.TMP_Text>();
        }

        // ---- Loot ----
        lootRate = UiKit.MakeSlider(RowWithLabel(loot, out lootRateLabel), 0f);
        WholePercent(lootRate);
        lootRate.onValueChanged.AddListener(v => { draft.lootRateAtMostPercent = v; PercentText(lootRateLabel, "Take drops of at most", v); Changed(); });
        lootGears = UiKit.Checkbox(Row(loot, "Gear", 0.85f), false);
        lootGears.onValueChanged.AddListener(v => { draft.lootGears = v; Changed(); });
        lootConsumables = UiKit.Checkbox(Row(loot, "Potions and food", 0.85f), false);
        lootConsumables.onValueChanged.AddListener(v => { draft.lootConsumables = v; Changed(); });
        lootMaterials = UiKit.Checkbox(Row(loot, "Materials", 0.85f), false);
        lootMaterials.onValueChanged.AddListener(v => { draft.lootMaterials = v; Changed(); });
        lootAll = UiKit.Checkbox(Row(loot, "Everything", 0.85f), false);
        lootAll.onValueChanged.AddListener(v => { draft.lootAll = v; Changed(); });

        // ---- Potions ----
        healthBelow = UiKit.MakeSlider(RowWithLabel(potions, out healthBelowLabel), 0f);
        WholePercent(healthBelow);
        healthBelow.onValueChanged.AddListener(v => { draft.hpPotionBelowPercent = v; PercentText(healthBelowLabel, "Drink for health below", v); Changed(); });
        manaBelow = UiKit.MakeSlider(RowWithLabel(potions, out manaBelowLabel), 0f);
        WholePercent(manaBelow);
        manaBelow.onValueChanged.AddListener(v => { draft.mpPotionBelowPercent = v; PercentText(manaBelowLabel, "Drink for mana below", v); Changed(); });
        var note = UiKit.Label("Note", potions, "The first one in your bag is drunk first. 0% = never.", 12, FontStyle.Italic,
                               TextAnchor.MiddleLeft);
        note.gameObject.AddComponent<LayoutElement>().preferredHeight = 20;
        note.color = UiKit.TextDim;
        note.raycastTarget = false;

        BuildPicker(body.transform);
        Select(0);
    }

    void OnDestroy() { if (instance == this) instance = null; }

    void Update()
    {
        if (root == null || !root.activeSelf) return;

        // THE PLAYER'S CHANGES GO a moment after they stop — the whole document, which the server answers in full.
        if (dirty && Time.unscaledTime >= sendAt)
        {
            dirty = false;
            AutoCombatClient.AskToKeep(draft);
        }

        // THE SERVER'S WORD IS DRAWN once per word, and never over changes still waiting to be sent.
        if (!dirty && drawnVersion != AutoCombatClient.Version)
        {
            drawnVersion = AutoCombatClient.Version;
            draft = AutoCombatClient.Settings.Clone();
            DrawAll();
        }
    }

    void Changed()
    {
        dirty = true;
        sendAt = Time.unscaledTime + QuietSecondsBeforeSending;
    }

    // ---- drawing --------------------------------------------------------------------------------------------------

    void DrawAll()
    {
        DrawHunt();
        for (int i = 0; i < slotLabels.Length; i++) slotLabels[i].text = SkillName(Slot(i));
        lootRate.SetValueWithoutNotify(draft.lootRateAtMostPercent);
        PercentText(lootRateLabel, "Take drops of at most", draft.lootRateAtMostPercent);
        lootGears.SetIsOnWithoutNotify(draft.lootGears);
        lootConsumables.SetIsOnWithoutNotify(draft.lootConsumables);
        lootMaterials.SetIsOnWithoutNotify(draft.lootMaterials);
        lootAll.SetIsOnWithoutNotify(draft.lootAll);
        healthBelow.SetValueWithoutNotify(draft.hpPotionBelowPercent);
        PercentText(healthBelowLabel, "Drink for health below", draft.hpPotionBelowPercent);
        manaBelow.SetValueWithoutNotify(draft.mpPotionBelowPercent);
        PercentText(manaBelowLabel, "Drink for mana below", draft.mpPotionBelowPercent);
    }

    void DrawHunt()
    {
        bool on = AutoCombatClient.On;
        onOffLabel.text = on ? "Stop" : "Start";
        stateLabel.text = on ? "Hunting — press " + GameKeys.Label(GameKeys.AutoPilot) + " or move to stop."
                             : "Off. Press " + GameKeys.Label(GameKeys.AutoPilot) + " or Start to begin.";
        int mode = (int)draft.Hunt;
        for (int m = 0; m < huntButtons.Length; m++) UiKit.MarkTab(huntButtons[m], m == mode);
    }

    string Slot(int i) => draft.slots != null && i < draft.slots.Length ? draft.slots[i] : "";

    static string SkillName(string id)
    {
        if (string.IsNullOrEmpty(id)) return "None";
        var def = SkillCatalog.Get(id);
        return def != null && !string.IsNullOrEmpty(def.displayName) ? def.displayName : id;
    }

    static void PercentText(TMPro.TMP_Text label, string what, float percent)
        => label.text = what + ": " + Mathf.RoundToInt(percent) + "%";

    static void WholePercent(Slider s)
    {
        s.minValue = 0f;
        s.maxValue = 100f;
        s.wholeNumbers = true;
    }

    // ---- the skill list -------------------------------------------------------------------------------------------

    // Every skill this character may press — learned, or belonging to no weapon — and never a passive or an item's.
    // The server holds each slot to the same test whatever is sent.
    void OpenSkillPicker(int slot)
    {
        var me = Character.Instance;
        if (me == null) return;
        OpenPicker(130f);
        PickerChoice("None", () => SetSlot(slot, ""));
        var all = SkillCatalog.All;
        for (int i = 0; i < all.Count; i++)
        {
            var def = all[i];
            if (def == null || string.IsNullOrEmpty(def.id) || !me.MayBind(def.id)) continue;
            string id = def.id;
            PickerChoice(SkillName(id), () => SetSlot(slot, id));
        }
    }

    void SetSlot(int slot, string id)
    {
        if (draft.slots == null || draft.slots.Length != AutoCombatSettings.SkillSlots)
            draft = AutoCombatMath.Clamped(draft);
        draft.slots[slot] = id ?? "";
        slotLabels[slot].text = SkillName(id);
        Changed();
    }

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
        pickerGrid.cellSize = new Vector2(130, 26);
        pickerGrid.spacing = new Vector2(4, 4);
        var cancel = UiKit.Btn("Cancel", panel.transform, () => picker.SetActive(false));
        var crt = (RectTransform)cancel.transform;
        crt.anchorMin = crt.anchorMax = new Vector2(1f, 0f); crt.pivot = new Vector2(1f, 0f);
        crt.sizeDelta = new Vector2(90, 28); crt.anchoredPosition = new Vector2(-4, 4);
        picker.SetActive(false);
    }

    void OpenPicker(float choiceWidth)
    {
        for (int i = pickerList.childCount - 1; i >= 0; i--) Destroy(pickerList.GetChild(i).gameObject);
        pickerGrid.cellSize = new Vector2(choiceWidth, 26);
        picker.SetActive(true);
        picker.transform.SetAsLastSibling();
    }

    void PickerChoice(string label, System.Action pick)
    {
        UiKit.Btn(label, pickerList, () => { pick(); picker.SetActive(false); });
    }

    // ---- layout (the Settings window's pieces) --------------------------------------------------------------------

    static Button ChoiceButton(Transform row, string label, System.Action open)
    {
        var btn = UiKit.Btn(label, row, open);
        var rt = (RectTransform)btn.transform;
        rt.anchorMin = new Vector2(0.6f, 0f); rt.anchorMax = new Vector2(1f, 1f);
        rt.offsetMin = Vector2.zero; rt.offsetMax = new Vector2(-4f, 0f);
        return btn;
    }

    Transform AddTab(Transform tabRow, string label, Transform pageParent)
    {
        int index = pages.Count;
        var btn = UiKit.Btn(label, tabRow, () => Select(index));
        tabs.Add(btn);

        var page = UiKit.Img("Page_" + label, pageParent, new Color(0, 0, 0, 0));
        page.raycastTarget = false;
        UiKit.Fill(page.rectTransform, Vector2.zero, Vector2.zero);
        var vlg = page.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 4;
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

    static Transform Row(Transform page, string label, float labelWidth = 0.45f)
        => RowOf(page, label, labelWidth, out _);

    // A row whose name is redrawn with its value ("Take drops of at most: 30%").
    static Transform RowWithLabel(Transform page, out TMPro.TMP_Text label) => RowOf(page, "", 0.45f, out label);

    static Transform RowOf(Transform page, string label, float labelWidth, out TMPro.TMP_Text text)
    {
        var row = UiKit.Img("Row_" + label, page, new Color(0, 0, 0, 0));
        row.raycastTarget = false;
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 28;

        text = UiKit.Label("Label", row.transform, label, 15, FontStyle.Normal, TextAnchor.MiddleLeft);
        text.rectTransform.anchorMin = new Vector2(0f, 0f);
        text.rectTransform.anchorMax = new Vector2(labelWidth, 1f);
        text.rectTransform.offsetMin = new Vector2(4f, 0f);
        text.rectTransform.offsetMax = new Vector2(-6f, 0f);
        text.raycastTarget = false;
        return row.transform;
    }
}
