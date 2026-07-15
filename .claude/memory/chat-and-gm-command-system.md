---
name: chat-and-gm-command-system
description: In-game chat window + slash-command dispatcher; where to declare GM/testing commands
metadata: 
  node_type: memory
  type: project
  originSessionId: d8bd6641-5497-422a-95de-f7200eb70bef
---

In-game **chat** built 2026-07-04 (branch `experiment/3d-characters`) for in-game testing capability.

- `ChatController.cs` — runtime chat window, self-builds on the persistent GameUICanvas (added in `UIShellBuilder` alongside XpBarHUD; re-run **Build UI Shell** to bake it in). **Enter** opens+focuses input, Enter again sends, **Esc** closes. Focus rides the shared [[function-centralization]] typing guard (`Hotkeys.Typing` true while the InputField is focused → all gameplay/HUD keys auto-suppressed, no per-site checks). Scrolling log capped at 200 lines; user/echo text run through `Sanitize` (zero-width space after `<`) so stray markup can't corrupt rich-text.
- `GmCommands.cs` — THE single slash-command dispatcher (choke point). `IsCommand` (starts with `/`) · `Execute(line, reply)` parses `/name args`, looks up in one registry, runs; unknown/exception → error reply, never throws.
- `GmCommand.cs` — command def (`name`, aliases, usage, summary, `Action<GmContext>`) + `GmContext` (parsed args, `Reply`, lazy `Ch`/`Player`).

To add a command: a single `GmCommands.Register(new GmCommand(...))` in `EnsureBootstrap()` + a static handler. User asked to **avoid the word "God"** in-game — the invuln command is `/invuln` (NOT `/god`).

**Commands authored (2026-07-04, all handlers in GmCommands.cs):** `/help`; Character `/lvl /xp /ringxp /statpts /heal /revive /invuln`; Economy `/gold /item <id|name> [n] /bind <skillId>`; Mobs `/kill /killall /spawn [n]`; World `/tp <scene> [spawn] /time <0..1> /weather <clear|cloudy|rain|storm|snow|fog> /speed <mult>`.

Wiring notes: `/invuln` → new `Character.Invincible` flag, folded into the ONE `DodgedByInvuln()` choke (covers physical+magic paths, silent). `/item` → new `InventoryStore.Give(def,count)` (the single add-item path; looks up `Item.id`/`displayName` in Resources/Items). `/spawn` clones the nearest live mob (no runtime factory — mobs are editor-built). `/kill*` via `Health.TakeTrueDamage`. `/weather` presets Clear/Cloudy/LightRain/Thunderstorm/Snow/Fog via `WeatherDirector.forcedPreset`+`autoFromCalendar=false`.
