# A Watcher Installed Into a Running App Dies on Its Next Reload

**Reach for this when:** you hooked a listener or probe into a running editor or app, the user went off to reproduce the
bug, and the probe came back with nothing.

## The rule

A listener added from a live console or one-off script lives only in that process's current memory. Anything that
reloads the code (entering Play mode in Unity, a recompile, a hot reload) throws it away without saying so. A probe
that has to wait for an event the USER triggers must be a file the app loads on every start, never a one-off injection.

## Why

2026-10-09: to catch what kept marking a Unity scene modified, a `sceneDirtied` listener was installed through a live
eval and the user was asked to play-test. Entering Play mode reloads every script; the probe logged "ExitingEditMode"
and was never heard from again. The user's play test was wasted. The same probe written as an `[InitializeOnLoad]`
editor script caught the real cause on the very next reload.

## How to apply

- Before asking anyone to reproduce, ask: *will the code reload between now and the event?* If it might, write the
  probe as a file that re-arms itself on load (Unity: an `[InitializeOnLoad]` static class in an `Editor/` folder).
- Make it log an "armed" line on every load, so its absence is visible.
- Delete the file once it has caught the cause.
