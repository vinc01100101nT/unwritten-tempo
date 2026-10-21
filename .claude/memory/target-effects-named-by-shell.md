---
name: target-effects-named-by-shell
description: A cast's target effects played on the caster because the cast message named a creature by its server object, which has no network id — name it by its NetMob shell.
metadata:
  type: project
---

Fixed 2026-10-21. `GameNetwork.AnnounceCastPlayed` named the aimed unit with `GetComponentInParent<NetworkIdentity>()`
on the SERVER's creature, which carries no identity (its identity is on the separate NetMob shell), so `targetNetId`
was 0 and every screen fell back to the cast's point — the caster's own position for a unit-aimed skill. Now
`IdentityOfServerSide` goes through `SharedIdentityOnServer` (shell first), the watcher draws on `DrawnBodyOf(identity)`,
and a unit-aimed skill announces the unit's position as its point.

**Why it hid:** every creature-aimed skill used `cue_trigger_target_hit`, which draws nothing, until the Bow got real
target effects.

**How to apply:** any server→watcher message that names a creature must resolve it through its shell
(`SharedIdentityOnServer` / `NetMob.ShellForServerMob`), never through the server object's own components.
