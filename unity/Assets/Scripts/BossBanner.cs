using UnityEngine;

/// <summary>
/// Gives an elite or shadow its name plate (<see cref="MobNameplate"/>). Nothing else: an elite reads through the
/// same hover plate, the same bar and the same behaviour as every other unit in the game.
///
/// Configures, rather than re-implements: it ensures the plate in <see cref="Awake"/> (before the plate builds itself
/// in its first LateUpdate). The NAME on it is written by <see cref="MobSpawner"/> from the creature's data, the one
/// place a creature's name lives, so nothing here holds a copy of it.
/// </summary>
[RequireComponent(typeof(Health))]
[DisallowMultipleComponent]
public class BossBanner : MonoBehaviour
{
    void Awake()
    {
        if (GetComponent<MobNameplate>() == null) gameObject.AddComponent<MobNameplate>();
    }
}
