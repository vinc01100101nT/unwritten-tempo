using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// THE READOUT THAT FLOATS OVER WHOEVER YOU ARE POINTING AT — a creature, another person, a villager, anything
/// carrying health. Four things, stacked, sitting just clear of their head:
/// <list type="number">
/// <item>their name, with their element beside it as a coloured tile with its letters</item>
/// <item>a health bar</item>
/// <item>a mana bar under it</item>
/// <item>their buffs and debuffs as small icons, wrapped onto as many rows as it takes to fit</item>
/// </list>
///
/// <para><b>The bars are as wide as the body they belong to.</b> Their width is that unit's real measured body
/// width (<see cref="UnitAnchor.HalfWidth"/>), so a big creature gets a long bar and a small one a short bar, and
/// the plate always reads as belonging to the thing under it.</para>
///
/// <para><b>ONE widget, not one per unit.</b> Only one thing can be under the cursor, so this builds a single plate
/// and re-points it. It hangs under the ONE shared world-space canvas every floating widget uses
/// (<see cref="MobNameplate.SharedHost"/>) — never a canvas of its own.</para>
///
/// <para><b>Where the buffs come from.</b> A unit's buffs are decided by the server and are not sent to watchers by
/// default. So while this plate is pointed at somebody it asks the server to keep it told about that one unit
/// (<see cref="NetPlayer.PointAt"/>), which answers at once and again on every change until the cursor moves away.
/// Nothing is computed here: the icons are drawn from what the server said is running.</para>
///
/// <para>PERFORMANCE (see PERFORMANCE.md): per frame this only follows the unit and faces the camera. Numbers are
/// rewritten when they move, icons are rebuilt when the buff SET changes, and the bar width is measured once when
/// the plate binds to a new unit — a body does not change size while it stands there.</para>
/// </summary>
public class HoverPlate : MonoBehaviour
{
    // World size of one UI pixel — matched to CastBar so the two bars over a unit read at the same weight.
    const float WorldScale = 0.0075f;

    const float NameH = 13f;
    const float HpBarH = 9f;
    const float MpBarH = 6f;
    const float RowGap = 1.5f;
    const float IconSize = 11f;
    const float IconGap = 1.5f;

    // A unit's element sits beside its name as a tile the size of a buff icon, so the two read as one family.
    const float ElementGap = 2f;

    // The narrowest a bar may be drawn, in UI pixels. A body width is honoured above this; below it the bar would
    // be a couple of pixels of nothing, which reads as a glitch rather than as a small creature.
    const float MinBarW = 24f;

    // More than this on one unit is already unreadable at this size, and the rest are dropped rather than wrapped
    // into a wall of icons taller than the character.
    const int MaxIcons = 12;

    static readonly Color TrackBg = new Color(0.06f, 0.05f, 0.07f, 0.85f);
    static readonly Color MpColor = new Color(0.35f, 0.55f, 0.95f);
    static readonly Color StackTint = new Color(1f, 0.87f, 0.45f);

    RectTransform root;
    Image hpFill, mpTrack, mpFill;
    TextMeshProUGUI nameLabel;
    Image elementTile;
    TextMeshProUGUI elementText;
    bool elementShown;
    Element shownElement;
    readonly List<Image> iconSlots = new List<Image>(8);
    readonly List<TextMeshProUGUI> iconSymbols = new List<TextMeshProUGUI>(8);
    readonly List<TextMeshProUGUI> iconCounts = new List<TextMeshProUGUI>(8);

    Transform camT;
    Health tracked;
    UnitStates subscribed;
    bool shown;
    bool iconsDirty;

    // What is currently drawn, so a unit standing at steady health costs nothing.
    int lastHP = int.MinValue;
    int lastMaxHP = int.MinValue;
    int lastMP = int.MinValue;
    int lastMaxMP = int.MinValue;

    // A creature's name carries its level, which the server re-sends when it changes; null for anyone else.
    NetMob shell;
    string baseName;
    int lastLevel;

    // Measured once per bound unit: how wide this body is, in this plate's pixels.
    float barW = MinBarW;
    bool hasMana;
    int iconRows;

    void Awake()
    {
        // A SERVER HAS NO CURSOR AND NO SCREEN. Left switched on it would run a raycast and two registry scans every
        // frame to work out what a person who is not there is pointing at. Same guard, same reason, as the minimap's.
        if (GameServer.IsServer || Application.isBatchMode) { enabled = false; return; }
    }

    void OnDestroy()
    {
        Unsubscribe();
    }

    void LateUpdate()
    {
        if (camT == null)
        {
            var cam = CameraRig.Cam;
            if (cam == null) return;
            camT = cam.transform;
        }

        var unit = Player3DController.UnitUnderCursor(CameraRig.Cam);
        if (unit != tracked) Bind(unit);

        // NOTHING UNDER THE CURSOR. The plate goes away and the server is told to stop talking about whoever it
        // was, so a person who points at empty ground pays for nothing.
        if (tracked == null || tracked.IsDead)
        {
            if (shown && root != null) { shown = false; root.gameObject.SetActive(false); }
            return;
        }

        if (root == null) Build();
        if (!shown) { shown = true; iconsDirty = true; root.gameObject.SetActive(true); }

        // Ride just clear of their head and yaw to face the camera — the same follow every floating widget uses.
        root.position = UnitAnchor.Overhead(tracked.transform);
        root.rotation = camT.rotation;

        RefreshNumbers();

        if (shell != null && shell.Level != lastLevel)
        {
            lastLevel = shell.Level;
            nameLabel.text = UnitLabel.WithLevel(baseName, lastLevel);
            PlaceNameRow();
        }

        if (subscribed != null)
        {
            subscribed.Prune();
            // An armour converter starting or ending is a state change, so the element tile is redrawn here too.
            if (iconsDirty) { iconsDirty = false; RebuildIcons(); DrawElement(); }
        }
    }

    // ---- binding ------------------------------------------------------------------------------------------

    void Bind(Health unit)
    {
        Unsubscribe();
        tracked = unit;
        shell = null;

        // TELL THE SERVER WHAT WE ARE LOOKING AT, or ask it to stop. This is what keeps the icons true while the
        // cursor rests: the answer arrives again every time something on that unit changes.
        NetPlayer.PointAt(unit != null ? unit.gameObject : null);

        if (unit == null) return;

        subscribed = unit.States;
        if (subscribed != null) subscribed.OnChanged += MarkIconsDirty;

        if (root == null) Build();

        // The body's own width, turned into this plate's pixels. Read once here rather than per frame: a body does
        // not change size while it is standing in front of you.
        float bodyWidth = 2f * UnitAnchor.HalfWidth(unit.transform);
        barW = Mathf.Max(MinBarW, bodyWidth / WorldScale);

        // Found once per bind — a dictionary probe, never per frame.
        shell = NetMob.ShellForView(unit.gameObject) ?? NetMob.ShellForServerMob(unit.gameObject);
        baseName = UnitLabel.Of(unit);
        lastLevel = shell != null ? shell.Level : 0;
        nameLabel.text = UnitLabel.WithLevel(baseName, lastLevel);

        DrawElement();

        // A unit with no mana pool gets no mana bar, rather than an empty one that looks broken.
        hasMana = unit.MaxMP > 0;

        lastHP = int.MinValue;
        lastMaxHP = int.MinValue;
        lastMP = int.MinValue;
        lastMaxMP = int.MinValue;
        iconsDirty = true;

        Layout();
    }

    void Unsubscribe()
    {
        if (subscribed != null) subscribed.OnChanged -= MarkIconsDirty;
        subscribed = null;
    }

    void MarkIconsDirty() => iconsDirty = true;

    // ---- the element --------------------------------------------------------------------------------------

    // WHAT THE CREATURE IS HIT AS RIGHT NOW — an armour converter's element while one runs, its own otherwise — read
    // through the same one answer the target panel reads, so the two can never show different elements. Every unit
    // gets a tile, Neutral included — a creature, an elite, a person, a villager alike. Runs on a bind and on a state
    // change, never per frame.
    void DrawElement()
    {
        bool show = tracked != null;
        Element el = Element.Neutral;
        if (show)
        {
            var st = tracked.States;
            el = st != null ? st.DefendingElementOr(tracked.element, tracked.elementLevel, out _) : tracked.element;
        }
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

    // The name and the element tile, centred together as one row. Runs when either one changes — a bind, a level, a
    // converter — never per frame.
    void PlaceNameRow()
    {
        if (root == null) return;
        float nameW = nameLabel.GetPreferredValues(nameLabel.text).x;   // perf-ok: on bind / level / element change only
        float extra = elementShown ? ElementGap + IconSize : 0f;
        float left = -(nameW + extra) * 0.5f;
        var nrt = nameLabel.rectTransform;
        nrt.anchoredPosition = new Vector2(left + nameW * 0.5f, 0f);
        if (elementShown)
            elementTile.rectTransform.anchoredPosition =
                new Vector2(left + nameW + ElementGap + IconSize * 0.5f, -(NameH - IconSize) * 0.5f);
    }

    // ---- the numbers --------------------------------------------------------------------------------------

    void RefreshNumbers()
    {
        if (tracked.Current != lastHP || tracked.maxHealth != lastMaxHP)
        {
            lastHP = tracked.Current;
            lastMaxHP = tracked.maxHealth;
            float frac = Mathf.Clamp01((float)lastHP / Mathf.Max(1, lastMaxHP));
            hpFill.fillAmount = frac;
            hpFill.color = HpColor(frac);
        }

        if (!hasMana) return;

        if (tracked.CurrentMP != lastMP || tracked.MaxMP != lastMaxMP)
        {
            lastMP = tracked.CurrentMP;
            lastMaxMP = tracked.MaxMP;
            mpFill.fillAmount = Mathf.Clamp01((float)lastMP / Mathf.Max(1, lastMaxMP));
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

    // ---- the icons ----------------------------------------------------------------------------------------

    void RebuildIcons()
    {
        if (root == null || subscribed == null) return;

        var list = subscribed.Active;
        int n = 0;
        for (int i = 0; i < list.Count && n < MaxIcons; i++)
        {
            var s = list[i];
            if (string.IsNullOrEmpty(s.symbol)) continue;

            var img = IconSlot(n);

            // The tile KEEPS its sprite. It is meant to be a plain coloured square with the letters riding on top,
            // and an image with no sprite draws nothing at all — which would throw the colour away and leave the
            // letters floating on the world behind them.
            img.color = SkillSymbol.ColorFor(s.sourceSkillId);

            var sym = iconSymbols[n];
            sym.text = s.symbol;
            sym.color = s.tint == default ? Color.white : s.tint;
            if (!sym.gameObject.activeSelf) sym.gameObject.SetActive(true);
            if (!img.gameObject.activeSelf) img.gameObject.SetActive(true);

            var cnt = iconCounts[n];
            int stacks = s.StackCount;
            if (stacks > 1)
            {
                cnt.text = stacks.ToString();
                if (!cnt.gameObject.activeSelf) cnt.gameObject.SetActive(true);
            }
            else if (cnt.gameObject.activeSelf) cnt.gameObject.SetActive(false);

            n++;
        }

        for (int i = n; i < iconSlots.Count; i++)
        {
            if (iconSlots[i].gameObject.activeSelf) iconSlots[i].gameObject.SetActive(false);
            if (iconSymbols[i].gameObject.activeSelf) iconSymbols[i].gameObject.SetActive(false);
            if (iconCounts[i].gameObject.activeSelf) iconCounts[i].gameObject.SetActive(false);
        }

        // WRAPPED TO THE BARS' WIDTH. As many icons as fit across the bar, then the next row underneath.
        int perRow = Mathf.Max(1, Mathf.FloorToInt((barW + IconGap) / (IconSize + IconGap)));
        int rows = n == 0 ? 0 : Mathf.CeilToInt(n / (float)perRow);

        float top = NameH + RowGap + HpBarH + (hasMana ? RowGap + MpBarH : 0f) + RowGap;
        for (int i = 0; i < n; i++)
        {
            int row = i / perRow;
            int col = i % perRow;
            int inThisRow = Mathf.Min(perRow, n - row * perRow);
            float rowWidth = inThisRow * IconSize + (inThisRow - 1) * IconGap;
            float x = -rowWidth * 0.5f + IconSize * 0.5f + col * (IconSize + IconGap);
            float y = -(top + row * (IconSize + IconGap));
            iconSlots[i].rectTransform.anchoredPosition = new Vector2(x, y);
        }

        // The plate is only as tall as what it is showing, so its own height changes with the number of rows.
        if (rows != iconRows) { iconRows = rows; Layout(); }
    }

    Image IconSlot(int i)
    {
        while (iconSlots.Count <= i)
        {
            var img = NewImage("StateIcon", root, Color.white);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(IconSize, IconSize);
            img.gameObject.SetActive(false);
            iconSlots.Add(img);

            var sym = NewText("Symbol", rt, 7);
            StretchFull(sym.rectTransform);
            sym.gameObject.SetActive(false);
            iconSymbols.Add(sym);

            var cnt = NewText("Count", rt, 6);
            cnt.color = StackTint;
            cnt.alignment = TextAlignmentOptions.BottomRight;
            StretchFull(cnt.rectTransform);
            cnt.gameObject.SetActive(false);
            iconCounts.Add(cnt);
        }
        return iconSlots[i];
    }

    // ---- the widget ---------------------------------------------------------------------------------------

    void Build()
    {
        root = NewRect("HoverPlate", MobNameplate.SharedHost);
        root.localScale = Vector3.one * WorldScale;

        // Anchored at the BOTTOM, so the whole stack sits above the unit's head and grows upward as rows are added
        // rather than creeping down over their face.
        root.pivot = new Vector2(0.5f, 0f);

        nameLabel = NewText("Name", root, 11);
        nameLabel.alignment = TextAlignmentOptions.Center;
        nameLabel.fontStyle = FontStyles.Bold;
        nameLabel.fontSharedMaterial = UiKit.WorldNameMaterial();

        // The element tile: a plain coloured square with its letters on top, built like a buff icon below.
        elementTile = NewImage("Element", root, Color.white);
        Place(elementTile.rectTransform, Vector2.zero, new Vector2(IconSize, IconSize));
        elementText = NewText("Symbol", elementTile.rectTransform, 7);
        StretchFull(elementText.rectTransform);
        elementTile.gameObject.SetActive(false);

        var hpTrack = NewImage("HpTrack", root, TrackBg);
        hpFill = NewBarFill("HpFill", hpTrack.rectTransform, HpColor(1f));
        FillInside(hpFill.rectTransform);

        mpTrack = NewImage("MpTrack", root, TrackBg);
        mpFill = NewBarFill("MpFill", mpTrack.rectTransform, MpColor);
        FillInside(mpFill.rectTransform);

        root.gameObject.SetActive(false);
        Layout();
    }

    // Everything's size and place, worked out from the bar width this unit gave us. Runs on a bind and when the
    // number of icon rows changes — never per frame.
    void Layout()
    {
        if (root == null) return;

        float h = NameH + RowGap + HpBarH
                + (hasMana ? RowGap + MpBarH : 0f)
                + (iconRows > 0 ? RowGap + iconRows * IconSize + (iconRows - 1) * IconGap : 0f);
        root.sizeDelta = new Vector2(barW, h);

        Place(nameLabel.rectTransform, new Vector2(0f, 0f), new Vector2(Mathf.Max(barW, 40f), NameH));
        nameLabel.fontSize = Mathf.Max(7f, Mathf.Min(11f, barW * 0.28f));

        float y = NameH + RowGap;
        var hpTrack = hpFill.rectTransform.parent as RectTransform;
        Place(hpTrack, new Vector2(0f, -y), new Vector2(barW, HpBarH));
        y += HpBarH;

        if (mpTrack.gameObject.activeSelf != hasMana) mpTrack.gameObject.SetActive(hasMana);
        if (hasMana)
        {
            y += RowGap;
            Place(mpTrack.rectTransform, new Vector2(0f, -y), new Vector2(barW, MpBarH));
        }

        PlaceNameRow();
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
