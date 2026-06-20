# SESSION_STATE — resume here (keep this SHORT; git holds history)

*2026-06-20 · branch* `experiment/3d-world` *· last commit TBD (loot-drops)*

## Protocols (full text in `~/.claude/.../memory/`)

- **Effort + model**: defaults **Sonnet 4.6 + Medium**. Assess each task first for BOTH effort + model; auto-GO on Low/Medium fitting the model; PAUSE for &gt;Medium OR higher tier until user sets dials + GO. Tag each task reply with bold first line `**EFFORT: X · MODEL: Y — …**`. Opus 4.8 for hard/ambiguous; model switches bust the cache (don't micro-toggle).
- **Checkpoint**: OVERWRITE current state (never append history — git holds it); on milestones + before `/clear` only; keep short.

## ✅ Recently shipped

Loot drops (Zeny) on mob death:

- `Wallet.cs` — static gold store + `OnChanged` event; persisted via SaveData.gold
- `WorldPickup.cs` — bobbing coin billboard in 3D world, auto-collects on player proximity
- `LootDropper.cs` — hooks `Health.onDeath`, spawns WorldPickup with per-archetype gold ranges
- `GoldCounter.cs` — HUD text (top-right) wired to Wallet.OnChanged
- `MobRoster.cs` — added `goldMin`/`goldMax` to MonsterArchetype + values for all 5 archetypes + boss
- `Build25DSpike.cs` — wires LootDropper in BuildMonster (GoldCoin.png sprite)
- `UIShellBuilder.cs` — gold panel (HUD_GoldPanel / HUD_Gold) in top-right corner
- `SaveData.cs` / `Character.cs` — gold captured + restored on save/load

**To activate**: run **Tools ▸ unwritten ▸ Build UI Shell** (new gold panel) then **Populate Mobs / Setup Systems Scene** (mobs get LootDropper). Press Play, kill a mob → coin spawns → walk over it → counter updates.

## ⏳ TODO

- Nothing pending. (Next idea is open — ask the user.)