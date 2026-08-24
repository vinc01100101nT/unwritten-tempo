using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The screen-space readout for ANOTHER unit — name, element, HP, MP and its live buffs/debuffs — drawn from ONE
/// panel implementation and shown in two places at once:
/// <list type="bullet">
/// <item><b>Top-center</b> — the mob the player is engaged with (the one wearing the target arrow,
/// <see cref="Player3DController.AttackTarget"/>). Persists while the fight lasts.</item>
/// <item><b>Top-right</b> — whatever mob is under the cursor right now
/// (<see cref="Player3DController.MonsterUnderCursor"/>), which is what this component used to do on its own.</item>
/// </list>
///
/// <para>Both readouts are the SAME <see cref="Panel"/> class with different anchors and widths, so a change to how a
/// unit is presented lands in both by construction — the hover readout is no longer a second implementation that can
/// drift from the target one.</para>
///
/// <para>Reads through <see cref="ICombatUnit"/> (HP, MP, state store), so nothing here is mob-specific. Rebuilds its
/// state icons only when that unit's state SET changes (its store's OnChanged), and rewrites a number only when the
/// number moves — no per-frame allocation. Self-builds its canvas on Awake; added to GameSystems by
/// <c>SystemsSceneBuilder</c>.</para>
/// </summary>
public class UnitStatusPanel : MonoBehaviour
{
    const float Pad    = 8f;
    const float BarH   = 10f;
    const float MpBarH = 6f;
    const float NameH  = 18f;
    const float BadgeW = 72f, BadgeH = 14f;
    const float IconH  = 24f, IconGap = 3f;
    const int   MaxIcons = 10;   // a unit showing more than this is already unreadable; the rest are dropped

    static readonly Color PanelBg   = new Color(0.05f, 0.04f, 0.08f, 0.88f);
    static readonly Color TrackBg   = new Color(0.10f, 0.08f, 0.13f, 1f);
    static readonly Color MpColor   = new Color(0.35f, 0.55f, 0.95f);
    static readonly Color CellBg    = new Color(0.10f, 0.10f, 0.13f, 0.9f);
    static readonly Color Shade     = new Color(0f, 0f, 0f, 0.5f);
    static readonly Color StackTint = new Color(1f, 0.87f, 0.45f);   // warm gold, same as the player's status bar

    static Sprite white;
    static Sprite White => white != null ? white
        : (white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f)));

    Panel target;   // top-center: the engaged mob
    Panel hover;    // top-right: the mob under the cursor
    Camera cam;
    Player3DController player;

    void Awake()
    {
        var cvGO   = new GameObject("UnitStatusCanvas") { hideFlags = HideFlags.DontSaveInEditor };
        var canvas = cvGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;   // above the HUD

        // Only the engaged target's icons answer the mouse: the hover readout follows whatever the cursor is over, so
        // reaching for one of ITS icons would drop the very mob it is describing.
        target = new Panel(canvas, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), 260f, true);
        hover  = new Panel(canvas, new Vector2(1f,   1f), new Vector2(1f,   1f), new Vector2(-12f, -12f), 180f, false);
    }

    void OnDestroy()
    {
        target?.Unbind();
        hover?.Unbind();
    }

    void Update()
    {
        if (cam == null) cam = Camera.main;
        if (player == null && PersistentPlayer3D.Instance != null)
            player = PersistentPlayer3D.Instance.GetComponentInChildren<Player3DController>();

        target.Track(player != null ? player.AttackTarget : null);
        hover.Track(Player3DController.MonsterUnderCursor(cam));
    }

    // ── one unit's readout ────────────────────────────────────────────────────

    class Panel
    {
        class Cell
        {
            public GameObject go;
            public RectTransform rt;      // hover anchor for the shared info card
            public Image icon;
            public Image fill;
            public TMP_Text time;
            public TMP_Text stacks;
            public UnitStates.State state; // what this cell is currently showing, else null
            public int lastSecs = -1, lastStacks = -1;
        }

        readonly float width;
        readonly Canvas canvas;
        readonly RectTransform root;
        readonly CanvasGroup group;
        readonly Image hpTrack, hpFill, mpTrack, mpFill, elementBadge;
        readonly TMP_Text nameLabel, hpLabel, elementLabel;
        readonly RectTransform statesRow;
        readonly List<Cell> cells = new List<Cell>(MaxIcons);
        readonly List<UnitStates.State> shown = new List<UnitStates.State>(MaxIcons);

        Health tracked;
        UnitStates subscribed;
        int lastHP = int.MinValue, lastMP = int.MinValue;
        bool statesDirty;
        bool hasMana;

        public Panel(Canvas parent, Vector2 anchor, Vector2 pivot, Vector2 offset, float w, bool hoverableIcons)
        {
            width  = w;
            canvas = parent;

            root = NewRect("Panel", parent.transform);
            root.anchorMin = root.anchorMax = anchor;
            root.pivot = pivot;
            root.anchoredPosition = offset;
            root.sizeDelta = new Vector2(width, 0f);

            group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            // Only the state icons are raycast targets, so even on the hoverable panel the mouse still reaches the
            // world through the bars, the name and the background.
            group.blocksRaycasts = hoverableIcons;
            group.interactable = false;

            var bg = NewImage("BG", root, PanelBg);
            StretchFull(bg.rectTransform);

            hpTrack = NewImage("HPTrack", root, TrackBg);
            hpFill  = NewBarFill(hpTrack.rectTransform, HpColor(1f));

            hpLabel = NewText("HPLabel", hpTrack.rectTransform, 9, TextAlignmentOptions.Center,
                              new Color(0.85f, 0.85f, 0.85f));
            StretchFull(hpLabel.rectTransform);

            mpTrack = NewImage("MPTrack", root, TrackBg);
            mpFill  = NewBarFill(mpTrack.rectTransform, MpColor);

            nameLabel = NewText("Name", root, 13, TextAlignmentOptions.TopLeft,
                                new Color(0.97f, 0.97f, 0.99f));

            elementBadge = NewImage("ElementBadge", root, new Color(0.2f, 0.15f, 0.28f, 0.90f));
            elementLabel = NewText("ElementLabel", elementBadge.rectTransform, 9, TextAlignmentOptions.Center, Color.white);
            StretchFull(elementLabel.rectTransform);

            statesRow = NewRect("States", root);
            statesRow.anchorMin = statesRow.anchorMax = new Vector2(0f, 1f);
            statesRow.pivot = new Vector2(0f, 1f);
            for (int i = 0; i < MaxIcons; i++) cells.Add(BuildCell(statesRow, hoverableIcons));

            Layout();
        }

        /// <summary>Point this readout at <paramref name="h"/> (null hides it) and keep it current.</summary>
        public void Track(Health h)
        {
            if (h != null && h.IsDead) h = null;
            if (h != tracked) Bind(h);
            if (tracked == null) return;

            tracked.States.Prune();                 // drop expired entries (fires OnChanged → statesDirty)
            if (statesDirty) { statesDirty = false; RebuildStates(); }
            RefreshNumbers();
            RefreshStates();
        }

        public void Unbind() => Bind(null);

        void Bind(Health h)
        {
            if (subscribed != null) subscribed.OnChanged -= MarkStatesDirty;
            subscribed = null;

            tracked = h;
            lastHP = lastMP = int.MinValue;
            statesDirty = true;

            if (h == null) { group.alpha = 0f; RebuildStates(); return; }   // drops the icons and any card they opened
            group.alpha = 1f;

            subscribed = h.States;
            if (subscribed != null) subscribed.OnChanged += MarkStatesDirty;

            var plate = h.GetComponent<MobNameplate>();
            nameLabel.text = plate != null && !string.IsNullOrEmpty(plate.displayName)
                ? plate.displayName
                : (h.name.StartsWith("Monster_") ? h.name.Substring("Monster_".Length) : h.name);

            Element el = h.element;
            if (el == Element.Neutral)
            {
                elementBadge.gameObject.SetActive(false);
            }
            else
            {
                elementBadge.gameObject.SetActive(true);
                Color c = Elements.Tint(el);
                elementBadge.color = new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 0.90f);
                elementLabel.color = c;
                elementLabel.text  = el.ToString().ToUpper();
            }

            bool mana = h.MaxMP > 0;               // no pool → no bar, rather than an always-empty one
            if (mana != hasMana) { hasMana = mana; Layout(); }
            mpTrack.gameObject.SetActive(hasMana);

            RebuildStates();
            statesDirty = false;
        }

        void MarkStatesDirty() => statesDirty = true;

        void RefreshNumbers()
        {
            if (tracked.Current != lastHP)
            {
                lastHP = tracked.Current;
                float frac = Mathf.Clamp01((float)lastHP / Mathf.Max(1, tracked.maxHealth));
                hpFill.fillAmount = frac;
                hpFill.color      = HpColor(frac);
                hpLabel.text      = lastHP + " / " + tracked.maxHealth;
            }

            if (!hasMana) return;
            if (tracked.CurrentMP != lastMP)
            {
                lastMP = tracked.CurrentMP;
                mpFill.fillAmount = Mathf.Clamp01((float)lastMP / Mathf.Max(1, tracked.MaxMP));
            }
        }

        // The icon row is rebuilt only when the unit's state SET changes; the per-frame pass below only moves the
        // shade and rewrites a number when it actually changed.
        void RebuildStates()
        {
            shown.Clear();
            if (tracked != null)
            {
                var list = tracked.States.Active;
                for (int i = 0; i < list.Count && shown.Count < MaxIcons; i++) shown.Add(list[i]);
            }

            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                bool on = i < shown.Count;
                var st = on ? shown[i] : null;
                // A cell that stops showing a state takes its open card down with it — it is describing something
                // this unit no longer has.
                if (c.state != null && c.state != st) StateInfoCard.HideIfShowing(c.state);
                c.state = st;
                if (c.go.activeSelf != on) c.go.SetActive(on);
                if (!on) continue;

                c.icon.sprite  = st.icon;
                c.icon.color   = st.icon != null ? (st.tint == default ? Color.white : st.tint) : Color.clear;
                c.lastSecs = c.lastStacks = -1;   // force both labels to write on the next refresh
                // Laid out by hand (no LayoutGroup) so a rebuild costs nothing but a few anchored positions.
                ((RectTransform)c.go.transform).anchoredPosition = new Vector2(i * (IconH + IconGap), 0f);
            }

            statesRow.sizeDelta = new Vector2(Mathf.Max(0f, shown.Count * (IconH + IconGap) - IconGap), IconH);
            Layout();
        }

        void RefreshStates()
        {
            for (int i = 0; i < shown.Count; i++)
            {
                var c = cells[i]; var st = c.state;
                if (st == null) continue;
                c.fill.fillAmount = 1f - st.Fraction01;
                int secs = Mathf.CeilToInt(st.Remaining);
                if (secs != c.lastSecs) { c.time.text = secs > 0 ? secs.ToString() : ""; c.lastSecs = secs; }
                int n = st.StackCount;
                if (n != c.lastStacks)
                {
                    c.stacks.text = n >= 0 && n < BuffBarPanel.StackLabels.Length ? BuffBarPanel.StackLabels[n] : "";
                    c.lastStacks = n;
                }
            }
        }

        // Stack the rows top-down and size the panel to exactly what it shows: no MP bar on a unit without a pool,
        // no icon strip on a unit with no states.
        void Layout()
        {
            float y = -Pad;
            float inner = width - Pad * 2f;

            PlaceTopLeft(hpTrack.rectTransform, new Vector2(Pad, y), new Vector2(inner, BarH));
            y -= BarH + 2f;

            if (hasMana)
            {
                PlaceTopLeft(mpTrack.rectTransform, new Vector2(Pad, y), new Vector2(inner, MpBarH));
                y -= MpBarH + 2f;
            }

            PlaceTopLeft(nameLabel.rectTransform, new Vector2(Pad, y), new Vector2(inner, NameH));
            y -= NameH + 2f;

            PlaceTopLeft(elementBadge.rectTransform, new Vector2(Pad, y), new Vector2(BadgeW, BadgeH));
            y -= BadgeH;

            if (shown.Count > 0)
            {
                y -= 4f;
                statesRow.anchoredPosition = new Vector2(Pad, y);
                y -= IconH;
            }
            statesRow.gameObject.SetActive(shown.Count > 0);

            root.sizeDelta = new Vector2(width, -y + Pad);
        }

        Cell BuildCell(Transform parent, bool hoverable)
        {
            var frame = NewImage("State", parent, CellBg);
            var rt = frame.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(IconH, IconH);

            var icon = NewImage("Icon", rt, Color.white);
            icon.preserveAspect = true;
            Inset(icon.rectTransform, 2f);

            var fill = NewImage("Fill", rt, Shade);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Radial360;
            fill.fillOrigin = (int)Image.Origin360.Top;
            fill.fillClockwise = false;
            fill.fillAmount = 0f;
            Inset(fill.rectTransform, 1f);

            var time = NewText("Time", rt, 9, TextAlignmentOptions.BottomRight, Color.white);
            Inset(time.rectTransform, 1f);

            var stacks = NewText("Stacks", rt, 9, TextAlignmentOptions.TopLeft, StackTint);
            Inset(stacks.rectTransform, 1f);

            var cell = new Cell { go = frame.gameObject, rt = rt, icon = icon, fill = fill, time = time, stacks = stacks };
            cell.go.SetActive(false);

            if (hoverable)
            {
                // The cell itself is the hover target, and it opens the SAME card the player's own status bar uses —
                // one description, one place it lives, whoever the state is sitting on.
                frame.raycastTarget = true;
                var trig = frame.gameObject.AddComponent<EventTrigger>();
                AddTrigger(trig, EventTriggerType.PointerEnter,
                           () => StateInfoCard.Show(canvas, cell.rt, cell.state, StateInfoCard.Side.Below));
                AddTrigger(trig, EventTriggerType.PointerExit,
                           () => StateInfoCard.HideIfShowing(cell.state));
            }
            return cell;
        }

        static void AddTrigger(EventTrigger trig,
                               EventTriggerType type, System.Action fn)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ => fn());
            trig.triggers.Add(entry);
        }

        static void Inset(RectTransform rt, float px)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(px, px); rt.offsetMax = new Vector2(-px, -px);
        }
    }

    // ── HP color (mirrors MobNameplate) ───────────────────────────────────────

    static Color HpColor(float f)
    {
        var green  = new Color(0.40f, 0.85f, 0.30f);
        var yellow = new Color(0.95f, 0.80f, 0.20f);
        var red    = new Color(0.88f, 0.20f, 0.20f);
        return f > 0.5f ? Color.Lerp(yellow, green, (f - 0.5f) * 2f)
                        : Color.Lerp(red,    yellow, f * 2f);
    }

    // ── shared UI helpers (no alloc after build) ──────────────────────────────

    static RectTransform NewRect(string name, Transform parent)
    {
        var rt = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go  = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.sprite = White;
        img.color  = color;
        img.raycastTarget = false;
        return img;
    }

    static Image NewBarFill(Transform track, Color color)
    {
        var fill = NewImage("Fill", track, color);
        var rt = fill.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = new Vector2(1.5f, 1.5f); rt.offsetMax = new Vector2(-1.5f, -1.5f);
        fill.type       = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;
        return fill;
    }

    static TMP_Text NewText(string name, Transform parent, int size, TextAlignmentOptions align, Color color)
    {
        var t = UiKit.MakeText(name, parent, "", size, FontStyle.Bold, TextAnchor.UpperLeft, wrap: false);
        t.alignment = align;
        t.color = color;
        t.raycastTarget = false;
        return t;
    }

    static void PlaceTopLeft(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
