#!/usr/bin/env bash
# Rebuild the set-dressing FBX kit into Assets/Render3D/Resources/Props3D (Blender flatpak).
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../../.." && pwd)"
BLENDER="${BLENDER:-flatpak run --user --filesystem=$REPO org.blender.Blender}"
$BLENDER -b --factory-startup -P "$HERE/build_dressing.py" -- --out "$REPO/Assets/Render3D/Resources/Props3D" ${ONLY:+--only "$ONLY"} 2>&1 \
  | grep -E "EXPORTED|FAILED|Error|Traceback|line [0-9]|EXPORT_DONE" || true
