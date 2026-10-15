# Save Only the Assets You Changed

**Reach for this when:** a script you ran through the live editor wrote some assets, and `git status` now shows files
you never touched — a font, a prefab, a model.

## The rule

Never end an editor script with `AssetDatabase.SaveAssets()`. It writes EVERY asset the editor holds as changed, not
the ones your script changed. Save each asset you edited with `AssetDatabase.SaveAssetIfDirty(asset)`, then run
`git status` and account for every file in it before reporting.

## Why

On 2026-10-15 a script set the icon on 202 item files and ended with `SaveAssets()`. It also wrote two files nobody
asked for: a prefab whose bounds flag an earlier read-only script had flipped in memory (one line), and the dynamic
font asset, which had grown 2,329 lines in memory while the game was played. The prefab line was put back by hand; the
font write could not be undone by a hand edit and had to be reported to the user instead.

## How to apply

```csharp
var so = new UnityEditor.SerializedObject(asset);
so.FindProperty("icon").objectReferenceValue = sprite;
so.ApplyModifiedPropertiesWithoutUndo();
UnityEditor.AssetDatabase.SaveAssetIfDirty(asset);   // this asset only
```

Then `git status --porcelain` and check that every changed file is one the script was meant to change.
Also: reading `.bounds` on a skinned renderer inside a prefab ASSET marks that prefab changed in memory, so inspect a
prefab through an instance, never the asset itself.
