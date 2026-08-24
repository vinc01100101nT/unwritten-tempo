using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A single shared tooltip that shows an item's name + stats while you hover an
/// <see cref="ItemSlot"/>. Lives on the persistent UI canvas (built by
/// UIShellBuilder); slots call the static <see cref="ShowFor"/> / <see cref="HideTip"/>.
/// It follows the cursor and clamps itself to the screen.
/// </summary>
public class ItemTooltip : MonoBehaviour
{
    public static ItemTooltip Instance { get; private set; }

    /// <summary>While true (e.g. during a drag), Show requests are ignored.</summary>
    public static bool Suppressed { get; set; }

    [Tooltip("The tooltip's own RectTransform (moved to follow the cursor).")]
    public RectTransform panel;
    public TMP_Text titleText;
    public TMP_Text bodyText;

    [Tooltip("Horizontal strip (HorizontalLayoutGroup) filled with the weapon's granted-skill icons. Optional.")]
    public RectTransform skillRow;
    readonly List<Image> skillIcons = new List<Image>();   // lazily-grown pool of icon cells under skillRow

    [Tooltip("Pixel offset from the cursor, before screen clamping.")]
    public Vector2 cursorOffset = new Vector2(18, -18);

    Canvas canvas;
    CanvasGroup group;

    void Awake()
    {
        Instance = this;
        canvas = GetComponentInParent<Canvas>();
        if (panel == null) panel = (RectTransform)transform;
        // Hide via CanvasGroup alpha, NOT SetActive — an inactive GameObject never
        // runs Awake, so Instance would never register and Show would be a no-op.
        group = GetComponent<CanvasGroup>();
        if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Show the tooltip for an item (no-op if suppressed or the UI isn't up yet).</summary>
    public static void ShowFor(Item item) { if (!Suppressed && Instance != null) Instance.Show(item); }

    /// <summary>Show the tooltip for an equipment instance (renders +N, cards, rarity).</summary>
    public static void ShowFor(EquipmentInstance inst) { if (!Suppressed && Instance != null) Instance.Show(inst); }

    /// <summary>Show an equip with a live "equip → stat delta" preview vs what's in
    /// <paramref name="previewSlot"/> right now (green/red rows). Powers the inventory hover preview.</summary>
    public static void ShowForPreview(EquipmentInstance inst, EquipSlotType previewSlot)
    { if (!Suppressed && Instance != null) Instance.ShowPreview(inst, previewSlot); }

    /// <summary>Hide the tooltip (no-op if the UI isn't up yet).</summary>
    public static void HideTip()
    {
        if (Instance == null) return;
        Instance.group.alpha = 0f;
        Instance.hovered = null;
        // NOT details.Hide() — leaving the slot may mean the cursor is on its way to the panel's scrollbar.
        // UpdateSkillDetails owns that decision (one place), and closes it once the pointer is off both.
    }

    void Show(Item item)
    {
        hovered = null;                    // a bare def has no rolled kit — nothing to detail
        if (item == null) { group.alpha = 0f; return; }
        titleText.text = item.displayName;
        titleText.color = Rarities.Text(item.rarity);
        bodyText.text = Describe(item);
        ShowSkillIcons(null);   // a kit belongs to a COPY, not the shared def — nothing to show without an instance
        group.alpha = 1f;
        Follow();   // place it now so it doesn't flash at the previous spot for a frame
    }

    void Show(EquipmentInstance inst)
    {
        hovered = inst;
        if (inst == null || inst.def == null) { group.alpha = 0f; return; }
        var def = inst.def;
        titleText.text = (inst.upgrade > 0 ? "+" + inst.upgrade + " " : "") + def.displayName;
        titleText.color = Rarities.Text(inst.Rarity);
        bodyText.text = DescribeInstance(inst);
        ShowSkillIcons(inst.skillIds);
        group.alpha = 1f;
        Follow();
    }

    void ShowPreview(EquipmentInstance inst, EquipSlotType previewSlot)
    {
        hovered = inst;
        if (inst == null || inst.def == null) { group.alpha = 0f; return; }
        var c = Character.Instance;
        var def = inst.def;
        titleText.text = (inst.upgrade > 0 ? "+" + inst.upgrade + " " : "") + def.displayName;
        titleText.color = Rarities.Text(inst.Rarity);

        var sb = new StringBuilder();
        sb.Append(DescribeInstance(inst));
        if (c != null)
        {
            var equipped = c.GetEquippedInstance(previewSlot);
            if (equipped != null && equipped.def != null && equipped != inst)
                sb.Append("\n\n<color=#B9B9B9>Replaces </color><color=#")
                  .Append(ColorUtility.ToHtmlStringRGB(Rarities.Text(equipped.Rarity))).Append('>')
                  .Append(equipped.upgrade > 0 ? "+" + equipped.upgrade + " " : "")
                  .Append(equipped.def.displayName).Append("</color>");

            var before = c.CurrentDerived;
            var after  = c.DeriveWith(previewSlot, inst);
            sb.Append("\n<color=#B9B9B9>If equipped:</color>");
            AppendDelta(sb, "ATK", before.atkMax, after.atkMax);
            AppendDelta(sb, "M.ATK", before.matkMax, after.matkMax);
            AppendDelta(sb, "DEF", before.def, after.def);
            AppendDelta(sb, "M.DEF", before.mdef, after.mdef);
            AppendDelta(sb, "HIT", before.hit, after.hit);
            AppendDelta(sb, "FLEE", before.flee, after.flee);
            AppendDelta(sb, "Max HP", before.maxHP, after.maxHP);
            AppendDeltaPct(sb, "CRIT", before.critChance, after.critChance);
        }
        bodyText.text = sb.ToString();
        ShowSkillIcons(inst.skillIds);
        group.alpha = 1f;
        Follow();
    }

    // Fill the icon strip with THIS copy's rolled skill icons, or hide it for items that carry none.
    void ShowSkillIcons(List<string> ids)
    {
        if (skillRow == null) return;
        int shown = 0;
        if (ids != null)
            for (int i = 0; i < ids.Count; i++)
            {
                var def = SkillCatalog.Get(ids[i]);
                if (def == null || def.icon == null) continue;
                GetIcon(shown++).sprite = def.icon;
            }
        for (int i = 0; i < skillIcons.Count; i++) skillIcons[i].gameObject.SetActive(i < shown);
        skillRow.gameObject.SetActive(shown > 0);
    }

    // Pooled icon cell i under skillRow — grows the pool on demand (only on hover, so no hot-path alloc).
    Image GetIcon(int i)
    {
        while (skillIcons.Count <= i)
        {
            var go = new GameObject("SkillIcon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(skillRow, false);
            var im = go.GetComponent<Image>();
            im.preserveAspect = true; im.raycastTarget = false;
            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = le.preferredHeight = 22;
            skillIcons.Add(im);
        }
        return skillIcons[i];
    }

    // ---- "hold Alt for skill details" ---------------------------------------------------------------------
    // Alt, not Shift: Shift is the WALK key (Player3DController.HeldShift), so reading a tooltip would change how
    // the character moves. Alt is already treated as a modifier (it suppresses dash) and is the genre convention.
    EquipmentInstance hovered;                  // the copy under the cursor right now (null = not hovering equipment)
    WeaponSkillDetailsPanel details;            // built on first use, so no scene re-bake is needed to get it

    static bool AltPressed() => Hotkeys.Down(KeyCode.LeftAlt) || Hotkeys.Down(KeyCode.RightAlt);

    /// <summary>True when the hovered item has skills worth a details panel — also what gates the hint line.</summary>
    static bool HasKit(EquipmentInstance inst) => inst != null && inst.skillIds != null && inst.skillIds.Count > 0;

    void Update()
    {
        if (group.alpha > 0f) Follow();
        UpdateSkillDetails();
    }

    // Alt PINS the window open; it is NOT held. Closing must never depend on the hover: the moment you leave the
    // item slot the cursor is in dead space between the tooltip and the panel, so a hover-lifetime window would shut
    // before you could reach its own scrollbar — the controls would be permanently out of reach. Open by Alt over a
    // weapon, close by Alt again or the X.
    void UpdateSkillDetails()
    {
        // ESCAPE always closes it, and it is checked FIRST. Hotkeys.CancelDown is deliberately not typing-guarded,
        // unlike Hotkeys.Down — so if a text field holds focus (very easy: you just typed /weapon in chat, and chat
        // keeps focus), Alt is swallowed and Escape is the only key that still gets through. Without this the window
        // could become genuinely un-closable by keyboard.
        if (details != null && details.IsOpen && Hotkeys.CancelDown()) { details.Hide(); Hotkeys.MarkCancelHandled(); return; }

        if (!AltPressed()) return;

        // Alt over a DIFFERENT weapon swaps what's on display; Alt with nothing new under the cursor closes it.
        if (details != null && details.IsOpen && (!HasKit(hovered) || details.Showing == hovered))
        { details.Hide(); return; }
        if (!HasKit(hovered)) return;                                        // nothing under the cursor to detail

        if (details == null)
        {
            if (canvas == null) return;
            details = new WeaponSkillDetailsPanel(canvas);
        }
        details.Show(hovered);
        details.PlaceBeside(panel);
    }

    void Follow()
    {
        Vector2 pos = (Vector2)Input.mousePosition + cursorOffset;
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        float w = panel.rect.width * scale;
        float h = panel.rect.height * scale;
        // pivot is top-left, so keep [pos.x .. pos.x+w] and [pos.y-h .. pos.y] on-screen
        pos.x = Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, Screen.width - w));
        pos.y = Mathf.Clamp(pos.y, h, Screen.height);
        panel.position = pos;
    }

    static string Describe(Item item)
    {
        var sb = new StringBuilder();
        AppendHeader(sb, item, item.rarity);
        AppendItemStats(sb, item);
        AppendUseEffect(sb, item);
        AppendFooter(sb, item);
        return sb.ToString();
    }

    // What a consumable DOES, in the same words the skill's own card uses — read straight off the skill it runs
    // (SkillTooltip is the one describer, so a potion and a spell can never word the same effect differently).
    static readonly System.Collections.Generic.List<string> useLines = new System.Collections.Generic.List<string>();
    static void AppendUseEffect(StringBuilder sb, Item item)
    {
        if (!item.IsUsable) return;
        var def = SkillCatalog.Get(item.useSkillId);
        if (def == null) return;

        useLines.Clear();
        SkillTooltip.EffectLines(def, SkillTooltip.Dialect.RichText, useLines);
        for (int i = 0; i < useLines.Count; i++)
            sb.Append("\n<color=#7FD1C9>").Append(useLines[i]).Append("</color>");
        sb.Append("\n<color=#7FD1C9>(right-click to use)</color>");
    }

    static string DescribeInstance(EquipmentInstance inst)
    {
        var sb = new StringBuilder();
        var def = inst.def;
        AppendHeader(sb, def, inst.Rarity);
        AppendItemStats(sb, def);
        AppendRolledSkills(sb, inst.skillIds);
        if (inst.upgrade > 0)
            sb.Append("\n<color=#F5B942>Enhancement +").Append(inst.upgrade).Append("</color>");

        int slots = def.cardSlots;
        if (slots > 0)
        {
            sb.Append("\n<color=#B9B9B9>Cards ").Append(inst.FilledSockets).Append('/').Append(slots).Append("</color>");
            if (inst.cards != null)
                foreach (var card in inst.cards)
                    if (card != null)
                        sb.Append("\n  <color=#C7B27A>◇ ").Append(card.displayName).Append("</color>");
        }
        AppendFooter(sb, def);
        return sb.ToString();
    }

    // Rarity-colored "<Rarity> · <Slot/Category>" line. The rarity is passed in because a COPY carries its own
    // (rolled at the drop) — the def's rarity is only the fallback for something with no instance behind it.
    static void AppendHeader(StringBuilder sb, Item item, Rarity rarity)
    {
        string category = item.slot != EquipSlotType.None ? Spaced(item.slot.ToString())
                        : item.IsUsable ? "Consumable" : item.category.ToString();
        sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(Rarities.Border(rarity)))
          .Append('>').Append(Rarities.Label(rarity)).Append("</color>")
          .Append(" <color=#B9B9B9>· ").Append(category).Append("</color>");
    }

    static void AppendItemStats(StringBuilder sb, Item item)
    {
        AppendStat(sb, "ATK", item.bonusAttack);
        AppendStat(sb, "DEF", item.bonusDefense);
        AppendStat(sb, "M.ATK", item.bonusMAtk);
        AppendStat(sb, "M.DEF", item.bonusMDef);
        AppendStat(sb, "Max HP", item.bonusHP);
        AppendStat(sb, "Max MP", item.bonusMP);
        AppendStat(sb, "STR", item.bonusStr);
        AppendStat(sb, "AGI", item.bonusAgi);
        AppendStat(sb, "VIT", item.bonusVit);
        AppendStat(sb, "INT", item.bonusInt);
        AppendStat(sb, "DEX", item.bonusDex);
        AppendStat(sb, "LUK", item.bonusLuk);
        AppendStat(sb, "CRIT", item.bonusCritPercent, "%");
        AppendStat(sb, "ASPD", item.bonusAspdPercent, "%");
    }

    // "Grants skills" block — THIS copy's rolled kit. Passives are marked, since they never take a hotbar slot:
    // they run on their trigger the moment the weapon is worn.
    static void AppendRolledSkills(StringBuilder sb, List<string> ids)
    {
        if (ids == null || ids.Count == 0) return;
        sb.Append("\n<color=#B9B9B9>Grants skills</color>");
        for (int i = 0; i < ids.Count; i++)
        {
            var def = SkillCatalog.Get(ids[i]);
            sb.Append("\n  <color=#8FB7E0>◈ ").Append(def != null ? def.displayName : ids[i]);
            if (def != null && def.IsUltimate) sb.Append(" <color=#F5B942>(ultimate)</color>");
            else if (def != null && def.IsPassive) sb.Append(" <color=#9FD8A8>(passive)</color>");
            sb.Append("</color>");
        }
        // The kit is the weapon's identity, so say out loud that the full text is one key away.
        sb.Append("\n<color=#8A8F99><i>Press Alt for skill details</i></color>");
    }

    static void AppendFooter(StringBuilder sb, Item item)
    {
        // Weight and value on their own lines — packed on one row they read as cramped.
        if (item.weight > 0)
            sb.Append("\n<color=#8A8F99>Weight ").Append(item.weight).Append("</color>");
        if (item.zeny > 0)
            sb.Append("\n<color=#8A8F99>Value ").Append(item.zeny).Append("z</color>");
        if (!string.IsNullOrWhiteSpace(item.description))
            sb.Append("\n\n<color=#D8D8D8>").Append(item.description).Append("</color>");
    }

    static void AppendStat(StringBuilder sb, string label, int v, string suffix = "")
    {
        if (v == 0) return;
        sb.Append(v > 0 ? "\n<color=#7FD17F>+" : "\n<color=#E08A8A>")
          .Append(v).Append(suffix).Append(' ').Append(label).Append("</color>");
    }

    // Green/red "LABEL  before → after (+delta)" preview rows. Unchanged rows are skipped.
    static void AppendDelta(StringBuilder sb, string label, int before, int after)
    {
        if (before == after) return;
        int d = after - before;
        string color = d > 0 ? "#7FD17F" : "#E08A8A";
        sb.Append("\n<color=#C9C9C9>").Append(label).Append("</color>  ")
          .Append(before).Append(" → ").Append(after)
          .Append(" <color=").Append(color).Append('>').Append(d > 0 ? "+" : "").Append(d).Append("</color>");
    }

    static void AppendDeltaPct(StringBuilder sb, string label, float before, float after)
    {
        int b = Mathf.RoundToInt(before * 100f), a = Mathf.RoundToInt(after * 100f);
        if (b == a) return;
        int d = a - b;
        string color = d > 0 ? "#7FD17F" : "#E08A8A";
        sb.Append("\n<color=#C9C9C9>").Append(label).Append("</color>  ")
          .Append(b).Append("% → ").Append(a).Append("% <color=").Append(color).Append('>')
          .Append(d > 0 ? "+" : "").Append(d).Append("%</color>");
    }

    // "OffHand" -> "Off Hand"
    static string Spaced(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i])) sb.Append(' ');
            sb.Append(s[i]);
        }
        return sb.ToString();
    }
}
