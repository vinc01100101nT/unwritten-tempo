using UnityEngine;

/// <summary>
/// Gives an elite or shadow its display name (<see cref="MobNameplate.displayName"/>). Nothing else: an elite reads
/// through the same hover plate, the same bar and the same behaviour as every other unit in the game.
///
/// Configures, rather than re-implements: it ensures a <see cref="MobNameplate"/> on the boss in
/// <see cref="Awake"/> (before the plate builds itself in its first LateUpdate). Adding the plate here also
/// upgrades a boss that was baked BEFORE the nameplate existed — no re-bake needed.
/// </summary>
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public class BossBanner : MonoBehaviour
{
    [Tooltip("The name every readout shows for this creature.")]
    public string bossName = "Boss";

    void Awake()
    {
        var plate = GetComponent<MobNameplate>();
        if (plate == null) plate = gameObject.AddComponent<MobNameplate>();
        plate.displayName = bossName;
    }
}
