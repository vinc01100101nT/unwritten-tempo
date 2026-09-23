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

    /// <summary>Show the tooltip for an equipment instance (renders its grade, folds, written stats, cards).</summary>
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
    }

    void Show(Item item)
    {
        if (item == null) { group.alpha = 0f; return; }
        titleText.text = item.displayName;
        titleText.color = PlainTitle;
        bodyText.text = Describe(item);
        ShowSkillIcons(null);   // a kit belongs to a COPY, not the shared def — nothing to show without an instance
        group.alpha = 1f;
        Follow();   // place it now so it doesn't flash at the previous spot for a frame
    }

    void Show(EquipmentInstance inst)
    {
        if (inst == null || inst.def == null) { group.alpha = 0f; return; }
        var def = inst.def;
        titleText.text = CopyTitle(inst);
        titleText.color = PlainTitle;
        bodyText.text = DescribeInstance(inst);
        ShowSkillIcons(null);
        group.alpha = 1f;
        Follow();
    }

    void ShowPreview(EquipmentInstance inst, EquipSlotType previewSlot)
    {
        if (inst == null || inst.def == null) { group.alpha = 0f; return; }
        var c = Character.Instance;
        var def = inst.def;
        titleText.text = CopyTitle(inst);
        titleText.color = PlainTitle;

        var sb = new StringBuilder();
        sb.Append(DescribeInstance(inst));
        if (c != null)
        {
            var equipped = c.GetEquippedInstance(previewSlot);
            if (equipped != null && equipped.def != null && equipped != inst)
                sb.Append("\n\n<color=#B9B9B9>Replaces </color><color=#D8D8D8>")
                  .Append(CopyTitle(equipped)).Append("</color>");

            var before = c.CurrentDerived;
            var after  = c.DeriveWith(previewSlot, inst);
            sb.Append("\n<color=#B9B9B9>If equipped:</color>");
            AppendDelta(sb, "MELEE ATK", before.meleeMax, after.meleeMax);
            AppendDelta(sb, "RANGED ATK", before.rangedMax, after.rangedMax);
            AppendDelta(sb, "M.ATK", before.matkMax, after.matkMax);
            AppendDelta(sb, "DEF", before.def, after.def);
            AppendDelta(sb, "M.DEF", before.mdef, after.mdef);
            AppendDelta(sb, "HIT", before.hit, after.hit);
            AppendDelta(sb, "FLEE", before.flee, after.flee);
            AppendDelta(sb, "Max HP", before.maxHP, after.maxHP);
            AppendDeltaPct(sb, "CRIT", before.critChance, after.critChance);
        }
        bodyText.text = sb.ToString();
        ShowSkillIcons(null);
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
                if (def == null) continue;
                var cell = GetIcon(shown++);
                cell.sprite = null;
                cell.color = SkillSymbol.ColorFor(def);
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

    void Update()
    {
        if (group.alpha > 0f) Follow();
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
        AppendHeader(sb, item);
        AppendAttackKind(sb, item);
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
        AppendHeader(sb, def);
        AppendAttackKind(sb, def);
        AppendCopyStats(sb, inst);
        AppendTrademark(sb, inst);
        AppendWord(sb, inst);

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

    // "<Slot/Category>" line. Nothing here is coloured by a tier: a crafted piece says what it is and what it
    // has been through, and there is no rarity in the game to colour it by.
    static void AppendHeader(StringBuilder sb, Item item)
    {
        // The slot's name from THE one list of slot words (EquipSlots.Label) — never the enum's own name, which says
        // "Shield" and "Boots" where a player reads "Off-hand" and "Shoes".
        string category = item.slot != EquipSlotType.None ? EquipSlots.Label(item.slot)
                        : item.IsUsable ? "Consumable" : item.category.ToString();
        sb.Append("<color=#B9B9B9>").Append(category).Append("</color>");
    }

    static readonly Color PlainTitle = new Color(0.91f, 0.91f, 0.91f);

    /// <summary>How a copy is named on screen. A weapon with a trademark goes by the trademark's name: its folds,
    /// the name, then its grade — "+15 Hadori [C]". Anything else: its grade, then its folds, then the piece —
    /// "B +12 Sword".</summary>
    public static string CopyTitle(EquipmentInstance inst)
    {
        if (inst == null || inst.def == null) return "";
        string grade = inst.quality == Quality.Raw ? "Raw" : inst.quality.ToString();
        var trademark = Trademarks.SkillOf(inst);
        if (trademark != null && !string.IsNullOrEmpty(trademark.trademarkName))
            return (inst.upgrade > 0 ? "+" + inst.upgrade + " " : "") + trademark.trademarkName + " [" + grade + "]";
        // AN ARMOUR PIECE WITH A WORD reads the same way, the word before the design: "+15 Fleet Iron Sabatons [C]".
        if (inst.word != ArmourWord.None)
            return (inst.upgrade > 0 ? "+" + inst.upgrade + " " : "") + inst.word + " " + inst.def.displayName + " [" + grade + "]";
        string folds = inst.upgrade > 0 ? " +" + inst.upgrade : "";
        return grade + folds + " " + inst.def.displayName;
    }

    // THIS COPY's trademark: the skill it empowers, then one line per effect of that skill the weapon raises — read
    // off the skill as it is now, so an edited skill is described as edited. The percentage is the finished lift the
    // server wrote on the copy.
    static void AppendTrademark(StringBuilder sb, EquipmentInstance inst)
    {
        var skill = Trademarks.SkillOf(inst);
        if (skill == null) return;
        sb.Append("\n\n<color=#B9B9B9>Trademark Skill: </color>").Append(skill.displayName);
        string pct = "+" + Mathf.RoundToInt(inst.trademarkLift * 100f) + "% ";
        for (int b = 0; b < skill.bindings.Count; b++)
        {
            var fx = skill.bindings[b]?.effects;
            if (fx == null) continue;
            for (int i = 0; i < fx.Count; i++)
            {
                string word = SkillTooltip.MainNumberWord(fx[i]);
                if (word != null) sb.Append('\n').Append(pct).Append(word);
            }
        }
    }

    // THIS COPY's armour word — its name, then the one thing it does, at the finished amount the server wrote.
    static void AppendWord(StringBuilder sb, EquipmentInstance inst)
    {
        if (inst == null || inst.word == ArmourWord.None) return;
        sb.Append("\n\n<color=#B9B9B9>").Append(inst.word).Append(": </color>")
          .Append(ArmourWords.Describe(inst.word, inst.wordAmount));
    }

    // A weapon says which of the two physical attacks it swings with; nothing else about it is spelled out.
    static void AppendAttackKind(StringBuilder sb, Item item)
    {
        if (item.slot != EquipSlotType.Weapon || item.weaponType == WeaponType.None) return;
        sb.Append('\n').Append(WeaponTypes.IsRanged(item.weaponType) ? "RANGED attack" : "MELEE attack");
    }

    // THIS COPY's own numbers — what it really adds, read from the same sum the stat sheet uses, so the card and
    // the sheet can never disagree. Its grade and folds are already inside them; cards have their own lines below.
    static void AppendCopyStats(StringBuilder sb, EquipmentInstance inst)
    {
        var eb = default(EquipBonuses);
        inst.AddPieceInto(ref eb);
        AppendStat(sb, "ATK", eb.atk);
        AppendStat(sb, "DEF", eb.def);
        AppendStat(sb, "M.ATK", eb.matk);
        AppendStat(sb, "M.DEF", eb.mdef);
        AppendStat(sb, "Max HP", eb.hp);
        AppendStat(sb, "Max MP", eb.mp);
        AppendStat(sb, "STR", eb.str);
        AppendStat(sb, "AGI", eb.agi);
        AppendStat(sb, "VIT", eb.vit);
        AppendStat(sb, "INT", eb.intel);
        AppendStat(sb, "DEX", eb.dex);
        AppendStat(sb, "LUK", eb.luk);
        AppendStat(sb, "ACC", eb.accuracy);
        AppendStat(sb, "EVA", eb.evasion);
        AppendStat(sb, "CRIT", eb.critPercent, "%");
        AppendStat(sb, "CRIT DMG", eb.critDamagePercent, "%");
        AppendStat(sb, "ASPD", eb.aspdPercent, "%");
        AppendStat(sb, "CAST SPEED", eb.castSpeedPercent, "%");
        AppendInkSlots(sb, inst);
    }

    /// <summary>The label a written stat goes by on screen — the SAME word the stat line above uses for it, so the
    /// station window's slot buttons and the item card can never call one stat two things.</summary>
    public static string InkStatLabel(InkStat stat)
    {
        switch (stat)
        {
            case InkStat.Attack:       return "ATK";
            case InkStat.Defense:      return "DEF";
            case InkStat.MagicAttack:  return "M.ATK";
            case InkStat.MagicDefense: return "M.DEF";
            case InkStat.HP:           return "Max HP";
            case InkStat.MP:           return "Max MP";
            case InkStat.STR:          return "STR";
            case InkStat.AGI:          return "AGI";
            case InkStat.VIT:          return "VIT";
            case InkStat.INT:          return "INT";
            case InkStat.DEX:          return "DEX";
            case InkStat.LUK:          return "LUK";
            case InkStat.Accuracy:     return "ACC";
            case InkStat.Evasion:      return "EVA";
            case InkStat.CritChance:   return "CRIT";
            case InkStat.CritDamage:   return "CRIT DMG";
            case InkStat.AttackSpeed:  return "ASPD";
            case InkStat.CastSpeed:    return "CAST SPEED";
            default:                   return stat.ToString();
        }
    }

    // How many stats this copy's grade lets it carry, and how many are written — so an empty slot reads as room
    // rather than as nothing.
    static void AppendInkSlots(StringBuilder sb, EquipmentInstance inst)
    {
        int slots = inst.InkSlots;
        if (slots <= 0) return;
        int used = inst.inkStats != null ? inst.inkStats.Count : 0;
        sb.Append("\n<color=#B9B9B9>Written ").Append(used).Append('/').Append(slots).Append("</color>");
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

    static void AppendStat(StringBuilder sb, string label, float v, string suffix = "")
    {
        if (Mathf.Approximately(v, 0f)) return;
        sb.Append(v > 0f ? "\n<color=#7FD17F>+" : "\n<color=#E08A8A>")
          .Append(v.ToString("0.##")).Append(suffix).Append(' ').Append(label).Append("</color>");
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
}
