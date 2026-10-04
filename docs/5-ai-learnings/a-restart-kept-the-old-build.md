# A Restart Kept Running the Old Build

**Reach for this when:** you restarted a server after changing it, it came back up, and it still behaves like the
old code — two servers refusing each other's version, a fix that "did not take".

## The rule

Before reasoning about the code, check which build is actually running: the program's start time against the
moment you restarted it, and the built file's time against your last source edit. If the program is older than the
source, the restart never rebuilt. On Windows the usual reason is a shell loop that restarts a program "if it
crashes": Ctrl+C makes a Windows program leave with an ordinary exit code, so the shell cannot tell it from a crash
and launches the same old file again. A loop like that needs `trap 'STOPPED=1' INT` and must exit when it is set.

## Why

On 2026-10-04 the user rebuilt everything, ran the restart script, and could not get past character selection from
their Mac. The game server's log said the character server expected build v18 while the map server was v19. The
character server's source said v19, but the program running was built at 13:08. Its shell loop had been running
since 16:33, the Ctrl+C landed at 19:01:50, and the old program was back up at 19:01:55, exactly the loop's 5-second
crash delay. The rebuild step outside the loop never ran. One round was spent finding it.

## How to apply

```
tools/platform.sh port-info 8081      # who is listening, and since when
ls -la --time-style=full-iso charserver/bin/Debug/net10.0/CharServer.dll charserver/*.cs
```

To prove a loop handles Ctrl+C, run it in a hidden console
(`Start-Process -WindowStyle Hidden`) and press Ctrl+C in that console the way `start-servers.sh restart` does,
with `AttachConsole` + `GenerateConsoleCtrlEvent`. No window opens on the user's screen.
