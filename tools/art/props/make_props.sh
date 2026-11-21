#!/usr/bin/env bash
# Build + render + pixelate + pack the rooftop prop set.
# Usage: tools/art/props/make_props.sh   (env: WORK scratch dir, BLENDER, ONLY comma list, SHEET png path, PPU (48))
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../../.." && pwd)"
WORK="${WORK:-${TMPDIR:-/tmp}/redline_props}"
RENDER="$WORK/render_$(date +%s)"
mkdir -p "$RENDER"
BLENDER="${BLENDER:-flatpak run --filesystem=$REPO --filesystem=$WORK org.blender.Blender}"
$BLENDER -b --factory-startup -P "$HERE/build_props.py" -- --out "$RENDER" --ppu "${PPU:-48}" ${ONLY:+--only "$ONLY"} 2>&1 \
  | grep -E "RENDERED|Error|Traceback|line [0-9]" || true
test -f "$RENDER/manifest.json" || { echo "render failed"; exit 1; }
python3 "$HERE/pack_props.py" --render "$RENDER" --out "$REPO/Assets/Resources/Art/Props" \
  --sheet "${SHEET:-$REPO/production/qa/evidence/props-sheet.png}" --zoom 4
