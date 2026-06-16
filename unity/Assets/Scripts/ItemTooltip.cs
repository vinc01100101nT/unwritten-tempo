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

    /// <summary>Hide the tooltip (no-op if the UI isn't up yet).</summary>
    public static void HideTip() { if (Instance != null) Instance.group.alpha = 0f; }

    void Show(Item item)
    {
        if (item == null) { group.alpha = 0f; return; }
        titleText.text = item.displayName;
        bodyText.text = Describe(item);
        group.alpha = 1f;
        Follow();   // place it now so it doesn't flash at the previous spot for a frame
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

        string category = item.slot != EquipSlotType.None ? Spaced(item.slot.ToString())
                        : item.healAmount > 0 ? "Consumable" : "Item";
        sb.Append("<color=#B9B9B9>").Append(category).Append("</color>");

        AppendStat(sb, "ATK", item.bonusAttack);
        AppendStat(sb, "DEF", item.bonusDefense);
        AppendStat(sb, "Max HP", item.bonusHP);
        if (item.healAmount > 0)
            sb.Append("\n<color=#7FD1C9>Heals ").Append(item.healAmount).Append(" HP  (right-click)</color>");

        if (!string.IsNullOrWhiteSpace(item.description))
            sb.Append("\n\n<color=#D8D8D8>").Append(item.description).Append("</color>");

        return sb.ToString();
    }

    static void AppendStat(StringBuilder sb, string label, int v)
    {
        if (v == 0) return;
        sb.Append(v > 0 ? "\n<color=#7FD17F>+" : "\n<color=#E08A8A>")
          .Append(v).Append(' ').Append(label).Append("</color>");
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
