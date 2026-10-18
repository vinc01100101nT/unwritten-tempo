using UnityEngine;

/// <summary>
/// A combat test dummy: a huge-HP, immobile target that resets to full HP on a timer so you can
/// measure damage / DPS / build output without it ever dying. Pair it with an inert
/// <see cref="MonsterAI3D"/> (so it's targetable by click-attack and the caster) and a big
/// <see cref="Health"/>. Placed in the world by the <c>/dummy</c> GM command, which the server runs, so every player sees and hits the same one.
///
/// <para>It shows NO readout of its own: its HP reads through the same hover plate as any other mob
/// (<see cref="HoverPlate"/>), so what you measure on the dummy is what you'd see in a real fight.</para>
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
            health.maxHealth = DummyHP;
            // A body built while the game runs has ALREADY woken, so its current health is still the creature's own
            // figure against a 10,000,000 maximum, which reads as an empty bar. Filled here so both callers start it full.
            health.RefillToMax();
            // Measure raw weapon/skill output, not post-mitigation: no defence and no dodge.
            health.softDefense = 0;
            health.softMagicDefense = 0;
            health.hardDefenseTenths = 0;
            health.hardMagicDefenseTenths = 0;
            health.flee = 0;
        }

        var ai = go.GetComponent<MonsterAI3D>();
        if (ai != null)
        {
            // A fixed target: it never chases, wanders or bites — the same switch a saved character that never fights uses.
            ai.ThinksForItself = false;
            // no free XP from the test rig
            ai.paysExperience = false;
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
        // …and the same kill must not turn it back into a character: an instrument was never a page.
        Strip<TurnsBackOnDeath>(go);
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
