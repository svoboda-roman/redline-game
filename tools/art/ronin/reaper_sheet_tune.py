"""Post-bake tuning of the Reaper sheet (run by bake_reaper.sh; safe to run again).

Slows the scythe swings: every attack animation plays at ATTACK_FPS instead of the baker's 30 fps. The presenter takes
its attack duration from attack1 (frames / fps), so the whole swing reads heavier; the Ronin's sheet is untouched.
Usage: python reaper_sheet_tune.py <path to reaper.json>
"""
import json
import sys

ATTACK_FPS = 25.0   # 10 frames = a 0.4 s swing
ATTACKS = {"attack1", "attack2", "jumpattack", "runattack1", "runattack2"}

path = sys.argv[1]
with open(path, encoding="utf-8") as f:
    sheet = json.load(f)
for anim in sheet["animations"]:
    if anim["name"] in ATTACKS:
        anim["fps"] = ATTACK_FPS
with open(path, "w", encoding="utf-8") as f:
    json.dump(sheet, f, indent=4)
print("reaper attacks at", ATTACK_FPS, "fps")
