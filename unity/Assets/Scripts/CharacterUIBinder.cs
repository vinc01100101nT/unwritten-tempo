using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Wires the (persistent) UI to the (persistent) <see cref="Character"/>: drives the HUD health/mana
/// bars. Lives on the GameUICanvas; because both it and Character persist, it binds once and never
/// needs to re-find anything across scene loads.
///
/// It drives nothing else: character stats are shown by <see cref="ProgressionWindows"/>, and equipment is
/// owned by <see cref="InventoryStore"/>.
/// </summary>
public class CharacterUIBinder : MonoBehaviour
{
    Image healthFill;
    TMP_Text healthLabel;
    Image manaFill;
    TMP_Text manaLabel;
    bool bound;

    void Start() => Bind();
    void Update() { if (!bound) Bind(); }   // safety net if Character isn't ready at Start

    void Bind()
    {
        var c = Character.Instance;
        if (c == null) return;

        healthFill  = transform.Find("HUD_HealthBar/Fill")?.GetComponent<Image>();
        healthLabel = transform.Find("HUD_HealthBar/Label")?.GetComponent<TMP_Text>();
        manaFill    = transform.Find("HUD_ManaBar/Fill")?.GetComponent<Image>();
        manaLabel   = transform.Find("HUD_ManaBar/Label")?.GetComponent<TMP_Text>();

        c.OnStatsChanged  += RefreshStats;
        c.OnHealthChanged += RefreshHealth;
        c.OnManaChanged   += RefreshMana;
        RefreshStats();
        RefreshHealth();
        RefreshMana();
        bound = true;
    }

    void RefreshHealth()
    {
        var c = Character.Instance;
        if (c == null) return;
        if (healthFill != null) healthFill.fillAmount = c.MaxHP > 0 ? (float)c.CurrentHP / c.MaxHP : 0f;
        if (healthLabel != null) healthLabel.text = $"{c.CurrentHP} / {c.MaxHP}";
    }

    void RefreshMana()
    {
        var c = Character.Instance;
        if (c == null) return;
        if (manaFill != null) manaFill.fillAmount = c.MaxMP > 0 ? (float)c.CurrentMP / c.MaxMP : 0f;
        if (manaLabel != null) manaLabel.text = $"{c.CurrentMP} / {c.MaxMP}";
    }

    // Stats feed the bars' max values (Max HP/MP shift with VIT/INT etc.), so a stat change just
    // refreshes the bars now — the numeric stat readout lives in the Character window (ProgressionWindows).
    void RefreshStats()
    {
        RefreshHealth();
        RefreshMana();
    }
}
