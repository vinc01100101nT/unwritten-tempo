using UnityEngine;

/// <summary>
/// A combat test dummy: a huge-HP, immobile target that resets to full HP on a timer so you can
/// measure damage / DPS / build output without it ever dying. Pair it with an inert
/// <see cref="MonsterAI3D"/> (so it's targetable by click-attack and the caster) and a big
/// <see cref="Health"/>. Spawned by <c>Tools ▸ unwritten ▸ Spawn Training Dummy</c>.
///
/// <para>It shows NO readout of its own: its HP reads through the same <see cref="MobNameplate"/> and the same
/// target/hover panels as any other mob (<see cref="UnitStatusPanel"/>), so what you measure on the dummy is what
/// you'd see in a real fight.</para>
/// </summary>
[RequireComponent(typeof(Health))]
public class TrainingDummy : MonoBehaviour
{
    [Tooltip("Seconds between full-HP resets.")]
    public float regenInterval = 5f;

    Health health;
    float timer;

    void Awake() => health = GetComponent<Health>();

    void Update()
    {
        if (health == null) return;

        timer += Time.deltaTime;
        if (timer >= regenInterval)
        {
            timer = 0f;
            health.Revive();   // resets Current to maxHealth (and clears the hit-flash tint)
        }
    }
}
