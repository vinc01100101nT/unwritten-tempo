using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Minimal screen-space HP / level / XP readout for the 2.5D slice — reads
/// <see cref="Character.Instance"/> and refreshes on its events. The first slice of the real
/// HUD coming into 3D (Phase 3D-D "A"). Built by RigBuilder25D.
/// </summary>
public class HealthBarHUD : MonoBehaviour
{
    public Image fill;
    public TMP_Text label;

    Character ch;

    void Start()
    {
        Bind();
        Refresh();
    }

    void Bind()
    {
        ch = Character.Instance;
        if (ch != null)
        {
            ch.OnHealthChanged += Refresh;
            ch.OnStatsChanged += Refresh;
        }
    }

    void OnDestroy()
    {
        if (ch != null)
        {
            ch.OnHealthChanged -= Refresh;
            ch.OnStatsChanged -= Refresh;
        }
    }

    void Refresh()
    {
        if (ch == null) { Bind(); if (ch == null) return; }
        if (fill != null) fill.fillAmount = ch.MaxHP > 0 ? (float)ch.CurrentHP / ch.MaxHP : 0f;
        if (label != null) label.text = $"Lv {ch.Level}    HP {ch.CurrentHP}/{ch.MaxHP}    XP {ch.XP:0.00}/{ch.XPToNext}";
    }
}
