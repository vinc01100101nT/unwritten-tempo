using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wires the (persistent) UI to the (persistent) <see cref="Character"/>: drives the HUD health/mana
/// bars and the Character-panel stats text. Lives on the GameUICanvas; because both it and Character
/// persist, it binds once and never needs to re-find anything across scene loads.
///
/// NOTE: this binder no longer pushes equip-slot changes into the character. Equipment is now owned by
/// <see cref="ProgressionWindows"/> + <see cref="InventoryStore"/>. The old wiring (re-equipping from
/// leftover slots on every Changed event) formed an infinite OnStatsChanged ↔ slot.Changed feedback
/// loop with the new windows that stack-overflowed; it's intentionally gone.
/// </summary>
public class CharacterUIBinder : MonoBehaviour
{
    Image healthFill;
    Text healthLabel;
    Image manaFill;
    Text manaLabel;
    Text statsText;
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
        statsText   = GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.name == "Stats");

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

    void RefreshStats()
    {
        var c = Character.Instance;
        if (c == null || statsText == null) return;
        statsText.text =
            $"Level   {c.Level}\n" +
            $"XP      {c.XP} / {c.XPToNext}\n" +
            $"HP      {c.CurrentHP} / {c.MaxHP}\n" +
            $"MP      {c.CurrentMP} / {c.MaxMP}\n" +
            $"ATK     {c.MinAttack}-{c.MaxAttack}\n" +
            $"DEF     {c.Defense}\n" +
            $"M.ATK   {c.MinMagicAttack}-{c.MaxMagicAttack}\n" +
            $"M.DEF   {c.MagicDefense}\n" +
            $"ASPD    +{Mathf.RoundToInt(c.Passives.Percent(StatId.AttackSpeed) * 100)}%\n" +
            $"CRIT    {Mathf.RoundToInt(c.CritChance01 * 100)}%  ×{c.CritMultiplier:0.0}\n" +
            $"HIT     {c.Hit}    FLEE {c.Flee}";
        RefreshHealth();
        RefreshMana();
    }
}
