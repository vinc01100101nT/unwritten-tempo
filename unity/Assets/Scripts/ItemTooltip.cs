using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// THE item card, and the small name strip beside it. Every place an item is shown answers the pointer the same way:
/// HOVERING it puts up only its full name, white on a dark see-through strip by the cursor (<see cref="ShowName"/>);
/// RIGHT-CLICKING it opens this card and leaves it open (<see cref="Pin"/>). The card is Ragnarok's item window, at
/// one fixed size: the item's picture at the left; its name in a bar along the top — drag the bar to move the card —
/// with the × at the corner; its details under the name, with a scrollbar once they run longer than the card; and its
/// card slots along the bottom, a diamond each. It closes from its ×, by clicking anywhere else, or by right-clicking
/// another item, which opens that one instead.
///
/// <para>THE TRIGGERS, every one of them — keep this list whole when adding a new place items are drawn:
/// <see cref="ItemSlot"/> (every bag, worn, storage, crafting and progression box, every shop row, and the hotbar's
/// item and gear tiles) and <see cref="ChatLinkRow"/> (an item named in chat).</para>
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

    [Tooltip("The item's picture, in the panel at the card's left.")]
    public Image icon;

    [Tooltip("The scrolling part holding the details.")]
    public ScrollRect detailsScroll;

    [Tooltip("One diamond per card slot, along the bottom strip.")]
    public Image[] slotDiamonds;

    /// <summary>The most card slots any piece has, and so how many diamonds the strip carries.</summary>
    public const int MostCardSlots = 4;

    [Tooltip("The hover strip: the item's full name, white on a dark see-through strip by the cursor.")]
    public RectTransform namePanel;
    public TMP_Text nameText;

    Canvas canvas;
    CanvasGroup group, nameGroup;
    // Open from a right-click: it stays where it opened and catches the pointer, so its details can be scrolled.
    bool pinned;
    int pinnedFrame = -1;

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

    // The hover strip's fade. Looked up on first use, NOT in Awake: the card is built by adding this component and
    // only then handing it its parts, and Awake runs inside that AddComponent — before namePanel is set.
    CanvasGroup NameGroup()
    {
        if (nameGroup == null && namePanel != null)
        {
            nameGroup = namePanel.GetComponent<CanvasGroup>();
            if (nameGroup == null) nameGroup = namePanel.gameObject.AddComponent<CanvasGroup>();
            nameGroup.blocksRaycasts = false;
            nameGroup.interactable = false;
        }
        return nameGroup;
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
        Instance.ShowPicture(null, null);
        Instance.ShowSlots(null, null);
        Instance.FitDetails();
        Instance.group.alpha = 1f;
        Instance.Follow();
    }

    /// <summary>The card's words for a thing — a piece's own (<paramref name="piece"/>), or the item's. What a chat link
    /// carries, built on the server by the same lines the bag's tooltip draws.</summary>
    public static string BodyFor(Item def, EquipmentInstance piece)
        => piece != null && piece.def != null ? DescribeInstance(piece) : def != null ? Describe(def) : "";

    /// <summary>HOVER: the item's full name on the dark strip by the cursor.</summary>
    public static void ShowName(string title)
    {
        var t = Instance;
        if (Suppressed || t == null || t.NameGroup() == null || string.IsNullOrEmpty(title)) return;
        if (t.nameText.text != title) t.nameText.text = title;
        t.nameGroup.alpha = 1f;
        t.FollowName();
    }

    public static void HideName()
    {
        if (Instance != null && Instance.nameGroup != null) Instance.nameGroup.alpha = 0f;
    }

    /// <summary>RIGHT-CLICK: open the card for <paramref name="item"/>, or <paramref name="copy"/> when it is a piece of
    /// gear — with the "if equipped" lines against <paramref name="previewSlot"/> when that is given — and leave it
    /// open.</summary>
    public static void Pin(Item item, EquipmentInstance copy, EquipSlotType? previewSlot = null)
    {
        if (Instance == null || (item == null && copy == null)) return;
        Suppressed = false;
        if (copy != null && previewSlot.HasValue && Character.Instance != null) Instance.ShowPreview(copy, previewSlot.Value);
        else if (copy != null) Instance.Show(copy);
        else Instance.Show(item);
        Instance.PinHere();
    }

    /// <summary>RIGHT-CLICK on words built elsewhere — an item linked in chat, whose words the server built from the
    /// sender's own copy. The same card, with <paramref name="def"/>'s picture and slots.</summary>
    public static void PinText(string title, Color titleColour, string body, Item def)
    {
        if (Instance == null) return;
        Suppressed = false;
        ShowText(title, titleColour, body);
        Instance.ShowPicture(def, null);
        Instance.ShowSlots(def, null);
        Instance.PinHere();
    }

    /// <summary>RIGHT-CLICK on a skill, wherever it is drawn: the same card — the skill's badge in the picture panel,
    /// its name in the title bar, its full description in the details, and no card-slot diamonds.</summary>
    public static void PinSkill(SkillDef def)
    {
        if (Instance == null || def == null) return;
        Suppressed = false;
        string name = string.IsNullOrEmpty(def.displayName) ? def.id : def.displayName;
        ShowText(name, UiKit.TextMain, SkillTooltip.Build(def));
        Instance.ShowBadge(def);
        Instance.PinHere();
    }

    [Tooltip("The skill badge in the picture panel: its colour, and its letters on it.")]
    public Image badge;
    public TMP_Text badgeText;

    // A skill's badge in the picture panel, in place of an item's picture.
    void ShowBadge(SkillDef def)
    {
        ShowPicture(null, null);
        if (badge == null) return;
        badge.gameObject.SetActive(true);
        badge.color = SkillSymbol.ColorFor(def);
        badgeText.text = def.Symbol;
    }

    /// <summary>Close the card.</summary>
    public static void Unpin()
    {
        if (Instance == null) return;
        Instance.pinned = false;
        Instance.group.alpha = 0f;
        Instance.group.blocksRaycasts = false;
        Instance.group.interactable = false;
    }

    void PinHere()
    {
        HideName();
        pinned = true;
        pinnedFrame = Time.frameCount;
        group.blocksRaycasts = true;
        group.interactable = true;
        Follow();
    }

    /// <summary>Take down what hovering put up: the name strip. An open card stays until it is closed.</summary>
    public static void HideTip()
    {
        if (Instance == null) return;
        HideName();
        if (!Instance.pinned) Instance.group.alpha = 0f;
    }

    void Show(Item item)
    {
        previewing = null;
        if (item == null) { group.alpha = 0f; return; }
        titleText.text = item.displayName;
        titleText.color = Rarities.Color(Rarities.Of(item, null));
        bodyText.text = Describe(item);
        ShowSkillIcons(null);   // a kit belongs to a COPY, not the shared def — nothing to show without an instance
        ShowPicture(item, null);
        ShowSlots(item, null);
        FitDetails();
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
        ShowPicture(def, inst);
        ShowSlots(def, inst);
        FitDetails();
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
        ShowPicture(inst.def, inst);
        ShowSlots(inst.def, inst);
        group.alpha = 1f;
        Follow();
    }

    // The item's picture in the panel at the card's left; an empty panel when there is none. Never a skill's badge.
    void ShowPicture(Item def, EquipmentInstance copy)
    {
        if (icon != null) ItemIcon.Show(icon, def);
        if (badge != null && badge.gameObject.activeSelf) badge.gameObject.SetActive(false);
    }

    // A diamond per card slot the piece has: grey when empty, the accent colour with the card's name on hover when
    // a card sits in it. A thing with no slots shows none.
    void ShowSlots(Item def, EquipmentInstance copy)
    {
        if (slotDiamonds == null) return;
        int slots = def != null ? Mathf.Min(def.cardSlots, slotDiamonds.Length) : 0;
        for (int i = 0; i < slotDiamonds.Length; i++)
        {
            var d = slotDiamonds[i];
            bool on = i < slots;
            if (d.gameObject.activeSelf != on) d.gameObject.SetActive(on);
            if (!on) continue;
            var card = copy != null && copy.cards != null && i < copy.cards.Count ? copy.cards[i] : null;
            d.color = card != null ? UiKit.Accent : UiKit.ButtonEdge;
            string cardName = card != null ? card.displayName : null;
            HoverRelay.Set(d.gameObject, cardName != null ? () => ShowName(cardName) : (System.Action)null, HideName);
        }
    }

    // A new card's details start from the top.
    void FitDetails()
    {
        if (detailsScroll != null) detailsScroll.verticalNormalizedPosition = 1f;
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
        FitDetails();
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
        if (nameGroup != null && nameGroup.alpha > 0f) FollowName();
        if (group.alpha <= 0f) { previewing = null; pinned = false; return; }
        if (pinned)
        {
            // A click anywhere off the card closes it — not the click that opened it, and not a click on the card.
            if ((PointerInput.PrimaryDown || PointerInput.SecondaryDown) && Time.frameCount != pinnedFrame
                && !RectTransformUtility.RectangleContainsScreenPoint(panel, PointerInput.Position, EventCamera()))
                Unpin();
        }
        else Follow();
        if (previewing != null && previewDrawnVersion != EquipPreview.Version) DrawPreviewBody();
    }

    Camera EventCamera() => canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

    void FollowName()
    {
        Vector2 pos = (Vector2)Input.mousePosition + cursorOffset;
        float scale = canvas != null ? canvas.scaleFactor : 1f;
        float w = namePanel.rect.width * scale;
        float h = namePanel.rect.height * scale;
        pos.x = Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, Screen.width - w));
        pos.y = Mathf.Clamp(pos.y, h, Screen.height);
        namePanel.position = pos;
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
                use = item.materialStep >= 3
                    ? "Put into a quench: if the quench fails, the piece keeps its grade, back to +9, and the chance to rise is 50% higher. Spent whether it works or not."
                    : item.materialStep == 2
                        ? "Put into a quench: if the quench fails, the piece keeps its grade, back to +4. Also raises the chance to rise by 25%. Spent whether it works or not."
                        : "Put into a quench: if the quench fails, the piece keeps its grade, back to +4. Spent whether it works or not.";
                break;
            default:
                return;
        }
        sb.Append("\n\n" + UseTag).Append(use);
        if (CraftingMath.Merges(item.materialKind) && item.mergesInto != null)
            sb.Append("\nMerge ").Append(CraftingMath.MergeCount(item.materialKind, item.materialStep)).Append(" into 1 ").Append(item.mergesInto.displayName).Append('.');
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
            sb.Append("\n\n" + DimTag + "Cards ").Append(inst.FilledSockets).Append('/').Append(slots).Append("</color>");
            if (inst.cards != null)
                foreach (var card in inst.cards)
                    if (card != null)
                        sb.Append("\n  " + CardTag + "◇ ").Append(card.displayName).Append("</color>");
        }
        sb.Append('\n');
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
        TrademarkLines(skill, inst.trademarkLift, trademarkScratch, true);
        foreach (var line in trademarkScratch) sb.Append('\n').Append(line);
        trademarkScratch.Clear();
    }

    static readonly System.Collections.Generic.List<string> trademarkScratch = new System.Collections.Generic.List<string>();

    /// <summary>What a trademark of <paramref name="skill"/> at <paramref name="lift"/> raises, one line per effect with a
    /// main number — "+12% damage". The one wording of it: this card and the wiki's trademark table both print it.
    /// <paramref name="nameTheSkill"/> says whose number it is — "+12% to Petal Storm's damage" — for the card, where a
    /// line stands alone; the wiki's table already names the skill on the row.</summary>
    public static void TrademarkLines(SkillDef skill, float lift, System.Collections.Generic.List<string> into,
                                      bool nameTheSkill = false)
    {
        into.Clear();
        if (skill == null) return;
        string owner = !nameTheSkill ? ""
                     : "to " + skill.displayName + (skill.displayName.EndsWith("s") ? "' " : "'s ");
        string pct = "+" + Mathf.RoundToInt(lift * 100f) + "% " + owner;
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

    // THIS COPY's numbers, in two groups a blank line apart: first what the piece itself gives (the folds' numbers,
    // grade and rarity already inside them), large and bold — then what Ink wrote on it, at ordinary weight, with how
    // many it may carry. Both read through the same sums the stat sheet uses (CraftingMath.AddPiece / AddInk), so
    // the card and the sheet can never disagree. Cards and the trademark have their own groups below.
    static void AppendCopyStats(StringBuilder sb, EquipmentInstance inst)
    {
        var own = default(EquipBonuses);
        CraftingMath.AddPiece(inst.piece, null, null, ref own);
        int before = sb.Length;
        sb.Append("\n<b><size=115%>");
        int opened = sb.Length;
        AppendStatList(sb, own);
        if (sb.Length == opened) sb.Length = before;
        else sb.Append("</size></b>");
        AppendBase(sb, inst);

        var ink = default(EquipBonuses);
        int n = inst.inkStats != null && inst.inkValues != null ? Mathf.Min(inst.inkStats.Count, inst.inkValues.Count) : 0;
        for (int i = 0; i < n; i++) CraftingMath.AddInk(inst.inkStats[i], inst.inkValues[i], ref ink);
        before = sb.Length;
        sb.Append('\n');
        opened = sb.Length;
        AppendStatList(sb, ink);
        AppendInkSlots(sb, inst);
        if (sb.Length == opened) sb.Length = before;
    }

    // The piece's base as the server wrote it — base × roll, before rarity, folds and quenches: "Base 54 (108%)" for a
    // piece that rolled, "Base 50" for one that never did. An armour piece shows its first stat. Nothing is worked out.
    static void AppendBase(StringBuilder sb, EquipmentInstance inst)
    {
        var b = inst.baseStats;
        float first = b.attack != 0 ? b.attack : b.magicAttack != 0 ? b.magicAttack : b.defense != 0 ? b.defense
                    : b.magicDefense != 0 ? b.magicDefense : b.hp != 0 ? b.hp : b.mp != 0 ? b.mp
                    : b.evasion != 0 ? b.evasion : b.attackSpeedPercent;
        if (Mathf.Approximately(first, 0f)) return;
        sb.Append('\n').Append("Base ").Append(first.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
        if (inst.basePercent > 0) sb.Append(" (").Append(inst.basePercent).Append("%)");
    }

    static void AppendStatList(StringBuilder sb, in EquipBonuses eb)
    {
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

    /// <summary>One written stat as a line — "+80 Max HP", "+2.4% CRIT DMG" — the words the item card's stat lines use,
    /// for the station's slot buttons and the server's "Wrote …" sentence.</summary>
    public static string InkStatLine(InkStat stat, float value)
        => "+" + value.ToString("0.##") + (IsPercentStat(stat) ? "% " : " ") + InkStatLabel(stat);

    /// <summary>The written stats that are whole percents rather than flat numbers.</summary>
    public static bool IsPercentStat(InkStat s)
        => s == InkStat.CritChance || s == InkStat.CritDamage || s == InkStat.AttackSpeed || s == InkStat.CastSpeed;

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
            sb.Append("\n" + DimTag + "Value ").Append(Wallet.Amount(item.value)).Append("</color>");
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

/// <summary>The item card's ×. Acts on pointer DOWN, as every window's close does, so a small drag cannot eat it.</summary>
public class ItemCardClose : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler
{
    public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e) => ItemTooltip.Unpin();
}

/// <summary>An arrow at an end of the item card's scrollbar: each press moves the details one line that way.</summary>
public class ItemCardScrollStep : MonoBehaviour, UnityEngine.EventSystems.IPointerDownHandler
{
    public ScrollRect scroll;
    public int direction;
    const float Line = 18f;

    public void OnPointerDown(UnityEngine.EventSystems.PointerEventData e)
    {
        if (scroll == null || scroll.content == null || scroll.viewport == null) return;
        float hidden = scroll.content.rect.height - scroll.viewport.rect.height;
        if (hidden <= 0f) return;
        scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + direction * Line / hidden);
    }
}
