using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The HUD <b>buff bar</b> — a vertical stack of the player's active timed self-buffs on the RIGHT edge of the screen
/// (<see cref="Character.Buffs"/>). Each cell shows the source skill's icon (the same icon the hotbar uses) with its
/// remaining seconds and a shade that fills in as the buff runs down. Read-only mirror: buffs are registered by the
/// skill system when a Buff / Guard / Regen effect lands (see <see cref="SkillEffectApplier"/>).
///
/// <para>Self-builds its holder on the persistent GameUICanvas (added by <c>UIShellBuilder</c>); rebuilds its cells
/// only when the set of buffs changes (a new buff or a prune), and the per-frame refresh only rewrites the number on a
/// whole-second change — no per-frame allocation.</para>
/// </summary>
public class BuffBarPanel : MonoBehaviour
{
    class Cell
    {
        public PlayerBuffs.Buff buff;
        public Image fill;
        public TMP_Text time;
        public int lastSecs = -1;
    }

    static readonly Color CellBg = new Color(0.10f, 0.10f, 0.13f, 0.9f);
    static readonly Color Shade  = new Color(0f, 0f, 0f, 0.5f);

    Canvas canvas;
    RectTransform holder;
    Sprite whiteSprite;
    readonly List<Cell> cells = new List<Cell>();
    bool built;
    bool dirty;
    PlayerBuffs subscribed;

    void Update()
    {
        if (!built) { TryBuild(); return; }
        var ch = Character.Instance;
        if (ch == null) return;
        EnsureSubscribed(ch.Buffs);
        ch.Buffs.Prune();                       // drop expired entries (fires OnChanged → dirty)
        if (dirty) { dirty = false; Rebuild(); }
        Refresh();
    }

    void EnsureSubscribed(PlayerBuffs buffs)
    {
        if (subscribed == buffs) return;
        if (subscribed != null) subscribed.OnChanged -= MarkDirty;
        subscribed = buffs;
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
        for (int i = holder.childCount - 1; i >= 0; i--) Destroy(holder.GetChild(i).gameObject);
        cells.Clear();

        var ch = Character.Instance;
        if (ch == null) return;
        var list = ch.Buffs.Active;
        for (int i = 0; i < list.Count; i++) cells.Add(BuildCell(list[i]));
    }

    Cell BuildCell(PlayerBuffs.Buff buff)
    {
        var frame = UiKit.Img("Buff", holder, CellBg);
        frame.rectTransform.sizeDelta = new Vector2(44f, 44f);
        var le = frame.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = 44f;
        frame.raycastTarget = false;

        if (buff.icon != null)
        {
            var icon = UiKit.Img("Icon", frame.transform, buff.tint == default ? Color.white : buff.tint);
            icon.sprite = buff.icon; icon.preserveAspect = true; icon.raycastTarget = false;
            UiKit.Fill(icon.rectTransform, new Vector2(3, 3), new Vector2(-3, -3));
        }
        else
        {
            var lbl = UiKit.MakeText("Label", frame.transform, buff.name, 9, FontStyle.Bold, TextAnchor.MiddleCenter);
            lbl.color = Color.white; lbl.raycastTarget = false;
            UiKit.Fill(lbl.rectTransform, Vector2.zero, Vector2.zero);
        }

        // Shade that fills IN (radial) as the buff runs down — a "time left" ring, like an inverse cooldown.
        var fill = UiKit.Img("Fill", frame.transform, Shade);
        fill.sprite = whiteSprite; fill.type = Image.Type.Filled; fill.fillMethod = Image.FillMethod.Radial360;
        fill.fillOrigin = (int)Image.Origin360.Top; fill.fillClockwise = false; fill.fillAmount = 0f; fill.raycastTarget = false;
        UiKit.Fill(fill.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));

        // Remaining whole seconds (bottom-right).
        var time = UiKit.MakeText("Time", frame.transform, "", 11, FontStyle.Bold, TextAnchor.LowerRight);
        time.color = Color.white; time.raycastTarget = false;
        UiKit.Fill(time.rectTransform, new Vector2(2, 1), new Vector2(-2, -1));

        return new Cell { buff = buff, fill = fill, time = time };
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
        }
    }
}
