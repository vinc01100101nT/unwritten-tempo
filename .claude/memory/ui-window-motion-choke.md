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

**Scar (2026-07-19) — the ✕ button was a hidden second close path that desynced `UiAnim.open`.** Symptom: after closing the CHAT window with the header ✕, pressing Enter would not reopen it ("cannot open again after closing"). Root cause (MEASURED in code, not guessed): `WindowCloseButton.OnPointerDown` closed via `window.SetActive(false)` DIRECTLY instead of `UiAnim.Close(window)`. That bypasses the state owner, so `UiAnim.open` stays **true** while the GameObject is inactive. `ChatController` (and every hotkey-toggled panel) reads `UiAnim.IsOpen(root)`, which trusts `open` → reports "already open" → skips `UiAnim.Open` → the window never comes back. It affected EVERY window closed via ✕, just most visible on chat (Enter-to-open). Fix: `WindowCloseButton.cs` now calls `UiAnim.Close(window)` (one line). Lesson: the "never SetActive a panel root" rule above applies to shared chrome (close buttons) too — any close/open path must go through `UiAnim`, or the intended-state flag silently forks from reality.
