using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Preset = WeatherDirector.Preset;

/// <summary>
/// The full browsable CALENDAR window (toggled with the L key / by clicking the top-right date widget).
/// One page = one real month (GMT+8), laid out as a week-per-row grid (Sun … Sat) of day-cells.
/// Each cell shows its day of the month and that day's weather tag ("Sunny / Rainy / Snow / …"), coloured by
/// weather; <b>today</b> is highlighted. Prev/Next arrows step a month; "Today" snaps back to this month.
///
/// <para>Every cell's weather comes from <see cref="WeatherDirector.PresetForDay"/> — the same deterministic
/// (worldSeed, dayIndex) hash the live sky uses — so the calendar shows real <b>past and future</b> weather
/// with no storage and every client agrees.</para>
///
/// <para>Mirrors <see cref="StatPanel"/>: lives on the persistent GameUICanvas, builds its widgets at runtime
/// into the "CalendarPanel/Content" that <see cref="UIShellBuilder"/> created. Per-frame cost while open is a
/// single day-index compare; the cells are rebuilt only on open, navigation, or a day rollover — never per
/// frame — so there's no steady-state string/GC churn.</para>
/// </summary>
public class CalendarPanel : MonoBehaviour
{
    // Six weeks of seven days: the most rows any month can touch.
    const int Columns = 7, Days = 42;
    static readonly string[] WeekdayShort = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };

    [Header("Weather icons — Ninja Adventure 'Meteo' set, assigned by UIShellBuilder at bake time")]
    public Sprite sunIcon;       // Clear (and dimmed for Cloudy)
    public Sprite rainIcon;      // LightRain / Thunderstorm
    public Sprite snowIcon;      // Snow

    GameObject panel;            // the CalendarPanel root (toggled by PanelToggle / the widget button)
    TMP_Text titleText;
    Button prevBtn, nextBtn;
    readonly Image[] cellBg     = new Image[Days];
    readonly Outline[] cellRing = new Outline[Days];
    readonly Image[] cellIcon   = new Image[Days];
    readonly TMP_Text[] cellDay = new TMP_Text[Days];
    readonly TMP_Text[] cellTag = new TMP_Text[Days];

    int viewYear, viewMonth;     // the month on the page
    long lastTodayShown = long.MinValue;
    bool built, wasActive;

    void Start() => TryBuild();

    void Update()
    {
        if (!built) { TryBuild(); return; }

        // L = toggle, Esc = close — owned here in code (no PanelToggle, no rebake to change the key).
        if (Hotkeys.Down(GameKeys.Calendar))
        {
            bool open = UiAnim.Toggle(panel);
            if (open) UISounds.PlayOpen(); else UISounds.PlayClose();
        }
        else if (UiAnim.IsOpen(panel) && Hotkeys.CancelDown())
        {
            UiAnim.Close(panel);
            UISounds.PlayClose();
            Hotkeys.MarkCancelHandled();
        }

        bool active = UiAnim.IsOpen(panel);
        if (active && !wasActive) GoToToday();           // every time it opens, snap to this month
        wasActive = active;

        // If the in-game day rolls over while the window is open, move the "today" highlight.
        if (active)
        {
            var cal = GameCalendar.Instance;
            if (cal != null && cal.DayIndex != lastTodayShown) Refresh();
        }
    }

    // ── Build (once, into the panel UIShellBuilder made) ─────────────────────────────────────────────────────────
    void TryBuild()
    {
        var window = FindWindow();
        // UiKit.DressWindow puts the content area inside the window's body ("Fill").
        var content = window != null ? window.Find("Fill/Content") as RectTransform : null;
        if (content == null) return;
        panel = window.gameObject;
        UiKit.ClickShield(panel.transform as RectTransform);   // catch clicks over the whole window (no leak to world)

        // Header: [<]  Month Year  [Today]  [>]
        var header = UiKit.Img("Image", content, new Color(0, 0, 0, 0)).rectTransform;
        header.name = "Header";
        header.anchorMin = new Vector2(0, 1); header.anchorMax = new Vector2(1, 1);
        header.pivot = new Vector2(0.5f, 1);
        header.sizeDelta = new Vector2(0, 34);
        header.anchoredPosition = Vector2.zero;
        var hl = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.spacing = 6; hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = false;   // keep button preferredWidths; the title's flexibleWidth eats the slack
        hl.childForceExpandHeight = true; hl.childAlignment = TextAnchor.MiddleCenter;

        prevBtn = NewButton(header, "<", 40, () => Shift(-1));
        titleText = NewText(header, "Month", 20, FontStyle.Bold, TextAnchor.MiddleCenter);
        titleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        NewButton(header, "Today", 72, GoToToday);
        nextBtn = NewButton(header, ">", 40, () => Shift(1));

        // The weekday names over the columns, laid out exactly like the grid below them.
        var namesGo = UiKit.Img("Image", content, new Color(0, 0, 0, 0)).rectTransform;
        namesGo.name = "Weekdays";
        namesGo.anchorMin = new Vector2(0, 1); namesGo.anchorMax = new Vector2(1, 1);
        namesGo.pivot = new Vector2(0.5f, 1);
        namesGo.sizeDelta = new Vector2(0, 18);
        namesGo.anchoredPosition = new Vector2(0, -40);
        var names = namesGo.gameObject.AddComponent<GridLayoutGroup>();
        names.cellSize = new Vector2(CellWidth, 18);
        names.spacing = new Vector2(CellGap, 0);
        names.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        names.constraintCount = Columns;
        names.childAlignment = TextAnchor.MiddleCenter;
        for (int i = 0; i < Columns; i++)
        {
            var n = NewText(namesGo, WeekdayShort[i], 12, FontStyle.Bold, TextAnchor.MiddleCenter);
            UiKit.Paint(n, UiKit.TextDim);
        }

        // Grid: one row per week, filling the area under the weekday names.
        var gridGo = UiKit.Img("Image", content, new Color(0, 0, 0, 0)).rectTransform;
        gridGo.name = "Grid";
        gridGo.anchorMin = Vector2.zero; gridGo.anchorMax = Vector2.one;
        gridGo.offsetMin = new Vector2(0, 0); gridGo.offsetMax = new Vector2(0, -62);   // sit below the weekday names
        var grid = gridGo.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(CellWidth, CellHeight);
        grid.spacing = new Vector2(CellGap, CellGap);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Columns;
        grid.childAlignment = TextAnchor.UpperCenter;

        for (int i = 0; i < Days; i++) BuildCell(gridGo, i);

        WireWidgetButton();

        built = true;
        GoToToday();
    }

    // Make the small top-right date/weather widget (HUD_CalendarPanel) a click-to-open button for this window,
    // so it works alongside the L hotkey. The child label keeps its own CalendarWeatherHUD readout untouched.
    void WireWidgetButton()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "HUD_CalendarPanel") continue;
            var img = t.GetComponent<Image>();
            if (img == null) break;
            img.raycastTarget = true;                       // it was informational-only until now
            if (t.GetComponent<Button>() != null) break;    // already wired (rebuilt prefab)
            var btn = t.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(() =>
            {
                bool nowOpen = UiAnim.Toggle(panel);
                if (nowOpen) UISounds.PlayOpen(); else UISounds.PlayClose();
            });
            break;
        }
    }

    const float CellWidth = 70f, CellHeight = 50f, CellGap = 5f;

    void BuildCell(Transform parent, int i)
    {
        var bg = UiKit.Img("Image", parent, UiKit.PanelRaise);
        bg.name = "Cell" + i;
        // A coloured ring we light up only for "today" (kept transparent otherwise — no per-refresh add/remove).
        var ring = bg.gameObject.AddComponent<Outline>();
        ring.effectColor = new Color(0, 0, 0, 0);
        ring.effectDistance = new Vector2(2, -2);

        var day = NewText(bg.transform, "1", 12, FontStyle.Bold, TextAnchor.UpperLeft);
        var drt = day.rectTransform;
        drt.anchorMin = new Vector2(0, 1); drt.anchorMax = new Vector2(1, 1); drt.pivot = new Vector2(0.5f, 1);
        drt.sizeDelta = new Vector2(-8, 16); drt.anchoredPosition = new Vector2(0, -3);

        // The weather icon sits at the cell's right (sprite + tint set per day in Refresh).
        var icon = UiKit.Img("Image", bg.transform, Color.white);
        icon.preserveAspect = true; icon.raycastTarget = false;
        var irt = icon.rectTransform;
        irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(1, 1);
        irt.sizeDelta = new Vector2(20, 20); irt.anchoredPosition = new Vector2(-4, -3);

        var tag = NewText(bg.transform, "Sunny", 11, FontStyle.Bold, TextAnchor.LowerCenter);
        var trt = tag.rectTransform;
        trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(1, 0); trt.pivot = new Vector2(0.5f, 0);
        trt.sizeDelta = new Vector2(0, 16); trt.anchoredPosition = new Vector2(0, 4);

        cellBg[i] = bg; cellRing[i] = ring; cellIcon[i] = icon; cellDay[i] = day; cellTag[i] = tag;
    }

    // ── Navigation ───────────────────────────────────────────────────────────────────────────────────────────────
    void Shift(int months)
    {
        RealCalendar.StepMonth(ref viewYear, ref viewMonth, months);
        // Not before the first month the world's calendar counts from.
        var first = RealCalendar.DateOf(0);
        if (viewYear < first.Year || (viewYear == first.Year && viewMonth < first.Month)) { viewYear = first.Year; viewMonth = first.Month; }
        Refresh();
    }

    void GoToToday()
    {
        var cal = GameCalendar.Instance;
        if (cal == null) return;
        viewYear = cal.Date.Year;
        viewMonth = cal.Date.Month;
        Refresh();
    }

    // ── Paint the cells for the month on the page ────────────────────────────────────────────────────────────────
    void Refresh()
    {
        var cal = GameCalendar.Instance;
        var wx  = WeatherDirector.Instance;
        if (cal == null) return;

        long today = cal.DayIndex;
        lastTodayShown = today;

        titleText.text = RealCalendar.MonthTitle(viewYear, viewMonth);
        var first = RealCalendar.DateOf(0);
        if (prevBtn != null) prevBtn.interactable = viewYear > first.Year || viewMonth > first.Month;

        var monthStart = new DateTime(viewYear, viewMonth, 1);
        long firstDay = RealCalendar.DayOf(monthStart);
        int lead = (int)monthStart.DayOfWeek;
        int length = DateTime.DaysInMonth(viewYear, viewMonth);

        for (int i = 0; i < Days; i++)
        {
            int dayOfMonth = i - lead + 1;
            if (dayOfMonth < 1 || dayOfMonth > length)
            {
                cellDay[i].text = "";
                cellTag[i].text = "";
                cellIcon[i].enabled = false;
                cellBg[i].color = new Color(0, 0, 0, 0);
                cellRing[i].effectColor = new Color(0, 0, 0, 0);
                continue;
            }

            long day = firstDay + dayOfMonth - 1;
            Preset p = wx != null ? wx.PresetForDay(day) : Preset.Clear;
            Color c  = WeatherDirector.ColorOf(p);
            bool isToday = day == today;

            cellDay[i].text  = isToday ? dayOfMonth + " ★" : dayOfMonth.ToString();
            cellTag[i].text  = WeatherDirector.TagOf(p);
            UiKit.PaintReadable(cellTag[i], c);
            // Weather icon (sprite + tint). Hide gracefully if the art wasn't assigned (e.g. pre-rebake).
            var (sprite, iconTint) = IconFor(p);
            if (sprite != null) { cellIcon[i].sprite = sprite; cellIcon[i].color = iconTint; cellIcon[i].enabled = true; }
            else cellIcon[i].enabled = false;
            // Tinted background; today gets a brighter wash + a coloured ring so it pops.
            cellBg[i].color    = new Color(c.r, c.g, c.b, isToday ? 0.28f : 0.10f);
            cellRing[i].effectColor = isToday ? new Color(c.r, c.g, c.b, 0.95f) : new Color(0, 0, 0, 0);
            UiKit.Paint(cellDay[i], isToday ? UiKit.TitleText : UiKit.TextMain);
        }
    }

    // Icon sprite + tint per weather. The pack ships clean Sun/Rain/Snow pixel icons but no Cloud/Storm,
    // so the gaps reuse the closest icon with a wash — overcast = dimmed Sun, storm = violet-washed Rain — and
    // the coloured text tag underneath names each one exactly.
    (Sprite sprite, Color tint) IconFor(Preset p)
    {
        switch (p)
        {
            case Preset.Clear:        return (sunIcon,  Color.white);
            case Preset.Cloudy:       return (sunIcon,  new Color(0.62f, 0.65f, 0.70f));   // dimmed = overcast
            case Preset.LightRain:    return (rainIcon, Color.white);
            case Preset.Thunderstorm: return (rainIcon, new Color(0.80f, 0.74f, 1.00f));   // violet storm wash
            case Preset.Snow:         return (snowIcon, Color.white);
            default:                  return (sunIcon,  Color.white);
        }
    }

    // ── Wiring + helpers ─────────────────────────────────────────────────────────────────────────────────────────

    Transform FindWindow()
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == "CalendarPanel") return t;
        return null;
    }

    // The game's one button, plus the click sound and the width this header gives it.
    Button NewButton(Transform parent, string label, float width, Action onClick)
    {
        var btn = UiKit.Btn(label, parent, () => { onClick(); UISounds.PlayClick(); });
        btn.gameObject.AddComponent<LayoutElement>().preferredWidth = width;
        return btn;
    }

    TMP_Text NewText(Transform parent, string str, int size, FontStyle style, TextAnchor anchor)
    {
        var t = UiKit.MakeText("Text", parent, str, size, style, anchor, wrap: false);
        UiKit.Paint(t, UiKit.TextMain);
        return t;
    }
}
