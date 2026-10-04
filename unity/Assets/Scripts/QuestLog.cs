using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One quest record as it is saved and as it arrives with a character — the shape the character server's
/// <c>character_quest</c> rows travel in: quest id, step id, followed, finished.</summary>
[Serializable]
public class QuestRowWire
{
    public string q = "";
    public string s = "";
    public bool t;
    public bool f;
    /// <summary>How far along the current step is — creatures defeated for a Kill step; 0 otherwise.</summary>
    public int p;
}

/// <summary>An open quest, as a player's machine is told it. Finished numbers only: the step it is on, and for a fold
/// step how far along it is.</summary>
[Serializable]
public struct QuestOpenWire
{
    public string questId;
    public string stepId;
    public int have;
    public int need;
    public bool tracked;
}

/// <summary>Where the followed quest's step happens and whether it can be done now — decided on the server.</summary>
[Serializable]
public struct QuestTargetWire
{
    public bool any;
    public string questId;
    public string stepId;
    /// <summary>The map the step happens in, and the spot there in that map's own coordinates.</summary>
    public string map;
    public Vector3 at;
    /// <summary>When the step is on another map: the next map to walk into, on the way with the fewest doors.</summary>
    public string nextMap;
    /// <summary>The station the step is worked at (a <see cref="CraftStation"/>), or 0.</summary>
    public int station;
    /// <summary>Why it cannot be done right now (a <see cref="QuestBlock"/>), or 0.</summary>
    public int block;
}

/// <summary>
/// A CHARACTER'S QUESTS — server side. Holds one record per quest ever given, moves each open one forward through
/// every step whose condition holds, and remembers which records changed so the next save carries exactly those.
///
/// <para><b>Forward only.</b> A finished step never comes undone and a finished quest is never given again. A step id
/// the quest no longer has (the designer removed it) resumes at the first step, and the holdings carry it straight
/// back to wherever the character really is.</para>
///
/// <para><b>Saving.</b> Every change stamps the record with a rising number. A save carries the records stamped after
/// the last save that LANDED (<see cref="CapturePending"/>), and a landed save acknowledges up to the number it was
/// taken at (<see cref="Acknowledge"/>). A newer save waiting in the outbox therefore always carries everything an
/// older one did, so replacing the older one loses nothing.</para>
/// </summary>
public sealed class QuestLog
{
    sealed class Record
    {
        public string questId;
        public string stepId;
        public bool tracked;
        public bool finished;
        public int progress;
        public int changedAt;
    }

    // Quests that finished since the server last asked — their rewards are owed (see TakeJustFinished).
    readonly List<QuestDef> justFinished = new List<QuestDef>(2);

    readonly Dictionary<string, Record> records = new Dictionary<string, Record>();
    int stamp, acked;

    /// <summary>Goes up on every change — what tells the shell to send the player a new list.</summary>
    public int Version { get; private set; }

    static readonly EquipSlotType[] WornSlots =
    {
        EquipSlotType.Weapon, EquipSlotType.Shield, EquipSlotType.Head, EquipSlotType.Armor,
        EquipSlotType.Garment, EquipSlotType.Boots,
    };

    // Reused for every check: what the character holds, read once per check.
    readonly List<QuestHeld> held = new List<QuestHeld>(48);

    /// <summary>Lay on the records that arrived with the character. Nothing is pending: they are what the store holds.</summary>
    public void Load(QuestRowWire[] rows)
    {
        records.Clear();
        stamp = acked = 0;
        if (rows != null)
            foreach (var r in rows)
                if (r != null && !string.IsNullOrEmpty(r.q))
                    records[r.q] = new Record { questId = r.q, stepId = r.s ?? "", tracked = r.t, finished = r.f,
                                                progress = Mathf.Max(0, r.p) };
        Version++;
    }

    void Touch(Record r)
    {
        r.changedAt = ++stamp;
        Version++;
    }

    /// <summary>Give every quest marked "given automatically" that this character has never had. A quest given while
    /// nothing is being followed becomes the one followed. True when anything was given.</summary>
    public bool GiveAutomatic()
    {
        bool any = false;
        foreach (var def in QuestCatalog.All)
        {
            if (!def.givenAutomatically || records.ContainsKey(def.id)) continue;
            var r = new Record { questId = def.id, stepId = def.steps[0].id };
            records[def.id] = r;
            if (Followed() == null) r.tracked = true;
            Touch(r);
            any = true;
        }
        return any;
    }

    Record Followed()
    {
        foreach (var r in records.Values) if (r.tracked && !r.finished) return r;
        return null;
    }

    /// <summary>Move every open quest forward as far as what <paramref name="c"/> holds allows, and — when
    /// <paramref name="talkedTo"/> names a townsperson just spoken to — through a "talk to" step naming them. True when
    /// anything moved.</summary>
    public bool Advance(Character c, string talkedTo = null)
    {
        if (c == null || records.Count == 0) return false;
        bool heldRead = false, changed = false;
        foreach (var r in records.Values)
        {
            if (r.finished) continue;
            var def = QuestCatalog.Get(r.questId);
            if (def == null) continue;   // suspended or retired: kept exactly as it is
            int i = def.IndexOf(r.stepId);
            if (i < 0) i = 0;
            string spoken = talkedTo;
            while (i < def.steps.Length)
            {
                var o = def.steps[i].objective;
                bool met;
                if (o.kind == QuestObjectiveKind.TalkTo)
                {
                    met = !string.IsNullOrEmpty(spoken) && o.townspersonId == spoken;
                    // ONE conversation finishes one talk step, never two in a row naming the same person.
                    if (met) spoken = null;
                }
                else if (o.kind == QuestObjectiveKind.Kill)
                {
                    // Counted only while this step is the current one (see CountKill), so a step reached later starts at 0.
                    met = i == def.IndexOf(r.stepId) && r.progress >= o.count;
                }
                else
                {
                    if (!heldRead) { ReadHeld(c); heldRead = true; }
                    met = QuestMath.Met(o.kind, o.itemId, o.folds, held);
                }
                if (!met) break;
                i++;
            }
            bool done = i >= def.steps.Length;
            string step = done ? "" : def.steps[i].id;
            if (done == r.finished && step == r.stepId) continue;
            r.finished = done;
            r.stepId = step;
            // A new step starts counting from nothing.
            r.progress = 0;
            if (done) { r.tracked = false; justFinished.Add(def); }
            Touch(r);
            changed = true;
        }
        return changed;
    }

    /// <summary>One <paramref name="creatureId"/> was defeated by this character: every open quest whose CURRENT step is
    /// to defeat that creature counts it, up to the number the step asks for. True when anything counted.</summary>
    public bool CountKill(string creatureId)
    {
        if (string.IsNullOrEmpty(creatureId)) return false;
        bool counted = false;
        foreach (var r in records.Values)
        {
            if (r.finished) continue;
            var def = QuestCatalog.Get(r.questId);
            int i = def != null ? def.IndexOf(r.stepId) : -1;
            if (i < 0) continue;
            var o = def.steps[i].objective;
            if (o.kind != QuestObjectiveKind.Kill || o.creatureId != creatureId || r.progress >= o.count) continue;
            r.progress++;
            Touch(r);
            counted = true;
        }
        return counted;
    }

    /// <summary>Accept <paramref name="questId"/>, offered by <paramref name="personId"/> — only a playable quest that
    /// person offers and this character has never had. It is followed when nothing else is. True when accepted.</summary>
    public bool Accept(string questId, string personId)
    {
        var def = QuestCatalog.Get(questId);
        if (def == null || string.IsNullOrEmpty(def.offeredBy) || def.offeredBy != personId || records.ContainsKey(questId))
            return false;
        var r = new Record { questId = def.id, stepId = def.steps[0].id };
        records[def.id] = r;
        if (Followed() == null) r.tracked = true;
        Touch(r);
        return true;
    }

    /// <summary>The quests finished since this was last asked, whose rewards are owed now — each once, ever, because a
    /// finished record never finishes again. Emptied by the asking.</summary>
    public int TakeJustFinished(List<QuestDef> into)
    {
        into.Clear();
        into.AddRange(justFinished);
        justFinished.Clear();
        return into.Count;
    }

    /// <summary>Every playable quest offered by somebody who lives in <paramref name="map"/> that this character has never
    /// had — what earns a "!". Only this map's: nobody elsewhere can be seen or spoken to, and the full list grows with
    /// every quest in the game.</summary>
    public string[] Offers(string map)
    {
        var list = new List<string>();
        foreach (var def in QuestCatalog.All)
        {
            if (string.IsNullOrEmpty(def.offeredBy) || records.ContainsKey(def.id)) continue;
            var giver = WorldTownsfolkIds.Find(def.offeredBy);
            if (giver != null && giver.map == map) list.Add(def.id);
        }
        return list.ToArray();
    }

    /// <summary>The townspeople where one of this character's open quests' current step happens, seen from
    /// <paramref name="map"/> — what earns a "?". A station step names the nearest worker of that station.</summary>
    public string[] OngoingAt(string map)
    {
        var list = new List<string>();
        foreach (var r in records.Values)
        {
            if (r.finished) continue;
            var def = QuestCatalog.Get(r.questId);
            int i = def != null ? def.IndexOf(r.stepId) : -1;
            if (i < 0) continue;
            var who = PersonFor(def.steps[i].objective, map);
            if (who != null && !string.IsNullOrEmpty(who.id) && !list.Contains(who.id)) list.Add(who.id);
        }
        return list.ToArray();
    }

    // The townsperson a step happens at, if it happens at one.
    static WorldTownsfolk.Row PersonFor(QuestDef.Objective o, string map)
    {
        switch (o.kind)
        {
            case QuestObjectiveKind.TalkTo: return WorldTownsfolkIds.Find(o.townspersonId);
            case QuestObjectiveKind.Have:
            case QuestObjectiveKind.FoldedTo:
                var item = ItemCatalog.Resolve(o.itemId);
                return item != null ? WorldTownsfolkIds.WorkingAt(CraftStations.For(item), map) : null;
            default: return null;
        }
    }

    /// <summary>Follow <paramref name="questId"/> — only one of this character's own open, playable quests, and only
    /// when it is not already the one followed. True when anything changed.</summary>
    public bool Track(string questId)
    {
        if (string.IsNullOrEmpty(questId) || !records.TryGetValue(questId, out var pick) || pick.finished
            || pick.tracked || QuestCatalog.Get(questId) == null) return false;
        foreach (var r in records.Values)
            if (r.tracked) { r.tracked = false; Touch(r); }
        pick.tracked = true;
        Touch(pick);
        return true;
    }

    /// <summary>The records changed since the last save that landed, and the number to acknowledge once this one does.
    /// Empty most of the time.</summary>
    public QuestRowWire[] CapturePending(out int upTo)
    {
        upTo = stamp;
        int n = 0;
        foreach (var r in records.Values) if (r.changedAt > acked) n++;
        if (n == 0) return null;
        var rows = new QuestRowWire[n];
        int k = 0;
        foreach (var r in records.Values)
            if (r.changedAt > acked)
                rows[k++] = new QuestRowWire { q = r.questId, s = r.stepId, t = r.tracked, f = r.finished, p = r.progress };
        return rows;
    }

    /// <summary>A save taken at <paramref name="upTo"/> is done with — landed, or refused for good and kept in a file.</summary>
    public void Acknowledge(int upTo)
    {
        if (upTo > acked) acked = upTo;
    }

    /// <summary>Every open quest of a playable kind, as its owner is shown it.</summary>
    public QuestOpenWire[] Snapshot(Character c)
    {
        var list = new List<QuestOpenWire>();
        bool heldRead = false;
        foreach (var r in records.Values)
        {
            if (r.finished) continue;
            var def = QuestCatalog.Get(r.questId);
            if (def == null) continue;
            var w = new QuestOpenWire { questId = r.questId, stepId = r.stepId, tracked = r.tracked };
            int i = def.IndexOf(r.stepId);
            if (i >= 0 && def.steps[i].objective.kind == QuestObjectiveKind.Kill)
            {
                w.need = def.steps[i].objective.count;
                w.have = Mathf.Min(w.need, r.progress);
            }
            else if (i >= 0 && def.steps[i].objective.kind == QuestObjectiveKind.FoldedTo)
            {
                if (!heldRead) { ReadHeld(c); heldRead = true; }
                var o = def.steps[i].objective;
                w.need = o.folds;
                w.have = Mathf.Min(o.folds, QuestMath.BestFolds(o.itemId, held));
            }
            list.Add(w);
        }
        return list.ToArray();
    }

    /// <summary>Where the followed quest's step happens, seen from <paramref name="map"/>, and whether it can be done now.</summary>
    public QuestTargetWire Target(Character c, string map)
    {
        var t = new QuestTargetWire();
        var r = Followed();
        var def = r != null ? QuestCatalog.Get(r.questId) : null;
        int i = def != null ? def.IndexOf(r.stepId) : -1;
        if (i < 0) return t;
        var o = def.steps[i].objective;
        t.any = true;
        t.questId = r.questId;
        t.stepId = r.stepId;

        WorldTownsfolk.Row person = null;
        if (o.kind == QuestObjectiveKind.Kill)
        {
            // WHERE THE CREATURES LIVE: the nearest map, counted in doors, whose spawn list has them.
            if (NearestSpawnOf(o.creatureId, o.map, map, out string spawnMap, out Vector3 center))
            {
                t.map = spawnMap;
                t.at = center;
                if (spawnMap != map) t.nextMap = WorldRoutes.NextMap(map, spawnMap, out _) ?? "";
            }
            return t;
        }
        if (o.kind == QuestObjectiveKind.TalkTo) person = WorldTownsfolkIds.Find(o.townspersonId);
        else if (o.kind != QuestObjectiveKind.Wear)
        {
            var item = ItemCatalog.Resolve(o.itemId);
            var station = item != null ? CraftStations.For(item) : CraftStation.None;
            t.station = (int)station;
            person = WorldTownsfolkIds.WorkingAt(station, map);
            ReadHeld(c);
            t.block = (int)BlockFor(c, o, item);
        }
        else
        {
            ReadHeld(c);
            if (!QuestMath.Met(QuestObjectiveKind.Have, o.itemId, 1, held)) t.block = (int)QuestBlock.NotCarried;
        }

        if (person != null)
        {
            t.map = person.map;
            t.at = person.home;
            if (person.map != map) t.nextMap = WorldRoutes.NextMap(map, person.map, out _) ?? "";
        }
        return t;
    }

    // Where to hunt a creature: the map the step names, or else the nearest, counted in doors from `map`, that places it.
    static bool NearestSpawnOf(string creatureId, string huntIn, string map, out string spawnMap, out Vector3 center)
    {
        spawnMap = null; center = default;
        var spawns = WorldSpawns.Instance;
        if (spawns == null) return false;
        int best = int.MaxValue;
        foreach (var row in spawns.rows)
        {
            if (row == null || row.creature != creatureId || row.population <= 0) continue;
            if (!string.IsNullOrEmpty(huntIn) && row.map != huntIn) continue;
            WorldRoutes.NextMap(map, row.map, out int doors);
            if (doors < 0 || doors >= best) continue;
            best = doors; spawnMap = row.map; center = row.center;
        }
        return spawnMap != null;
    }

    // What stops the station step from being done now — measured against what the character holds this moment.
    QuestBlock BlockFor(Character c, QuestDef.Objective o, Item item)
    {
        if (item == null) return QuestBlock.None;
        if (o.kind == QuestObjectiveKind.Have)
        {
            var material = MaterialCatalog.Band(item.madeOf, 1);
            return QuestMath.CanPay(Count(c, material), item.forgeMaterialCost, c.Gold, item.forgeGoldCost)
                ? QuestBlock.None : QuestBlock.CannotPay;
        }
        // Folded To: a station works only on a piece in the bag.
        if (!QuestMath.InBag(o.itemId, held))
            return QuestMath.Met(QuestObjectiveKind.Have, o.itemId, 1, held) ? QuestBlock.TakeItOff : QuestBlock.NotCarried;
        var grade = LowestGradeInBag(o.itemId);
        var foldMaterial = MaterialCatalog.Band(item.madeOf, CraftingMath.Band(grade));
        return QuestMath.CanPay(Count(c, foldMaterial), CraftingMath.FoldMaterialCost, c.Gold, CraftingMath.FoldGoldCost(grade))
            ? QuestBlock.None : QuestBlock.CannotPay;
    }

    Quality LowestGradeInBag(string itemId)
    {
        var lowest = Quality.A;
        foreach (var h in held) if (h.itemId == itemId && !h.worn && h.quality < lowest) lowest = h.quality;
        return lowest;
    }

    static int Count(Character c, Item material)
    {
        if (material == null) return 0;
        int n = 0;
        foreach (var e in c.Bag) if (e != null && e.def == material) n += e.count;
        return n;
    }

    void ReadHeld(Character c)
    {
        held.Clear();
        foreach (var e in c.Bag)
        {
            if (e == null || e.def == null) continue;
            held.Add(new QuestHeld
            {
                itemId = e.def.id,
                folds = e.instance != null ? e.instance.upgrade : 0,
                quality = e.instance != null ? e.instance.quality : Quality.Raw,
                worn = false,
            });
        }
        foreach (var slot in WornSlots)
        {
            var w = c.GetEquippedInstance(slot);
            if (w == null || w.def == null) continue;
            held.Add(new QuestHeld { itemId = w.def.id, folds = w.upgrade, quality = w.quality, worn = true });
        }
    }
}
