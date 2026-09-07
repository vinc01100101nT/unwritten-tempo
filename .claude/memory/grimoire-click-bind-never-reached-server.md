---
name: grimoire-click-bind-never-reached-server
description: "Clicking a skill in the Grimoire bound it locally but told the server the PASSIVES, so the skill did nothing when pressed and the bar was empty at next login."
metadata: 
  node_type: memory
  type: project
  originSessionId: 3ea91dff-99e2-4f09-afe2-d7374b950f2f
  modified: 2026-09-07T09:14:20.498Z
---

Fixed 2026-09-07. Two of three play-test bugs ("meteor from the Grimoire casts nothing", "hotbar slots empty
after logging in") were one wrong identifier.

**Symptom:** drag a skill from the Grimoire onto a hotbar slot → works. CLICK the same row → the slot fills, the
icon looks live, pressing the key does nothing at all, and the binding is gone at the next sign-in.

**Root cause:** `SkillBookPanel.BindFirstEmpty` called `PassivesSaved.Now()` instead of `HotbarCommit.Now()`.
The two helpers share their first two lines (`RefreshUsableSkills` + `HotbarPanel.RebuildRequested`), which is
why the slot looked right; they differ only in the third — one sends `AskToSetPassives`, the other
`AskToSetHotbar`. So the server never heard about the binding, `GameNetwork.Owns()` refused every cast of it
(logged by `WarnNotTheirSkill`, server-side only), and the server's own save 15 s later wrote the old bar back.
`GmCommands.CmdBind` (`/bind`) had the identical defect and nobody had hit it.

**Also changed, as the general half:** `Character.BindSkills` now fires `HotbarPanel.RebuildRequested` itself
(guarded to `Character.Instance`, so a server holding 20 people does nothing). A hotbar cell resolves its skill
through `Character.RunnerById`, which needs both `slotById` filled and `skillHost` non-null — so a cell built
before the save arrived draws as an empty slot and stayed that way until something unrelated redrew the bar.

**Gotchas found while measuring, both still true:**
- The CLIENT never writes progress any more — `SaveManager` only READS. Every write is the server's, so anything
  the server has not been told is erased on its next beat. Read ground truth with
  `psql postgres://vince@localhost:5432/unwritten -c "select name, progress->'hotbarBindings' from character"`.
- Equipping a weapon runs `Hotbar.SetWeaponKit`, which CLEARS the whole bar and refills from that weapon copy's
  rolled kit. A Grimoire-bound skill therefore survives only until the next weapon equip — by design, not a bug,
  but it is a second way a binding "disappears". Loading a save does NOT do this (`RestoreEquipment` writes the
  gear dict directly, bypassing `SetEquipped`).

See also [[whole-skill-runs-on-the-server]] and the learnings page "One Route Saves, the Other Doesn't".
