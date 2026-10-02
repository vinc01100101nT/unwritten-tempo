---
status: current
owner: vince
last-updated: 2026-10-02
tags:
  - ai-learnings
---

# A Build for Another Platform Refuses, or the Platform Switch Will Not Stick

**Reach for this when** a running Unity editor refuses to build for another platform ("Unable to build with the
current configuration"), or you switched its platform and it is back on the old one a minute later.

## The rule

Three separate causes, each one measured, and each one silent: check all three before trying again.

1. **A build module installed while the editor is open is not loaded until the editor restarts.** The editor's API
   says the platform is supported, and the build still refuses. The Build Profiles window says it plainly: "No
   <platform> module loaded … Editor will need to be restarted". Ask the user to restart; never retry first.
2. **While a build profile is active, switching the platform any other way is undone on the next reload.** Switch
   the PROFILE (`BuildProfile.SetActiveBuildProfile`), and judge success by the platform the editor reports — it can
   report no active profile at all while sitting on the right platform.
3. **A request sent while the editor is busy is dropped** ("main thread operation timed out"), and a switch only
   finishes after the request that asked for it has returned. Switch in its own request, sent only while the editor
   reports ready, re-sent if it did not take — never in the same request as the build.

## Why

2026-10-02, building the Mac game on the Windows PC. Three build attempts and two platform switches over about 40
minutes, with the user asking twice where the build was. Attempt 1 switched and built in one request: the switch had
not happened when the content build ran, and it refused. Attempt 2 sent its switch while the editor was compiling: it
was dropped, and the script waited on a switch nobody made. A forced switch then reported success and reverted after
the reload, because the Windows build profile was active. Switched by profile, the editor stayed on Mac — and the
build still refused, until the user opened Build Profiles, saw "No macOS module loaded", and restarted the editor. The
next build succeeded first time.

## How to apply

- After installing a module (`Unity Hub.exe -- --headless install-modules …`, with `ELECTRON_RUN_AS_NODE` unset or Hub
  runs as a script runner), ask for an editor restart before anything else.
- Read the active profile and platform first:
  `BuildProfile.GetActiveBuildProfile()` and `EditorUserBuildSettings.activeBuildTarget`.
- `tools/build-game.sh` does the profile switch, the ready-wait and the switch back; use it rather than hand-driving.
