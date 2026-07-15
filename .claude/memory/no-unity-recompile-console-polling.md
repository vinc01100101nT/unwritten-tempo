# Do NOT recompile / refresh / read Unity console — the USER watches logs

## Rule (hard)
NEVER call `refresh_unity`, `read_console`, or poll `editor_state` for compile/error checking after editing
C#. It's slow (each `refresh_unity` blocks ~60s and often times out) and makes the task drag. Just make the
edits and STOP. The USER watches the Unity console and will tell me if there's a compile/log error.

## Why
User explicitly, angrily, told me to stop: "stop recompiling refreshing and reading unity console, just go
straight to your task. i will tell you if there is error in logs. it makes your task long." (2026-07-05)

## What's still OK
- `execute_code` when I genuinely need a one-off measurement/mutation the user asked for is fine, but don't
  chain it with refresh/console polling.
- If I truly need to know whether something compiled, ASK the user rather than polling.

## Default flow after a C# edit
Edit → done. No verify loop. Trust the Edit tool (it errors if the change didn't apply). Hand off testing.
