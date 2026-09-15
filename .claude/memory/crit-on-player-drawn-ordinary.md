---
name: crit-on-player-drawn-ordinary
description: "A creature's crit on the player is drawn in the ordinary red. INTENDED — confirmed by the user 2026-09-15; not a bug."
metadata: 
  node_type: memory
  type: project
  originSessionId: 99f08ae2-01f3-49ab-a776-212cb043a3a2
  modified: 2026-09-14T11:47:12.648Z
---

**Symptom (play-test 2026-09-14, checklist 3.7):** a Reptile's crit of 154 on Alpha (ordinary blows 1–11) drew in the
same red as a normal blow. The tester's "crit sound set holds zero clips" was read off the old empty second sound
player, not the composer cue, so it proved nothing about the crit sound.

**Measured causes (debugger, cited):**
- The crit flag DOES reach the player's machine (`isCrit` on the blow message). `Character.ShowBlowFromServer` and the
  matching branch in `NetPlayer` pick the number colour only from magic-or-not, so a crit on a person is never drawn
  differently. A crit on a creature IS drawn gold (`Health`). Never written, not broken.
- A separate cue path (server picks the crit cue, announced to the player's machine) should still play an enlarged
  burst and the crit sound — unverified in play.
- Combat sounds come ONLY from the Vfx Cue Composer's sequences; the empty second sound player was removed on
  2026-09-14 (see [[vfx-cue-system]]). There is no miss cue, so misses are silent.
- The creature hit tint (a 0.08 s sprite colour swap, white on white on all 86 creatures, so never visible) was removed
  on 2026-09-14 at the user's instruction; hit feedback is the Vfx Cue Composer's hit effect alone.

**Decided (user, 2026-09-15):** a crit on a person is drawn in the same red as an ordinary hit, on purpose — never "fix" it or report it as a failure. Still open: whether misses get a sound (a new composer cue). Related: [[stat-jumps-and-melee-ranged-attack]].
