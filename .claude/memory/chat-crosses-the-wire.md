---
name: chat-crosses-the-wire
description: Chat became networked on 2026-09-07 — a said line and a skill call go to the server and come back to everyone who can see the speaker.
metadata: 
  node_type: memory
  type: project
  originSessionId: 3ea91dff-99e2-4f09-afe2-d7374b950f2f
  modified: 2026-09-07T09:14:43.254Z
---

Built 2026-09-07. Chat was entirely local until then: two people standing together saw each other's spells go off
and heard neither the words nor the skill names.

**The shape, deliberately the same as a cast:** the speaker ASKS, the server DECIDES who is near enough, and one
message goes back to all of them — the speaker included. `ChatController` draws NOTHING at the keyboard that
typed the line when a world exists (`GameNetwork.SendSayToServer` returning true means "do not show it yourself");
the row and the bubble come back from the server. One chat log, no second copy to drift.

- `ChatSaid` (client→server) carries the words and nothing else — never who said them; the server names the
  speaker from the connection it arrived on.
- `ChatHeard` (server→observers of the speaker) carries the name, the shell's netId (which body the white bubble
  hangs over) and `isSkillCall` (tinted, otherwise identical).
- A skill call is announced by `GameNetwork.AnnounceSkillCall`, called from `SkillRunner.PlayCycles` — once per
  CAST. NOT from `NetPlayer.ShowCast`, which runs once per PLAY, so a repeating skill would have called its own
  name five times.
- `includeOwner: false` on the call announcement only; the caster's own machine says it at the press.
- Guards: 4 lines/second per connection (`chatLedgers`, same `TooMany` counter as swings/heals/casts), 200
  characters, commands refused at that door (they have their own, which asks whether the person is a GM).

`ChatController.Post` is the single body every spoken line goes through — said or called, mine or somebody
else's. `NetPlayer.SpeechPose` is where a watched player's bubble anchors; `Agent3DVisual.Pose` was made public
for it, matching `Character3DVisual.Pose`.

Doc: `docs/2-systems/networking/implementation-notes.md`.
