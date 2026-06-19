using UnityEngine;

/// <summary>
/// Marks a field boss and gives it a scaled-up <see cref="MobNameplate"/> — the SAME HP bar + name that
/// hangs at the BOTTOM of every mob's sprite, just larger so a boss reads as a boss. (It used to draw a
/// huge banner over the boss's head; that was replaced with the shared bottom nameplate so bosses and
/// mobs are visually consistent.)
///
/// Configures, rather than re-implements: it ensures a <see cref="MobNameplate"/> on the boss and bumps
/// its size in <see cref="Awake"/> (before the plate builds itself in its first LateUpdate). Adding the
/// plate here also upgrades a boss that was baked BEFORE the nameplate existed — no re-bake needed.
/// </summary>
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public class BossBanner : MonoBehaviour
{
    [Tooltip("Name shown under the boss's HP bar.")]
    public string bossName = "Boss";

    [Header("Boss plate size (a scaled-up MobNameplate, still bottom-anchored)")]
    [Tooltip("World size of the plate. Mobs use ~0.012; bump this so the boss bar reads bigger.")]
    public float worldScale = 0.018f;
    [Tooltip("Bar width / height in UI pixels (before worldScale).")]
    public float barWidth = 92f, barHeight = 12f;
    [Tooltip("Name font size in UI pixels (before worldScale).")]
    public int nameFontSize = 26;

    void Awake()
    {
        var plate = GetComponent<MobNameplate>();
        if (plate == null) plate = gameObject.AddComponent<MobNameplate>();
        plate.displayName  = bossName;
        plate.worldScale   = worldScale;
        plate.barWidth     = barWidth;
        plate.barHeight    = barHeight;
        plate.nameFontSize = nameFontSize;
    }
}
