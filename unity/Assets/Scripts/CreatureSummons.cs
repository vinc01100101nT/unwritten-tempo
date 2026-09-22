using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// WHAT A SUMMONER HAS CALLED UP — an elite's normals, a shadow's elites. The summon skill (an effect of kind
/// <see cref="SkillEffectKind.Summon"/>) asks this to top its summons back up to the kind's maximum; this decides which
/// creatures they are (<see cref="SummonPicks"/>), makes or reuses them, and sends them away when the summoner dies.
///
/// <para><b>Nothing is destroyed and re-made on a cadence.</b> A summon that dies, or is sent away, is switched off and
/// kept here, and the next cast switches it back on — the same recycling a field's pool does. After the first few casts
/// a summoner costs no new objects at all.</para>
///
/// <para>Server only. Added by <see cref="MobSpawner"/> to every creature whose kind summons.</para>
/// </summary>
public class CreatureSummons : MonoBehaviour
{
    /// <summary>The creature data this summoner was made from — what its picks are worked out from.</summary>
    [System.NonSerialized] public MonsterDef def;

    Health health;
    MonsterAI3D brain;
    readonly List<SummonPicks.Pick> picks = new List<SummonPicks.Pick>(3);
    bool picked;

    // Out now, by group. A body still loading from its prefab counts as out, so one cast can never ask twice for it.
    readonly List<SummonedCreature> live = new List<SummonedCreature>(10);
    int[] pending = System.Array.Empty<int>();
    // Switched off and waiting to be called again, by group.
    readonly List<Stack<SummonedCreature>> banked = new List<Stack<SummonedCreature>>(3);

    void Awake()
    {
        health = GetComponent<Health>();
        brain = GetComponent<MonsterAI3D>();
        if (health != null) health.onDeath.AddListener(DismissAll);
    }

    /// <summary>Is everything this summoner may have out, out? True for a summoner that has nothing it could call.</summary>
    public bool AtMax
    {
        get
        {
            EnsurePicks();
            if (picks.Count == 0) return true;
            int max = 0, have = 0;
            for (int g = 0; g < picks.Count; g++) { max += picks[g].count; have += Out(g); }
            return have >= max;
        }
    }

    /// <summary>Top every group back up to its share. What one cast of the summon skill does.</summary>
    public void SummonNow()
    {
        if (!GameServer.IsServer || health == null || health.IsDead || health.IsSummoned) return;
        EnsurePicks();
        for (int g = 0; g < picks.Count; g++)
        {
            int need = picks[g].count - Out(g);
            for (int k = 0; k < need; k++) CallOne(g);
        }
    }

    /// <summary>A summon's death fade finished: switch it off and keep it for the next cast.</summary>
    public void Bank(SummonedCreature s)
    {
        if (s == null) return;
        live.Remove(s);
        Hide(s.gameObject);
        if (s.group >= 0 && s.group < banked.Count) banked[s.group].Push(s);
    }

    /// <summary>Switch a creature body off cleanly: its controller first, or switching it back on later throws.</summary>
    public static void Hide(GameObject go)
    {
        if (go == null) return;
        var cc = go.GetComponent<CharacterController>();   // perf-ok: once per summon sent away
        if (cc != null) cc.enabled = false;
        go.SetActive(false);
    }

    // THE SUMMONER DIED: every summon still standing goes at once. One already dying finishes its fade and is banked then.
    void DismissAll()
    {
        for (int i = live.Count - 1; i >= 0; i--)
        {
            var s = live[i];
            if (s == null) { live.RemoveAt(i); continue; }
            var h = s.GetComponent<Health>();   // perf-ok: once per summon, at the summoner's death
            if (h != null && h.IsDead) continue;
            Bank(s);
        }
    }

    int Out(int group)
    {
        int n = group < pending.Length ? pending[group] : 0;
        for (int i = 0; i < live.Count; i++)
        {
            var s = live[i];
            if (s != null && s.group == group && s.gameObject.activeSelf) n++;
        }
        return n;
    }

    void EnsurePicks()
    {
        if (picked) return;
        picked = true;
        SummonPicks.For(def, MonsterCatalog.All, picks);
        pending = new int[picks.Count];
        banked.Clear();
        for (int g = 0; g < picks.Count; g++) banked.Add(new Stack<SummonedCreature>());
    }

    void CallOne(int group)
    {
        Vector3 at = SpotBeside();
        var stack = banked[group];
        while (stack.Count > 0)
        {
            var reused = stack.Pop();
            if (reused == null) continue;
            MobRespawn.Reactivate(reused.gameObject, at);
            Adopt(reused.gameObject, group);
            return;
        }

        var pick = picks[group];
        pending[group]++;
        MobSpawner.Spawn(pick.def, at, gameObject.scene, go =>
        {
            pending[group]--;
            if (go == null) return;
            // THE SUMMONER DIED WHILE THIS WAS LOADING: it arrives already sent away.
            if (this == null || health == null || health.IsDead)
            {
                if (this != null) { var s = go.AddComponent<SummonedCreature>(); s.group = group; s.summoner = this; Bank(s); }   // perf-ok: once per summon made
                else Hide(go);
                return;
            }
            Adopt(go, group);
        });
    }

    // Make a body this summoner's, and tell every watching machine about it.
    void Adopt(GameObject go, int group)
    {
        var s = go.GetComponent<SummonedCreature>();   // perf-ok: once per summon called
        if (s == null) s = go.AddComponent<SummonedCreature>();   // perf-ok: once per summon ever made; kept through reuse
        s.summoner = this;
        s.group = group;
        live.Add(s);

        var h = go.GetComponent<Health>();   // perf-ok: once per summon called
        if (h != null) h.SetSummonedBy(s);
        var ai = go.GetComponent<MonsterAI3D>();   // perf-ok: once per summon called
        if (ai != null) { ai.SetHome(go.transform.position); ai.Follow(brain); }
        GameNetwork.PublishMob(go, picks[group].def.id);
    }

    static int groundMask;

    // A floor spot a couple of steps from the summoner; its own feet when no floor is found.
    Vector3 SpotBeside()
    {
        Vector3 c = transform.position;
        Vector2 r = Random.insideUnitCircle.normalized * Random.Range(1.2f, 2.5f);
        Vector3 probe = new Vector3(c.x + r.x, c.y + 10f, c.z + r.y);
        if (groundMask == 0) groundMask = LayerMask.GetMask("Ground");
        if (Physics.Raycast(probe, Vector3.down, out var hit, 30f, groundMask, QueryTriggerInteraction.Ignore))
            return hit.point;
        return c;
    }
}
