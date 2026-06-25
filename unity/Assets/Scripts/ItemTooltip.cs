using System.Text;
using UnityEngine;
using UnityEngine.UI;

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
    public Text titleText;
    public Text bodyText;

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
    public static void HideTip() { if (Instance != null) Instance.group.alpha = 0f; }

    void Show(Item item)
    {
        if (item == null) { group.alpha = 0f; return; }
        titleText.text = item.displayName;
        titleText.color = Rarities.Text(item.rarity);
        bodyText.text = Describe(item);
        group.alpha = 1f;
        Follow();   // place it now so it doesn't flash at the previous spot for a frame
    }

    void Show(EquipmentInstance inst)
    {
        if (inst == null || inst.def == null) { group.alpha = 0f; return; }
        var def = inst.def;
        titleText.text = (inst.upgrade > 0 ? "+" + inst.upgrade + " " : "") + def.displayName;
        titleText.color = Rarities.Text(def.rarity);
        bodyText.text = DescribeInstance(inst);
        group.alpha = 1f;
        Follow();
    }

    void ShowPreview(EquipmentInstance inst, EquipSlotType previewSlot)
    {
        if (inst == null || inst.def == null) { group.alpha = 0f; return; }
        var c = Character.Instance;
        var def = inst.def;
        titleText.text = (inst.upgrade > 0 ? "+" + inst.upgrade + " " : "") + def.displayName;
        titleText.color = Rarities.Text(def.rarity);

        var sb = new StringBuilder();
        sb.Append(DescribeInstance(inst));
        if (c != null)
        {
            var equipped = c.GetEquippedInstance(previewSlot);
            if (equipped != null && equipped.def != null && equipped != inst)
                sb.Append("\n\n<color=#B9B9B9>Replaces </color><color=#")
                  .Append(ColorUtility.ToHtmlStringRGB(Rarities.Text(equipped.def.rarity))).Append('>')
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
        group.alpha = 1f;
        Follow();
    }

    void Update() { if (group.alpha > 0f) Follow(); }

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
        AppendItemStats(sb, item);
        if (item.healAmount > 0)
            sb.Append("\n<color=#7FD1C9>Heals ").Append(item.healAmount).Append(" HP  (right-click)</color>");
        AppendFooter(sb, item);
        return sb.ToString();
    }

    static string DescribeInstance(EquipmentInstance inst)
    {
        var sb = new StringBuilder();
        var def = inst.def;
        AppendHeader(sb, def);
        AppendItemStats(sb, def);
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

    // Rarity-colored "<Rarity> · <Slot/Category>" line.
    static void AppendHeader(StringBuilder sb, Item item)
    {
        string category = item.slot != EquipSlotType.None ? Spaced(item.slot.ToString())
                        : item.healAmount > 0 ? "Consumable" : item.category.ToString();
        sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(Rarities.Border(item.rarity)))
          .Append('>').Append(Rarities.Label(item.rarity)).Append("</color>")
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

    static void AppendFooter(StringBuilder sb, Item item)
    {
        if (item.weight > 0 || item.zeny > 0)
        {
            sb.Append("\n<color=#8A8F99>");
            if (item.weight > 0) sb.Append("Weight ").Append(item.weight);
            if (item.weight > 0 && item.zeny > 0) sb.Append("   ");
            if (item.zeny > 0) sb.Append(item.zeny).Append("z");
            sb.Append("</color>");
        }
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
