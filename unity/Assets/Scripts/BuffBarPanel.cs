using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// The HUD <b>status bar</b> — a vertical stack of the player's active timed states (buffs AND debuffs) on the RIGHT
/// edge of the screen, read straight from the unified <see cref="UnitStates"/> store (<see cref="Character.States"/>).
/// Each cell shows the source skill's icon (the same icon the hotbar uses) with its remaining seconds and a shade that
/// fills in as it runs down. Hovering a cell pops a shared info card with the state's name, a one-line "what it does",
/// and its live remaining time. The store is the single source: an effect lands, writes a state, and this reads it.
///
/// <para>Self-builds its holder on the persistent GameUICanvas (added by <c>UIShellBuilder</c>); rebuilds its cells
/// only when the set of buffs changes (a new buff or a prune), and the per-frame refresh only rewrites the number on a
/// whole-second change — no per-frame allocation.</para>
/// </summary>
public class BuffBarPanel : MonoBehaviour
{
    class Cell
    {
        public UnitStates.State buff;
        public RectTransform rt;      // the 44×44 frame — hover anchor for the info card
        public Image fill;
        public TMP_Text time;
        public TMP_Text stacks;       // "x3" badge, top-left; hidden while the state holds a single stack
        public int lastSecs = -1;
        public int lastStacks = -1;
    }

    static readonly Color StackColor = new Color(1f, 0.87f, 0.45f);   // warm gold — reads as "count", distinct from the white timer
    // Pre-built "x1".."x99" labels: the count changes on every stack gain/expiry, and building the string there would
    // allocate during combat. Indexed by count, so a refresh is a lookup (PERFORMANCE.md: no per-frame/per-hit strings).
    /// <summary>Shared with <see cref="UnitStatusPanel"/> (the target/hover readouts) so both status displays read one
    /// table rather than each building its own.</summary>
    public static readonly string[] StackLabels = BuildStackLabels();
    static string[] BuildStackLabels()
    {
        var a = new string[UnitStates.MaxStacks + 1];
        for (int i = 0; i < a.Length; i++) a[i] = i > 1 ? "x" + i : "";
        return a;
    }

    static readonly Color CellBg = new Color(0.10f, 0.10f, 0.13f, 0.9f);
    static readonly Color Shade  = new Color(0f, 0f, 0f, 0.5f);

    Canvas canvas;
    RectTransform holder;
    Sprite whiteSprite;
    readonly List<Cell> cells = new List<Cell>();
    bool built;
    bool dirty;
    UnitStates subscribed;

    void Update()
    {
        if (!built) { TryBuild(); return; }
        var ch = Character.Instance;
        if (ch == null) return;
        EnsureSubscribed(ch.States);
        ch.States.Prune();                      // drop expired entries (fires OnChanged → dirty)
        if (dirty) { dirty = false; Rebuild(); }
        Refresh();
    }

    void EnsureSubscribed(UnitStates states)
    {
        if (subscribed == states) return;
        if (subscribed != null) subscribed.OnChanged -= MarkDirty;
        subscribed = states;
        if (subscribed != null) subscribed.OnChanged += MarkDirty;
        dirty = true;
    }

    void MarkDirty() => dirty = true;
    void OnDestroy() { if (subscribed != null) subscribed.OnChanged -= MarkDirty; }

    void TryBuild()
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        if (whiteSprite == null)
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

        if (holder == null)
        {
            var go = new GameObject("HUD_BuffBar", typeof(RectTransform));
            holder = (RectTransform)go.transform;
            holder.SetParent(canvas.transform, false);
            holder.anchorMin = holder.anchorMax = new Vector2(1f, 0.5f);   // right edge, vertically centered
            holder.pivot = new Vector2(1f, 0.5f);
            holder.anchoredPosition = new Vector2(-10f, 0f);               // small inset from the edge
            holder.sizeDelta = new Vector2(46f, 420f);
            var vlg = go.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6; vlg.childAlignment = TextAnchor.MiddleRight;
            vlg.childControlWidth = vlg.childControlHeight = false;
            vlg.childForceExpandWidth = vlg.childForceExpandHeight = false;
        }

        built = true;
        dirty = true;
    }

    void Rebuild()
    {
        if (holder == null) return;
        StateInfoCard.Hide();   // the cells (and the one being hovered) are about to be destroyed
        for (int i = holder.childCount - 1; i >= 0; i--) Destroy(holder.GetChild(i).gameObject);
        cells.Clear();

        var ch = Character.Instance;
        if (ch == null) return;
        var list = ch.States.Active;
        for (int i = 0; i < list.Count; i++) cells.Add(BuildCell(list[i]));
    }

    Cell BuildCell(UnitStates.State buff)
    {
        // The cell wears the granting skill's weapon colour; a state from no skill falls back to the plain backdrop.
        var weapon = SkillSymbol.ColorFor(buff.sourceSkillId);
        var frame = UiKit.Img("Buff", holder,
            string.IsNullOrEmpty(buff.sourceSkillId) ? CellBg : new Color(weapon.r, weapon.g, weapon.b, CellBg.a));
        frame.rectTransform.sizeDelta = new Vector2(44f, 44f);
        var le = frame.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = 44f;
        frame.raycastTarget = true;   // the cell IS the hover target for the info card

        // The granting skill's two letters are the face. Tinted by the state's own colour, which is how an armed
        // charge and the debt it becomes stay told apart without a second thing to draw.
        string face = !string.IsNullOrEmpty(buff.symbol) ? buff.symbol
                    : string.IsNullOrEmpty(buff.name) ? buff.id.ToString() : buff.name;
        var lbl = UiKit.MakeText("Symbol", frame.transform, face, face.Length <= 2 ? 20 : 9, FontStyle.Bold, TextAnchor.MiddleCenter);
        lbl.color = buff.tint == default ? Color.white : buff.tint; lbl.raycastTarget = false;
        UiKit.Fill(lbl.rectTransform, Vector2.zero, Vector2.zero);

        // Shade that fills IN (radial) as the buff runs down — a "time left" ring, like an inverse cooldown.
        var fill = UiKit.Img("Fill", frame.transform, Shade);
        fill.sprite = whiteSprite; fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top; fill.fillClockwise = false; fill.fillAmount = 0f; fill.raycastTarget = false;
        UiKit.Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));

        // Remaining whole seconds (bottom-right).
        var time = UiKit.MakeText("Time", frame.transform, "", 11, FontStyle.Bold, TextAnchor.LowerRight);
        time.color = Color.white; time.raycastTarget = false;
        UiKit.Fill(time.rectTransform, new Vector2(2, 1), new Vector2(-2, -1));

        // Stack count (top-LEFT, opposite the timer so they never collide). Blank at one stack — a lone "x1" is noise.
        var stacks = UiKit.MakeText("Stacks", frame.transform, "", 11, FontStyle.Bold, TextAnchor.UpperLeft);
        stacks.color = StackColor; stacks.raycastTarget = false;
        UiKit.Fill(stacks.rectTransform, new Vector2(2, 1), new Vector2(-2, -1));

        var cell = new Cell { buff = buff, rt = frame.rectTransform, fill = fill, time = time, stacks = stacks };

        // Hover → show the shared info card beside this cell; exit → hide it.
        var trig = frame.gameObject.AddComponent<EventTrigger>();
        AddTrigger(trig, EventTriggerType.PointerEnter,
                   () => StateInfoCard.Show(canvas, cell.rt, cell.buff, StateInfoCard.Side.Left));
        AddTrigger(trig, EventTriggerType.PointerExit, StateInfoCard.Hide);
        return cell;
    }

    static void AddTrigger(EventTrigger trig, EventTriggerType type, System.Action fn)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => fn());
        trig.triggers.Add(entry);
    }

    void Refresh()
    {
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (c.buff == null || c.fill == null) continue;
            c.fill.fillAmount = 1f - c.buff.Fraction01;   // dark ring grows as the buff expires
            int secs = Mathf.CeilToInt(c.buff.Remaining);
            if (secs != c.lastSecs) { c.time.text = secs > 0 ? secs.ToString() : ""; c.lastSecs = secs; }
            // Stack badge — rewritten only when the count actually moves, from the pre-built label table (no alloc).
            int n = c.buff.StackCount;
            if (n != c.lastStacks && c.stacks != null)
            {
                c.stacks.text = n >= 0 && n < StackLabels.Length ? StackLabels[n] : "";
                c.lastStacks = n;
            }
        }
    }
}
