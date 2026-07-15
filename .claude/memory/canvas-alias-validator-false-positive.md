---
name: canvas-alias-validator-false-positive
description: "Tempo canvas validate-canvas reports @/ path-alias imports as \"missing_package\" — it's a false positive"
metadata: 
  node_type: memory
  type: reference
  originSessionId: cec72265-2905-4691-b027-c21591836e99
---

In this Vite canvas host, `validate-canvas` reports `missing_package` for `@/...` imports (e.g. `@/pages`, `@/skill-draft`) and returns `valid: false`. This is a false positive: its npm-walker mis-reads the `@/` path alias as a scoped package name. There is nothing to `npm install`.

The `@/` alias is canonical (tempo guide endorses it) and resolves at runtime via `vite-tsconfig-paths` (tempo/tsconfig.json maps `@/*` → `../src/*`). TypeScript resolves it fine — no `unresolved_import`/`type_error` is emitted, only the bogus `missing_package`.

**How to apply:** Don't try to "fix" these by switching off `@/` or installing packages. Confirm via `canvas_screenshot` that the storyboard renders; if it does, the warning is noise.
