using UnityEngine;

/// <summary>
/// Marks a field boss and configures its <see cref="MobNameplate"/> — the SAME HP bar, at the SAME size, that
/// hangs at the BOTTOM of every mob. A boss differs only in keeping its name on the plate and its bar always
/// visible; the size is deliberately left at the nameplate's own defaults so bosses and mobs match.
///
/// Configures, rather than re-implements: it ensures a <see cref="MobNameplate"/> on the boss in
/// <see cref="Awake"/> (before the plate builds itself in its first LateUpdate). Adding the plate here also
/// upgrades a boss that was baked BEFORE the nameplate existed — no re-bake needed.
/// </summary>
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public class BossBanner : MonoBehaviour
{
    [Tooltip("Name shown under the boss's HP bar.")]
    public string bossName = "Boss";

    void Awake()
    {
        var plate = GetComponent<MobNameplate>();
        if (plate == null) plate = gameObject.AddComponent<MobNameplate>();
        plate.displayName     = bossName;
        plate.showName        = true;    // a boss keeps its name…
        plate.hideAtFullHealth = false;  // …and its bar is always visible (not hidden at full HP)
    }
}
