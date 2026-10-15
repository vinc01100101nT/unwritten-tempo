# A Frozen Editor Tells You Where It Is Stuck

**Reach for this when:** the Unity editor (or any program) freezes mid-play — Windows says "Not Responding", its log
just stops, and you are about to guess which change caused it or ask the user to reproduce it again and again.

## The rule

A freeze where the process uses almost no processor time is a wait, not a loop: some thread is waiting on another.
Its own threads say exactly which, so read them before theorising: attach Microsoft's console debugger WITHOUT
stopping the process and print every thread's call chain. The thread waiting on a lock, plus the thread holding it,
is the whole answer.

## Why

2026-10-15: "Return to last saved point" froze the editor on the second or fourth try. Three rounds went on log
probes and on switching a new feature off to bisect — and the switch-off run froze too, so the bisect proved only
that the feature was innocent. One read of the frozen editor's threads then showed the cause in a minute: the main
thread was destroying an object with a sprite on it and waiting for the asset lock, while the map-loading thread held
that lock and waited for the main thread — a Unity deadlock between destroying sprite objects and an async map load.
No amount of log probing could have shown it, because the log stops the moment the freeze starts.

## How to apply

1. Confirm it is a wait, not a loop: `(Get-Process -Id <pid>).CPU` twice, three seconds apart. Near zero = a wait.
2. Install the debugger once if missing: `winget install --id Microsoft.WinDbg` (gives `cdbX64.exe`).
3. Read every thread without stopping the program (`-pv` = look only):
   ```
   cdbX64.exe -pv -p <pid> -y "srv*C:\symcache*https://msdl.microsoft.com/download/symbols;srv*C:\symcache*http://symbolserver.unity3d.com/" -c "~* k 40; qd" > stacks.txt
   ```
   The first run downloads symbols and can stall for many minutes on one thread; if it does, stop YOUR debugger by
   its process id and rerun with `-y "srv*C:\symcache"` — it then uses only what was already downloaded, which is
   enough for Unity and Windows.
4. Look for the thread named "Unity Main Thread" and for the threads waiting in `Lock` / `WaitForSignal`; the pair
   that wait on each other is the cause. Thread names (`Loading.PreloadManager`, `BatchDeleteObjects`) say what each
   one was doing.
5. Only after that, ask the user to close the frozen program — the threads are gone once it closes.
