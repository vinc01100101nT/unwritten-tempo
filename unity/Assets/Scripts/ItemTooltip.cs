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

    /// <summary>Show a tooltip whose words were worked out elsewhere — an item linked in chat, whose title, colour and
    /// card the SERVER built from the sender's own copy. Drawn as given; nothing is worked out here.</summary>
    public static void ShowText(string title, Color titleColour, string body)
    {
        if (Suppressed || Instance == null) return;
        Instance.previewing = null;
        Instance.titleText.text = title ?? "";
        Instance.titleText.color = titleColour;
        Instance.bodyText.text = body ?? "";
        Instance.ShowSkillIcons(null);
        Instance.group.alpha = 1f;
        Instance.Follow();
    }

    /// <summary>The card's words for a thing — a piece's own (<paramref name="piece"/>), or the item's. What a chat link
    /// carries, built on the server by the same lines the bag's tooltip draws.</summary>
    public static string BodyFor(Item def, EquipmentInstance piece)
        => piece != null && piece.def != null ? DescribeInstance(piece) : def != null ? Describe(def) : "";

    /// <summary>Hide the tooltip (no-op if the UI isn't up yet).</summary>
    public static void HideTip()
    {
        if (Instance == null) return;
        Instance.group.alpha = 0f;
    }

    void Show(Item item)
    {
        previewing = null;
        if (item == null) { group.alpha = 0f; return; }
        titleText.text = item.displayName;
        titleText.color = Rarities.Color(Rarities.Of(item, null));
        bodyText.text = Describe(item);
        ShowSkillIcons(null);   // a kit belongs to a COPY, not the shared def — nothing to show without an instance
        group.alpha = 1f;
        Follow();   // place it now so it doesn't flash at the previous spot for a frame
    }

    void Show(EquipmentInstance inst)
    {
        previewing = null;
        if (inst == null || inst.def == null) { group.alpha = 0f; return; }
        var def = inst.def;
        titleText.text = CopyTitle(inst);
        titleText.color = Rarities.Color(Rarities.Of(def, inst));
        bodyText.text = DescribeInstance(inst);
        ShowSkillIcons(null);
        group.alpha = 1f;
        Follow();
    }

    // The piece whose "if equipped" lines are being waited for, and the answers' version when it was drawn — so the
    // tooltip redraws once, when the server's lines arrive, while the pointer is still on it.
    EquipmentInstance previewing;
    EquipSlotType previewingSlot;
    int previewDrawnVersion = -1;

    void ShowPreview(EquipmentInstance inst, EquipSlotType previewSlot)
    {
        if (inst == null || inst.def == null) { group.alpha = 0f; previewing = null; return; }
        titleText.text = CopyTitle(inst);
        titleText.color = Rarities.Color(Rarities.Of(inst.def, inst));
        previewing = inst;
        previewingSlot = previewSlot;
        DrawPreviewBody();
        ShowSkillIcons(null);
        group.alpha = 1f;
        Follow();
    }

    // The piece's own card, then the server's "if equipped" lines (EquipPreview) — asked for when not yet here.
    void DrawPreviewBody()
    {
        var inst = previewing;
        previewDrawnVersion = EquipPreview.Version;
        inst.EnsureCopyId();
        string body = DescribeInstance(inst);
        if (EquipPreview.TryGetSent(inst.copyId, previewingSlot, out string ifEquipped)) body += ifEquipped;
        else
        {
            EquipPreview.Ask(inst.copyId, previewingSlot);
            body += "\n" + DimTag + "If equipped: …</color>";
        }
        bodyText.text = body;
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
        if (group.alpha <= 0f) { previewing = null; return; }
        Follow();
        if (previewing != null && previewDrawnVersion != EquipPreview.Version) DrawPreviewBody();
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
        AppendRarity(sb, Rarities.Of(item, null));
        AppendAttackKind(sb, item);
        AppendUseEffect(sb, item);
        AppendFooter(sb, item);
        AppendMaterialUse(sb, item);
        return sb.ToString();
    }

    // What a crafting material is FOR, under its description — read off its own kind and step, so every material
    // (and every one authored later) says it without a line typed per item.
    static void AppendMaterialUse(StringBuilder sb, Item item)
    {
        string use;
        switch (item.materialKind)
        {
            case MaterialKind.TemperDust:
                use = "Used to fold a piece at a smith, loom or tannery.";
                break;
            case MaterialKind.QuenchOil:
                use = "Used to quench a piece at a smith, loom or tannery.";
                break;
            case MaterialKind.Ink:
                use = "Writes one random stat onto a piece of gear. Stronger Ink writes a bigger number.";
                break;
            case MaterialKind.Salt:
                use = "Put into a quench to raise its chance of going up a grade. Spent whether it works or not. " +
                      "Stronger Salt raises it more.";
                break;
            default:
                return;
        }
        sb.Append("\n\n" + UseTag).Append(use);
        if (CraftingMath.Merges(item.materialKind) && item.mergesInto != null)
            sb.Append("\nMerge ").Append(CraftingMath.MergeCount).Append(" into 1 ").Append(item.mergesInto.displayName).Append('.');
        sb.Append("</color>");
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
            sb.Append("\n" + UseTag).Append(useLines[i]).Append("</color>");
        sb.Append("\n" + UseTag + "(double-click to use)</color>");
    }

    static string DescribeInstance(EquipmentInstance inst)
    {
        var sb = new StringBuilder();
        var def = inst.def;
        AppendHeader(sb, def);
        AppendRarity(sb, Rarities.Of(def, inst));
        AppendAttackKind(sb, def);
        AppendCopyStats(sb, inst);
        AppendTrademark(sb, inst);
        AppendWord(sb, inst);

        int slots = def.cardSlots;
        if (slots > 0)
        {
            sb.Append("\n" + DimTag + "Cards ").Append(inst.FilledSockets).Append('/').Append(slots).Append("</color>");
            if (inst.cards != null)
                foreach (var card in inst.cards)
                    if (card != null)
                        sb.Append("\n  " + CardTag + "◇ ").Append(card.displayName).Append("</color>");
        }
        AppendFooter(sb, def);
        return sb.ToString();
    }

    // "<Slot/Category>" line. Its grade is in the title, never a colour: a grade says what the piece is worth, a
    // rarity only how rare it is to come by (the line below).
    static void AppendHeader(StringBuilder sb, Item item)
    {
        // The slot's name from THE one list of slot words (EquipSlots.Label) — never the enum's own name, which says
        // "Shield" and "Boots" where a player reads "Off-hand" and "Shoes".
        string category = item.slot != EquipSlotType.None ? EquipSlots.Label(item.slot)
                        : item.IsUsable ? "Consumable" : item.category.ToString();
        sb.Append(DimTag).Append(category).Append("</color>");
    }

    // The rarity named, in its own colour — the same colour the title is written in (Rarities owns both).
    static void AppendRarity(StringBuilder sb, Rarity rarity)
    {
        sb.Append('\n').Append(Rarities.Tag(rarity)).Append(Rarities.Label(rarity)).Append("</color>");
    }


    // Rich-text tints, read from the interface theme once: labels, values, a use effect, a slotted card, and a stat
    // going up or down.
    internal static readonly string GoodHex = UiKit.Hex(UiKit.Good);
    internal static readonly string BadHex = UiKit.Hex(UiKit.Bad);
    internal static readonly string DimTag = "<color=" + UiKit.Hex(UiKit.TextDim) + ">";
    internal static readonly string MainTag = "<color=" + UiKit.Hex(UiKit.TextMain) + ">";
    static readonly string UseTag = "<color=" + UiKit.Hex(UiKit.Accent) + ">";
    static readonly string CardTag = "<color=" + UiKit.Hex(UiKit.Accent) + ">";
    static readonly string GoodTag = "<color=" + GoodHex + ">";
    static readonly string BadTag = "<color=" + BadHex + ">";

    /// <summary>How a copy is named on screen. A weapon with a trademark goes by its trademark skill's own name — a
    /// form of its weapon's style: its folds, the name, then its grade — "+15 Petal Storm [C]". Anything else: its
    /// grade, then its folds, then the piece — "B +12 Sword".</summary>
    public static string CopyTitle(EquipmentInstance inst)
    {
        if (inst == null || inst.def == null) return "";
        string grade = inst.quality == Quality.Raw ? "Raw" : inst.quality.ToString();
        var trademark = Trademarks.SkillOf(inst);
        if (trademark != null && !string.IsNullOrEmpty(trademark.displayName))
            return (inst.upgrade > 0 ? "+" + inst.upgrade + " " : "") + trademark.displayName + " [" + grade + "]";
        // AN ARMOUR PIECE WITH A WORD reads the same way, the word before the design: "+15 Tireless Iron Sabatons [C]".
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
        sb.Append("\n\n" + DimTag + "Trademark Skill: </color>").Append(skill.displayName);
        TrademarkLines(skill, inst.trademarkLift, trademarkScratch);
        foreach (var line in trademarkScratch) sb.Append('\n').Append(line);
        trademarkScratch.Clear();
    }

    static readonly System.Collections.Generic.List<string> trademarkScratch = new System.Collections.Generic.List<string>();

    /// <summary>What a trademark of <paramref name="skill"/> at <paramref name="lift"/> raises, one line per effect with a
    /// main number — "+12% damage". The one wording of it: this card and the wiki's trademark table both print it.</summary>
    public static void TrademarkLines(SkillDef skill, float lift, System.Collections.Generic.List<string> into)
    {
        into.Clear();
        if (skill == null) return;
        string pct = "+" + Mathf.RoundToInt(lift * 100f) + "% ";
        for (int b = 0; b < skill.bindings.Count; b++)
        {
            var fx = skill.bindings[b]?.effects;
            if (fx == null) continue;
            for (int i = 0; i < fx.Count; i++)
            {
                string word = SkillTooltip.MainNumberWord(fx[i]);
                if (word != null) into.Add(pct + word);
            }
        }
    }

    // THIS COPY's armour word — its name, then the one thing it does, at the finished amount the server wrote.
    static void AppendWord(StringBuilder sb, EquipmentInstance inst)
    {
        if (inst == null || inst.word == ArmourWord.None) return;
        sb.Append("\n\n" + DimTag).Append(inst.word).Append(": </color>")
          .Append(ArmourWords.Describe(inst.word, inst.wordAmount));
    }

    // A weapon says which of the two physical attacks it swings with; nothing else about it is spelled out.
    static void AppendAttackKind(StringBuilder sb, Item item)
    {
        if (item.slot != EquipSlotType.Weapon || item.weaponType == WeaponType.None) return;
        if (WeaponTypes.RangedByStat(item.weaponType))
            sb.Append('\n').Append("MELEE or RANGED attack — whichever of STR or DEX is higher");
        else
            sb.Append('\n').Append(WeaponTypes.SwingsRanged(item.weaponType, 0, 0) ? "RANGED attack" : "MELEE attack");
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
        sb.Append("\n" + DimTag + "Written ").Append(used).Append('/').Append(slots).Append("</color>");
    }

    static void AppendFooter(StringBuilder sb, Item item)
    {
        // Weight and value on their own lines — packed on one row they read as cramped.
        if (item.weight > 0)
            sb.Append("\n" + DimTag + "Weight ").Append(item.weight).Append("</color>");
        // A SELL VALUE OF 0 IS NOT SHOWN AS A 0: it means no shop will buy it, and it says so.
        if (item.CanBeSold)
            sb.Append("\n" + DimTag + "Value ").Append(item.zeny).Append("z</color>");
        else
            sb.Append("\n" + DimTag).Append(Item.CannotBeSoldText).Append("</color>");
        if (!string.IsNullOrWhiteSpace(item.description))
            sb.Append("\n\n" + MainTag).Append(item.description).Append("</color>");
    }

    static void AppendStat(StringBuilder sb, string label, int v, string suffix = "")
    {
        if (v == 0) return;
        sb.Append(v > 0 ? "\n" + GoodTag + "+" : "\n" + BadTag)
          .Append(v).Append(suffix).Append(' ').Append(label).Append("</color>");
    }

    static void AppendStat(StringBuilder sb, string label, float v, string suffix = "")
    {
        if (Mathf.Approximately(v, 0f)) return;
        sb.Append(v > 0f ? "\n" + GoodTag + "+" : "\n" + BadTag)
          .Append(v.ToString("0.##")).Append(suffix).Append(' ').Append(label).Append("</color>");
    }

}
