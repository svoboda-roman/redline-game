#!/usr/bin/env bash
# Re-bakes the Reaper (scythe girl) sprite sheet into Resources/Art/Characters/Reaper and leaves the Ronin untouched.
#
# The Ronin pipeline only knows one character (Assets/Art3D/Ronin -> Resources/Art/Characters/Ronin), so this:
#   1. builds the reaper model with build_reaper.py (it writes Assets/Art3D/Ronin/ronin.fbx + palettes),
#   2. bakes it with REDLINE_BAKE_TWOHAND=1 (left hand on the scythe shaft),
#   3. copies the baked ronin.* sheet to Reaper/reaper.* (renaming the texture names inside the json),
#   4. restores the original Ronin model, palettes and sheet from the backup.
# Needs Unity Hub running (licence) and no Unity editor open on the project.
# Usage (Git Bash): tools/art/ronin/bake_reaper.sh
set -euo pipefail
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
GGG="$(cd "$REPO/.." && pwd)"
BLENDER="${BLENDER:-$GGG/blender/blender-4.2.3-windows-x64/blender.exe}"
UNITY="${UNITY:-/c/Program Files/Unity/Hub/Editor/6000.6.10f1/Editor/Unity.exe}"
BACKUP="${RONIN_BACKUP:-$GGG/backup-ronin-2026-11-20}"
R="$REPO/Assets/Resources/Art/Characters"
P="$(cygpath -w "$REPO")"

restore_ronin() {
  cp "$BACKUP/Characters/Ronin/ronin.json" "$BACKUP"/Characters/Ronin/ronin_*.png "$R/Ronin/"
  cp "$BACKUP/Art3D/Ronin/ronin.fbx" "$REPO/Assets/Art3D/Ronin/ronin.fbx"
  cp "$BACKUP"/Art3D/Ronin/Materials/Ronin_Palette_*.png "$REPO/Assets/Art3D/Ronin/Materials/"
}
trap restore_ronin EXIT

echo "== build reaper model"
"$BLENDER" -b --factory-startup -P "$REPO/tools/art/ronin/build_reaper.py" 2>&1 | grep -E "REAPER (exported|DONE)|Error|Traceback"

echo "== bake (two-handed)"
REDLINE_BAKE_TWOHAND=1 "$UNITY" -batchmode -projectPath "$P" \
  -executeMethod Redline.RoninEditor.RoninSpriteBaker.BakeAndKeepVariantsFromCommandLine -logFile "$P\\bake-reaper.log"
grep -E "RONIN_BAKE_OK|RONIN_BAKE_FAIL" "$REPO/bake-reaper.log"
grep -q RONIN_BAKE_OK "$REPO/bake-reaper.log"

echo "== copy sheet to Reaper"
cp "$R/Ronin/ronin.json" "$R/Reaper/reaper.json"
for t in color normal emissive; do cp "$R/Ronin/ronin_$t.png" "$R/Reaper/reaper_$t.png"; done
sed -i 's/"name": "ronin"/"name": "reaper"/; s/ronin_color.png/reaper_color.png/; s/ronin_normal.png/reaper_normal.png/; s/ronin_emissive.png/reaper_emissive.png/' "$R/Reaper/reaper.json"
PY="${PY:-/c/Users/jirik/AppData/Roaming/uv/python/cpython-3.12.16-windows-x86_64-none/python.exe}"
"$PY" -I "$REPO/tools/art/ronin/reaper_sheet_tune.py" "$R/Reaper/reaper.json"   # slower scythe swings
echo "== done (the Ronin is restored on exit)"
