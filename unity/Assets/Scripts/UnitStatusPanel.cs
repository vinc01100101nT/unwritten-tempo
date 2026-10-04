using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// THE READOUT FOR THE UNIT YOU CLICKED — top-center of the screen, showing the one wearing the target arrow: the
/// creature you are fighting (<see cref="Player3DController.AttackTarget"/>), or else the person you selected
/// (<see cref="Player3DController.SelectedPerson"/>). Its name (a creature's portrait on its left), its element, its HP
/// and MP, its live buffs/debuffs, and what it is carrying. It stays up until that fight or selection ends.
///
/// <para><b>The only place another unit's buffs and debuffs are drawn.</b> While a unit is shown here the server is
/// asked to keep this machine told about it (<see cref="NetPlayer.PointAt"/>), so its icons stay live however long you
/// look at it — and nothing is sent about units nobody has selected.</para>
///
/// <para><b>Merely POINTING at something is not this readout's job</b> — that is <see cref="HoverPlate"/>, which
/// floats a name and bars over whichever creature is under the cursor.</para>
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
    const float GearRowH = 14f;
    // One row per place a unit can wear something: head, armour, weapon, shield, garment and the ring.
    const int   MaxGearRows = 6;

    static readonly Color TrackBg   = UiKit.Rule;
    static readonly Color MpColor   = new Color(0.35f, 0.55f, 0.95f);
    static readonly Color CellBg    = UiKit.ButtonEdge;
    static readonly Color Shade     = new Color(0f, 0f, 0f, 0.5f);
    static readonly Color StackTint = new Color(1f, 0.87f, 0.45f);   // warm gold, same as the player's status bar

    static Sprite white;
    static Sprite White => white != null ? white
        : (white = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f)));

    Panel target;   // top-center: the unit this person is fighting
    Player3DController player;

    void Awake()
    {
        var cvGO   = new GameObject("UnitStatusCanvas") { hideFlags = HideFlags.DontSaveInEditor };
        var canvas = cvGO.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;   // above the HUD

        // Its icons answer the mouse, because the unit being described stays put while you reach for one — it is
        // whoever you are fighting, not whoever the cursor happens to be resting on.
        target = new Panel(canvas, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -12f), 260f, true);
    }

    void OnDestroy()
    {
        target?.Unbind();
    }

    void Update()
    {
        if (player == null && PersistentPlayer3D.Instance != null)
            player = PersistentPlayer3D.Instance.GetComponentInChildren<Player3DController>();

        target.Track(player != null ? (player.AttackTarget ?? player.SelectedPerson) : null);
    }

    // ── one unit's readout ────────────────────────────────────────────────────

    class Panel
    {
        class Cell
        {
            public GameObject go;
            public RectTransform rt;      // hover anchor for the shared info card
            public Image icon;
            public TMP_Text symbol;
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
        readonly Image hpTrack, hpFill, mpTrack, mpFill, elementBadge, portrait;
        // The creature's face, left of its name — see CreaturePortraits. Hidden for anyone without one.
        bool portraitShown;
        readonly TMP_Text nameLabel, hpLabel, elementLabel;
        readonly RectTransform statesRow;
        readonly List<Cell> cells = new List<Cell>(MaxIcons);
        readonly List<UnitStates.State> shown = new List<UnitStates.State>(MaxIcons);

        // WHAT THE UNIT WEARS: one row per piece, its grade and folds then its name, exactly
        // as the server wrote them (IWatchedShell.WornText). Rebuilt only when that text changes.
        readonly List<TMP_Text> gearRows = new List<TMP_Text>(MaxGearRows);
        readonly List<WornList.Entry> gearEntries = new List<WornList.Entry>(MaxGearRows);
        // What the server says about the unit beyond its health — a creature's shell or a person's.
        IWatchedShell shell;
        // The creature's own shell, for the one thing only a creature has: its face.
        NetMob creature;
        string shownWorn;
        // The name without its level, and the level drawn beside it — rewritten only when the server's level changes.
        string baseName;
        int lastLevel;

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

            UiKit.Plate(root);

            hpTrack = NewImage("HPTrack", root, TrackBg);
            hpFill  = NewBarFill(hpTrack.rectTransform, HpColor(1f));

            hpLabel = NewText("HPLabel", hpTrack.rectTransform, 9, TextAlignmentOptions.Center, UiKit.TextMain);
            StretchFull(hpLabel.rectTransform);

            mpTrack = NewImage("MPTrack", root, TrackBg);
            mpFill  = NewBarFill(mpTrack.rectTransform, MpColor);

            nameLabel = NewText("Name", root, 13, TextAlignmentOptions.TopLeft, UiKit.TitleText);

            portrait = NewImage("Portrait", root, Color.white);
            portrait.preserveAspect = true;
            portrait.gameObject.SetActive(false);

            elementBadge = NewImage("ElementBadge", root, new Color(0.2f, 0.15f, 0.28f, 0.90f));
            elementLabel = NewText("ElementLabel", elementBadge.rectTransform, 9, TextAlignmentOptions.Center, Color.white);
            StretchFull(elementLabel.rectTransform);

            statesRow = NewRect("States", root);
            statesRow.anchorMin = statesRow.anchorMax = new Vector2(0f, 1f);
            statesRow.pivot = new Vector2(0f, 1f);
            for (int i = 0; i < MaxIcons; i++) cells.Add(BuildCell(statesRow, hoverableIcons));

            for (int i = 0; i < MaxGearRows; i++)
            {
                var row = NewText("Gear", root, 10, TextAlignmentOptions.TopLeft, UiKit.TextMain);
                row.gameObject.SetActive(false);
                gearRows.Add(row);
            }

            Layout();
        }

        /// <summary>Point this readout at <paramref name="h"/> (null hides it) and keep it current.</summary>
        public void Track(Health h)
        {
            if (h != null && h.IsDead) h = null;
            if (h != tracked) Bind(h);
            if (tracked == null) return;

            tracked.States.Prune();                 // drop expired entries (fires OnChanged → statesDirty)
            if (statesDirty) { statesDirty = false; RebuildStates(); DrawElementBadge(tracked); }
            RefreshNumbers();
            RefreshStates();
            // A new life wears new gear on the same body, so the list is re-read whenever the server's text changes.
            if (shell != null && !ReferenceEquals(shell.WornText, shownWorn)) RebuildGear();
            if (shell != null && shell.Level != lastLevel)
            {
                lastLevel = shell.Level;
                nameLabel.text = UnitLabel.WithLevel(baseName, lastLevel);
            }
        }

        public void Unbind() => Bind(null);

        void Bind(Health h)
        {
            if (subscribed != null) subscribed.OnChanged -= MarkStatesDirty;
            subscribed = null;

            tracked = h;
            lastHP = lastMP = int.MinValue;
            // The shell speaking for this body, found once per bind — a dictionary probe, never per frame.
            creature = h != null ? (NetMob.ShellForView(h.gameObject) ?? NetMob.ShellForServerMob(h.gameObject)) : null;
            shell = creature != null ? (IWatchedShell)creature : (h != null ? NetPlayer.ShellForBody(h.gameObject) : null);
            // AND THE SERVER IS TOLD WHO THIS IS, so it keeps sending their buffs for as long as they are shown here.
            NetPlayer.PointAt(h != null ? h.gameObject : null);
            RebuildGear();
            statesDirty = true;

            if (h == null) { group.alpha = 0f; RebuildStates(); return; }   // drops the icons and any card they opened
            group.alpha = 1f;

            subscribed = h.States;
            if (subscribed != null) subscribed.OnChanged += MarkStatesDirty;

            baseName = UnitLabel.Of(h);
            lastLevel = shell != null ? shell.Level : 0;
            nameLabel.text = UnitLabel.WithLevel(baseName, lastLevel);

            var face = CreaturePortraits.For(h, creature);
            bool faceChanged = (face != null) != portraitShown;
            portraitShown = face != null;
            portrait.sprite = face;
            portrait.gameObject.SetActive(portraitShown);

            DrawElementBadge(h);

            bool mana = h.MaxMP > 0;               // no pool → no bar, rather than an always-empty one
            if (mana != hasMana || faceChanged) { hasMana = mana; Layout(); }
            mpTrack.gameObject.SetActive(hasMana);

            RebuildStates();
            statesDirty = false;
        }

        void MarkStatesDirty() => statesDirty = true;

        // What the target is hit as RIGHT NOW, and at what level: an armour converter's element while one runs, its own
        // otherwise. Redrawn on a target change and whenever its states change, never per frame.
        void DrawElementBadge(Health h)
        {
            if (h == null) { elementBadge.gameObject.SetActive(false); return; }
            Element el = h.States.DefendingElementOr(h.element, h.elementLevel, out int level);
            if (el == Element.Neutral)
            {
                elementBadge.gameObject.SetActive(false);
                return;
            }
            elementBadge.gameObject.SetActive(true);
            Color c = Elements.Tint(el);
            elementBadge.color = new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 0.90f);
            elementLabel.color = c;
            elementLabel.text  = UnitLabel.WithLevel(el.ToString().ToUpper(), level);
        }

        void RebuildGear()
        {
            shownWorn = shell != null ? shell.WornText : null;
            WornList.Decode(shownWorn, gearEntries);
            for (int i = 0; i < gearRows.Count; i++)
            {
                var row = gearRows[i];
                bool on = i < gearEntries.Count;
                if (row.gameObject.activeSelf != on) row.gameObject.SetActive(on);
                if (!on) continue;
                var e = gearEntries[i];
                // perf-ok: runs only when the server's worn text changes, never per frame
                row.text = (e.quality == Quality.Raw ? "Raw" : e.quality.ToString())
                           + (e.folds > 0 ? " +" + e.folds : "") + "  " + e.def.displayName;
                // Rows are pooled and reused, so the colour is set every time or a row keeps the last one's.
                row.color = UiKit.TextMain;
            }
            Layout();
        }

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

                c.icon.sprite  = null;
                c.icon.color   = SkillSymbol.ColorFor(st.sourceSkillId);
                c.symbol.text  = st.symbol ?? "";
                c.symbol.color = st.tint == default ? Color.white : st.tint;
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

            float face = portraitShown ? NameH + 4f : 0f;
            if (portraitShown) PlaceTopLeft(portrait.rectTransform, new Vector2(Pad, y), new Vector2(NameH, NameH));
            PlaceTopLeft(nameLabel.rectTransform, new Vector2(Pad + face, y), new Vector2(inner - face, NameH));
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

            int gear = Mathf.Min(gearEntries.Count, gearRows.Count);
            if (gear > 0) y -= 4f;
            for (int i = 0; i < gear; i++)
            {
                PlaceTopLeft(gearRows[i].rectTransform, new Vector2(Pad, y), new Vector2(inner, GearRowH));
                y -= GearRowH;
            }

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
            Inset(icon.rectTransform, 2f);

            // The granting skill's two letters, centred on the tile — what used to be a picture.
            var symbol = NewText("Symbol", rt, 15, TextAlignmentOptions.Center, Color.white);
            symbol.raycastTarget = false;
            Inset(symbol.rectTransform, 2f);

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

            var cell = new Cell { go = frame.gameObject, rt = rt, icon = icon, symbol = symbol, fill = fill, time = time, stacks = stacks };
            cell.go.SetActive(false);

            if (hoverable)
            {
                // The cell itself is the hover target, and it opens the SAME card the player's own status bar uses —
                // one description, one place it lives, whoever the state is sitting on.
                frame.raycastTarget = true;
                HoverRelay.Set(frame.gameObject,
                               () => StateInfoCard.Show(canvas, cell.rt, cell.state, StateInfoCard.Side.Below),
                               () => StateInfoCard.HideIfShowing(cell.state));
            }
            return cell;
        }

        static void Inset(RectTransform rt, float px)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(px, px); rt.offsetMax = new Vector2(-px, -px);
        }
    }

    // ── HP color (mirrors HoverPlate) ───────────────────────────────────────

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
