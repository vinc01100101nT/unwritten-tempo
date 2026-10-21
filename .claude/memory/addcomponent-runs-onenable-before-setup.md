---
name: addcomponent-runs-onenable-before-setup
description: "AddComponent runs OnEnable immediately, before any field set on the next line — broke every TMP input field's caret/highlight and EnterKey's button watch (2026-10-21)"
metadata:
  node_type: memory
  type: project
  originSessionId: b98049bf-e24f-477d-9e96-4f111eb48ba0
  modified: 2026-10-20T20:13:06.856Z
---

`AddComponent<T>()` on an active object runs `OnEnable` inside the call, before the caller assigns anything.

- **TMP_InputField** builds its blinking caret and selection highlight in `OnEnable` only if `textComponent` is
  already set (TMP_InputField.cs ~line 1172). Every field built in code had no caret and no highlight. Fix:
  `UiKit.StyleTyping(field)` — call it on EVERY new input field after `textComponent` is set; it toggles the field
  off/on so the caret is built, and sets caret/selection colours.
- **EnterKey.Watch** read a `button` field assigned after AddComponent → registered null → Enter did nothing on
  sign-in. Fixed by looking the button up in OnEnable.

**How to apply:** any component whose OnEnable needs data must find it itself, or be re-enabled after setup.
Related: [[enter-presses-the-affirming-button]].
