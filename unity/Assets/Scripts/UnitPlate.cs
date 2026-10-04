using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// ONE FLOATING PLATE over one unit. Three things, stacked, sitting just clear of its head:
/// <list type="number">
/// <item>its name — a creature's portrait on its left, its element on its right as a coloured tile with its letters</item>
/// <item>a smaller line under the name saying what it is — a townsperson's trade, say — for a unit that has one</item>
/// <item>a health bar — left off for a unit with no health to show, a townsperson, whose plate is its name alone</item>
/// <item>a mana bar under it, for a unit that has mana</item>
/// </list>
/// A unit's buffs and debuffs are not drawn here: they show only on the target panel (<see cref="UnitStatusPanel"/>).
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
    // A quest mark over the name — "!" or "?" (see QuestMarks) — or nothing.
    TextMeshProUGUI markLabel;
    string shownMark = "";
    const float MarkH = 18f;
    // The same yellow the health bar passes through: a mark reads as "look here", in a colour the plate already uses.
    static readonly Color MarkColor = new Color(0.95f, 0.80f, 0.20f);
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

    float barW = MinBarW;
    bool hasMana;
    // False for a unit with no health to show — a townsperson — whose plate is its name and nothing else.
    bool hasBars = true;

    /// <param name="objectName">What the plate's object is called in the scene, so a person reading the hierarchy
    /// can tell one owner's plates from another's.</param>
    public UnitPlate(string objectName) { this.objectName = objectName; }

    public bool Shown => shown && root != null;

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
        markLabel.color = MarkColor;
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
        float top = hasDescription ? NameH + DescriptionH : NameH;

        // The plate is only as tall as what it is showing.
        float h = top + (hasBars ? RowGap + HpBarH : 0f) + (mana ? RowGap + MpBarH : 0f);   // server-math: the plate's height in screen pixels, not a game number
        root.sizeDelta = new Vector2(barW, h);

        Place(nameLabel.rectTransform, new Vector2(0f, 0f), new Vector2(Mathf.Max(barW, 40f), NameH));
        nameLabel.fontSize = Mathf.Max(7f, Mathf.Min(11f, barW * 0.28f));

        if (descriptionLabel.gameObject.activeSelf != hasDescription) descriptionLabel.gameObject.SetActive(hasDescription);
        if (hasDescription)
        {
            // Wider than the bars: a caption is a few words, and a narrow body should not squeeze it.
            Place(descriptionLabel.rectTransform, new Vector2(0f, -NameH), new Vector2(Mathf.Max(barW, 160f), DescriptionH));
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
        if (portraitShown) portrait.rectTransform.anchoredPosition = new Vector2(left + NameH * 0.5f, 0f);
        left += face;
        var nrt = nameLabel.rectTransform;
        nrt.anchoredPosition = new Vector2(left + nameW * 0.5f, 0f);
        if (elementShown)
            elementTile.rectTransform.anchoredPosition =
                new Vector2(left + nameW + ElementGap + ElementSize * 0.5f, -(NameH - ElementSize) * 0.5f);
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
