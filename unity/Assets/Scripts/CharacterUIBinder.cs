using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wires the (persistent) UI to the (persistent) <see cref="Character"/>: drives the HUD health/mana
/// bars. Lives on the GameUICanvas; because both it and Character persist, it binds once and never
/// needs to re-find anything across scene loads.
///
/// NOTE: this binder no longer drives a "Character-panel stats text" — that panel is gone and the
/// character stats are now owned by <see cref="ProgressionWindows"/>. The old code grabbed the first
/// Text named "Stats" on the canvas, which collided with the Equipment detail panel's "Stats" label
/// and clobbered it with a character summary; that hijack is intentionally removed.
///
/// It also no longer pushes equip-slot changes into the character (equipment is owned by
/// ProgressionWindows + <see cref="InventoryStore"/>); the old re-equip-on-Changed wiring formed an
/// infinite OnStatsChanged ↔ slot.Changed loop that stack-overflowed.
/// </summary>
public class CharacterUIBinder : MonoBehaviour
{
    Image healthFill;
    Text healthLabel;
    Image manaFill;
    Text manaLabel;
    bool bound;

    void Start() => Bind();
    void Update() { if (!bound) Bind(); }   // safety net if Character isn't ready at Start

    void Bind()
    {
        var c = Character.Instance;
        if (c == null) return;

        healthFill  = transform.Find("HUD_HealthBar/Fill")?.GetComponent<Image>();
        healthLabel = transform.Find("HUD_HealthBar/Label")?.GetComponent<Text>();
        manaFill    = transform.Find("HUD_ManaBar/Fill")?.GetComponent<Image>();
        manaLabel   = transform.Find("HUD_ManaBar/Label")?.GetComponent<Text>();

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
