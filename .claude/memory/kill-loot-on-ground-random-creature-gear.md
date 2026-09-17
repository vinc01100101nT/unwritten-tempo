---
name: kill-loot-on-ground-random-creature-gear
description: "Kill loot lands on the GROUND (held 10s for the killer, gone at 60s); gold still to the purse; creatures put on random gear per life (CreatureWardrobe), worn drop chance by rarity, and the hover/target panel lists worn pieces with drop % sent by the server. Built 2026-09-15, unplayed."
metadata:
  node_type: memory
  type: project
---

**Built 2026-09-15 at the user's request (unplayed — `docs/1-planning/play-test-checklist.md` section 7).** Reverses the
earlier "a kill never leaves anything lying in the world" design.

- **Where loot goes:** `Rewards.PayLoot` — gold → `AddGold`; every item (loot table, the 30% `LootDropper.RandomItemChance`
  random non-equipment item from `ItemCatalog.RandomUnworn`, and won worn copies) → `GroundDrop.Place` at the victim +
  `ReserveFor(killer, KillerOnlySeconds 10, KillDropLifetimeSeconds 60)` + `GameNetwork.PublishDrop`.
  `NetPlayer.CmdPickUpDrop` refuses with `MayBeTakenBy`; `NetGroundDrop.Update` destroys an `Expired` drop on the server.
  Player-put-down drops have no reservation and still never expire.
- **Random gear:** `UnitEquipment.SeatStartingGear` calls `CreatureWardrobe.FillEmptyPlaces` only when `WearsRandomGear`
  (playing, server/offline, has `MonsterAI3D`, no `TrainingDummy`) — so never the player, never a watched copy. Wear chance
  by copy rarity 40/30/20/10/5%; authored startingGear places are kept. `TrainingDummy.MakeOne` clears gear.
- **Worn drop chance:** `LootDropper.WornDropChance(inst)` — 20/12/6/3/1% by rarity, ring 0.001%. The per-prefab
  `wornDropChance` field was DELETED, and all 86 baked mob prefabs under `2_Generated/MobPrefabs` were re-saved on
  2026-09-15 so none carries it — the same save also dropped three older dead values (flashColor, flashTime, attackType).
  `AssetDatabase.ForceReserializeAssets` wrote NOTHING to these prefabs while reporting success;
  `PrefabUtility.LoadPrefabContents` + `SaveAsPrefabAsset` did the job.
- **Wire:** `NetMob` private SyncVar `worn` = `WornList.Encode` ("id\trarity\tchance" per piece, body order), re-sent when
  `UnitEquipment.Version` changes. Watchers `ApplyWornToView` (Clear + plain copies, so swing pose/reach follow the real
  weapon; re-applied after revive) and `UnitStatusPanel` (hover top-right, target top-centre) reads `NetMob.WornText` and lists one row per piece under the states. The floating bar under a creature shows no gear.
- **Parity fixes in the same change:** creature shield block (`Combat.Blocked`, shared with `Character`, in
  `Health.TakeDamage` + `TakeDamageOverkill`); creature swing speed now `UnitStatMath.AttackSpeedMult` (AGI/DEX/gear/ring/buff)
  stamped as `MonsterAI3D.attackSpeedMult` — a balance change for every creature; creature attack element = held weapon's
  element, else its authored `attackElement`.
- **Known gaps left:** a refused pick-up tells the player nothing. (Weapon models on 3D creatures were drawn from 2026-09-16 — see [[creatures-cast-weapon-kit]].)

Related: [[armour-rolls-per-copy]], [[mobs-get-everything-players-get]], [[creatures-cast-weapon-kit]].
