using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ONE FLOATING PLATE over one unit, stacked, sitting just clear of its head:
/// <list type="number">
/// <item>its name — a creature's portrait on its left, its element on its right as a coloured tile with its letters</item>
/// <item>a smaller line under the name saying what it is — a townsperson's trade, say — for a unit that has one</item>
/// <item>a row of small tiles — the buffs and debuffs running on the unit, each wearing the face the buff tray gives
/// it (<see cref="StateFace"/>) — for the unit whose states the owner hands in</item>
/// <item>a health bar — left off for a unit with no health to show, a townsperson, whose plate is its name alone</item>
/// <item>a mana bar under it, for a unit that has mana</item>
/// </list>
///
/// <para><b>Drawing only.</b> Whoever owns a plate decides which unit it rides and hands it the numbers the server
/// sent; nothing here works a number out. <see cref="HoverPlate"/> owns one and points it at whatever is under the
/// cursor, and <see cref="CharacterPlates"/> owns one per person on screen and keeps it up — so a creature's plate and
/// a person's are the same widget, drawn by the same code.</para>
///
/// <para><b>The bars are as wide as the body they belong to</b> (<see cref="UnitAnchor.HalfWidth"/>), so a big
/// creature gets a long bar and a small one a short bar, and the plate always reads as belonging to the thing under
/// it.</para>
///
/// <para>Every plate hangs under the ONE shared world-space canvas (<see cref="MobNameplate.SharedHost"/>) — never a
/// canvas of its own. A map change destroys that canvas and every plate on it; <see cref="Ensure"/> builds a fresh
/// one and says so, and the owner hands it everything again.</para>
///
/// <para>PERFORMANCE (see PERFORMANCE.md): per frame a plate only follows its unit and faces the camera. Numbers are
/// rewritten when they move, and the layout when a part appears or the width changes.</para>
/// </summary>
public sealed class UnitPlate
{
    // World size of one UI pixel — matched to CastBar so the two bars over a unit read at the same weight.
    const float WorldScale = 0.0075f;

    const float NameH = 13f;
    const float DescriptionH = 9f;
    // The line under the name is drawn this much smaller than the name, so the two read as a name and its caption.
    const float DescriptionScale = 0.72f;
    const float HpBarH = 9f;
    const float MpBarH = 6f;
    const float RowGap = 1.5f;

    // A unit's element sits beside its name as a small coloured tile.
    const float ElementSize = 11f;
    const float ElementGap = 2f;

    // A running buff or debuff: a tile the size of the element tile, wrapped into as many rows as the bar's width needs.
    const float StateSize = 11f;
    const float StateGap = 1f;

    // The narrowest a bar may be drawn, in UI pixels. A body width is honoured above this; below it the bar would
    // be a couple of pixels of nothing, which reads as a glitch rather than as a small creature.
    const float MinBarW = 24f;

    static readonly Color TrackBg = new Color(0.06f, 0.05f, 0.07f, 0.85f);
    static readonly Color MpColor = new Color(0.35f, 0.55f, 0.95f);

    readonly string objectName;

    RectTransform root;
    Image hpFill, mpTrack, mpFill;
    TextMeshProUGUI nameLabel;
    TextMeshProUGUI descriptionLabel;
    bool hasDescription;
    // How far down the name row starts: below the buffs and debuffs, when there are any.
    float nameY;
    // A quest mark over the name — "!" or "?" (see QuestMarks) — or nothing.
    TextMeshProUGUI markLabel;
    string shownMark = "";
    const float MarkH = 18f;
    Image elementTile;
    TextMeshProUGUI elementText;
    // The creature's face, left of its name — see CreaturePortraits. Hidden for anyone without one.
    Image portrait;
    bool portraitShown;
    bool elementShown;
    Element shownElement;
    bool shown;

    // What is currently drawn, so a unit standing at steady health costs nothing.
    int lastHP = int.MinValue;
    int lastMaxHP = int.MinValue;
    int lastMP = int.MinValue;
    int lastMaxMP = int.MinValue;

    // The buffs and debuffs row. The store is listened to only while it is handed in; tiles are pooled and redrawn
    // when the store changes or the soonest one shown runs out — never per frame.
    sealed class StateTile { public Image back, icon; public TextMeshProUGUI letters, count; public int shownCount = -1; }
    RectTransform stateRow;
    readonly System.Collections.Generic.List<StateTile> stateTiles = new System.Collections.Generic.List<StateTile>(4);
    UnitStates stateStore;
    readonly System.Action onStatesChanged;
    bool statesDirty;
    int stateCount;
    float statesNextExpiry = float.PositiveInfinity;

    float barW = MinBarW;
    bool hasMana;
    // False for a unit with no health to show — a townsperson — whose plate is its name and nothing else.
    bool hasBars = true;

    /// <param name="objectName">What the plate's object is called in the scene, so a person reading the hierarchy
    /// can tell one owner's plates from another's.</param>
    public UnitPlate(string objectName)
    {
        this.objectName = objectName;
        onStatesChanged = () => statesDirty = true;
    }

    public bool Shown => shown && root != null;

    /// <summary>The health bar's own object — what the tutorial guide may point at. Null until the plate is built.</summary>
    public GameObject HealthBar => hpFill != null ? hpFill.rectTransform.parent.gameObject : null;

    /// <summary>Build the widget when it is not there — the first use, or after a map change took the shared canvas
    /// and this plate with it. True when it had to be built: everything it showed is gone, and the owner hands it
    /// the unit again.</summary>
    public bool Ensure()
    {
        if (root != null) return false;
        Build();
        return true;
    }

    public void Show(bool on)
    {
        if (root == null || on == shown) return;
        shown = on;
        root.gameObject.SetActive(on);
    }

    /// <summary>Ride just clear of <paramref name="unit"/>'s head and face the camera — the same follow every
    /// floating widget uses.</summary>
    public void Follow(Transform unit, Quaternion facing)
    {
        root.position = UnitAnchor.Overhead(unit);
        root.rotation = facing;
    }

    /// <summary>Point the plate at a new unit: its body's half-width (<see cref="UnitAnchor.HalfWidth"/>), its name,
    /// and its face or null. Everything drawn before is forgotten, so the next numbers land whatever they are.</summary>
    public void Begin(float halfWidth, string name, Sprite face)
    {
        barW = Mathf.Max(MinBarW, 2f * halfWidth / WorldScale);
        nameLabel.text = name;
        portraitShown = face != null;
        portrait.sprite = face;
        portrait.gameObject.SetActive(portraitShown);

        lastHP = lastMaxHP = lastMP = lastMaxMP = int.MinValue;
        Layout();
    }

    /// <summary>The body's half-width changed (a model finished loading, a costume changed): the bars follow it.</summary>
    public void SetWidth(float halfWidth)
    {
        float w = Mathf.Max(MinBarW, 2f * halfWidth / WorldScale);
        if (Mathf.Approximately(w, barW)) return;
        barW = w;
        Layout();
    }

    /// <summary>A new name — a creature's level moved, say. Called on change only.</summary>
    public void SetName(string name)
    {
        nameLabel.text = name;
        PlaceNameRow();
    }

    /// <summary>Draw the health and mana bars, or the name alone for a unit with no health to show. Kept across a
    /// rebuild: it is what the plate is for, not something it was showing.</summary>
    public void SetBars(bool on)
    {
        if (on == hasBars) return;
        hasBars = on;
        lastHP = lastMaxHP = lastMP = lastMaxMP = int.MinValue;
        Layout();
    }

    /// <summary>The smaller line under the name saying what the unit is, or empty for none.</summary>
    public void SetDescription(string text)
    {
        bool want = !string.IsNullOrEmpty(text);
        descriptionLabel.text = want ? text : "";
        if (want == hasDescription) return;
        hasDescription = want;
        Layout();
    }

    /// <summary>The quest mark above the name — "!" or "?" — or empty for none. Rewritten only when it changes.</summary>
    public void SetMark(string mark)
    {
        mark ??= "";
        if (markLabel == null || mark == shownMark) return;
        shownMark = mark;
        markLabel.text = mark;
        markLabel.gameObject.SetActive(mark.Length > 0);
    }

    /// <summary>The element tile beside the name, or none.</summary>
    public void SetElement(bool show, Element el)
    {
        if (show == elementShown && el == shownElement) return;
        elementShown = show;
        shownElement = el;
        elementTile.gameObject.SetActive(show);
        if (show)
        {
            elementTile.color = Elements.Tint(el);
            elementText.text = Elements.Symbol(el);
        }
        PlaceNameRow();
    }

    public void SetHealth(int current, int max)
    {
        if (current == lastHP && max == lastMaxHP) return;
        lastHP = current;
        lastMaxHP = max;
        float frac = Mathf.Clamp01((float)current / Mathf.Max(1, max));
        hpFill.fillAmount = frac;
        hpFill.color = HpColor(frac);
    }

    /// <summary>The mana bar. A unit with no mana pool gets no bar at all, rather than an empty one that looks broken;
    /// one whose pool arrives later gets its bar then.</summary>
    public void SetMana(int current, int max)
    {
        bool want = max > 0;
        if (want != hasMana)
        {
            hasMana = want;
            lastMP = lastMaxMP = int.MinValue;
            Layout();
        }
        if (!hasMana || (current == lastMP && max == lastMaxMP)) return;
        lastMP = current;
        lastMaxMP = max;
        mpFill.fillAmount = Mathf.Clamp01((float)current / Mathf.Max(1, max));
    }

    /// <summary>Draw the buffs and debuffs held in <paramref name="store"/> — what the server sent about this unit —
    /// or none for null. Safe to call every frame: it redraws only when the store changes or a shown one runs out.
    /// Hand in null before letting go of a plate, so it stops listening to a store that is no longer its unit's.</summary>
    public void ShowStates(UnitStates store)
    {
        if (store != stateStore)
        {
            if (stateStore != null) stateStore.OnChanged -= onStatesChanged;
            stateStore = store;
            if (stateStore != null) stateStore.OnChanged += onStatesChanged;
            statesDirty = true;
        }
        if (root == null || (!statesDirty && Time.time < statesNextExpiry)) return;
        statesDirty = false;
        DrawStates();
    }

    void DrawStates()
    {
        int n = 0;
        float now = Time.time;
        statesNextExpiry = float.PositiveInfinity;
        if (stateStore != null)
        {
            // Reading the list sweeps out what has run out on this copy; a state the server let go of early is gone
            // with the next message it sends.
            var list = stateStore.Active;
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.endTime <= now) continue;
                PaintState(StateTileAt(n++), s);
                if (s.endTime < statesNextExpiry) statesNextExpiry = s.endTime;
            }
        }
        for (int i = n; i < stateTiles.Count; i++)
            if (stateTiles[i].back.gameObject.activeSelf) stateTiles[i].back.gameObject.SetActive(false);

        if (n != stateCount) { stateCount = n; Layout(); }
        else PlaceStateTiles();
    }

    StateTile StateTileAt(int i)
    {
        while (stateTiles.Count <= i)
        {
            var back = NewImage("State", stateRow, Color.white);
            var icon = NewImage("Icon", back.rectTransform, Color.white);
            icon.preserveAspect = true;
            StretchFull(icon.rectTransform);
            var letters = NewText("Letters", back.rectTransform, 6);
            letters.enableAutoSizing = true;
            letters.fontSizeMin = 3f;
            letters.fontSizeMax = 6f;
            StretchFull(letters.rectTransform);
            // The stack count in the tile's lower-right corner, blank at one stack — the buff tray's own rule.
            var count = NewText("Count", back.rectTransform, 5);
            count.alignment = TextAlignmentOptions.BottomRight;
            count.color = BuffBarPanel.StackColor;
            StretchFull(count.rectTransform);
            stateTiles.Add(new StateTile { back = back, icon = icon, letters = letters, count = count });
        }
        return stateTiles[i];
    }

    static void PaintState(StateTile t, UnitStates.State s)
    {
        if (!t.back.gameObject.activeSelf) t.back.gameObject.SetActive(true);
        t.back.color = StateFace.Back(s);
        var sprite = StateFace.Icon(s);
        bool pictured = sprite != null;
        t.icon.gameObject.SetActive(pictured);
        t.letters.gameObject.SetActive(!pictured);
        if (pictured) { t.icon.sprite = sprite; t.icon.color = StateFace.Tint(s); }
        else { t.letters.text = StateFace.Letters(s); t.letters.color = StateFace.Tint(s); }
        int n = s.StackCount;
        if (n != t.shownCount)
        {
            t.count.text = n >= 0 && n < BuffBarPanel.StackLabels.Length ? BuffBarPanel.StackLabels[n] : "";
            t.shownCount = n;
        }
    }

    int StatesPerRow => Mathf.Max(1, Mathf.FloorToInt((barW + StateGap) / (StateSize + StateGap)));

    // The tiles centred row by row inside the states row. Runs on a redraw or a layout — never per frame.
    void PlaceStateTiles()
    {
        int perRow = StatesPerRow;
        for (int i = 0; i < stateCount; i++)
        {
            int row = i / perRow, col = i % perRow;
            int inRow = Mathf.Min(perRow, stateCount - row * perRow);
            float x = (col - (inRow - 1) * 0.5f) * (StateSize + StateGap);   // server-math: a tile's place in screen pixels, not a game number
            Place(stateTiles[i].back.rectTransform, new Vector2(x, -row * (StateSize + StateGap)),
                  new Vector2(StateSize, StateSize));
        }
    }

    // Green when healthy, through yellow, to red when nearly dead — the same reading as the target panel's bar,
    // because a player should not have to learn two colour languages for one number.
    static Color HpColor(float f)
    {
        var green = new Color(0.40f, 0.85f, 0.30f);
        var yellow = new Color(0.95f, 0.80f, 0.20f);
        var red = new Color(0.88f, 0.20f, 0.20f);
        return f > 0.5f ? Color.Lerp(yellow, green, (f - 0.5f) * 2f) : Color.Lerp(red, yellow, f * 2f);
    }

    // ---- the widget ---------------------------------------------------------------------------------------

    void Build()
    {
        // A MAP CHANGE DESTROYS THE SHARED HOST, and this plate with it, so a rebuild starts from nothing: every
        // "already showing" note describes a widget that no longer exists.
        elementShown = false;
        shownElement = Element.Neutral;
        hasDescription = false;
        portraitShown = false;
        hasMana = false;
        shown = false;
        lastHP = lastMaxHP = lastMP = lastMaxMP = int.MinValue;
        stateTiles.Clear();
        stateCount = 0;
        statesDirty = true;

        root = NewRect(objectName, MobNameplate.SharedHost);
        root.localScale = Vector3.one * WorldScale;

        // Anchored at the BOTTOM, so the whole stack sits above the unit's head and grows upward as rows are added
        // rather than creeping down over their face.
        root.pivot = new Vector2(0.5f, 0f);

        nameLabel = NewText("Name", root, 11);
        nameLabel.alignment = TextAlignmentOptions.Center;
        nameLabel.fontStyle = FontStyles.Bold;
        nameLabel.fontSharedMaterial = UiKit.WorldNameMaterial();

        descriptionLabel = NewText("Description", root, 8);
        descriptionLabel.alignment = TextAlignmentOptions.Center;
        descriptionLabel.fontSharedMaterial = UiKit.WorldNameMaterial();
        descriptionLabel.gameObject.SetActive(false);

        markLabel = NewText("QuestMark", root, 16);
        markLabel.fontStyle = FontStyles.Bold;
        markLabel.color = QuestMarks.Color;
        markLabel.fontSharedMaterial = UiKit.WorldNameMaterial();
        Place(markLabel.rectTransform, new Vector2(0f, MarkH + 2f), new Vector2(MarkH * 2f, MarkH));
        markLabel.gameObject.SetActive(false);
        shownMark = "";

        // The element tile: a plain coloured square with its letters on top.
        elementTile = NewImage("Element", root, Color.white);
        Place(elementTile.rectTransform, Vector2.zero, new Vector2(ElementSize, ElementSize));
        elementText = NewText("Symbol", elementTile.rectTransform, 7);
        StretchFull(elementText.rectTransform);
        elementTile.gameObject.SetActive(false);

        portrait = NewImage("Portrait", root, Color.white);
        portrait.preserveAspect = true;
        Place(portrait.rectTransform, Vector2.zero, new Vector2(NameH, NameH));
        portrait.gameObject.SetActive(false);

        stateRow = NewRect("States", root);
        stateRow.gameObject.SetActive(false);

        var hpTrack = NewImage("HpTrack", root, TrackBg);
        hpFill = NewBarFill("HpFill", hpTrack.rectTransform, HpColor(1f));
        FillInside(hpFill.rectTransform);

        mpTrack = NewImage("MpTrack", root, TrackBg);
        mpFill = NewBarFill("MpFill", mpTrack.rectTransform, MpColor);
        FillInside(mpFill.rectTransform);

        root.gameObject.SetActive(false);
        Layout();
    }

    // Everything's size and place: the bars at this unit's width, and the mana bar when it has one. Runs when a part
    // appears or goes or the width changes — never per frame.
    void Layout()
    {
        if (root == null) return;

        bool mana = hasBars && hasMana;

        // The buffs and debuffs sit on top, above the name, so the name stays right over the bars it names.
        nameY = 0f;
        int stateRows = stateCount == 0 ? 0 : (stateCount + StatesPerRow - 1) / StatesPerRow;
        if (stateRow.gameObject.activeSelf != stateRows > 0) stateRow.gameObject.SetActive(stateRows > 0);
        if (stateRows > 0)
        {
            float rowsH = stateRows * StateSize + (stateRows - 1) * StateGap;   // server-math: screen pixels, not a game number
            Place(stateRow, Vector2.zero, new Vector2(barW, rowsH));
            nameY = rowsH + RowGap;
            PlaceStateTiles();
        }
        float top = nameY + (hasDescription ? NameH + DescriptionH : NameH);

        // The plate is only as tall as what it is showing.
        float h = top + (hasBars ? RowGap + HpBarH : 0f) + (mana ? RowGap + MpBarH : 0f);   // server-math: the plate's height in screen pixels, not a game number
        root.sizeDelta = new Vector2(barW, h);

        Place(nameLabel.rectTransform, new Vector2(0f, -nameY), new Vector2(Mathf.Max(barW, 40f), NameH));
        nameLabel.fontSize = Mathf.Max(7f, Mathf.Min(11f, barW * 0.28f));

        if (descriptionLabel.gameObject.activeSelf != hasDescription) descriptionLabel.gameObject.SetActive(hasDescription);
        if (hasDescription)
        {
            // Wider than the bars: a caption is a few words, and a narrow body should not squeeze it.
            Place(descriptionLabel.rectTransform, new Vector2(0f, -(nameY + NameH)), new Vector2(Mathf.Max(barW, 160f), DescriptionH));
            descriptionLabel.fontSize = Mathf.Max(6f, nameLabel.fontSize * DescriptionScale);
        }

        float y = top + RowGap;
        var hpTrack = hpFill.rectTransform.parent as RectTransform;
        if (hpTrack.gameObject.activeSelf != hasBars) hpTrack.gameObject.SetActive(hasBars);
        Place(hpTrack, new Vector2(0f, -y), new Vector2(barW, HpBarH));
        y += HpBarH;

        if (mpTrack.gameObject.activeSelf != mana) mpTrack.gameObject.SetActive(mana);
        if (mana)
        {
            y += RowGap;
            Place(mpTrack.rectTransform, new Vector2(0f, -y), new Vector2(barW, MpBarH));
        }

        PlaceNameRow();
    }

    // The portrait, the name and the element tile, centred together as one row. Runs when any of them changes — a
    // bind, a level, a converter — never per frame.
    void PlaceNameRow()
    {
        if (root == null) return;
        float nameW = nameLabel.GetPreferredValues(nameLabel.text).x;   // perf-ok: on bind / level / element change only
        float extra = elementShown ? ElementGap + ElementSize : 0f;
        float face = portraitShown ? NameH + ElementGap : 0f;
        float left = -(face + nameW + extra) * 0.5f;
        if (portraitShown) portrait.rectTransform.anchoredPosition = new Vector2(left + NameH * 0.5f, -nameY);
        left += face;
        var nrt = nameLabel.rectTransform;
        nrt.anchoredPosition = new Vector2(left + nameW * 0.5f, -nameY);
        if (elementShown)
            elementTile.rectTransform.anchoredPosition =
                new Vector2(left + nameW + ElementGap + ElementSize * 0.5f, -nameY - (NameH - ElementSize) * 0.5f);
    }

    // ---- small builders ------------------------------------------------------------------------------------

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
        img.sprite = MobNameplate.SharedWhite;
        img.color = color;
        img.raycastTarget = false;
        var onTop = MobNameplate.SharedOnTop;
        if (onTop != null) img.material = onTop;
        return img;
    }

    // A bar, rather than a plain square: it empties from right to left as the number it shows drops.
    static Image NewBarFill(string name, Transform parent, Color color)
    {
        var img = NewImage(name, parent, color);
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillAmount = 1f;
        return img;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, float size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var txt = go.AddComponent<TextMeshProUGUI>();
        txt.font = UiKit.Font;
        txt.fontSharedMaterial = UiKit.OutlinedMaterial(UiKit.Font);
        txt.fontSize = size;
        txt.alignment = TextAlignmentOptions.Center;
        txt.raycastTarget = false;
        txt.textWrappingMode = TextWrappingModes.NoWrap;
        return txt;
    }

    static void Place(RectTransform rt, Vector2 anchoredPos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // A fill inset inside its dark track, so the track reads as a border at any bar width.
    static void FillInside(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(1f, 1f);
        rt.offsetMax = new Vector2(-1f, -1f);
    }
}
