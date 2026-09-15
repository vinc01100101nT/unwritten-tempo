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

    /// <summary>Hit points a dummy holds. Large enough that nothing you can build will ever empty it, so a damage
    /// reading is never cut short by the thing you are measuring against dying.</summary>
    public const int DummyHP = 10_000_000;

    /// <summary>Turn a freshly built creature into a punching bag: enormous health, no mitigation to muddy the
    /// reading, and no ability to chase, wander, bite or reward you.
    ///
    /// <para><b>THE one description of what a dummy is.</b> It used to live inside the editor tool, which meant the
    /// server had no way to make one — and a second copy written for the server would have drifted from it the first
    /// time a number changed. Both callers hand a body to this.</para></summary>
    public static void MakeOne(GameObject go)
    {
        if (go == null) return;

        var health = go.GetComponent<Health>();
        if (health != null)
        {
            health.maxHealth = DummyHP;   // Health.Awake copies this into Current on Play
            health.defense = 0;           // measure raw weapon/skill output, not post-mitigation
            health.magicDefense = 0;
            health.flee = 15;             // RO FLEE: low DEX misses it a fair bit (red MISS), accuracy climbs as you add DEX
        }

        var ai = go.GetComponent<MonsterAI3D>();
        if (ai != null)
        {
            ai.moveSpeed = 0f;
            ai.aggroRadius = 0f;        // never chase
            ai.leashRadius = 0f;
            ai.wanderRadius = 0f;       // never wander — stays put as a fixed target
            ai.touchRange = 0f;         // never bite anybody
            ai.attack = 0;
            ai.xpReward = 0;            // no free XP from the test rig
        }

        // The stat block is the single writer of health/defence/attack on a normal creature and re-derives them
        // from level on Awake, which would wipe every number set above and turn the dummy back into an ordinary
        // creature. A dummy deliberately opts out of the shared unit model — it is an instrument, not a fighter.
        Strip<UnitStatBlock>(go);
        // It never dies, so the recycle and death effects would never fire — dropped so it stays predictable.
        Strip<MobRespawn>(go);
        Strip<DeathDissolve>(go);
        // A GM kill command CAN kill it, and a dummy that kept its drop paid gold to whoever had been hitting it.
        Strip<LootDropper>(go);
        // NOTHING WORN, including whatever it was handed at random as it was built: an instrument has no gear, and its
        // health bar must not list pieces it can never give up.
        var gear = go.GetComponent<UnitEquipment>();
        if (gear != null) gear.Clear();

        var sr = go.GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = new Color(0.7f, 0.72f, 0.78f);   // slate-gray "training" tint

        if (go.GetComponent<TrainingDummy>() == null) go.AddComponent<TrainingDummy>().regenInterval = 5f;
    }

    static void Strip<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) return;
        if (Application.isPlaying) Destroy(c);
        else DestroyImmediate(c);
    }

    void Update()
    {
        if (health == null) return;

        timer += Time.deltaTime;
        if (timer >= regenInterval)
        {
            timer = 0f;
            health.Revive();   // resets Current to maxHealth
        }
    }
}
