# NinjaAdventure VFX fully purged from combat (mob SPRITES kept)

2026-07-01. Directive: "do not leave a single ninja adventure vfx." VFX = visual EFFECTS, NOT the mob
sprite sheets (`Actor/Monster` sheets stay — those ARE the mobs, explicitly kept).

## What was removed (3 FX, all NinjaAdventure `FX/…` sheets)
1. **Death smoke puff** (`FX/Smoke`) — stripped the `smokeSheet` fields + the `SlashEffect.Spawn(...)`
   call out of `DeathDissolve.Play`, and the assignment + `SmokeSheetPath` const in `Build25DSpike.cs`.
2. **Mob magic projectile** (`FX/Projectile/Fireball.png`) — removed the `ai.projectileSheet`
   assignment in `Build25DSpike.cs`.
3. **Stun swirl** (`FX/Magic/Spirit`) — removed the `ai.stunSheet` assignment.

## Why it works with no crashes
Both consumers already null-guard: `MonsterAI3D` magic attack with null `projectileSheet` falls to the
instant-touch `else` branch. The stun-swirl fields and `StatusFX` were later removed entirely (2026-10-07).

## Lessons
- **Strip at the guarded CONSUMER, not just the assigner.** Removing smoke from `DeathDissolve.Play`
  itself means it won't fire even for mobs already baked into scenes with the sheet serialized — no
  scene re-bake needed. (Removing only the builder assignment would leave baked mobs unaffected.)
- **"All" means grep the whole surface.** First pass only caught the smoke; user had to point out the
  fireball + stun swirl. When told "remove all X," sweep the entire `FX/` surface, not just the obvious hit.

## NOT removed (correctly)
- `Actor/Monster` mob sprite sheets (the mobs). Also UI, item icons (GoldCoin), SFX — none are VFX.
- Hovl `EffectLibrary` "Smoke effects" entry — that's the generic non-Ninja Vfx catalog, separate.

## Follow-up
Magic mobs now have NO projectile visual (instant touch) and stun has no indicator until the Hovl
`Vfx` library is wired to these call sites. Re-run the mob builder to strip serialized ninja sheet
refs from already-baked mobs (assignments happen at build time).
