# Evidence: Atmosphere pass (night city, rain, fog, 2D lights)

**Build**: Unity 6000.3.26f1 editor, batchmode Play Mode, branch `feat/redline-mvp`, 2026-11-03
**How captured**: `REDLINE_ATMO_CAPTURE=<abs evidence dir> Unity -batchmode -projectPath . -executeMethod Redline.Game.Atmosphere.AtmosphereCapture.RunFromCommandLine`
(bare view: arena + actors + effects + PostFx, no HUD; camera rendered into a 1280x720 RenderTexture)

| Screenshot | Shows |
|---|---|
| `atmo-loop01.png` | Loop01 gameplay view: 4 parallax skyline layers with fog, rain, lamp posts with halos, wet-metal tiles, spike glow. 10 lights touching the view. |
| `atmo-loop03.png` | Loop03, framed at its third lamp post (upper platforms, spike rows), not the spawn. 8 lights touching the view. |
| `atmo-closeup-lamp.png` | Close-up (ortho size 3.4): player beside a lamp post, tier-1 rim light and sword glow. |

**Fix pass (2026-11-03)**: dark foundation under the level, near-black near layers, darker sky, fog bands halved, contrast +10,
lamp intensity 3.2 with volumetric cones, wet-floor reflections and a stronger sheen, darker player body and rim 0.6.
Spot cone direction measured on a lit white wall: below 0.40, above 0.00, so the cone points down.

**Final tuning (2026-11-03)**: lift offset 0, bloom scatter 0.5, darker far layers and haze, cone alpha 0.25, lamp intensity 5,
sheen 1 px at 0.55, signal reflections at 35% alpha and half length.
Luminance percentiles (Rec.709 luma of the PNG), before -> after:
loop01 p10 0.208 -> 0.016, p50 0.224 -> 0.058, p90 0.267 -> 0.149;
loop03 p10 0.208 -> 0.017, p50 0.231 -> 0.070, p90 0.271 -> 0.154;
close-up p10 0.209 -> 0.023, p50 0.236 -> 0.078, p90 0.301 -> 0.187.

**Still open**: the floor pools read in the close-up but are modest in the wide shots; the player body still lightens noticeably next to a lamp.

**Sign-off**: pending. Art-director review of the look and a playtest in motion (rain, drift, beacons, splashes).
