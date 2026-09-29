using UnityEngine;

/// <summary>SERVER: added to every creature that can turn back (see <see cref="MobSpawner"/>), so its death asks
/// <see cref="SavedCharacters.OnDeath"/>.</summary>
[RequireComponent(typeof(Health))]
public class TurnsBackOnDeath : MonoBehaviour
{
    /// <summary>The creature this body is.</summary>
    public MonsterDef def;

    Health health;

    void Awake()
    {
        health = GetComponent<Health>();
        health.onDeath.AddListener(OnDied);
    }

    // Taken off with this component — a training dummy strips it, and a death must not reach a component that is gone.
    void OnDestroy()
    {
        if (health != null) health.onDeath.RemoveListener(OnDied);
    }

    void OnDied() => SavedCharacters.OnDeath(health, def);
}
