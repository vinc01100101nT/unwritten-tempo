---
name: set-defaults
description: Use when the user says "set the default", "change the default value", or asks for a new starting/baseline value for anything in the Unity game — a stat, a knob, a component field, a config. Covers every layer a value can hide in, and why changing only the code silently does nothing.
---

# "Set default values" means change it EVERYWHERE

When the user says "set default(s)", they mean the new value must be **what the game ACTUALLY loads** — not what
the code says it should be. A value can live in four places, and a saved one always beats the code.

Hunt all four, make them agree, and report which you touched:

1. **The code field initializer / constant** — the real default, and the only one most people change.
2. **EVERY serialized override that would shadow it** — scene `.unity` and prefab `.prefab` instances of that
   component. **A serialized value ALWAYS wins over the code default**, so changing only the code silently does
   nothing for those instances. This is the layer that gets missed.
3. **Any persisted copy** — `PlayerPrefs`, the save file (`unwritten_save.json`), an on-disk cache, a config.
   Delete or rewrite the stale key so a reload can't resurrect the old value.
4. **Any ScriptableObject / asset holding it** — the data the game loads at runtime.

If a persisted value can't be code-defaulted, **clear it**. Never leave one layer disagreeing: a lingering
override or a cached key means the "default" is a lie at runtime.

## The neighbouring trap: a NEW authored field

If you are ADDING a field that existing assets must answer (a new gate half, a new required id, one setting split
into two), that is a harder version of the same problem and it is covered by its own hard-pinned rule in
`CLAUDE.md`: the field deserializes to a **real, legal-looking** zero value in every asset saved before it, so the
old data now confidently says something nobody meant. Fill it in for every existing asset in the same change,
make "unset" distinguishable from a real value, and never let an editor draw a value it hasn't saved.
