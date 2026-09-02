# Map isolation, entity ownership, and the creature bugs it uncovered

**Task/bug** — The user reported standing in `town_0` and also seeing `field_11`: the server holds both maps
open and they occupied the same coordinates. Fixing that uncovered five further bugs, all fixed here. No Tempo
issue id.

## Status

**IN PROGRESS — code complete, LARGELY UNVERIFIED.** Everything compiles, both games are built with their
Addressables content, and the server runs clean with all 33 creatures. But **only the first three items below
were ever watched working in a real play session.** Everything after them is measured-and-reasoned, not played.

**Don't trust prior "it works" claims.** Two specific ones already turned out wrong this session:
- I claimed summoned creatures sink through the world (a mechanism I could point at). A play-test measured six
  summons holding their height exactly. **Dead.**
- I claimed the step-offset error came from the controller being enabled. Measured in the live editor: an
  activation validates the controller whether it is enabled or not. The real fix was writing the clamped step
  **while the object is still switched off**.

## What's done

### Watched working in a play session
1. **One map per playing machine.** `MapManager.LetGoOfEveryOtherMap`, called from `MapManager.Activate` — the
   single moment a map becomes current. Every other loaded gameplay scene is unloaded. Servers exempt.
   `MapManager.Travel` no longer unloads the previous scene itself (that was a second answer to the same
   question).
2. **Entities belong to the map that made them.** `MapManager.PutInMap(go, scene)`. `MobSpawner.Spawn` now
   REQUIRES a `Scene` so no caller can forget; `FieldMobSpawner`, `GmCommands` (×2), `NetMob` updated;
   `LootDropper` (×2) and `AmbientLife` call it directly. **Measured before the fix:** all 12 of the town's
   people were being built into `field_11`, because their model arrives after the second map has loaded and
   Unity drops a new object into whichever scene loaded last.
3. **Creatures no longer pile on one spot.** `MapManager.ParkAwayFromTheOthers` moves a map's roots and then
   calls `Physics.SyncTransforms()`. Without it the spawner's 16 ground rays all missed (this project sets
   `m_AutoSyncTransforms: 0`) and `FieldMobSpawner.PickSpot` silently returned the spawner's own position —
   all 32 creatures on one point.

### Measured but never played
4. **Maps are set apart on a server.** `ParkingGap = 2000f`; each held map after the first is offset on X.
   Only a server parks. What crosses the wire became a position INSIDE a map: `MapManager.InMap` / `InWorld`,
   applied in `NetMob` (`BindOnServer`, `PushFromTheRealMob`, `FollowTheServer`, `EnsureView`,
   `ReviveTheWatchedCopy`) and `NetPlayer` (`CmdReportPosition`, `FollowTheServer`, `EnsureBody`).
   `NetMob.StandWhereTheMobStands` deliberately still uses raw server world coords — Mirror's interest
   management measures that.
   Also: `MapManager.OnSceneUnloaded` releases a map's parking place; `MapManager.Hook` moved to
   `BeforeSceneLoad` so parking happens before anything reacts to a map arriving.
5. **Creatures never moved — root cause.** `SimulationLod.Evaluate` was finding "the one player at this
   keyboard" with `FindAnyObjectByType<Player3DController>()`. A server has no avatar of its own (or finds the
   idle one in the first map), so every creature measured itself against the wrong body, went not-simulating,
   and had its mover switched off. It now asks `PlayerUnits.AnyoneWithin(pos, simulateRadius, scene.name)` —
   the same question `MonsterAI3D.PickPrey` already asks. **Measured on a running server:** a field creature
   reporting `lod=False agentOn=False speed=0.69`.
6. **The 78 "Step Offset" errors.** `MobRespawn.Reactivate` now clamps the step offset and switches the object
   on itself when it arrives banked; `NetMob.EnsureView` and `FieldMobSpawner` no longer call `SetActive(true)`
   first.
7. **Summons into a map this machine does not hold.** `GmCommands.TryAskersMap` gates `dummy` and `summon`,
   replying "The world isn't holding {map} — nothing can be built there." `GmContext.MapName` added beside
   `GmContext.Map` so the message and the lookup can never name different maps.
8. **A person is drawn only when they are in your map.** `NetPlayer.FollowTheServer` gained the same map gate
   `NetMob` has, plus `NetPlayer.DropTheBody`. Previously two people in different maps stayed apart only
   because they fell outside `GameNetwork.VisibleRange = 60`.
9. **Creatures below the floor.** `MonsterAI3D`'s wander branch now puts a creature back on its floor if it is
   under it (ray from 60 above, 200 down), and only wanders to a spot that HAS a floor. `WanderGroundMask`
   caches the layer lookup. `FieldMobSpawner.PickSpot` now warns once per spawner when all 16 probes miss.
10. **Loot reaches players.** NEW FILE `NetLoot.cs` (the wire half, mirroring `NetMob`) plus NEW ASSET
    `Assets/3_Data/Resources/NetLoot.prefab` (NetworkIdentity + NetLoot). `LootDropper.Drop` /
    `LootDropper.BuildBody` / `LootDropper.WatchedCoinSprite`, `WorldPickup.Award`, and in `GameNetwork`:
    `PublishLoot`, `SendPickupToServer`, `LootPrefabName`, prefab registration. Claiming is a request:
    `NetPlayer.AskForLoot` → `CmdClaimLoot` → `NetLoot.ClaimOnServer` → `TargetLootGranted` → destroy.
11. **Build tooling.** `tools/build-game.sh` now builds Addressables content itself (a build without it comes
    up with ZERO creatures and says nothing), writes a `<out>.no-content.txt` marker and fails loudly if that
    content build refuses (it refuses over an unsaved scene); waits for the editor to be READY before sending
    its request (a request sent while the editor is busy is silently dropped — that is why `both` produced
    nothing for its second half four times); and waits on the editor rather than a fixed tick count.

## Uncommitted working-tree changes (re-measured at the end of the session)

Modified: `.claude/settings.json`, `docs/.obsidian/workspace.json`, `docs/5-ai-learnings/README.md`,
`tools/build-game.sh`, `tools/check-before-done.py`, `tools/hook-analytics/hook-analytics.jsonl`,
and under `unity/Assets/Scripts/`: `AmbientLife.cs`, `Character.cs`, `FieldMobSpawner.cs`, `GameNetwork.cs`,
`GmCommand.cs`, `GmCommands.cs`, `LootDropper.cs`, `MapManager.cs`, `MobRespawn.cs`, `MobSpawner.cs`,
`MonsterAI3D.cs`, `NetMob.cs`, `NetPlayer.cs`, `SimulationLod.cs`, `WorldPickup.cs`.

Untracked: `unity/Assets/Scripts/NetLoot.cs` (+ `.meta`),
`unity/Assets/3_Data/Resources/NetLoot.prefab` (+ `.meta`),
`docs/5-ai-learnings/{bisect-by-removing-your-own-change,make-the-code-narrate-its-decision,prove-your-decoder-on-a-known-good-sample}.md`,
`tools/hook-analytics/{repeat-tally.json,repeat_watch.py}`.

**NOT MINE — leave alone:** the damage-popup COLOUR changes in `Character.cs`, `MonsterAI3D.cs` and
`NetPlayer.cs` are the user's own work in progress, already in the tree when this session started (the session
began on a clean tree; they appeared during it). The position changes in `NetPlayer.cs` and the wander changes
in `MonsterAI3D.cs` ARE from this session. Nothing is committed.

## What's left

1. **Play-test everything.** A numbered list was handed to the user; items 5 (creatures move) and 9 (loot) are
   the ones never observed at all.
2. **Loot has never run once.** Most likely first failures: the drop shell not spawning (check the server log
   for "no 'NetLoot' prefab in Resources"), the coin having no picture on a watching machine (it loads a sprite
   named `GoldCoin` from Resources and logs an error if it is missing), or the claim never arriving.
3. **The server still holds only two maps** (`GameRoot.alsoHeldOpen`), so every other map is empty of creatures
   on a player's machine. The user has confirmed this is EXPECTED — do not "fix" it.
4. `MobSpawner.Spawn` still keeps its clone when `MapManager.PutInMap` cannot move it (the map is not held
   here). The GM path is now gated, but a future caller inherits the trap. Making the move report failure would
   close the class.

## Dead ends

**Proven by measurement — do not re-test:**
- Summoned creatures do NOT sink. Six of them held identical height to three decimals over 60 seconds.
- Switching a CharacterController OFF does not spare it from validation at activation; only a step value
  written while the object is off survives the switch-on.
- Creatures standing still is NOT caused by the map separation: a server run with the gap set to 0 was equally
  motionless.
- `field_11` contains nothing below y = -5, so a creature at y = -31 is resting on nothing at all.
- A player's client build never loaded two maps from the boot path — `GameRoot` already gated the extra maps on
  `GameServer.IsServer` before this session.

**Hypotheses, NOT established:**
- That the underground creatures walked off an edge and were frozen mid-fall. It fits every measurement (static,
  alive, nothing beneath them, a frozen creature's mover does not run) but nobody watched one fall. The fix is
  deliberately a recovery net that does not depend on this being right.
- Which window the user originally saw the overlap in. Still unknown.

## Next single step

Play the checklist, starting with: enter the field, stand within a few metres of a creature, and watch for 20
seconds. If they still do not move, the discriminating measurement is whether `SimulationLod.Simulating` is true
for that creature while a player stands beside it — if it is true and the creature still does not move, the
cause is downstream in `PathAgent3D`/pathfinding, not in the switch.

## How to resume

Paste into a fresh session:

    Read handoffs/map-isolation-and-creature-fixes.md and continue from "Next single step".
