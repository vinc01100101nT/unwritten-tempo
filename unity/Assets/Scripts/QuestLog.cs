using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One quest record as it is saved and as it arrives with a character — the shape the character server's
/// <c>character_quest</c> rows travel in: quest id, step id, followed, finished, replaying, the quest to follow again
/// when this one is done, and the pieces of the run under way.</summary>
[Serializable]
public class QuestRowWire
{
    public string q = "";
    public string s = "";
    public bool t;
    public bool f;
    /// <summary>How far along the current step is — creatures defeated for a Kill step; 0 otherwise.</summary>
    public int p;
    /// <summary>A finished tutorial being played again. Its steps count only pieces made during the replay.</summary>
    public bool r;
    /// <summary>The quest that was followed when this one took over, followed again when this one is done; empty when none.</summary>
    public string u = "";
    /// <summary>The pieces this run folds — empty once the quest is done.</summary>
    public QuestPieceWire[] a;
}

/// <summary>ONE PIECE OF A TUTORIAL RUN, as it is saved: which item, the copy this run made or folds (empty until there
/// is one), and what is still free on it — a character's first run of a tutorial folds each of its pieces free, once;
/// a replay frees nothing.</summary>
[Serializable]
public class QuestPieceWire
{
    public string i = "";
    public string c = "";
    /// <summary>Free forges left: 1 on a first run until the piece is forged, else 0.</summary>
    public int g;
    /// <summary>Free folds left on this run's copy.</summary>
    public int n;
    /// <summary>Free purchases left: an item a first run has you buy.</summary>
    public int b;
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
    /// <summary>Whether its current step happens somewhere the arrows can lead — what decides whether the Quests window
    /// offers "Show path" for it.</summary>
    public bool hasPath;
    /// <summary>A tutorial finished and not being replayed — every step done, listed so it can be played again.</summary>
    public bool finished;
}

/// <summary>One thing a tutorial needs before Play starts it, as its owner is told it — finished numbers: what is still
/// needed from the step it is on to its end, and whether the character has it right now.</summary>
[Serializable]
public struct QuestNeedWire
{
    public string questId;
    public QuestRequirementKind kind;
    public string itemId;
    public int amount;
    public bool met;
}

/// <summary>Why Play did or did not start a tutorial.</summary>
public enum QuestPlay { Ignored = 0, Started = 1, AlreadyPlaying = 2, NotMet = 3, MissingPiece = 4 }

/// <summary>A kill that counted toward a quest, as its owner is told it: the creature, and the step's count after it.</summary>
[Serializable]
public struct QuestKillWire
{
    public string questId;
    public string creatureId;
    public int have;
    public int need;
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
/// <para><b>A finished tutorial can be REPLAYED</b> — Play again, from its first step, paying its whole bill. It stays
/// finished throughout (its reward was paid once and is never paid again), and its steps count only the pieces made
/// during the replay, so gear already held cannot finish it at once.</para>
///
/// <para><b>A character's first run of a tutorial is free</b>: each piece it folds carries its free folds, saved on the
/// record and used up by the press that does the work (<see cref="TakeFreeFold"/>), so a quick second press, a door or
/// a restart can never spend it twice.</para>
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
        public bool replaying;
        public string resume = "";
        public readonly List<QuestPieceWire> pieces = new List<QuestPieceWire>(0);

        /// <summary>Open, or finished and being replayed — a record whose steps are still being worked through.</summary>
        public bool Running => !finished || replaying;
    }

    // Quests that finished since the server last asked — their rewards are owed (see TakeJustFinished).
    readonly List<QuestDef> justFinished = new List<QuestDef>(2);

    // Kills counted since the owner was last told, one per quest carrying its latest count (see TakeKills).
    readonly List<QuestKillWire> killsUntold = new List<QuestKillWire>(2);

    readonly Dictionary<string, Record> records = new Dictionary<string, Record>();
    int stamp, acked;

    /// <summary>Goes up on every change — what tells the shell to send the player a new list.</summary>
    public int Version { get; private set; }

    static readonly EquipSlotType[] WornSlots =
    {
        EquipSlotType.Weapon, EquipSlotType.Head, EquipSlotType.Armor,
        EquipSlotType.Garment, EquipSlotType.Boots, EquipSlotType.CostumeBody,
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
            {
                if (r == null || string.IsNullOrEmpty(r.q)) continue;
                var rec = new Record { questId = r.q, stepId = r.s ?? "", tracked = r.t, finished = r.f,
                                       progress = Mathf.Max(0, r.p), replaying = r.f && r.r, resume = r.u ?? "" };
                if (r.a != null)
                    foreach (var p in r.a)
                        if (p != null && !string.IsNullOrEmpty(p.i))
                            rec.pieces.Add(new QuestPieceWire { i = p.i, c = p.c ?? "", g = Mathf.Clamp(p.g, 0, 1),
                                                                n = Mathf.Max(0, p.n), b = Mathf.Max(0, p.b) });
                records[r.q] = rec;
            }
        Version++;
    }

    void Touch(Record r)
    {
        r.changedAt = ++stamp;
        Version++;
    }

    /// <summary>UNLOCK every quest whose unlock condition is <paramref name="moment"/> that this character has never had,
    /// as they stand on <paramref name="hp"/> of <paramref name="maxHp"/> health. A tutorial PLAYS the moment it
    /// unlocks — followed at once, taking over from the quest followed until then, which is remembered and followed again
    /// when it is done. Any other quest is followed only when nothing else is. True when anything was given.</summary>
    public bool Unlock(QuestUnlock moment, int hp, int maxHp)
    {
        if (moment == QuestUnlock.None) return false;
        bool any = false;
        foreach (var def in QuestCatalog.All)
        {
            if (def.unlockCondition != moment || records.ContainsKey(def.id)) continue;
            var before = Followed();
            var r = NewRecord(def, hp, maxHp);
            if (def.category == QuestCategory.Tutorial)
            {
                r.resume = before != null ? before.questId : "";
                Follow(r);
            }
            else if (before == null) r.tracked = true;
            Touch(r);
            any = true;
        }
        return any;
    }

    // A quest given for the first time. A tutorial's first run takes nothing its steps use up: every piece it has you
    // fold carries its free folds, and every item it has you buy is free — as many as it asks for,
    // or, when a Health step after it is healed with that item, as many as it takes to get there from the health the
    // character has now.
    Record NewRecord(QuestDef def, int hp, int maxHp)
    {
        var r = new Record { questId = def.id, stepId = def.steps[0].id };
        if (def.category == QuestCategory.Tutorial)
        {
            foreach (var p in PiecesOf(def))
                r.pieces.Add(new QuestPieceWire { i = p.itemId, n = p.folds });
            for (int i = 0; i < def.steps.Length; i++)
            {
                var o = def.steps[i].objective;
                if (o.kind != QuestObjectiveKind.Buy) continue;
                int free = Mathf.Max(1, o.count);
                for (int k = i + 1; k < def.steps.Length; k++)
                {
                    var h = def.steps[k].objective;
                    if (h.kind != QuestObjectiveKind.Health || h.itemId != o.itemId) continue;
                    free = Mathf.Max(free, QuestMath.UsesToReach(hp, maxHp, h.percent, HealPerUse(ItemCatalog.Resolve(o.itemId))));
                    break;
                }
                AddFreeBuys(r, o.itemId, free);
            }
        }
        records[def.id] = r;
        return r;
    }

    static void AddFreeBuys(Record r, string itemId, int count)
    {
        foreach (var p in r.pieces) if (p.i == itemId) { p.b += count; return; }
        r.pieces.Add(new QuestPieceWire { i = itemId, b = count });
    }

    // WHAT ONE USE OF AN ITEM HEALS, as its skill is written: every Heal effect's own amount. Read once per item — item
    // and skill data never change while the server runs.
    static readonly Dictionary<string, int> healPerUse = new Dictionary<string, int>();
    static readonly List<float> healScratch = new List<float>(2);

    static int HealPerUse(Item item)
    {
        if (item == null) return 0;
        if (healPerUse.TryGetValue(item.id, out int known)) return known;
        healScratch.Clear();
        var skill = item.IsUsable ? SkillCatalog.Get(item.useSkillId) : null;
        if (skill != null && skill.bindings != null)
            foreach (var b in skill.bindings)
                if (b != null && b.effects != null)
                    foreach (var e in b.effects)
                        if (e != null && e.kind == SkillEffectKind.Heal && !e.amount.useStacks) healScratch.Add(e.amount.value);
        int heal = QuestMath.HealOfOneUse(healScratch);
        healPerUse[item.id] = heal;
        return heal;
    }

    // A quest the catalog no longer has (suspended or retired) is never the followed one: it is shown nowhere, so it
    // could not be turned off, and it would stop every new quest being followed.
    Record Followed()
    {
        foreach (var r in records.Values)
            if (r.tracked && r.Running && QuestCatalog.Get(r.questId) != null) return r;
        return null;
    }

    /// <summary>Move every open quest forward as far as what <paramref name="c"/> holds allows, and — when
    /// <paramref name="talkedTo"/> names a townsperson just spoken to — through a "talk to" step naming them. True when
    /// anything moved.</summary>
    public bool Advance(Character c, string talkedTo = null)
    {
        if (c == null || records.Count == 0) return false;
        bool heldRead = false, changed = false;
        Record followAgain = null;
        foreach (var r in records.Values)
        {
            if (!r.Running) continue;
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
                else if (o.kind == QuestObjectiveKind.Kill || o.kind == QuestObjectiveKind.Buy)
                {
                    // Counted only while this step is the current one (see CountKill, CountBought), so a step reached
                    // later starts at 0.
                    met = i == def.IndexOf(r.stepId) && r.progress >= o.count;
                }
                else if (o.kind == QuestObjectiveKind.Health)
                {
                    met = !c.IsDead && QuestMath.HealthMet(c.CurrentHP, c.MaxHP, o.percent);
                }
                else
                {
                    if (!heldRead) { ReadHeld(c); heldRead = true; }
                    met = QuestMath.Met(o.kind, o.itemId, o.folds, HeldFor(r));
                }
                if (!met) break;
                i++;
            }
            bool done = i >= def.steps.Length;
            string step = done ? "" : def.steps[i].id;
            if (!done && step == r.stepId) continue;
            r.stepId = step;
            // A new step starts counting from nothing.
            r.progress = 0;
            // WHERE A TUTORIAL WAS AT, AND WHEN: each step reached is a line, so the gap between two of them is how long
            // a tester spent on a step — and the last one before they stopped is where they got stuck.
            if (!done && GameServer.IsServer && !c.IsWatchedCopy && GameLog.On(LogCat.Server))
                GameLog.Info(LogCat.Server, string.Concat(SpawnIds.MapOf(c), " ", SpawnIds.Label(c), " reached step ",
                                                          (i + 1).ToString(), " of ", def.steps.Length.ToString(), " (",
                                                          step, ") of quest ", def.id));
            if (done)
            {
                r.tracked = false;
                r.pieces.Clear();
                // A REPLAY ENDS as the finished quest it always was: nothing is paid again.
                if (r.replaying) r.replaying = false;
                else { r.finished = true; justFinished.Add(def); }
                if (!string.IsNullOrEmpty(r.resume))
                {
                    if (records.TryGetValue(r.resume, out var back) && back.Running) followAgain = back;
                    r.resume = "";
                }
            }
            Touch(r);
            changed = true;
        }
        // THE TUTORIAL THIS ONE TOOK OVER FROM is followed again, once nothing else has been chosen in the meantime.
        if (followAgain != null && Followed() == null) Follow(followAgain);
        return changed;
    }

    // What a running record's held-things steps are judged on: everything held — or, on a replay, only the copies made
    // during it, so gear already held cannot finish a replay at once. Expects `held` read.
    readonly List<QuestHeld> runHeld = new List<QuestHeld>(8);

    List<QuestHeld> HeldFor(Record r)
    {
        if (!r.replaying && !r.finished) return held;
        runHeld.Clear();
        foreach (var h in held)
            if (!string.IsNullOrEmpty(h.copyId))
                foreach (var p in r.pieces)
                    if (p.i == h.itemId && p.c == h.copyId) { runHeld.Add(h); break; }
        return runHeld;
    }

    /// <summary>SERVER, at a shop: <paramref name="count"/> of <paramref name="itemId"/> were bought. Every running quest
    /// whose CURRENT step is to buy that item counts them, up to the number the step asks for. True when anything counted.</summary>
    public bool CountBought(string itemId, int count)
    {
        if (string.IsNullOrEmpty(itemId) || count <= 0) return false;
        bool counted = false;
        foreach (var r in records.Values)
        {
            if (!r.Running) continue;
            var def = QuestCatalog.Get(r.questId);
            int i = def != null ? def.IndexOf(r.stepId) : -1;
            if (i < 0) continue;
            var o = def.steps[i].objective;
            if (o.kind != QuestObjectiveKind.Buy || o.itemId != itemId || r.progress >= o.count) continue;
            r.progress = Mathf.Min(o.count, r.progress + count);
            Touch(r);
            counted = true;
        }
        return counted;
    }

    /// <summary>SERVER, at a shop: how many of <paramref name="howMany"/> <paramref name="itemId"/> a first run of a
    /// tutorial gives free — used up here, in the same call that does the buying, so they are given once.</summary>
    public int TakeFreeBuys(string itemId, int howMany)
    {
        int free = 0;
        foreach (var r in records.Values)
        {
            if (free >= howMany) break;
            if (r.finished || QuestCatalog.Get(r.questId) == null) continue;
            foreach (var p in r.pieces)
            {
                if (p.i != itemId || p.b <= 0) continue;
                int take = Mathf.Min(p.b, howMany - free);
                p.b -= take;
                free += take;
                Touch(r);
                break;
            }
        }
        return free;
    }

    /// <summary>How many <paramref name="itemId"/> a first run of a tutorial still gives free at a shop.</summary>
    public int FreeBuysOf(string itemId)
    {
        int free = 0;
        foreach (var r in records.Values)
            if (!r.finished && QuestCatalog.Get(r.questId) != null) free += FreeBuysLeft(r, itemId);
        return free;
    }

    /// <summary>The items a first run of a tutorial still gives free at a shop, and how many — what a shop's rows say.</summary>
    public void FreeBuys(List<string> ids, List<int> counts)
    {
        ids.Clear();
        counts.Clear();
        foreach (var r in records.Values)
        {
            if (r.finished || QuestCatalog.Get(r.questId) == null) continue;
            foreach (var p in r.pieces)
            {
                if (p.b <= 0) continue;
                int at = ids.IndexOf(p.i);
                if (at < 0) { ids.Add(p.i); counts.Add(p.b); }
                else counts[at] += p.b;
            }
        }
    }

    /// <summary>One <paramref name="creatureId"/> was defeated by this character: every open quest whose CURRENT step is
    /// to defeat that creature counts it, up to the number the step asks for. True when anything counted.</summary>
    public bool CountKill(string creatureId)
    {
        if (string.IsNullOrEmpty(creatureId)) return false;
        bool counted = false;
        foreach (var r in records.Values)
        {
            if (!r.Running) continue;
            var def = QuestCatalog.Get(r.questId);
            int i = def != null ? def.IndexOf(r.stepId) : -1;
            if (i < 0) continue;
            var o = def.steps[i].objective;
            if (o.kind != QuestObjectiveKind.Kill || o.creatureId != creatureId || r.progress >= o.count) continue;
            r.progress++;
            Touch(r);
            counted = true;
            TellKill(new QuestKillWire { questId = r.questId, creatureId = creatureId, have = r.progress, need = o.count });
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

    // ONE LINE PER QUEST however many of its kills land before the owner is next told: the latest count replaces the last.
    void TellKill(QuestKillWire k)
    {
        for (int i = 0; i < killsUntold.Count; i++)
            if (killsUntold[i].questId == k.questId) { killsUntold[i] = k; return; }
        killsUntold.Add(k);
    }

    /// <summary>The kills counted since this was last asked, for the owner's screen — null when none. Emptied by the
    /// asking.</summary>
    public QuestKillWire[] TakeKills()
    {
        if (killsUntold.Count == 0) return null;
        var told = killsUntold.ToArray();
        killsUntold.Clear();
        return told;
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
            if (!r.Running) continue;
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
            case QuestObjectiveKind.Buy:
                return WorldTownsfolkIds.Selling(ItemCatalog.Resolve(o.itemId), map);
            default: return null;
        }
    }

    /// <summary>Follow <paramref name="questId"/> — only one of this character's own open, playable quests, and only
    /// when it is not already the one followed — or, given no id, follow nothing (a tutorial's Stop). It names the result wanted, never
    /// "switch", so the same ask twice changes nothing the second time. True when anything changed.
    /// A tutorial is never followed this way — only <see cref="Play"/> starts one, because only Play checks what it needs.</summary>
    public bool Track(string questId)
    {
        Record pick = null;
        if (!string.IsNullOrEmpty(questId))
        {
            var def = QuestCatalog.Get(questId);
            if (def == null || def.category == QuestCategory.Tutorial || !records.TryGetValue(questId, out pick)
                || pick.finished || pick.tracked) return false;
        }
        return Follow(pick);
    }

    /// <summary>PLAY a tutorial: follow it — its guide and its path — but only when the character has what the rest of
    /// it needs right now (<see cref="CollectNeeds"/>). An open tutorial carries on from its step; a FINISHED one is
    /// replayed from its first step, which needs its whole bill — and, for every piece it folds, a Raw copy held with
    /// room for that many folds; <paramref name="missingItemId"/> names the first piece without one. Checked at the
    /// press only: what is spent afterwards is the guide's to explain.</summary>
    public QuestPlay Play(string questId, Character c, out string missingItemId)
    {
        missingItemId = "";
        var def = QuestCatalog.Get(questId);
        if (def == null || def.category != QuestCategory.Tutorial || !records.TryGetValue(questId, out var r))
            return QuestPlay.Ignored;
        if (r.tracked && r.Running) return QuestPlay.AlreadyPlaying;
        if (!r.Running)
        {
            ReadHeld(c);
            foreach (var p in PiecesOf(def))
                if (!HoldsRoomFor(p)) { missingItemId = p.itemId; return QuestPlay.MissingPiece; }
        }
        if (!NeedsMet(r, def, c)) return QuestPlay.NotMet;
        if (!r.Running)
        {
            // A REPLAY: from the first step, with a fresh list of the pieces it makes and nothing free on any of them.
            r.replaying = true;
            r.stepId = def.steps[0].id;
            r.progress = 0;
            r.resume = "";
            r.pieces.Clear();
            foreach (var p in PiecesOf(def)) r.pieces.Add(new QuestPieceWire { i = p.itemId });
            Touch(r);
        }
        Follow(r);
        return QuestPlay.Started;
    }

    // Does the character hold — worn or carried — an UNFOLDED Raw copy of the piece? What a replay needs: it is folded
    // from Raw +0 again, every fold of it. Room under the cap is not enough — with a cap of 10, a Raw +5 copy would meet
    // a "fold five times" step in one fold. Expects `held` read.
    bool HoldsRoomFor(Piece p)
    {
        foreach (var h in held)
            if (h.itemId == p.itemId && h.quality == Quality.Raw && h.folds == 0)
                return true;
        return false;
    }

    // Follow `pick` and nothing else — or, given nothing, follow nothing. True when anything changed.
    bool Follow(Record pick)
    {
        bool changed = false;
        foreach (var r in records.Values)
            if (r.tracked && r != pick) { r.tracked = false; Touch(r); changed = true; }
        if (pick != null && !pick.tracked) { pick.tracked = true; Touch(pick); changed = true; }
        return changed;
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
                rows[k++] = new QuestRowWire { q = r.questId, s = r.stepId, t = r.tracked, f = r.finished, p = r.progress,
                                               r = r.replaying, u = r.resume, a = CopyPieces(r) };
        return rows;
    }

    // A copy: a save's rows never share an object with the live record, which goes on changing.
    static QuestPieceWire[] CopyPieces(Record r)
    {
        var a = new QuestPieceWire[r.pieces.Count];
        for (int i = 0; i < a.Length; i++)
        {
            var p = r.pieces[i];
            a[i] = new QuestPieceWire { i = p.i, c = p.c, g = p.g, n = p.n, b = p.b };
        }
        return a;
    }

    /// <summary>A save taken at <paramref name="upTo"/> is done with — landed, or refused for good and kept in a file.</summary>
    public void Acknowledge(int upTo)
    {
        if (upTo > acked) acked = upTo;
    }

    /// <summary>Every open quest of a playable kind, as its owner is shown it — and every finished tutorial, which can be
    /// played again.</summary>
    public QuestOpenWire[] Snapshot(Character c, string map)
    {
        var list = new List<QuestOpenWire>();
        bool heldRead = false;
        foreach (var r in records.Values)
        {
            var def = QuestCatalog.Get(r.questId);
            if (def == null) continue;
            if (!r.Running)
            {
                if (def.category == QuestCategory.Tutorial)
                    list.Add(new QuestOpenWire { questId = r.questId, stepId = "", finished = true });
                continue;
            }
            var w = new QuestOpenWire { questId = r.questId, stepId = r.stepId, tracked = r.tracked };
            int i = def.IndexOf(r.stepId);
            if (i >= 0) w.hasPath = HasPlace(def.steps[i].objective, map);
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
                w.have = Mathf.Min(o.folds, QuestMath.BestFolds(o.itemId, HeldFor(r)));
            }
            else if (i >= 0 && def.steps[i].objective.kind == QuestObjectiveKind.Health && c != null)
            {
                w.need = QuestMath.HealthNeeded(c.MaxHP, def.steps[i].objective.percent);
                w.have = Mathf.Min(w.need, c.CurrentHP);
            }
            list.Add(w);
        }
        return list.ToArray();
    }

    /// <summary>Where the followed quest's step happens, seen from <paramref name="map"/>, and whether it can be done now.</summary>
    public QuestTargetWire Target(Character c, string map) => TargetOf(Followed(), c, map);

    /// <summary>THE QUEST THE GUIDE TEACHES: the followed quest when it is a tutorial — a tutorial plays by being
    /// followed, so its guide and its path are one choice — and nothing otherwise. Only tutorials carry hints.</summary>
    public static QuestTargetWire GuideOf(QuestTargetWire followed)
    {
        var def = followed.any ? QuestCatalog.Get(followed.questId) : null;
        return def != null && def.category == QuestCategory.Tutorial ? followed : default;
    }

    QuestTargetWire TargetOf(Record r, Character c, string map)
    {
        var t = new QuestTargetWire();
        var def = r != null ? QuestCatalog.Get(r.questId) : null;
        int i = def != null ? def.IndexOf(r.stepId) : -1;
        if (i < 0) return t;
        var o = def.steps[i].objective;
        t.any = true;
        t.questId = r.questId;
        t.stepId = r.stepId;

        WorldTownsfolk.Row person = null;
        if (o.kind == QuestObjectiveKind.Health)
        {
            // Done wherever they stand, from the bag: there is nowhere to lead, only the item to run out of.
            var use = ItemCatalog.Resolve(o.itemId);
            if (use != null && Count(c, use) <= 0) t.block = (int)QuestBlock.NoneLeft;
            return t;
        }
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
        else if (o.kind == QuestObjectiveKind.Buy)
        {
            // WHERE IT IS SOLD, and whether it can be paid for: free on a first run while its free purchases last.
            var item = ItemCatalog.Resolve(o.itemId);
            person = WorldTownsfolkIds.Selling(item, map);
            if (item != null && FreeBuysLeft(r, o.itemId) <= 0 && !QuestMath.CanPay(0, 0, c.Gold, ShopMath.BuyCost(item.value, 1)))
                t.block = (int)QuestBlock.CannotPay;
        }
        else if (o.kind != QuestObjectiveKind.Wear)
        {
            var item = ItemCatalog.Resolve(o.itemId);
            var station = item != null ? CraftStations.For(item) : CraftStation.None;
            t.station = (int)station;
            person = WorldTownsfolkIds.WorkingAt(station, map);
            ReadHeld(c);
            t.block = (int)BlockFor(r, def, c, o, item);
        }
        else
        {
            ReadHeld(c);
            if (!QuestMath.Met(QuestObjectiveKind.Have, o.itemId, 1, HeldFor(r))) t.block = (int)QuestBlock.NotCarried;
        }

        if (person != null)
        {
            t.map = person.map;
            t.at = person.home;
            if (person.map != map) t.nextMap = WorldRoutes.NextMap(map, person.map, out _) ?? "";
        }
        return t;
    }

    // ---- what a tutorial needs ---------------------------------------------------------------------------------------
    // THE PIECES A QUEST HAS YOU FOLD — every item a Folded To step names — read once per quest, with what one fold of
    // a Raw piece costs, so the bill is never typed twice and never drifts from what the station charges.
    sealed class Piece
    {
        public string itemId;
        public Item material;
        public int folds, foldMaterialEach, foldGoldEach;
        public int lastStep;   // the last step that needs this piece: once past it, nothing of it is owed
    }

    static readonly Dictionary<string, List<Piece>> piecesOf = new Dictionary<string, List<Piece>>();

    static List<Piece> PiecesOf(QuestDef def)
    {
        if (piecesOf.TryGetValue(def.id, out var list)) return list;
        list = new List<Piece>();
        for (int i = 0; i < def.steps.Length; i++)
        {
            var o = def.steps[i].objective;
            if (o.kind != QuestObjectiveKind.FoldedTo || ItemCatalog.Resolve(o.itemId) == null) continue;
            Piece p = null;
            foreach (var q in list) if (q.itemId == o.itemId) { p = q; break; }
            if (p == null)
            {
                // A tutorial's piece is folded from Raw.
                p = new Piece
                {
                    itemId = o.itemId, material = MaterialCatalog.TemperDust,
                    foldMaterialEach = CraftingMath.FoldMaterialCost(Quality.Raw), foldGoldEach = CraftingMath.FoldGoldCost(Quality.Raw),
                };
                list.Add(p);
            }
            p.folds = Mathf.Max(p.folds, o.folds);
            p.lastStep = i;
        }
        piecesOf[def.id] = list;
        return list;
    }

    // Reused for every count — no allocation per push.
    readonly List<QuestNeedWire> needs = new List<QuestNeedWire>(8);
    readonly List<(Item material, int amount)> materialScratch = new List<(Item, int)>(4);

    /// <summary>Work out what every open tutorial still needs and whether this character has it, and return a number
    /// that changes whenever any of it does — what tells the shell the window's list is out of date. Read the result
    /// with <see cref="NeedsSnapshot"/>.</summary>
    public int CollectNeeds(Character c)
    {
        needs.Clear();
        if (c == null) return 0;
        bool heldRead = false;
        // Every tutorial — a finished one too, which shows what playing it again needs.
        foreach (var r in records.Values)
        {
            var def = QuestCatalog.Get(r.questId);
            if (def == null || def.category != QuestCategory.Tutorial) continue;
            if (!heldRead) { ReadHeld(c); heldRead = true; }
            AddNeeds(r, def, c);
        }
        int hash = needs.Count;
        foreach (var n in needs)
            hash = hash * 31 + (n.questId.GetHashCode() ^ (int)n.kind * 7919 ^ (n.itemId ?? "").GetHashCode()
                                ^ n.amount * 104729 ^ (n.met ? 1 : 0));
        return hash;
    }

    /// <summary>The list <see cref="CollectNeeds"/> last worked out, for sending.</summary>
    public QuestNeedWire[] NeedsSnapshot() => needs.ToArray();

    // Does this character have, right now, everything the rest of this tutorial needs?
    bool NeedsMet(Record r, QuestDef def, Character c)
    {
        if (c == null) return false;
        needs.Clear();
        ReadHeld(c);
        AddNeeds(r, def, c);
        foreach (var n in needs) if (!n.met) return false;
        return true;
    }

    // What is STILL needed from the step `r` is on to the quest's end: folds already made are not paid again, a piece no
    // step ahead needs costs nothing, and what a first run has free costs nothing. A finished tutorial is priced as a replay: from its first step, with nothing held counting. Then the
    // level the quest names. Expects `held` read.
    void AddNeeds(Record r, QuestDef def, Character c)
    {
        int at = r.Running ? def.IndexOf(r.stepId) : 0;
        if (at < 0) at = 0;
        int gold = 0, level = def.levelNeeded;
        var judged = HeldFor(r);
        materialScratch.Clear();
        foreach (var p in PiecesOf(def))
        {
            if (p.lastStep < at) continue;
            int foldsDone = QuestMath.Met(QuestObjectiveKind.FoldedTo, p.itemId, p.folds, judged)
                ? p.folds : QuestMath.BestFolds(p.itemId, judged);
            int foldsFree = 0;
            if (!r.finished)
                foreach (var q in r.pieces)
                    if (q.i == p.itemId) { foldsFree = q.n; break; }
            int material = 0;
            QuestMath.AddForgedPiece(Mathf.Max(0, p.folds - foldsDone - foldsFree),
                                     p.foldMaterialEach, p.foldGoldEach, ref material, ref gold);
            AddMaterial(p.material, material);
        }
        // WHAT IT HAS YOU BUY, at the shop's price — less what a first run still gives free.
        for (int i = at; i < def.steps.Length; i++)
        {
            var o = def.steps[i].objective;
            if (o.kind != QuestObjectiveKind.Buy) continue;
            var item = ItemCatalog.Resolve(o.itemId);
            if (item == null) continue;
            int bought = r.Running && i == def.IndexOf(r.stepId) ? r.progress : 0;
            QuestMath.AddBought((int)ShopMath.BuyCost(item.value, 1), o.count, bought, FreeBuysLeft(r, o.itemId), ref gold);
        }
        foreach (var (material, amount) in materialScratch)
            needs.Add(new QuestNeedWire { questId = r.questId, kind = QuestRequirementKind.Item, itemId = material.id,
                                          amount = amount, met = Count(c, material) >= amount });
        if (gold > 0)
            needs.Add(new QuestNeedWire { questId = r.questId, kind = QuestRequirementKind.Gold, itemId = "",
                                          amount = gold, met = c.Gold >= gold });
        if (level > 0)
            needs.Add(new QuestNeedWire { questId = r.questId, kind = QuestRequirementKind.Level, itemId = "",
                                          amount = level, met = c.Level >= level });
    }

    void AddMaterial(Item material, int amount)
    {
        if (material == null || amount <= 0) return;
        for (int i = 0; i < materialScratch.Count; i++)
            if (materialScratch[i].material == material)
            {
                materialScratch[i] = (material, materialScratch[i].amount + amount);
                return;
            }
        materialScratch.Add((material, amount));
    }

    // WHETHER A STEP HAPPENS SOMEWHERE, seen from `map` — the same lookups Target uses, kept per question because they
    // walk the world's spawn and townsfolk lists, which never change while the server runs, and every open quest of
    // every player asks on every push.
    static readonly Dictionary<(QuestObjectiveKind, string, string, string), bool> placeKnown =
        new Dictionary<(QuestObjectiveKind, string, string, string), bool>();

    static bool HasPlace(QuestDef.Objective o, string map)
    {
        switch (o.kind)
        {
            case QuestObjectiveKind.Wear:
            case QuestObjectiveKind.Health: return false;
            case QuestObjectiveKind.TalkTo: return WorldTownsfolkIds.Find(o.townspersonId) != null;
        }
        var key = o.kind == QuestObjectiveKind.Kill ? (o.kind, o.creatureId, o.map, map) : (o.kind, o.itemId, "", map);
        if (placeKnown.TryGetValue(key, out bool known)) return known;
        if (o.kind == QuestObjectiveKind.Kill) known = NearestSpawnOf(o.creatureId, o.map, map, out _, out _);
        else if (o.kind == QuestObjectiveKind.Buy) known = WorldTownsfolkIds.Selling(ItemCatalog.Resolve(o.itemId), map) != null;
        else
        {
            var item = ItemCatalog.Resolve(o.itemId);
            known = item != null && WorldTownsfolkIds.WorkingAt(CraftStations.For(item), map) != null;
        }
        placeKnown[key] = known;
        return known;
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

    // What stops a Folded To step from being done now — measured against what the character holds this moment, worn or
    // carried (a station works on both), and what this run still has free. Any other station step has nothing to pay.
    QuestBlock BlockFor(Record r, QuestDef def, Character c, QuestDef.Objective o, Item item)
    {
        if (item == null || o.kind != QuestObjectiveKind.FoldedTo) return QuestBlock.None;
        // On a replay, only the piece the replay is folding counts — once its first fold has named it; before that,
        // every copy held.
        var judged = HeldFor(r);
        if (r.replaying && !QuestMath.Met(QuestObjectiveKind.Have, o.itemId, 1, judged)) judged = held;
        if (!QuestMath.Met(QuestObjectiveKind.Have, o.itemId, 1, judged)) return QuestBlock.NotCarried;
        var grade = LowestGradeHeld(o.itemId, judged);
        if (grade == Quality.Raw)
            foreach (var h in judged)
                if (h.itemId == o.itemId && FreeFoldOn(r, def, o.itemId, h.copyId, h.folds) != null) return QuestBlock.None;
        return QuestMath.CanPay(Count(c, MaterialCatalog.TemperDust), CraftingMath.FoldMaterialCost(grade), c.Gold,
                                CraftingMath.FoldGoldCost(grade))
            ? QuestBlock.None : QuestBlock.CannotPay;
    }

    // ---- what a first run gives free --------------------------------------------------------------------------------
    // Free only while the record is on its FIRST run (never a replay) and the piece is still owed — a step ahead still
    // needs it — so nothing free is ever left lying about to be spent on something the tutorial no longer asks for.

    static int FreeBuysLeft(Record r, string itemId)
    {
        if (r == null || r.finished) return 0;
        foreach (var p in r.pieces) if (p.i == itemId) return p.b;
        return 0;
    }

    // Free only below the piece's own target: with a cap of 10, a free fold spent past the tutorial's +5 would turn
    // the folds that can fail into free retries the tutorial never asked for.
    QuestPieceWire FreeFoldOn(Record r, QuestDef def, string itemId, string copyId, int foldsNow)
    {
        if (r == null || def == null || r.finished || r.replaying || string.IsNullOrEmpty(copyId)
            || !Owed(r, def, itemId) || foldsNow >= TargetFolds(def, itemId)) return null;
        foreach (var p in r.pieces)
            if (p.i == itemId && p.n > 0 && (p.c == "" || p.c == copyId)) return p;
        return null;
    }

    static int TargetFolds(QuestDef def, string itemId)
    {
        foreach (var p in PiecesOf(def)) if (p.itemId == itemId) return p.folds;
        return 0;
    }

    static bool Owed(Record r, QuestDef def, string itemId)
    {
        int at = def.IndexOf(r.stepId);
        if (at < 0) at = 0;
        foreach (var p in PiecesOf(def)) if (p.itemId == itemId) return p.lastStep >= at;
        return false;
    }

    /// <summary>SERVER: the first run of a tutorial still owes a step on <paramref name="itemId"/> — so recycling, selling
    /// or dropping one is refused, or a new character could destroy the piece the tutorial needs and nothing could make
    /// another. The title of that tutorial, or null when none owes it.</summary>
    public string FirstRunOwing(string itemId)
    {
        foreach (var r in records.Values)
        {
            if (r.finished || r.replaying) continue;
            var def = QuestCatalog.Get(r.questId);
            if (def != null && Owed(r, def, itemId)) return def.title;
        }
        return null;
    }

    /// <summary>SERVER, at the fold: <paramref name="copyId"/>, a <paramref name="itemId"/> of grade
    /// <paramref name="grade"/> with <paramref name="foldsBefore"/> folds, is being folded. Every REPLAY with that piece
    /// and no copy yet takes it as its run's copy when it is Raw and unfolded — the one the replay's fold and wear steps
    /// count.</summary>
    public void NoteReplayFold(string itemId, string copyId, Quality grade, int foldsBefore)
    {
        if (string.IsNullOrEmpty(copyId) || grade != Quality.Raw || foldsBefore != 0) return;
        foreach (var r in records.Values)
        {
            if (!r.replaying) continue;
            var def = QuestCatalog.Get(r.questId);
            if (def == null) continue;
            foreach (var piece in PiecesOf(def))
            {
                if (piece.itemId != itemId) continue;
                foreach (var p in r.pieces)
                    if (p.i == itemId && p.c == "") { p.c = copyId; Touch(r); break; }
            }
        }
    }

    /// <summary>Is the next fold of <paramref name="copyId"/>, a <paramref name="itemId"/> of grade
    /// <paramref name="grade"/> at <paramref name="foldsNow"/> folds, free? Only a Raw piece's fold is ever free, and only
    /// below the tutorial's own target. What the station's offer says it costs, and what a fold checks BEFORE its roll.</summary>
    public bool FoldIsFree(string itemId, string copyId, Quality grade, int foldsNow)
    {
        if (grade != Quality.Raw) return false;
        foreach (var r in records.Values)
            if (FreeFoldOn(r, QuestCatalog.Get(r.questId), itemId, copyId, foldsNow) != null) return true;
        return false;
    }

    /// <summary>SERVER, after a free fold SUCCEEDED: use up one free fold on <paramref name="copyId"/>. The copy becomes
    /// that run's copy, so its other free folds can go on no other piece. A free fold that fails is never used up — a
    /// new character has no gold to pay for the try again.</summary>
    public bool TakeFreeFold(string itemId, string copyId, Quality grade, int foldsNow)
    {
        if (grade != Quality.Raw) return false;
        foreach (var r in records.Values)
        {
            var p = FreeFoldOn(r, QuestCatalog.Get(r.questId), itemId, copyId, foldsNow);
            if (p == null) continue;
            p.n--;
            p.c = copyId;
            Touch(r);
            return true;
        }
        return false;
    }

    // ---- what is watching health ------------------------------------------------------------------------------------
    int watchedAt = -1;
    bool watching;

    /// <summary>True while a running quest's current step is judged on health — the only time a change of health is
    /// worth looking at. Worked out again only when a quest changed.</summary>
    public bool WatchesHealth
    {
        get
        {
            if (watchedAt == Version) return watching;
            watchedAt = Version;
            watching = false;
            foreach (var r in records.Values)
            {
                if (!r.Running) continue;
                var def = QuestCatalog.Get(r.questId);
                int i = def != null ? def.IndexOf(r.stepId) : -1;
                if (i >= 0 && def.steps[i].objective.kind == QuestObjectiveKind.Health) { watching = true; break; }
            }
            return watching;
        }
    }

    static Quality LowestGradeHeld(string itemId, List<QuestHeld> from)
    {
        var lowest = Quality.A;
        foreach (var h in from) if (h.itemId == itemId && h.quality < lowest) lowest = h.quality;
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
                copyId = e.instance != null ? e.instance.copyId : "",
            });
        }
        foreach (var slot in WornSlots)
        {
            var w = c.GetEquippedInstance(slot);
            if (w == null || w.def == null) continue;
            held.Add(new QuestHeld { itemId = w.def.id, folds = w.upgrade, quality = w.quality, worn = true,
                                     copyId = w.copyId });
        }
    }
}
