---
name: window-headings-and-status-box
description: Every window's title lives in a fixed 24-unit blue bar (UiKit.Header); in-game windows get minimise + close, front screens neither; the status box replaced the bottom XP strip and HP/MP bars (user decisions 2026-09-29).
metadata:
  type: project
---

**User rules 2026-09-29:** "make the blue bar the standard heading of all the windows, including the login"; "all heading title must go into that blue bar always"; "the blue bar must have the same height for everything, fixed height" (the status box prototype's — `UiKit.TitleBarHeight` = 24).

- **One path:** `UiKit.Header(body, title, window, closable, minimizable, draggable)`, called by `UiKit.DressWindow` (in-game windows, default close+minimise+drag) and `FrontScreen.Panel(canvas, size, title, out window)` (Login, Your Characters, Character Creation: no buttons, no drag). A menu strip uses `UiKit.TitleBar` at `TitleBarHeight`.
- **Minimise** (`WindowMinimize` + `WindowMinimizeButton`) = only the blue bar stays, still draggable; restore re-enables exactly what was on. The user explicitly rejected a separate "fold" — minimise IS that.
- **Status box** (`StatusBox`, added by `UIShellBuilder`): top-left default, draggable, `WindowFrame` persistKey "Status", minimise but NO close. Bar = name + HP + MP; body = Lv / Ring Lv / [weapon] Mastery Lv bars + zeny + a +/− toggle beside the zeny for 9 window buttons. Removed: `XpBarHUD`, `CharacterUIBinder`, HUD_HealthBar/HUD_ManaBar.
- **Window buttons open windows through their existing keys:** `Hotkeys.PressOnScreen(key)` — one-frame presses answered for the whole NEXT frame, started by `VirtualPressClock` on every platform. The phone tray (`TouchControls.PressVirtual`) forwards to it; there is one mechanism. Before this, virtual presses only existed on Android.
- **Open:** zeny also still shows in the top-right gold panel (not asked to remove).

Relates to [[hud-progression-windows]].
