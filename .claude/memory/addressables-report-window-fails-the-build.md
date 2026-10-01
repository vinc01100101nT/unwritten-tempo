---
name: addressables-report-window-fails-the-build
description: "On the Windows PC, tools/build-game.sh failed in under a second with 'Addressable content build failure' — the add-ons package's auto-opened Build Report window crashes and takes the content build with it"
metadata:
  type: project
---

**Symptom (measured 2026-10-01, Windows PC, Unity 6000.4.11f1):** `tools/build-game.sh server` printed "server
build FAILED — nothing was produced". The console showed `Addressable content build failure (duration : 0:00:00)`,
preceded by a `NullReferenceException` in the Addressables package's `BuildReportVisualizer.DetailsView.CreateGUI`
(line 38: `rootVisualElement.Q<RibbonButton>("ReferencesToTab")` returns null). The same exception fires at editor
start whenever that window is restored, next to the warnings "Control … RibbonButton uses the deprecated UxmlTraits
API. Its attributes were ignored on import".

**Cause:** after every content build the package calls `BuildReportWindow.ShowWindowAfterBuild()` when
`ProjectConfigData.AutoOpenAddressablesReport && GenerateBuildLayout` (`BuildScriptBase.DisplayBuildReport`). The
window cannot build its buttons in this Unity version, throws, and the exception fails the content build.

**Fix applied:** turned the per-machine preference off — Edit ▸ Preferences ▸ Addressables ▸ "Open Addressables
Report after build" — through the editor link (`ProjectConfigData.AutoOpenAddressablesReport = false`, internal,
set by reflection). It is stored in `unity/Library/AddressablesConfig.dat`, which is never committed, so each
machine has its own; the Mac's was never measured. After it, both the Windows server (402 MB) and client
(743 MB) built.

**How to apply:** a content build failing in 0 seconds right after a `BuildReportVisualizer` exception → check
that preference on the machine doing the build before suspecting the project.
