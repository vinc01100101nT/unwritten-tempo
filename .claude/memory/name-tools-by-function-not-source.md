---
name: name-tools-by-function-not-source
description: "Name reusable/general tools by what they DO, not by the asset/source that prompted them"
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 8fb485fe-a7aa-424e-be64-f757116106ac
---

User directive (2026-06-29): when a tool/class/menu item is general-purpose, name it generically by its function — do NOT bake in the specific asset, vendor, or source that happened to prompt it. E.g. a GLB importer that works on ANY glb is `GlbPropImporter` / "Import GLB Prop…", NOT "Trellis/Hunyuan" anything.

**Why:** asset-source names are a leaky abstraction — they misstate scope, mislead the next user, and rot the moment a second source appears. The user considers source-named general tools "a mess."

**How to apply:** before naming a tool/class/file/menu item, ask "is this logic specific to that source, or format/general?" If general, name it by function. Source names are only OK when the logic is genuinely source-specific (e.g. `PxltgrMaterialFixer` repairs a Pxltgr-pack-specific quirk). Relates to [[editor-tools-menu]].
