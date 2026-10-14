---
name: overhead-notice-term
description: "Overhead notice" = the stacking flashing text, fixed centred on the screen; the game's default notification for warnings and every transaction result.
metadata:
  type: project
---

**"Overhead notice"** is the user's agreed term (2026-10-14) for the stacking message strip that flashes on the
screen (it began over the player's plate; it is now fixed on the screen). In code it is `OverheadNotice` (renamed from `BattleNotice` the same day).

- Drawn on a screen canvas above every HUD window (sorting 150), under the item tooltip layer (200).
- FIXED ON THE SCREEN (user, 2026-10-14): centred, a little above the middle, one size (18pt) — never on the character.
- SHORT LINES ONLY, at most 40 characters (`MostCharacters`); the full sentence goes to chat (rule in CLAUDE.md).
- Three colours only: `Success` green, `Fail` red, `Warn` yellow (`UiKit.NoticeSuccess/Failure/Warning`).
- It is the DEFAULT flashing notification: `Warn` for "you can't do that" warnings, `Result(line, ok)` for every
  transaction's server sentence — shop buy/sell/buy-back, keeper (save point, trips, storage), station services
  (fold, quench, Ink, recycle, merge), quest rewards. The chat keeps the same line.

**How to apply:** when the user says "overhead notice" (or "the stacking message"), this is it. Any new transaction
result routes through `OverheadNotice.Result`, never its own floating text. See [[server-never-draws]].
