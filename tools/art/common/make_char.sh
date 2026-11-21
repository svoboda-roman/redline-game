#!/usr/bin/env bash
# End-to-end for one character script: model + animate (one Blender pass) -> render -> bake + pack.
# Default packer: bake_char.py (3D-lit: plain albedo + view-space normal + emissive, the ronin's material language).
# PACKER=pixelate selects the old 2D stylizer (pixelate_char.py: toon bands, rim, outline) for the 2D path.
# Usage: tools/art/common/make_char.sh <script.py> <name> [OutDirName]
#   e.g. tools/art/common/make_char.sh tools/art/enemies/grunt.py grunt Grunt
# Env overrides: PACKER (bake|pixelate), PPU (48 = Pixel3DCamera.TexelsPerUnit), SCALE (8), WORK, BLENDER, ONLY (comma list of animations), PREVIEW (dir)
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../../.." && pwd)"
SCRIPT="$(cd "$(dirname "$1")" && pwd)/$(basename "$1")"
NAME="$2"
DIRNAME="${3:-${NAME^}}"
PPU="${PPU:-48}"
SCALE="${SCALE:-8}"
OUT="${OUT:-$REPO/Assets/Resources/Art/Characters/$DIRNAME}"
WORKROOT="${WORK:-${TMPDIR:-/tmp}/redline_chars}"
WORKDIR="$WORKROOT/$NAME"
RENDER="$WORKDIR/render_ppu${PPU}_x${SCALE}_$(date +%s)"
mkdir -p "$WORKDIR" "$RENDER"
BLENDER="${BLENDER:-flatpak run --filesystem=$REPO --filesystem=$WORKROOT org.blender.Blender}"
BLEND="$WORKDIR/$NAME.blend"

echo "== $NAME: model + animate"
$BLENDER -b --factory-startup -P "$SCRIPT" -- --out "$BLEND" 2>&1 | grep -E "SAVED|Error|Traceback|line [0-9]" || true
test -f "$BLEND" || { echo "build failed"; exit 1; }
echo "== $NAME: render (PPU $PPU, x$SCALE)"
$BLENDER -b "$BLEND" -P "$HERE/render_char.py" -- --ppu "$PPU" --scale "$SCALE" --out "$RENDER" ${ONLY:+--only "$ONLY"} 2>&1 \
  | grep -E "RENDERED|Error|Traceback|line [0-9]" || true
test -f "$RENDER/manifest.json" || { echo "render failed"; exit 1; }
if [ "${PACKER:-bake}" = pixelate ]; then PACK="$HERE/pixelate_char.py"; else PACK="$HERE/bake_char.py"; fi
echo "== $NAME: pack ($(basename "$PACK"))"
python3 "$PACK" --render "$RENDER" --out "$OUT" --name "$NAME" ${PREVIEW:+--preview "$PREVIEW"}
