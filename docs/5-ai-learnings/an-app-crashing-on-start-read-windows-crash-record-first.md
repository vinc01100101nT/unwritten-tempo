# An App Crashing on Start: Read Windows' Crash Record Before Clearing Anything

**Reach for this when** — an editor or program shows its loading screen, then closes or freezes, every time it is opened.

## The rule

Before deleting or resetting any cache, read Windows' own crash record (Application log, event 1000). It names the
module that crashed and the exact address. The same module at the same address on every launch means one piece of
input crashes the same way each time, and a cleared cache will rebuild into the same crash. Next, start the
program once with that part switched off. That shows whether the part is the only thing crashing.

## Why

2026-10-15: Unity crashed on every open, and its log showed the Burst compiler failing. The cache was set aside first,
which is the common advice. The cache was rebuilt and Unity crashed the same way, which cost the user one full
launch-and-wait round. Windows' record showed all five crashes at the same place: `burst-llvm-19.dll`, offset
`0xbed1e2`, access violation. Starting Unity with `--burst-disable-compilation` opened it on the first try.

## How to apply

```powershell
Get-WinEvent -FilterHashtable @{LogName='Application'; Id=1000; StartTime=(Get-Date).AddDays(-2)} |
  Where-Object { $_.Message -match 'Unity' } | Select-Object -First 5 TimeCreated, Message
```

- Same module and same offset every time: the crash comes from a fixed input. Switch that part off for one launch
  (for Unity's Burst: `Unity.exe -projectPath <project> --burst-disable-compilation`), then find the input.
- Different offsets each time: suspect the machine (memory, drivers), not the input.
- Compare the first crash's time with what changed just before it (built assemblies, packages, settings).
