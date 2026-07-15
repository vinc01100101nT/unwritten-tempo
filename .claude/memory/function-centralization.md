# Function centralization — one choke point per cross-cutting concern

User directive (2026-07-01), reinforced after the `Hotkeys.cs` refactor. This is a STANDING design
principle, not a one-off.

## The principle
When the same concern is queried/handled in many places (input, save/load, time, RNG, audio, feature
flags, network calls, logging), route ALL callers through ONE centralized function/class. Do NOT scatter
the raw primitive (`Input.GetKeyDown`, `DateTime.Now`, `Random`, direct `PlayerPrefs`, etc.) across the
codebase.

## Why it matters (the user's actual point)
1. **Prevents "unknown things" / emergent bugs.** Scattered call sites mean a new rule (e.g. "suppress
   hotkeys while typing in a text box") has to be remembered at EVERY site. Miss one → a silent, hard-to-
   reproduce bug. With a choke point, the rule is applied once and is impossible to forget.
2. **Easy to manage / change later.** Swap the backend (legacy `Input` → new Input System), add a global
   gate, add logging, add rebinding — edit ONE file instead of hunting ~20 sites.
3. **Discoverability.** The next dev (or agent) sees the one class and reuses it, instead of reinventing a
   parallel path (this project already had the dual-path smell: HotbarPanel was the "central router" yet
   every skill ALSO kept its own key binding).

## The concrete case that taught this
Feature: "disable HUD hotkeys while typing in any text box." Before, input was called raw at ~20 sites
(every skill's `Pressed()`, `Player3DController` `KeyA/HeldW/...`, window toggles B/V/C/K/L/O, hotbar,
interact). Guarding each would be fragile and easy to miss one. Fix = created `Hotkeys.cs` (single class:
`Typing` guard + `Down/Held/DigitDown`), routed every site through it. Now the typing-guard — and any
future input change — lives in exactly one place. See memory `hotkeys-central-input-layer` if written.

## How to apply going forward
- Before adding the Nth raw call to a shared primitive, STOP and ask "should this go through a central
  wrapper?" If the concern is cross-cutting (a rule might later apply to all of them), centralize it.
- When a bug is "we forgot to apply rule X at one call site," the real fix is usually centralization, not
  just patching that site.
- Name the central thing by function (`Hotkeys`, `GameClock`, `Rng`), per the naming rule.
- Watch for and collapse DUAL PATHS — two systems doing the same job is the scattered-state bug waiting to
  happen.
