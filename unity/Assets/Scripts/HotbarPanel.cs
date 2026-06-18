using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the on-screen action bar (the <c>HUD_Hotbar</c> holder baked by <see cref="UIShellBuilder"/>).
/// It finds the player's active skills — every component implementing <see cref="IHotbarSkill"/> on the
/// <see cref="Player3DController"/> — and decorates one baked cell per skill with: an icon (or short
/// label), the hotkey glyph, the MP cost, a radial cooldown sweep, and a live countdown number. Clicking
/// a slot fires the skill (same targeting as the keypress, via <see cref="IHotbarSkill.Activate"/>).
///
/// Lives on the persistent GameUICanvas alongside the other binders. Builds once (retries until the
/// player + holder exist), then just refreshes the cheap per-frame bits (fill amount, countdown text).
/// Adding a new active is free: implement <see cref="IHotbarSkill"/> and it appears on the bar, ordered
/// by <see cref="IHotbarSkill.SlotOrder"/>.
/// </summary>
public class HotbarPanel : MonoBehaviour
{
    // One decorated cell. Cached colour/cooldown state lets Refresh skip work (and string allocs) when
    // nothing visible changed — important since Refresh runs every frame.
    class Slot
    {
        public IHotbarSkill skill;
        public Image cell, icon, cooldown;
        public Text mp, label, cdNum;
        public GameObject hideOnCd;     // the center label to hide while counting down (null when an icon shows)
        public int lastSecs = -1;
        public sbyte lastOnCd = -1, lastAff = -1;
    }

    static readonly Color CellReady = Color.white;
    static readonly Color CellDim    = new Color(0.5f, 0.5f, 0.55f, 1f);
    static readonly Color KeyColor   = new Color(1f, 0.85f, 0.45f);
    static readonly Color MpColor     = new Color(0.6f, 0.78f, 1f);
    static readonly Color MpNoColor   = new Color(1f, 0.5f, 0.45f);
    static readonly Color CdShade     = new Color(0f, 0f, 0f, 0.55f);

    // Pre-baked second strings so the countdown repaint never allocates (cooldowns are short).
    static readonly string[] Secs = BuildSecs();
    static string[] BuildSecs() { var a = new string[61]; for (int i = 0; i < a.Length; i++) a[i] = i.ToString(); return a; }

    /// <summary>Invoke to rebuild the bar (e.g. after a class switch). Set by the live HotbarPanel.</summary>
    public static System.Action RebuildRequested;

    Font font;
    Sprite whiteSprite;
    Player3DController player;
    Transform holder;
    readonly List<Slot> slots = new List<Slot>();
    readonly List<IHotbarSkill> enabledSkills = new List<IHotbarSkill>();
    bool built;

    void Start()
    {
        RebuildRequested = Rebuild;
        TryBuild();
    }

    void Update()
    {
        if (!built) { TryBuild(); return; }
        Refresh();
    }

    /// <summary>Tear down the decorated cells and rebuild from the player's currently-enabled skills (the
    /// class changed). Safe to call before the first build.</summary>
    public void Rebuild()
    {
        if (holder != null)
            for (int i = 0; i < holder.childCount; i++)
            {
                var cell = holder.GetChild(i);
                for (int c = cell.childCount - 1; c >= 0; c--) Destroy(cell.GetChild(c).gameObject);
                var btn = cell.GetComponent<Button>();
                if (btn != null) Destroy(btn);
            }
        slots.Clear();
        built = false;   // Update() will TryBuild() again next frame, reading the new enabled set
    }

    void TryBuild()
    {
        if (player == null) player = Object.FindFirstObjectByType<Player3DController>();
        if (player == null) return;
        if (holder == null) holder = FindByName("HUD_Hotbar");
        if (holder == null) return;

        // Only the ENABLED skills (the current class's kit — see Player3DController.ApplyClass) get a slot.
        var all = player.GetComponents<IHotbarSkill>();
        if (all == null || all.Length == 0) return;
        enabledSkills.Clear();
        for (int i = 0; i < all.Length; i++)
            if (all[i] is MonoBehaviour mb && mb.enabled) enabledSkills.Add(all[i]);
        if (enabledSkills.Count == 0) return;
        enabledSkills.Sort((a, b) => a.SlotOrder.CompareTo(b.SlotOrder));

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        // 1×1 white sprite for the radial cooldown shade — avoids any built-in/Resources dependency.
        if (whiteSprite == null)
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

        slots.Clear();
        int n = Mathf.Min(enabledSkills.Count, holder.childCount);   // decorate the first N baked cells; extras stay empty
        for (int i = 0; i < n; i++)
        {
            var cell = holder.GetChild(i).GetComponent<Image>();
            if (cell != null) slots.Add(BuildSlot(cell, enabledSkills[i]));
        }
        built = slots.Count > 0;
    }

    Slot BuildSlot(Image cell, IHotbarSkill skill)
    {
        var s = new Slot { skill = skill, cell = cell };

        // Make the cell a button that fires the skill (Activate() does its own cursor-or-nearest targeting).
        cell.raycastTarget = true;
        var btn = cell.gameObject.GetComponent<Button>() ?? cell.gameObject.AddComponent<Button>();
        btn.transition = Selectable.Transition.None;          // we drive our own dim; no grey tint-swap
        btn.targetGraphic = cell;
        var captured = skill;
        btn.onClick.AddListener(() => { if (captured.Activate()) UISounds.PlayClick(); });

        // Icon (below the sweep so it greys out under it). Falls back to a centered text label if unset.
        if (skill.Icon != null)
        {
            s.icon = NewImage("Icon", cell.transform, CellReady);
            s.icon.sprite = skill.Icon;
            s.icon.preserveAspect = true;
            s.icon.raycastTarget = false;
            Stretch(s.icon.rectTransform, 5, 5);
        }

        // Radial cooldown shade.
        s.cooldown = NewImage("Cooldown", cell.transform, CdShade);
        s.cooldown.sprite = whiteSprite;
        s.cooldown.type = Image.Type.Filled;
        s.cooldown.fillMethod = Image.FillMethod.Radial360;
        s.cooldown.fillOrigin = (int)Image.Origin360.Top;
        s.cooldown.fillClockwise = true;
        s.cooldown.fillAmount = 0f;
        s.cooldown.raycastTarget = false;
        Stretch(s.cooldown.rectTransform, 3, 3);

        // Center text label — only when there's no icon (above the sweep so it reads when ready).
        if (skill.Icon == null)
        {
            s.label = NewText("Label", cell.transform, skill.ShortLabel, 11, FontStyle.Bold, TextAnchor.MiddleCenter, skill.Tint);
            Stretch(s.label.rectTransform, 2, 2);
            s.hideOnCd = s.label.gameObject;
        }

        // Corners: hotkey (top-left), MP cost (bottom-right). Always on top.
        var key = NewText("Key", cell.transform, skill.KeyLabel, 12, FontStyle.Bold, TextAnchor.UpperLeft, KeyColor);
        Stretch(key.rectTransform, 4, 2);
        s.mp = NewText("Mp", cell.transform, skill.ManaCost.ToString(), 10, FontStyle.Bold, TextAnchor.LowerRight, MpColor);
        Stretch(s.mp.rectTransform, 2, 3);

        // Countdown number (center), shown only while on cooldown.
        s.cdNum = NewText("CdNum", cell.transform, "", 18, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
        Stretch(s.cdNum.rectTransform, 2, 2);
        s.cdNum.gameObject.SetActive(false);

        return s;
    }

    void Refresh()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (s.skill == null) continue;

            float frac = s.skill.CooldownFraction01;
            s.cooldown.fillAmount = frac;

            sbyte onCd = (sbyte)(frac > 0.0001f ? 1 : 0);
            if (onCd != s.lastOnCd)
            {
                if (s.hideOnCd != null) s.hideOnCd.SetActive(onCd == 0);
                s.cdNum.gameObject.SetActive(onCd == 1);
                s.lastOnCd = onCd;
                s.lastSecs = -1;                       // force the number to repaint
            }
            if (onCd == 1)
            {
                int secs = Mathf.CeilToInt(s.skill.CooldownRemaining);
                if (secs != s.lastSecs)
                {
                    s.cdNum.text = (secs >= 0 && secs < Secs.Length) ? Secs[secs] : secs.ToString();
                    s.lastSecs = secs;
                }
            }

            sbyte aff = (sbyte)(s.skill.Affordable ? 1 : 0);
            if (aff != s.lastAff)
            {
                s.cell.color = aff == 1 ? CellReady : CellDim;
                if (s.icon != null) s.icon.color = aff == 1 ? CellReady : CellDim;
                s.mp.color = aff == 1 ? MpColor : MpNoColor;
                s.lastAff = aff;
            }
        }
    }

    Transform FindByName(string n)
    {
        foreach (var t in GetComponentsInChildren<Transform>(true))
            if (t.name == n) return t;
        return null;
    }

    // ---- UI primitives (match StatPanel's style) ----
    Text NewText(string name, Transform parent, string str, int size, FontStyle style, TextAnchor anchor, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = font; t.fontSize = size; t.fontStyle = style; t.alignment = anchor;
        t.color = color; t.raycastTarget = false; t.supportRichText = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.text = str;
        var sh = go.AddComponent<Shadow>();
        sh.effectColor = new Color(0, 0, 0, 0.85f);
        sh.effectDistance = new Vector2(1, -1);
        return t;
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static void Stretch(RectTransform rt, float padX, float padY)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padX, padY);
        rt.offsetMax = new Vector2(-padX, -padY);
    }
}
