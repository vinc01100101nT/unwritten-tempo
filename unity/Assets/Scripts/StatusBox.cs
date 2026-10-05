using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The status box, top-left by default: a window whose blue bar carries the character's name, HP and MP, and whose
/// body shows the character level, the ring level and the held weapon's mastery level, each with its experience
/// bar, then the zeny. A button beside the zeny shows or hides a button for every HUD window. It drags by its bar,
/// minimises to its bar, and cannot be closed. It replaced the bars that ran along the bottom of the screen.
///
/// Every number on it is the one the server sent — it only draws them. It rebuilds nothing per frame: the text
/// is rewritten only when a number it shows actually moved.
/// </summary>
public class StatusBox : MonoBehaviour
{
    const float Width = 330f, RowHeight = 18f, RowGap = 5f, BarHeight = 12f, LabelWidth = 124f;
    const float ButtonHeight = 24f, ButtonGap = 4f, Pad = 10f;

    // The bars' fills — the colours they have always had: the character's green and the ring's blue; the mastery bar
    // takes the held weapon's own colour.
    static readonly Color CharFill = new Color(0.55f, 0.85f, 0.5f, 0.9f);
    static readonly Color RingFill = new Color(0.55f, 0.8f, 1f, 0.9f);

    // Every HUD window, by the name it carries, and the key that already opens it. Settings has no key of its own
    // and is opened directly.
    static readonly (string label, KeyCode key)[] Windows =
    {
        ("Inventory", GameKeys.Inventory), ("Character", GameKeys.CharacterWindow), ("Skills", GameKeys.Skills),
        ("Quests", GameKeys.Quests), ("Calendar", GameKeys.Calendar), ("Map", GameKeys.Map),
        ("Chat", GameKeys.Chat), ("Settings", KeyCode.None), ("System", GameKeys.System),
    };

    Character ch;
    bool built;
    RectTransform window;
    TMP_Text title, charLabel, ringLabel, masteryLabel, charName, ringName, masteryName, zeny;
    RectTransform charFill, ringFill, masteryFill;
    Image masteryFillImage;
    GameObject masteryRow, windowButtons;
    TMP_Text toggleGlyph;
    float collapsedHeight, expandedHeight;

    // change-detection: the text is only rewritten when a number it shows actually moves
    int lastHp = -1, lastMaxHp = -1, lastMp = -1, lastMaxMp = -1;
    int lastCharLvl = -1, lastCharToNext = -1, lastRingLvl = -1, lastGold = -1;
    double lastCharXp = -1.0, lastRingXp = -1.0;
    string lastName;
    WeaponType lastMasteryWeapon = (WeaponType)(-1);
    int lastMasteryLvl = -1;
    double lastMasteryInto = -1.0, lastMasterySize = -1.0;
    bool lastMasteryCapped, lastMasteryKnown;

    void Update()
    {
        if (!built) { TryBuild(); return; }
        if (ch == null) { Bind(); return; }
    }

    void TryBuild()
    {
        if (Character.Instance == null) return;
        Build();
        built = true;
        Bind();
    }

    void Build()
    {
        var border = UiKit.Img("Window_Status", transform, UiKit.WindowEdge);
        window = border.rectTransform;
        window.anchorMin = window.anchorMax = new Vector2(0f, 1f);
        window.pivot = new Vector2(0f, 1f);
        window.anchoredPosition = new Vector2(12f, -12f);
        border.gameObject.AddComponent<WindowFrame>().persistKey = "Status";
        UiKit.ClickShield(window);
        var content = UiKit.DressWindow(border, "", closable: false, minimizable: true, draggable: true);
        title = border.transform.Find("Fill/Header/Title").GetComponent<TMP_Text>();
        title.fontSize = 12;
        title.richText = true;

        // The content area starts under the bar with the window's own inset; the rows are laid top-down in it.
        float y = 0f;
        charFill = Row(content, y, CharFill, out charName, out charLabel, out _);
        y -= RowHeight + RowGap;
        ringFill = Row(content, y, RingFill, out ringName, out ringLabel, out _);
        y -= RowHeight + RowGap;
        masteryFill = Row(content, y, RingFill, out masteryName, out masteryLabel, out masteryRow);
        masteryFillImage = masteryFill.GetComponent<Image>();
        y -= RowHeight + RowGap;

        zeny = UiKit.MakeText("Zeny", content, "", 12, FontStyle.Normal, TextAnchor.MiddleRight, null, false);
        zeny.raycastTarget = false;
        Anchor(zeny.rectTransform, y, RowHeight, 0f, 28f);
        var toggle = UiKit.Btn("+", content, ToggleWindowButtons);
        var trt = (RectTransform)toggle.transform;
        trt.anchorMin = trt.anchorMax = new Vector2(1f, 1f); trt.pivot = new Vector2(1f, 1f);
        trt.sizeDelta = new Vector2(20f, RowHeight); trt.anchoredPosition = new Vector2(0f, y);
        toggleGlyph = toggle.GetComponentInChildren<TMP_Text>();
        y -= RowHeight + RowGap;

        // The window buttons, three to a row.
        windowButtons = new GameObject("WindowButtons", typeof(RectTransform));
        windowButtons.transform.SetParent(content, false);
        var wrt = (RectTransform)windowButtons.transform;
        int rows = (Windows.Length + 2) / 3;
        float gridHeight = rows * ButtonHeight + (rows - 1) * ButtonGap;
        Anchor(wrt, y, gridHeight, 0f, 0f);
        var grid = windowButtons.AddComponent<GridLayoutGroup>();
        float cellWidth = (Width - 2 * 16f - 2 * Pad - 2 * ButtonGap) / 3f;
        grid.cellSize = new Vector2(cellWidth, ButtonHeight);
        grid.spacing = new Vector2(ButtonGap, ButtonGap);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        foreach (var (label, key) in Windows)
        {
            var b = UiKit.Btn(label, windowButtons.transform, () => Open(label, key));
            var t = b.GetComponentInChildren<TMP_Text>();
            if (t != null)
            {
                t.fontSize = 12;
                if (key != KeyCode.None) t.text = $"{label} [{GameKeys.Label(key)}]";
            }
        }
        windowButtons.SetActive(false);

        // Content inset (UiKit.DressWindow): the bar plus 8 above, 14 below; the window's edge adds 4.
        collapsedHeight = UiKit.TitleBarHeight + 8f + (3 * (RowHeight + RowGap)) + RowHeight + 14f + 4f;
        expandedHeight = collapsedHeight + RowGap + gridHeight;
        window.sizeDelta = new Vector2(Width, collapsedHeight);
        border.gameObject.SetActive(true);
    }

    // A labelled bar: its name on the left ("Lv 12"), the bar filling the rest with "x / y" on it. Returns the fill,
    // left-anchored, whose width is the progress.
    RectTransform Row(Transform parent, float top, Color fillColor, out TMP_Text name, out TMP_Text numbers, out GameObject row)
    {
        row = new GameObject("Row", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        Anchor((RectTransform)row.transform, top, RowHeight, 0f, 0f);

        name = UiKit.MakeText("Name", row.transform, "", 12, FontStyle.Normal, TextAnchor.MiddleLeft, null, false);
        name.raycastTarget = false;
        var lrt = name.rectTransform;
        lrt.anchorMin = new Vector2(0f, 0f); lrt.anchorMax = new Vector2(0f, 1f); lrt.pivot = new Vector2(0f, 0.5f);
        lrt.sizeDelta = new Vector2(LabelWidth, 0f); lrt.anchoredPosition = Vector2.zero;

        var track = UiKit.Round(UiKit.Img("Track", row.transform, UiKit.Rule), UiKit.ButtonRadius);
        track.raycastTarget = false;
        var trt = track.rectTransform;
        trt.anchorMin = new Vector2(0f, 0.5f); trt.anchorMax = new Vector2(1f, 0.5f); trt.pivot = new Vector2(0.5f, 0.5f);
        trt.offsetMin = new Vector2(LabelWidth + 8f, -BarHeight * 0.5f); trt.offsetMax = new Vector2(0f, BarHeight * 0.5f);

        var fill = UiKit.Round(UiKit.Img("Fill", track.transform, fillColor), UiKit.ButtonRadius);
        fill.raycastTarget = false;
        var frt = fill.rectTransform;
        frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(0f, 1f); frt.pivot = new Vector2(0f, 0.5f);
        frt.offsetMin = frt.offsetMax = Vector2.zero;

        numbers = UiKit.MakeText("Numbers", track.transform, "", 10, FontStyle.Bold, TextAnchor.MiddleCenter, null, false);
        numbers.raycastTarget = false;
        UiKit.Fill(numbers.rectTransform, Vector2.zero, Vector2.zero);
        return frt;
    }

    static void Anchor(RectTransform rt, float top, float height, float left, float right)
    {
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(left, top - height); rt.offsetMax = new Vector2(-right, top);
    }

    void ToggleWindowButtons()
    {
        bool show = !windowButtons.activeSelf;
        windowButtons.SetActive(show);
        toggleGlyph.text = show ? "−" : "+";
        window.sizeDelta = new Vector2(Width, show ? expandedHeight : collapsedHeight);
    }

    // Opens a window through the key that already opens it — a one-shot press of that key — so the
    // status box adds no second way into any window.
    void Open(string label, KeyCode key)
    {
        if (key != KeyCode.None) { Hotkeys.PressOnScreen(key); return; }
        var settings = GetComponent<SettingsWindow>();
        if (settings != null) settings.Open();
    }

    void Bind()
    {
        if (ch != null) return;
        ch = Character.Instance;
        if (ch == null) return;
        ch.OnStatsChanged += Refresh;
        ch.OnHealthChanged += Refresh;
        ch.OnManaChanged += Refresh;
        ch.Ring.OnChanged += Refresh;
        ch.Mastery.OnChanged += Refresh;
        Wallet.OnChanged += OnGold;
        Refresh();
    }

    void OnDestroy()
    {
        Wallet.OnChanged -= OnGold;
        if (ch == null) return;
        ch.OnStatsChanged -= Refresh;
        ch.OnHealthChanged -= Refresh;
        ch.OnManaChanged -= Refresh;
        ch.Ring.OnChanged -= Refresh;
        ch.Mastery.OnChanged -= Refresh;
    }

    void OnGold(int _) => Refresh();

    void Refresh()
    {
        if (ch == null || title == null) return;

        string who = AccountSession.CharacterName ?? "";
        if (who != lastName || ch.CurrentHP != lastHp || ch.MaxHP != lastMaxHp || ch.CurrentMP != lastMp || ch.MaxMP != lastMaxMp)
        {
            lastName = who; lastHp = ch.CurrentHP; lastMaxHp = ch.MaxHP; lastMp = ch.CurrentMP; lastMaxMp = ch.MaxMP;
            title.text = $"<b>{who}</b>   HP {ch.CurrentHP} / {ch.MaxHP}   MP {ch.CurrentMP} / {ch.MaxMP}";
        }

        if (ch.Level != lastCharLvl || ch.XP != lastCharXp || ch.XPToNext != lastCharToNext)
        {
            lastCharLvl = ch.Level; lastCharXp = ch.XP; lastCharToNext = ch.XPToNext;
            SetFill(charFill, ch.XPToNext > 0 ? (float)(ch.XP / ch.XPToNext) : 0f);
            charLabel.text = $"{ch.XP:0.00} / {ch.XPToNext}";
            charName.text = "Lv " + ch.Level;
        }

        var r = ch.Ring;
        if (r.Level != lastRingLvl || r.XP != lastRingXp)
        {
            lastRingLvl = r.Level; lastRingXp = r.XP;
            SetFill(ringFill, r.XPToNext > 0 ? (float)(r.XP / r.XPToNext) : 0f);
            ringLabel.text = $"{r.XP:0.00} / {r.XPToNext}";
            ringName.text = "Ring Lv " + r.Level;
        }

        if (Wallet.Gold != lastGold)
        {
            lastGold = Wallet.Gold;
            zeny.text = Wallet.Gold.ToString("N0") + " z";
        }

        RefreshMastery();
    }

    // The held weapon's mastery, drawn from the level and progress the server sent — nothing worked out here.
    void RefreshMastery()
    {
        var weapon = ch.HeldWeapon;
        int level = ch.Mastery.SentLevel(weapon);
        bool known = ch.Mastery.SentProgress(weapon, out double into, out double size, out bool capped);
        if (weapon == lastMasteryWeapon && level == lastMasteryLvl && known == lastMasteryKnown
            && into == lastMasteryInto && size == lastMasterySize
            && capped == lastMasteryCapped) return;
        lastMasteryWeapon = weapon; lastMasteryLvl = level; lastMasteryKnown = known;
        lastMasteryInto = into; lastMasterySize = size; lastMasteryCapped = capped;

        bool show = weapon != WeaponType.None && known;
        masteryRow.SetActive(show);
        if (!show) return;

        masteryFillImage.color = WeaponColors.Load()?.Lookup(weapon) ?? RingFill;
        masteryName.text = $"{weapon} Mastery Lv {level}";   // perf-ok: gated by the early return above
        if (capped)
        {
            SetFill(masteryFill, 1f);
            masteryLabel.text = "capped by this weapon's grade";
        }
        else
        {
            SetFill(masteryFill, size > 0.0 ? (float)(into / size) : 0f);
            masteryLabel.text = $"{into:0.00} / {size:0}";   // perf-ok: gated by the early return above
        }
    }

    static void SetFill(RectTransform fill, float fraction)
    {
        fill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
    }
}
