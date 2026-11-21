# props3d — 3D prop + set-dressing kits (Blender -> FBX -> Unity)

Two Blender scripts write FBX into `Assets/Render3D/Resources/Props3D/` (loaded at runtime by
`Assets/Scripts/Game/Render3D/Props3D.cs`):

| Script | What | Rebuild |
|---|---|---|
| `export_props_fbx.py` | rooftop props (reuses `tools/art/props/build_props.py` geometry) | `blender -b --factory-startup -P export_props_fbx.py -- --out <abs Props3D dir>` |
| `build_dressing.py` | set-dressing kit: `girder_col girder_x truss_span crane pillar overpass_span jersey_barrier guard_rail facade_block gallery_arcade statue banner chimney pipe_run cable_sag fg_girder fg_pipe fg_fence fg_cables` | `tools/art/props3d/make_dressing.sh` (`ONLY=crane,pillar` to rebuild a subset) |

Blender 5.2 is the flatpak (`flatpak run --user --filesystem=<repo> org.blender.Blender`); paths must be absolute.

## Conventions
- Blender Z up, -Y faces the camera; exported Y up, front toward Unity -Z. 1 unit = 1 Unity unit (40 texels).
- Material slots are **names only** (`metal gunmetal steel dark rust concrete brick hazard warm neon`);
  `Props3D.Palette` maps them to URP Lit materials (dark, low chroma; `warm`/`neon` are emissive).
  `Props3D.GroupOf` folds gunmetal/steel into metal when layers are merged.
- Nothing that must read is thinner than 0.1 unit (4 texels). Tri budgets: hero 2k, filler 500, occluder 800
  (current kit: crane 876, gallery_arcade 840, facade_block 588, others <= 576; fg_* <= 360).
- The dressing FBX `.meta` files have **Read/Write enabled** (`isReadable: 1`): `Props3D.Merger` combines them
  at runtime into one mesh per depth layer. Keep that flag when re-importing, or the merge fails in a player.
- Placement lives in `Arena3DDressing.cs` (partial of `Arena3DRenderer`), per level: Loop01 plant, Loop02
  overpass, Loop03 gallery.
