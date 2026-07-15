---
name: ui-window-motion-choke
description: UiAnim is the ONE choke point for HUD window/panel open+close motion — never SetActive a panel root directly.
metadata: 
  node_type: memory
  type: project
  originSessionId: d2da104f-c9e3-4ac9-94da-b273a075d6df
---

HUD windows/panels animate open/close (subtle ~0.12s fade + scale-pop from 0.96) through **`UiAnim.cs`** — the single choke point. Buttons get hover/press feel via **`UiButtonFx.cs`**, added once inside `UiKit.Btn` so every factory button gets it free.

**Rule for any new panel:** toggle it with `UiAnim.Toggle(root)` (returns new intended-open bool), `UiAnim.Open/Close(root)`, `UiAnim.SetOpen(root,bool)`. Read state with `UiAnim.IsOpen(root)`, NOT `root.activeSelf` — activeSelf lags during the close anim (the object stays active until the fade finishes, then UiAnim deactivates it so `WindowFrame.OnDisable` still saves position). Do NOT call `root.SetActive(true/false)` on a window root directly — that bypasses the motion and re-forks the concern.

Migrated call sites (2026-07-05): ProgressionWindows (Inv/Equip/Char + card dialog + Esc-close-all), SkillBookPanel, SkillDraftPanel, CalendarPanel, CharacterCustomizePanel, ChatController, DialogueUI, Minimap. **DeathScreen was intentionally left alone** — it already has bespoke fade + countdown/respawn sequencing; routing it through UiAnim would break that flow.

Perf: coroutine runs only during the transition, CanvasGroup/RectTransform cached, `Time.unscaledDeltaTime` (HUD ignores timeScale), zero steady-state/combat-hot-path cost. Related: [[hud-build-process]], [[ui-click-shield]], [[hud-progression-windows]].
