#!/usr/bin/env bash
# End-to-end: build model -> key animations -> render passes -> pixelate + pack atlases + evidence.
# Usage: tools/art/samurai/make_samurai.sh [PPU]      (default PPU 40; env SCALE=8 render supersampling)
# Env overrides: OUT, EVIDENCE, WORK, BLENDER, ONLY (comma list of animations), PREVIEW (dir for review sheets)
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../../.." && pwd)"
PPU="${1:-${PPU:-40}}"
SCALE="${SCALE:-8}"
OUT="${OUT:-$REPO/Assets/Resources/Art/Characters/Samurai}"
EVIDENCE="${EVIDENCE:-$REPO/production/qa/evidence}"
WORK="${WORK:-${TMPDIR:-/tmp}/samurai_build}"
RENDER="$WORK/render_ppu${PPU}_x${SCALE}"
mkdir -p "$WORK" "$RENDER"
BLENDER="${BLENDER:-flatpak run --filesystem=$REPO --filesystem=$WORK org.blender.Blender}"
BLEND="$HERE/samurai.blend"

echo "== build_model"
$BLENDER -b --factory-startup -P "$HERE/build_model.py" -- --out "$WORK/model.blend" 2>&1 | grep -E "SAVED|Error|Traceback|line [0-9]" || true
echo "== animate"
$BLENDER -b "$WORK/model.blend" -P "$HERE/animate.py" -- --out "$BLEND" 2>&1 | grep -E "SAVED|Error|Traceback|line [0-9]" || true
test -f "$BLEND" || { echo "animate failed"; exit 1; }
echo "== render (PPU $PPU, x$SCALE)"
$BLENDER -b "$BLEND" -P "$HERE/render.py" -- --ppu "$PPU" --scale "$SCALE" --out "$RENDER" ${ONLY:+--only "$ONLY"} 2>&1 \
  | grep -E "RENDERED|Error|Traceback|line [0-9]" || true
test -f "$RENDER/manifest.json" || { echo "render failed"; exit 1; }
echo "== pixelate"
python3 "$HERE/pixelate.py" --render "$RENDER" --out "$OUT" --evidence "$EVIDENCE" ${PREVIEW:+--preview "$PREVIEW"}
