# Same Code, Different Result — Diff the Data, Flag by Flag

**Reach for this when:** two things go through the identical code and only one of them misbehaves — one kind of
creature animates and another stands frozen, one item works and its sibling does not.

## The rule

When the code path is proven identical, stop reading code and stop asking for more runtime logs. List every saved
setting on the working one and on the broken one, side by side, down to the import flags of the files they point at,
and find the one that splits them exactly along the line the user reported. Then ask what the shared code does when
that setting differs — most often "what happens when the same thing is asked for again while it is still showing".

## Why

2026-10-14: melee shadow creatures froze while attacking and ranged ones did not. Both went through one function. I
compared the pose table, the clip names, the clip contents and the swing speed, found each identical, and then asked
the user to switch the server's logging on and fight twice — three round trips while they waited to go to sleep. The
log proved the server sent both the same thing every 0.233 seconds, which only ruled out what the code reading had
already ruled out. The answer was one flag I had not compared: the working attack animations were imported as looping
and the frozen ones were not. With swings arriving back to back, the shared code never restarted an animation that was
already playing, so a non-looping one stopped on its last frame. One more column in the first comparison table would
have found it before any message was sent.

## How to apply

1. Write down the split the user reported, by member: which ones work, which do not.
2. For each member, print EVERY saved field of its entry and of the files that entry points at, including the
   `.meta` import settings (`loopTime`, `wrapMode`, mirror) — not only the fields that look relevant.
3. Keep only the fields whose values divide the members exactly as the report does.
4. For each survivor, read the shared code for what it does on a REPEAT: an "already in this state, do nothing" early
   return is the usual place two settings start behaving differently.
5. Ask the user for a runtime measurement only if step 3 leaves nothing.
