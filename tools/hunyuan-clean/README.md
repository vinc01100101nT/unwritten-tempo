# hunyuan-clean

Headless Blender batch cleaner for Hunyuan3D turbo props: import GLB → Voxel
Remesh (watertight, fills holes) → Decimate → export GLB. No clicking.

## Use

Single file:

```bash
blender --background --python clean_prop.py -- input.glb output.glb
```

Whole folder (every `*.glb` in IN → `<name>_clean.glb` in OUT):

```bash
blender --background --python clean_prop.py -- --dir ./raw_props ./clean_props
```

Tune without editing the script:

```bash
blender --background --python clean_prop.py -- --voxel 0.025 --decimate 0.4 in.glb out.glb
```

Flags: `--voxel 0.03` (smaller = more detail, heavier), `--decimate 0.5`
(1.0 keeps all tris), `--no-decimate`, `--dir` (batch mode).

## Notes

- Voxel remesh **discards UVs/textures** — intended, props are re-textured
  downstream. Don't run it on a mesh whose Hunyuan texture you want to keep.
- Needs `blender` on PATH (or call the full `blender.exe` path).
- Best fix is upstream: clean concept input (see the **Asset Generation Style
  Guide v1.0**, "Geometry Cleanliness" section). With good inputs you often
  skip this step entirely. This is the safety net, not the primary fix.
