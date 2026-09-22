using UnityEngine;

/// <summary>
/// Marks a mob as owned by a <see cref="FieldMobSpawner"/>'s object pool. When the mob dies,
/// <see cref="Health.Die"/> hands off to <see cref="OnFadedOut"/> (instead of <c>MobRespawn</c>'s in-place
/// recycle or Destroy), which returns the object to its spawner's pool for reuse — the Unity-cheap version of
/// RO's "respawn from the map's mob table": no Destroy, no re-Instantiate, no garbage.
/// </summary>
public class PooledMob : MonoBehaviour
{
    /// <summary>The spawner that owns this mob and its pool.</summary>
    public FieldMobSpawner owner;

    /// <summary>The pool bucket key (the mob type / <see cref="MonsterDef"/> asset name), so a returned mob
    /// only ever reuses into the same-type bucket.</summary>
    public string typeKey;

    /// <summary>What kind of creature it is — how long it stays gone after it dies is the kind's answer.</summary>
    public CreatureKind kind;

    /// <summary>Called by <see cref="Health.Die"/> after the death fade finishes: hand the object back to the
    /// pool. Falls back to hiding itself if its spawner is gone (e.g. scene torn down).</summary>
    public void OnFadedOut()
    {
        if (owner != null) owner.ReturnToPool(this);
        else gameObject.SetActive(false);
    }
}
