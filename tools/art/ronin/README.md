# Tech Ronin — character pipeline

Sources: `mixamo/` (X Bot rig + Mixamo Great Sword Pack; royalty-free for games).

## Build (Blender 5.2)

```
flatpak run --filesystem=/home/roman/Projects/x --filesystem=/tmp org.blender.Blender -b --factory-startup \
    -P /home/roman/Projects/x/tools/art/ronin/build_ronin.py -- [--preview /abs/dir] [--no-export]
```

- X Bot is FBX 6.1; the legacy Blender importer rejects it, so the script uses the ufbx importer (`wm.fbx_import`).
- The outfit is modelled procedurally and skinned to the X Bot skeleton, covering every visible part of the mannequin:
  a tech kasa (emissive rim band, 12 ribs, a metal trim ring, chin cord), curved emissive eye shards in front of the
  face, mask, high collar, neck wrap and a long notched scarf tail, an open coat torso (crimson lapels, crimson inside)
  over a chest wrap and a diagonal harness strap with buckle, a five-panel duster skirt (front opening and two back
  slits with crimson edge facings), an obi with a hanging end, coat sleeves (left torn at the elbow over the cyber
  forearm), wrapped hands, techwear trousers tucked into strapped boots, shin plates and a right vambrace. Limb
  tubes are sized from the body surface (`measure`) plus a margin. The katana is a separate mesh parented to
  `mixamorig:RightHand`.
- Rows of every shell must run top-down (outward winding); the v1 torso shell and collar were inside-out.
- Colours live in `PALETTE` (one entry per palette key). Before export each face gets the UV of its key's cell and the
  keys collapse into three materials: `Ronin_Atlas` (body, one draw call), `Ronin_Rim` (brim band) and `Ronin_Blade`
  (whole katana; the emission mask lights only the blade cell). The export writes `Ronin_Palette_{Base,MS,Emit}.png`
  to `Assets/Art3D/Ronin/Materials/`; `EMIT_SCALE` must match `RoninSetup.EmitScale`.
- Extra bones for the spring bones: `Coat_{FL,FR,BL,BC,BR}_1..4` (front panels under the thighs, back tails under
  the hips), `Sash_1..2` and `Scarf_1..4` (under Spine2).
- Clips are retargeted onto the same armature, so every FBX in `Animations/` has the same hierarchy as `ronin.fbx`.
  The forward root travel of run, slide_attack, jump, jump_attack, spin_attack and attack_3 is subtracted per frame,
  so the clips play in place. `fall` holds late jump frames; `hang` is posed in the script (left arm locked overhead, torso
  hanging under it, right arm down with the katana, legs hanging, slight sway); RoninView adds a 28 degree turn toward
  the camera while hanging.
- `--preview` writes workbench renders for quick iteration.

Clip map (v3): idle, run, sprint, jump, fall, land, attack_1, attack_2, jump_attack, run_attack_1 and run_attack_2
are procedural keyposed clips from `ronin_anims.py` (one-handed katana fighter: blade trailing low behind in the run,
sprint and air states, light ready stance, bounding run with short foot contact, 10-frame slashes with the impact at
frame 2 = 0.067 s, a 6-frame land squash). Legs are solved by two-bone IK on ankle targets, the right hand is rolled
so the katana follows the requested blade direction with the straightest wrist (when the forearm is within 8 degrees
of the blade the forearm plane is biased to the blade's lower side, else the sprint carry flipped upright); fingers
come from the Mixamo idle. Consecutive quaternion keys are kept in one hemisphere (`make_action`).
Baker (`RoninSpriteBaker`): the land's last frame is idle frame 0 itself (seamless hand-off); the slashes carry a
filled crescent smear (emissive R, graded 0.15 to 0.6 trailing to leading, emissive-only outside the body) on the 1-2
fastest frames up to frame 3.
Mixamo: slide_attack, attack_3 = slash (4), spin_attack = high spin attack, hurt = impact (2),
death = two handed sword death; hang = derived from the Mixamo jump.
The kasa is HAT_R 29 (was 37), apex 15 cm, tilted 5 degrees back so the eye glints read under the brim.
Quick iteration without the export: open `ronin.blend`, call `ronin_anims.build(...)` and render (see the module doc).

## Unity

- `Assets/Editor/Ronin/RoninImporter.cs` sets the Humanoid import rules. Its `RoninSetup.EnsureAssets` generates the
  URP materials and `Assets/Art3D/Ronin/Resources/Ronin/RoninAssets.asset`.
- Runtime: `Assets/Scripts/Game/Ronin/` contains `RoninView`, `SpringBone` and `RoninAssets`.
- Evidence (studio, edit mode): `REDLINE_RONIN_CAPTURE=/abs/dir Unity -batchmode -projectPath . -executeMethod Redline.RoninEditor.RoninSetup.CaptureFromCommandLine`
- Evidence (in the Loop01 3D world, play mode; writes ronin-v2-closeup/poses/gamescale.png):
  `REDLINE_RONIN_WORLD=/abs/dir [REDLINE_RONIN_REIMPORT=1] Unity -batchmode -projectPath . -executeMethod Redline.RoninEditor.RoninSetup.CaptureWorldFromCommandLine`
- Draw calls: body 2 (atlas + rim), katana 1. The old per-colour `.mat` files in `Materials/` are no longer referenced.
