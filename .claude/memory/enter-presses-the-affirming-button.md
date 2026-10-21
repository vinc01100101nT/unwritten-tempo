---
name: enter-presses-the-affirming-button
description: "STANDING LAW 2026-10-21 — Enter must always press the affirming button of the window in front (popup OK, Enter World, Create, Confirm…), never what sits behind; every new window/button inherits it"
metadata:
  node_type: memory
  type: feedback
  originSessionId: b98049bf-e24f-477d-9e96-4f111eb48ba0
  modified: 2026-10-20T20:04:18.515Z
---

Pressing Enter affirms what the player's current intent is, everywhere — front screens, popups, every in-game window.
The user called it "the ultra standard behavior" and was furious it had to be said twice (first fixed only for the
error popup, then demanded for everything).

**Why:** Enter pressing a button hidden behind a popup (or nothing at all on character select) made the UI feel broken.

Second law, same day: **whatever window came last takes the keyboard** — its yes-button is selected the moment it
appears, and the keyboard goes back to what held it when it closes. EnterKey presses buttons itself; never rely on
Unity's event-system Submit (the first version deferred to it and the popup still didn't take Enter).

**How to apply:** every affirming button is made with `UiKit.PrimaryBtn` / `UiKit.MarkPrimary`, which registers it
with `EnterKey` (one central Enter handler, runs first). Never add a per-screen Enter handler or an input field
`onSubmit` that presses a button — it double-fires. A screen that also reads Enter (chat) must check
`EnterKey.TakenThisFrame`. A new window gets Enter for free only if its yes-button is marked primary.
Related: [[centralize-cross-cutting]].
