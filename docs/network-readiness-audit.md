# Network-Readiness — Combat Audit & Refactor Plan

*Measured audit, 2026-07-23. Purpose: decide the multiplayer path and make the combat core **cheap to plug a network layer into later** — without building the network layer now.*

> **One-line verdict:** Content is safe to build full-speed. The combat core is **well-funneled** (single choke points exist), but it is **hard-wired to one local player = the truth**. Deferring the *network work* is fine; deferring the *architecture decision* gets more expensive with every new skill authored.

> **STATUS (2026-07-24):** ✅ Prep Steps A–D DONE + committed to `main` (`9f393fac`) — `CombatRng`, caster-threading, `IDamageable`, `CombatAuthority`. ⏸️ **Networking itself DEFERRED** — prove the game is fun in a vertical slice first, then build the netcode (phases N0–N5 below). The seams sit dormant and cost nothing until then.

---

## The decision that gates everything

**Co-op / peer (friend-hosted, listen-server)** vs **true server-authoritative MMO.** This single call changes how every *new* gameplay script is written from today on. Until it's made, keep new skill code authority-agnostic (see Refactor Plan §1).

- **Co-op/peer** → retrofit is *moderate*: wrap the existing choke points, seed RNG, de-singleton the player.
- **Server-authoritative** → retrofit is *heavy* (months): move all RNG server-side, convert local applies into validated commands with client prediction/reconciliation, replicate mob state + AI.

Both are *bounded* (not a rewrite) **because** the choke points below already exist.

---

## What's already good (the choke points a net layer hooks)

- **Two symmetric damage sinks** — mob damage lands through `Health.TakeDamage*`, player damage through `Character.TakeDamage*` (each with magic + overkill + true-damage variants). Nothing mutates HP from outside them. **Only ~5 files** call the sinks.
- **One skill→gameplay bridge** — `SkillEffectApplier` is self-described as *"The ONE place a data-authored SkillEffect becomes real gameplay… never a second combat engine."* Every effect (damage/status/heal/buff/teleport) routes through it.
- **One shared combat-math file** — `Combat.AfterDefense` / `Combat.Lands` are the single source for defense + accuracy, shared by both attack directions.

This is the single-source-of-truth discipline paying off: the seams a network layer needs are already single points.

## What blocks multiplayer (measured blast radius)

| Concern                                              | Files | Occurrences | Note                                                                     |
| ---------------------------------------------------- | ----- | ----------- | ------------------------------------------------------------------------ |
| `Character.Instance` (player is a global singleton)  | 41    | 192         | Many are **local UI/HUD** (fine to keep); true-authority sites are fewer |
| `StatusController.Player` singleton                  | 7     | 27          | Same pattern                                                             |
| `Random.value` / `Range` (unsynced client RNG)       | 24    | 56          | Crit / proc / hit-miss roll locally — must become server-seeded          |
| `MonsterAI3D.Active` (local mob registry + local AI) | 10    | —           | Mobs run client-side                                                     |
| Direct damage sinks (`TakeDamage`)                   | 5     | —           | **Narrow — very hookable**                                               |

**True authority sites** needing de-singletoning (vs. local-HUD reads that can stay): `SkillRunner`, `Player3DAttacker`, `MonsterAI3D`, `SkillEffectApplier`, `ComboRunner`, and `Health.ApplyLeech`.

### The three structural assumptions

1. **One player, globally.** `Character.Instance` / `StatusController.Player` are reached *directly inside the skill math* (`RollPower`, `AffinityMult`, `Health.ApplyLeech`). N players = thread a per-player caster through those sites.
2. **RNG is local & unsynced.** Crit/proc/accuracy roll `UnityEngine.Random.value` at the point of application. In MP that's client-side dice → disagreement/desync.
3. **Damage is applied instantly & locally.** `Health.Current -= dmg` happens on the attacking client with no command/validate/broadcast indirection, no prediction/reconciliation. Mobs + AI run locally.

---

## Refactor Plan — make it plug-ready (does NOT add networking)

**Decision made 2026-07-23: target is SERVER-AUTHORITATIVE MMO.** That reshapes the plan. Key insight from reading the code: on a server-authoritative client each player still has exactly ONE *local* player, so the `Character.Instance` singleton is fine on the client — **full de-singletoning is mostly server-side work we're not writing now.** What server authority actually needs is a seedable RNG choke point and a command/authority seam. Re-ordered by that leverage; every step keeps the game 100% singleplayer and identical to play.

### Step A — `IDamageable` over BOTH sinks *(low risk, ~7 files)*

Extract an interface (`IsDead` / `Position` / damage entry) implemented by `Health` **and** `Character` — the two sinks are asymmetric today (`Health.TakeDamage(int,bool,int,Element)` vs `Character.TakeDamage(int,int,Element,MonsterAI3D)`), so this is additive: the interface exposes the common contract a net layer intercepts for remote entities, without breaking existing concrete calls.

### Step B — `CombatRng` choke point *(mechanical, ~4 files) — THE KEYSTONE*

Route combat-OUTCOME rolls through one `CombatRng`: crit + damage/magic spread + block (`Character`), accuracy (`Combat.Lands`), proc (`SkillEffectApplier.Fires`), mob ailment + attack-spread (`MonsterAI3D`). Same distribution today; **the single file that becomes server-seeded** later. Leaves *movement/wander/VFX* random alone. Server authority is impossible without this — with it, it's a swap.

### Step C — pass the caster explicitly *(refactor)*

Thread `Character caster` into `SkillEffectApplier.ApplyBurst` / `RollPower` / `AffinityMult` instead of reaching for `Character.Instance`. Client = the local player; server = the acting connection's entity.

### Step D — the command/authority seam *(the real server-authority enabler — the deep work)*

Funnel the three action origins — `Player3DAttacker` (swing), `SkillRunner` (cast), `MonsterAI3D` (mob attack) — through a thin `CombatAuthority.Submit(intent)` that today calls the local apply synchronously (zero behavior change) but is the ONE place a net layer swaps in "send to server → server validates + rolls `CombatRng` → broadcasts result." This — not de-singletoning — is what server authority hinges on.

### Deferred — full de-singleton *(mostly server-side)*

Each client keeps its one local `Character.Instance`. Replacing the singleton with per-connection entities is server code, out of scope for client-side prep.

> Steps A–C are safe, additive, singleplayer-invisible. Step D is behavior-preserving too (synchronous local apply) but is the substantive seam. Nothing here builds the netcode itself — that's Tier 2.

---

## Explicitly out of scope here

Picking a transport (Netcode for GameObjects / Mirror / Fishnet), accounts/auth, world-persistence backend, zoning/instancing, anti-cheat, hosting. Those are the Tier-2 "networking core" — this doc only makes the *client-side combat* ready to receive one.

*Related: **unwritten — Roadmap to Launch** (Tier 2 › Networking core).*
