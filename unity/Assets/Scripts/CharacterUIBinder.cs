using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wires the (persistent) UI to the (persistent) <see cref="Character"/>: drives
/// the HUD health bar, the Character-panel stats text, and pushes equip-slot
/// changes into the character so gear affects stats. Lives on the GameUICanvas;
/// because both it and Character persist, it binds once and never needs to
/// re-find anything across scene loads.
/// </summary>
public class CharacterUIBinder : MonoBehaviour
{
    Image healthFill;
    Text healthLabel;
    Text statsText;
    ItemSlot[] equipSlots;
    bool bound;

    void Start() => Bind();
    void Update() { if (!bound) Bind(); }   // safety net if Character isn't ready at Start

    void Bind()
    {
        var c = Character.Instance;
        if (c == null) return;

        healthFill  = transform.Find("HUD_HealthBar/Fill")?.GetComponent<Image>();
        healthLabel = transform.Find("HUD_HealthBar/Label")?.GetComponent<Text>();
        statsText   = GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == "Stats");
        equipSlots  = GetComponentsInChildren<ItemSlot>(true).Where(s => s.accepts != EquipSlotType.None).ToArray();

        foreach (var s in equipSlots)
        {
            var slot = s;                                   // capture for the closure
            c.SetEquipped(slot.accepts, slot.Item);         // sync whatever is already equipped
            slot.Changed += () => Character.Instance?.SetEquipped(slot.accepts, slot.Item);
        }

        c.OnStatsChanged  += RefreshStats;
        c.OnHealthChanged += RefreshHealth;
        RefreshStats();
        RefreshHealth();
        bound = true;
    }

    void RefreshHealth()
    {
        var c = Character.Instance;
        if (c == null) return;
        if (healthFill != null) healthFill.fillAmount = c.MaxHP > 0 ? (float)c.CurrentHP / c.MaxHP : 0f;
        if (healthLabel != null) healthLabel.text = $"{c.CurrentHP} / {c.MaxHP}";
    }

    void RefreshStats()
    {
        var c = Character.Instance;
        if (c == null || statsText == null) return;
        statsText.text =
            $"Level   {c.Level}\n" +
            $"XP      {c.XP} / {c.XPToNext}\n" +
            $"HP      {c.CurrentHP} / {c.MaxHP}\n" +
            $"ATK     {c.Attack}\n" +
            $"DEF     {c.Defense}";
        RefreshHealth();
    }
}
