using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// A BEATEN MOB TURNING BACK INTO THE CHARACTER THE ARTIST DREW. Every creature that can turn back carries a
/// <see cref="TurnsBackOnDeath"/>; when it dies to a person the server rolls <see cref="SavedCharacterRules.TransformChance"/>,
/// and on a hit a body of the SAME creature is brought out where it fell, wearing its character's picture — so it
/// fights with the mob's own stats at the mob's own level, and only its look, its side and what it pays change.
/// Design: docs/3-world/saved-characters.md.
///
/// <para><b>Server only</b>, apart from <see cref="Wear"/>, which every machine runs on the body it draws.</para>
/// </summary>
public static class SavedCharacters
{
    // THE BODIES WAITING TO BE USED AGAIN, by creature id. A saved character is a creature body wearing another picture,
    // and it only ever becomes a saved character again — so it never mixes with a field's own pool.
    static readonly Dictionary<string, Stack<GameObject>> bank = new Dictionary<string, Stack<GameObject>>();

    /// <summary>SERVER: <paramref name="died"/> — a <paramref name="def"/> — has just died. Rolls whether it turns back,
    /// and brings out its character when it does.</summary>
    public static void OnDeath(Health died, MonsterDef def)
    {
        if (!GameServer.IsServer || died == null || def == null) return;
        if (died.IsSummoned || died.IsSavedCharacter || !SavedCharacterRules.CanTurnBack(died.kind)) return;

        // Only a kill earned by a person turns a page back — an ally's kill is its saver's (see Rewards.WhoEarned).
        var saver = Rewards.WhoEarned(died) as Character;
        if (saver == null || PlayerUnits.Gone(saver)) return;
        if (!CombatRng.Chance(SavedCharacterRules.TransformChance)) return;

        var who = died.kind == CreatureKind.Elite
            ? SavedCharacterCatalog.ForElite(def.id)
            : SavedCharacterCatalog.ForNormal(CombatRng.Range(0, int.MaxValue));
        if (who == null || who.sheet == null) return;

        // Everything it carries over, read off the creature AS IT DIED: its level may have grown in the fight.
        var block = died.GetComponent<UnitStatBlock>();   // perf-ok: once, at the moment a creature turns back
        // AND THE SAVER'S RUN OF KILLS this death belongs to — the same count its experience is paid under, whichever of
        // the two is asked first. A creature whose kill pays nothing (a training dummy) is no part of a run.
        var brain = died.GetComponent<MonsterAI3D>();   // perf-ok: once, at the moment a creature turns back
        int combo = brain != null && brain.paysExperience ? Rewards.CountKill(saver, died, KillCombo.Now) : 0;
        var carried = new Carried
        {
            def = def,
            who = who,
            saver = saver,
            at = died.transform.position,
            map = died.gameObject.scene,
            hasInputs = block != null,
            inputs = block != null ? block.inputs : default,
            combo = combo,
        };

        var body = TakeBanked(def.id);
        if (body != null)
        {
            MapManager.PutInMap(body, carried.map);
            MobRespawn.Reactivate(body, carried.at);
            Become(body, carried);
            return;
        }

        MobSpawner.Spawn(def, carried.at, carried.map, go =>
        {
            if (go == null) return;
            var pooled = go.AddComponent<PooledMob>();   // perf-ok: once per body created; every later life is banked
            pooled.typeKey = def.id;
            pooled.kind = def.kind;
            pooled.returnTo = Bank;
            Become(go, carried);
        });
    }

    struct Carried
    {
        public MonsterDef def;
        public SavedCharacter who;
        public Character saver;
        public Vector3 at;
        public Scene map;
        public bool hasInputs;
        public UnitStatInputs inputs;
        public int combo;
    }

    // The part of a rescue's material the saver's bag had no room for, filled and emptied inside one rescue.
    static readonly List<Item> restItems = new List<Item>(1);
    static readonly List<int> restCounts = new List<int>(1);

    // Make `go` — a fresh or banked body of the creature that died — into its character, announce it, and say its line.
    static void Become(GameObject go, in Carried c)
    {
        var health = go.GetComponent<Health>();   // perf-ok: once, at the moment a creature turns back
        if (health == null) return;
        var behaviour = c.who.behaviour;

        health.BecomeSaved(c.who, SavedCharacterRules.SideOf(behaviour), behaviour == SavedBehaviour.Ally ? c.saver : null);
        Wear(go, c.who, health.kind);

        var block = go.GetComponent<UnitStatBlock>();   // perf-ok: once, at the moment a creature turns back
        if (block != null && c.hasInputs) block.SetSpawnInputs(c.inputs);

        // A CHARACTER CALLS NOBODY: the elite it came from summoned, the ninja it became does not.
        var summons = go.GetComponent<CreatureSummons>();   // perf-ok: once, at the moment a creature turns back
        if (summons != null) Object.Destroy(summons);

        var brain = go.GetComponent<MonsterAI3D>();   // perf-ok: once, at the moment a creature turns back
        if (brain != null)
        {
            // A REBEL PAYS what its level and kind are worth, like any creature; the others are not a kill.
            brain.paysExperience = behaviour == SavedBehaviour.Rebel;
            brain.SetHome(c.at);
            brain.ThinksForItself = behaviour != SavedBehaviour.NeverFights;
        }
        var agent = go.GetComponent<PathAgent3D>();   // perf-ok: once, at the moment a creature turns back
        if (agent != null) agent.Halt();

        var life = go.GetComponent<SavedCharacterLife>();   // perf-ok: once, at the moment a creature turns back
        if (life == null) life = go.AddComponent<SavedCharacterLife>();
        life.Begin(c.who);

        // Announced AFTER it is the character, so the very first message about it already says which one and whose side.
        GameNetwork.PublishMob(go, c.def.id);
        life.Say(SavedCharacterLines.Pick(SavedCharacterLines.Appears(behaviour, c.who.group), CombatRng.Range(0, int.MaxValue)));

        // THE ONE ZENY A KILL CAUSES, so the saver's run of kills multiplies it exactly as it does the kill's experience.
        // The creature's own material comes with it, never multiplied: into the saver's bag, and whatever the bag has
        // no room for falls where the character stands, kept for them like any drop.
        int rescued = (int)System.Math.Min(int.MaxValue,
            SavedCharacterRules.RescueGold(behaviour, c.who.goldTier, c.def.level, c.def.multiplier));
        int gold = KillComboMath.Paid(rescued, c.combo);
        bool saverHere = !PlayerUnits.Gone(c.saver);
        restItems.Clear();
        restCounts.Clear();
        int handed = CreatureMaterials.Is(c.def.material)
            ? SavedCharacterRules.RescuePieces(behaviour, c.who.goldTier,
                                               CreatureMaterials.TierOfStep(c.def.material.materialStep), CombatRng.Range(0, 10000))
            : 0;
        if (handed > 0 && saverHere)
        {
            int fits = c.saver.MostThatFits(c.def.material, handed);
            if (fits > 0)
            {
                c.saver.TakeIntoBag(c.def.material, fits);
                var shell = GameNetwork.ShellFor(c.saver);
                if (shell != null)
                {
                    shell.PushBagToOwner();
                    shell.SaveSoon();
                }
            }
            if (handed > fits) { restItems.Add(c.def.material); restCounts.Add(handed - fits); }
        }
        if ((gold > 0 || restItems.Count > 0) && saverHere)
        {
            Rewards.PayLoot(c.saver, null, true, gold, health, restItems, restCounts);
            c.saver.KillCombo.Earned(KillCombo.Now, 0, gold);
            GameNetwork.TellOwnerTheirKillCombo(c.saver, health, false, 0, 0, gold, gold - rescued);
        }

        if (GameLog.On(LogCat.Loot))
            GameLog.Info(LogCat.Loot, string.Concat(SpawnIds.MapOf(health), " ", SpawnIds.Label(health), " turned back into ",
                c.who.id, " (", behaviour.ToString(), ") for ", SpawnIds.Label(c.saver),
                gold > 0 && saverHere ? ", who was paid " + gold + " gold" + Rewards.ComboWords(c.combo, gold - rescued) : ""));
    }

    /// <summary>EVERY MACHINE: make <paramref name="go"/> into <paramref name="who"/> — a character's size, fitted
    /// against the character's own art, and its name; and on a machine that draws, its picture. The server runs it on
    /// the real creature, a watching machine on the body it draws.</summary>
    public static void Wear(GameObject go, SavedCharacter who, CreatureKind kind)
    {
        if (go == null || who == null) return;
        var anim = go.GetComponent<BillboardAnimator3D>();   // perf-ok: once, when a body becomes a character
        if (anim != null)
            anim.UseSheet(who.sheet, who.crownAtScaleOne, who.paintedWidthAtScaleOne, SavedCharacterRules.DrawnWidth,
                          who.footPx >= 0f && anim.cell > 0 ? who.footPx / anim.cell : SpriteSheetCache.MobFootPivotY);
        MobBody.Fit(go, kind, MobSizes.Character, flies: false,
                    new MobBody.FlatArt(who.crownAtScaleOne, who.paintedWidthAtScaleOne, SavedCharacterRules.DrawnWidth));
        var plate = go.GetComponent<MobNameplate>();   // perf-ok: once, when a body becomes a character
        if (plate != null) plate.displayName = who.displayName;
    }

    /// <summary>The aura that marks a character of <paramref name="behaviour"/> as it appears: love for one that never
    /// fights, stars for an ally, the debuff aura for a rebel. Ids in the effect library.</summary>
    public static string AppearAuraFor(SavedBehaviour behaviour) =>
        behaviour == SavedBehaviour.NeverFights ? "aura.love"
      : behaviour == SavedBehaviour.Ally ? "aura.star"
      : "aura.debuff";

    /// <summary>SERVER: take <paramref name="go"/> off the field without a death — a character that said goodbye.</summary>
    public static void Vanish(GameObject go)
    {
        var pooled = go != null ? go.GetComponent<PooledMob>() : null;   // perf-ok: once, when a character leaves
        if (pooled != null) Bank(pooled);
        else if (go != null) go.SetActive(false);
    }

    // A body whose life as a character is over — gone, or faded out after dying — waits here to be used again. Switching
    // it off is also what takes it off every screen: its shell sees it gone and goes with it.
    static void Bank(PooledMob pooled)
    {
        if (pooled == null) return;
        var go = pooled.gameObject;
        // Off BEFORE the object, for the same reason a field spawner's pool does it (see FieldMobSpawner.ReturnToPool).
        var cc = go.GetComponent<CharacterController>();   // perf-ok: once, when a character leaves
        if (cc != null) cc.enabled = false;
        go.SetActive(false);
        if (!bank.TryGetValue(pooled.typeKey, out var stack)) { stack = new Stack<GameObject>(); bank[pooled.typeKey] = stack; }
        stack.Push(go);
    }

    static GameObject TakeBanked(string key)
    {
        if (string.IsNullOrEmpty(key) || !bank.TryGetValue(key, out var stack)) return null;
        // A body destroyed under us (its map let go) leaves a hole in the stack, so keep taking until one is real.
        while (stack.Count > 0)
        {
            var body = stack.Pop();
            if (body != null) return body;
        }
        return null;
    }
}
