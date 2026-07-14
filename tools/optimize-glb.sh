#!/usr/bin/env bash
# Optimize dense GLB props (TRELLIS/Hunyuan output ~250k tris) into game-ready meshes.
# One offline pass via gltf-transform does: weld + quadric simplify (UV/normal error-bounded,
# so no texture "squish") + texture downsize + geometry compression.
# Default COMPRESS=quantize → KHR_mesh_quantization, which glTFast imports natively with NO extra package.
# COMPRESS=meshopt (EXT_meshopt_compression) is smaller but needs the "meshoptimizer decompression for Unity"
# package installed in Unity, else the mesh imports EMPTY (undraggable). See com.unity.cloud.gltfast README.
#
# Usage:
#   tools/optimize-glb.sh                 # optimize every .glb in Assets/Prefabs -> *_opt.glb (SAFE, non-destructive)
#   tools/optimize-glb.sh path/to/x.glb   # optimize a single file
#   INPLACE=1 tools/optimize-glb.sh       # overwrite originals in place (keeps prefab GUID refs); backs up to tools/glb_backups/
#
# Tunables (env override, e.g. RATIO=0.5 tools/optimize-glb.sh):
RATIO="${RATIO:-0.3}"          # fraction of verts to KEEP (0.3 = ~30%). Higher = more detail, less reduction.
ERROR="${ERROR:-0.005}"        # simplify error as fraction of mesh size. Higher = more aggressive.
TEX_SIZE="${TEX_SIZE:-1024}"   # max texture dimension in px (2048 -> 1024 halves VRAM, invisible at iso distance).
COMPRESS="${COMPRESS:-quantize}" # geometry compression: quantize (glTFast-native) | meshopt (needs Unity pkg) | draco | false

set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PREFABS="$ROOT/unity/Assets/Prefabs"
INPLACE="${INPLACE:-0}"

# Build the file list: explicit arg, or every .glb under Prefabs (excluding already-optimized copies).
if [ $# -gt 0 ]; then
  FILES=("$@")
else
  mapfile -t FILES < <(find "$PREFABS" -maxdepth 3 -iname "*.glb" ! -iname "*_opt.glb")
fi

[ ${#FILES[@]} -eq 0 ] && { echo "No .glb files found under $PREFABS"; exit 0; }

opt() {
  local in="$1" out="$2"
  npx --yes @gltf-transform/cli@latest optimize "$in" "$out" \
    --simplify-ratio "$RATIO" --simplify-error "$ERROR" --texture-size "$TEX_SIZE" \
    --compress "$COMPRESS" --texture-compress auto --instance false --palette false
}

for f in "${FILES[@]}"; do
  [ -f "$f" ] || { echo "skip (missing): $f"; continue; }
  before=$(stat -c%s "$f")
  if [ "$INPLACE" = "1" ]; then
    mkdir -p "$ROOT/tools/glb_backups"
    cp -n "$f" "$ROOT/tools/glb_backups/$(basename "$f")"   # -n: never clobber an existing backup
    tmp="$(dirname "$f")/.__opt_tmp.glb"
    opt "$f" "$tmp"
    mv -f "$tmp" "$f"
    after=$(stat -c%s "$f")
    echo ">> $(basename "$f")  $((before/1024/1024))MB -> $((after/1024/1024))MB  (in place; backup in tools/glb_backups/)"
  else
    out="${f%.glb}_opt.glb"
    opt "$f" "$out"
    after=$(stat -c%s "$out")
    echo ">> $(basename "$out")  $((before/1024/1024))MB -> $((after/1024/1024))MB"
  fi
done

echo "Done. Focus the Unity editor to let it reimport."
if [ "$INPLACE" != "1" ]; then
  echo "NOTE: *_opt.glb are NEW assets (new GUID). To use them, swap the mesh/prefab reference, or re-run with INPLACE=1 to keep existing prefab references."
fi
