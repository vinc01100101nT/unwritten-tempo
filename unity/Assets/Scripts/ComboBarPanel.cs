using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// The HUD <b>combo bar</b> — one cell per COMPLETE combo chain (<see cref="Character.Combos"/>), sitting just above
/// the hotbar. Each cell shows the finisher skill's icon, the chain's trigger key, its shared-cooldown sweep +
/// countdown, and fires the chain on click (same path as the key, via <see cref="ComboRunner.TriggerChain"/>).
/// Read-only mirror of the chain data — composing happens in <see cref="ComboComposerPanel"/>.
///
/// <para>Builds its own holder on the persistent GameUICanvas; rebuilds on demand (<see cref="RebuildRequested"/>,
/// raised by the composer) or when the set of complete chains changes (e.g. a save load). The per-frame refresh only
/// updates the cooldown fill + countdown, and only rewrites the number on a whole-second change — no per-frame alloc.</para>
/// </summary>
public class ComboBarPanel : MonoBehaviour
{
    /// <summary>Rebuild the bar (after a composer edit). Set by the live panel.</summary>
    public static System.Action RebuildRequested;

    class Cell
    {
        public ComboChain chain;
        public Image cooldown;
        public TMP_Text cdNum;
        public int lastSecs = -1;
        public sbyte lastOnCd = -1;
    }

    static readonly Color CellBg   = new Color(0.14f, 0.12f, 0.10f, 0.95f);
    static readonly Color Border   = new Color(0.784f, 0.659f, 0.384f, 1f);   // combo cells wear a gold rim to read as "big"
    static readonly Color KeyColor = new Color(1f, 0.85f, 0.45f);
    static readonly Color CdShade  = new Color(0f, 0f, 0f, 0.55f);

    Canvas canvas;
    RectTransform holder;
    Sprite whiteSprite;
    readonly List<Cell> cells = new List<Cell>();
    bool built;

    void Start() { RebuildRequested = Rebuild; }

    void Update()
    {
        if (!built) { TryBuild(); return; }
        // Cheap self-heal: if the number of complete chains drifted from our cells (e.g. a save just loaded), rebuild.
        if (RunnableChainCount() != cells.Count) Rebuild();
        Refresh();
    }

    void TryBuild()
    {
        if (canvas == null) canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;
        if (whiteSprite == null)
            whiteSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

        if (holder == null)
        {
            var go = new GameObject("HUD_ComboBar", typeof(RectTransform));
            holder = (RectTransform)go.transform;
            holder.SetParent(canvas.transform, false);
            holder.anchorMin = holder.anchorMax = new Vector2(0.5f, 0f);
            holder.pivot = new Vector2(0.5f, 0f);
            holder.anchoredPosition = new Vector2(0f, 118f);   // above the hotbar strip
            holder.sizeDelta = new Vector2(400f, 52f);
            var hlg = go.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 6; hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = hlg.childControlHeight = false;
            hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;
        }

        built = true;
        Rebuild();
    }

    static int RunnableChainCount()
    {
        var ch = Character.Instance;
        if (ch == null) return 0;
        int n = 0;
        var list = ch.Combos.Chains;
        for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].IsRunnable) n++;
        return n;
    }

    void Rebuild()
    {
        if (holder == null) return;
        for (int i = holder.childCount - 1; i >= 0; i--) Destroy(holder.GetChild(i).gameObject);
        cells.Clear();

        var ch = Character.Instance;
        if (ch == null) return;
        var list = ch.Combos.Chains;
        for (int i = 0; i < list.Count; i++)
        {
            var chain = list[i];
            if (chain != null && chain.IsRunnable) cells.Add(BuildCell(chain));
        }
    }

    Cell BuildCell(ComboChain chain)
    {
        var border = UiKit.Img("ComboCell", holder, Border);
        var brt = border.rectTransform;
        brt.sizeDelta = new Vector2(48f, 48f);
        var le = border.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = 48f;
        border.raycastTarget = true;

        var bg = UiKit.Img("Bg", border.transform, CellBg);
        UiKit.Fill(bg.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));
        bg.raycastTarget = false;

        // The finisher (last filled slot) icon represents the chain; label fallback if it has no icon.
        var fin = chain.Def(chain.FinisherSlot);
        if (fin != null && fin.icon != null)
        {
            var icon = UiKit.Img("Icon", border.transform, Color.white);
            icon.sprite = fin.icon; icon.preserveAspect = true; icon.raycastTarget = false;
            UiKit.Fill(icon.rectTransform, new Vector2(5, 5), new Vector2(-5, -5));
        }
        else if (fin != null)
        {
            var lbl = UiKit.MakeText("Label", border.transform, fin.shortLabel, 10, FontStyle.Bold, TextAnchor.MiddleCenter);
            lbl.color = fin.tint == default ? Color.white : fin.tint; lbl.raycastTarget = false;
            UiKit.Fill(lbl.rectTransform, Vector2.zero, Vector2.zero);
        }

        // Radial cooldown shade.
        var cd = UiKit.Img("Cooldown", border.transform, CdShade);
        cd.sprite = whiteSprite; cd.type = Image.Type.Filled; cd.fillMethod = Image.FillMethod.Radial360;
        cd.fillOrigin = (int)Image.Origin360.Top; cd.fillClockwise = true; cd.fillAmount = 0f; cd.raycastTarget = false;
        UiKit.Fill(cd.rectTransform, new Vector2(3, 3), new Vector2(-3, -3));

        // Trigger key glyph (top-left).
        var key = UiKit.MakeText("Key", border.transform, chain.hotkey == KeyCode.None ? "—" : chain.hotkey.ToString(),
                                 11, FontStyle.Bold, TextAnchor.UpperLeft);
        key.color = KeyColor; key.raycastTarget = false;
        UiKit.Fill(key.rectTransform, new Vector2(3, 2), new Vector2(-3, -2));

        // "×N" finisher hint (bottom-right) — the last filled slot's rung, so a 3-chain reads ×2.5 not ×4.
        var mult = UiKit.MakeText("Mult", border.transform, "×" + ComboBook.SlotMultiplier(chain.FinisherSlot).ToString("0.#"),
                                  10, FontStyle.Bold, TextAnchor.LowerRight);
        mult.color = new Color(1f, 0.55f, 0.3f); mult.raycastTarget = false;
        UiKit.Fill(mult.rectTransform, new Vector2(2, 2), new Vector2(-2, -2));

        // Countdown number (center) — shown only while on cooldown.
        var cdNum = UiKit.MakeText("CdNum", border.transform, "", 18, FontStyle.Bold, TextAnchor.MiddleCenter);
        cdNum.color = Color.white; cdNum.raycastTarget = false;
        UiKit.Fill(cdNum.rectTransform, Vector2.zero, Vector2.zero);
        cdNum.gameObject.SetActive(false);

        var btn = border.gameObject.AddComponent<Button>();
        btn.targetGraphic = border;
        var capChain = chain;
        btn.onClick.AddListener(() => { if (ComboRunner.Active != null) { ComboRunner.Active.TriggerChain(capChain); UISounds.PlayClick(); } });

        return new Cell { chain = chain, cooldown = cd, cdNum = cdNum };
    }

    void Refresh()
    {
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (c.chain == null || c.cooldown == null) continue;

            float frac = c.chain.CooldownFraction01;
            c.cooldown.fillAmount = frac;

            sbyte onCd = (sbyte)(frac > 0.0001f ? 1 : 0);
            if (onCd != c.lastOnCd) { c.cdNum.gameObject.SetActive(onCd == 1); c.lastOnCd = onCd; c.lastSecs = -1; }
            if (onCd == 1)
            {
                int secs = Mathf.CeilToInt(c.chain.CooldownRemaining);
                if (secs != c.lastSecs) { c.cdNum.text = secs.ToString(); c.lastSecs = secs; }
            }
        }
    }
}
