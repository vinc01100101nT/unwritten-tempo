# SESSION_STATE — resume here (keep SHORT; git holds history)

*branch* `experiment/3d-world` *· dials: **Opus 4.8 always** + assess EFFORT only*

## Protocols
- **Effort**: model ALWAYS Opus 4.8 (never assess/print model). Assess EFFORT only; auto-GO Low/Medium; PAUSE >Medium until user GO. Tag `**EFFORT: X**`.
- **Checkpoint**: OVERWRITE this file (never append); prune landed; keep short.
- **Where the code is**: Unity game = `D:\tempo\projects\roguelike-mmo\unity\Assets\Scripts\` (NOT the Tempo React worktree). Drive the live editor via Unity MCP (`UnityMCP`). See memory [[unity-2d-direction]].

## 🎨 DESIGN (parallel, no code): "unwritten" brand sheet + StatPanel stat icons
GPT generates the logo/sheet; I give feedback + draft GPT prompts. **All decisions saved in Skill System doc §15** (`q17as16xh2gt1g058kemazjar588tvc8`): ring nodes = the 10 derived stats (logo == §5b ten-stat set); color by 3 archetype families (🔴Might Def/HP/Atk · 🔵Magic MAtk/MP/CastSpd · 🟢Finesse Accuracy/Evasion/Critical/AtkSpd; gold=neutral path); icons silhouette-first; fixing MATK vs Critical sparkle clash. ⏭️ User pastes GPT's next iteration → check crit icon + colors landed. Presentation only, no code/math impact. (Overlaps the HUD stat-family icons batch below.)

## 🎯 ACTIVE: HUD window trio — LOCKED + Ornament Kit v1.1 (GPT⇄Claude relay, 2026-06-25)
Full durable detail in memory **[[hud-progression-windows]]**. Trio (Inventory `B` · Equipment `V` · Character `C`) built at runtime by `ProgressionWindows.cs` on `UiKit.cs`. Visual design LOCKED by GPT: **charcoal + gold**, palette tokens in `UiKit.cs`, rarity in `Rarity.cs` (Legendary name = ivory `#F1E7D0`, label stays gold). Decisions doc `q172d54qewwwxxr2aq5s29wk0x898958`.

**Polish rounds DONE + verified in Play:** P0 empty-slot silhouette watermarks (Ninja icons wired by `UIShellBuilder`, serialized on `ProgressionWindows`); P1 charcoal detail panel (`UiKit.PanelRaise`); P2 item names Bold→Normal (no SemiBold — Cinzel single-weight); P3 hover(0.22)/select(0.78) ring hierarchy = rarity > select > hover; tooltip Weight/Value split; derived `lineSpacing` 1.4.

**Ornament Kit v1.1 DONE** (`Ornament.cs`, procedural ink+gold, drop-in `cornerArt`/`dividerArt` sprite hooks): pinwheel asymmetric corners (`└─◇`), chapter divider `────◇────` w/ ink ticks, Character portrait/name backplate (charcoal + thin gold border + top/bottom diamond studs). Guardrails: monochrome gold, ≤2px, never out-weighs item borders.

**🐛 Fixed:** `CharacterUIBinder` grabbed the first Text named "Stats" on the canvas → clobbered the Equipment detail panel with a char-stat dump. Hijack removed; binder now drives HP/MP bars only (char stats owned by `ProgressionWindows`).

⏭️ **NEXT (awaiting GPT's reply to the v1.1 relay):** either LOCK procedural v1.1 as the shipping baseline, OR produce hand-drawn ink corner/divider sprites and swap them into the `Ornament` hooks. Then DEFERRED art batch — #4 final watermark art, #5 stat-family icons (sourcing UNDECIDED: CC0 set tinted gold vs hand-author). Later: equipment save persistence (M3); delete orphan `BagSeeder.cs`/`StatPanel.cs`.

⚠️ **`town_0` left dirty** — `Build UI Shell` baked a `GameUICanvas` instance into it (causes the dup-EventSystem / dup-window artifacts seen while testing). REVERT it; maps shouldn't carry the canvas (it auto-spawns).

## 💤 PENDING: Map Painter Phase 3 + Prontera
ONE map → JSON → Unity import ([[map-painter-pipeline]]). Phase 1+2 done; **Phase 3 importer NOT STARTED** — read `map.json` → bake (ground→GroundBlend, objects→`PropPlacement`+yaw, walls→`BuildPerimeterWall`). BLOCKED on a real painted `map.json` to lock schema.

## Gotchas
- **Play loop**: user drives Play by DEFAULT ([[playtest-user-drives]]) — but Unity MCP CAN drive Play/screenshot when Unity is open + the user authorizes it (did so 2026-06-25 for HUD verify). Overlay UI only shows via screenshot `capture_source:"game_view"`; first grab per Play session is a throwaway; windows are hidden until SetActive via `execute_code`. Re-run `Tools ▸ unwritten ▸ Scene Setup ▸ Build UI Shell` only when serialized prefab refs change. Use `refresh_unity scope:all` when adding a NEW .cs (so it imports before compile).
- World ground standard: Y0 floor, Ground layer; custom shaders (WetGround/GroundBlend/FoliageWind) need Always-Included before a real build.
