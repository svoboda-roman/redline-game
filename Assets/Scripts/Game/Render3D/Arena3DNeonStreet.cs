using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Redline.Game.Render3D
{
    /// <summary>
    /// The neon street backdrop of <see cref="Arena3DRenderer"/> (<see cref="Arena3DLook.NeonStreet"/>): a dense
    /// East-Asian night street behind the play plane. Rows of building fronts (procedural window facades) covered with
    /// lit signs from the shop fronts up to the roofs: vertical blades, horizontal boards, pseudo-Hangul glyphs, shop
    /// windows with awnings, LED roof lines, soft halos, and some signs blinking. Four rows recede into pink fog; the
    /// nearest row is mirrored by the wet floor. Rows tile like the old city rows (world-fixed, follow the camera).
    /// </summary>
    /// <remarks>Everything is generated from <see cref="Arena3DLook.Seed"/>: same seed, same street. Sign and facade
    /// surfaces are unlit opaque (Proto3D/Fx opaque mode) so they keep their colours whatever the scene lighting.</remarks>
    public sealed partial class Arena3DRenderer
    {
        private Material _streetFacadeMat, _streetSignMat, _streetBlinkMat;
        private readonly List<SignCell> _signCells = new List<SignCell>(64);

        private struct SignCell
        {
            public Rect Uv;
            public int Kind;      // 0 vertical blade, 1 horizontal board, 2 shop front
            public Color Tint;    // average colour, for the halo
        }

        /// <summary>(depth of the facade fronts, detail 0..1) per row, nearest first.</summary>
        private static readonly (float z, float detail)[] StreetRows = { (16f, 1f), (29f, 0.85f), (48f, 0.65f), (80f, 0.45f) };

        private void NeonStreet()
        {
            var facadeTex = Own(FacadeTexture(_look.Seed));
            var atlas = Own(SignAtlas(_look.Seed, _look.SignColors, _signCells));
            _streetFacadeMat = Own(Proto3DMaterials.FxMat("streetFacade", facadeTex, Color.white, 2, 1f));
            var facadeMats = new Material[FacadeStyles];
            facadeMats[0] = _streetFacadeMat;
            for (int s = 1; s < FacadeStyles; s++)
                facadeMats[s] = Own(Proto3DMaterials.FxMat("streetFacade" + s, Own(FacadeStyleTexture(_look.Seed, s)), Color.white, 2, 1f));
            _streetBoardMat = Own(Proto3DMaterials.FxMat("streetBoards", Own(BoardAtlas(_look.Seed, _look.SignColors, _boardCells)), Color.white, 2, 0.3f));
            StreetSky();
            _streetSignMat = Own(Proto3DMaterials.FxMat("streetSigns", atlas, Color.white, 2, 0.3f));
            _streetBlinkMat = Own(Proto3DMaterials.FxMat("streetSignsBlink", atlas, Color.white, 2, 0.3f));
            var haloMat = Own(Proto3DMaterials.FxMat("streetHalo", _blob, Color.white, 0, 0.45f));

            _cityRows = new Transform[StreetRows.Length + 1];
            _cityPeriod = new float[StreetRows.Length + 1];
            var rnd = new System.Random(_look.Seed * 41 + 17);
            FarSkyline(rnd, StreetRows.Length);
            for (int r = 0; r < StreetRows.Length; r++)
            {
                var (z, detail) = StreetRows[r];
                float period = Mathf.Ceil((PlaneDistance + z) * 1.2f / 8f) * 8f;
                _cityRows[r] = new GameObject("StreetRow " + z).transform;
                _cityRows[r].SetParent(_follow, false);
                _cityPeriod[r] = period;

                var facades = new UvQuads();
                var bodies = new UvQuads[FacadeStyles];
                bodies[0] = facades;
                for (int s = 1; s < FacadeStyles; s++) bodies[s] = new UvQuads();
                var boards = new UvQuads();
                var signs = new UvQuads();
                var blink = new UvQuads();
                var halos = new QuadBatch();
                float x = 0f;
                while (x < period - 2f)
                {
                    float scale = 1f + z * 0.012f;
                    float w = Mathf.Min((5.5f + (float)rnd.NextDouble() * 6f) * scale, period - x);
                    if (period - x - w < 3f) w = period - x;   // no sliver at the seam
                    // Mixed heights: low walk-ups, mid blocks, tall towers (the sky shows between and above them).
                    // Near row mostly low (rooftop billboards and the sky read over it), towers further back.
                    double hr = rnd.NextDouble();
                    float q = (float)rnd.NextDouble();
                    // Low skyline so the night sky, the moon and the far city show above the street.
                    // Heights in world units, kept under the camera's view top at each depth (seen from the street:
                    // ~14 at z 16, ~18 at z 29, ~23 at z 48, ~31 at z 80) so a band of night sky is always visible.
                    float hu = r == 0 ? (hr < 0.75 ? 5f + q * 4f : 9f + q * 3f)
                             : r == 1 ? (hr < 0.75 ? 6.5f + q * 5f : 11.5f + q * 3f)
                             : r == 2 ? (hr < 0.75 ? 8f + q * 7f : 15f + q * 4f)
                             : (hr < 0.75 ? 10f + q * 9f : 19f + q * 5f);
                    float top = FloorTop + hu;
                    StreetBuilding(rnd, facades, bodies, boards, signs, blink, halos, x, w, top, z, detail, scale, r == 0);
                    // Power / telecom cables sagging between neighbouring buildings (the two nearest rows).
                    if (r <= 1 && x > 0f)
                    {
                        int nc = rnd.Next(0, 3);
                        for (int k = 0; k < nc; k++)
                        {
                            float ya = FloorTop + (5f + (float)rnd.NextDouble() * 14f) * scale, yb = ya + ((float)rnd.NextDouble() - 0.5f) * 3f * scale;
                            var a = new Vector3(x - (1f + (float)rnd.NextDouble() * 3f) * scale, ya, z - 1.4f * scale);
                            var b = new Vector3(x + (1f + (float)rnd.NextDouble() * 4f) * scale, yb, z - 1.4f * scale);
                            // Some cables are strings of coloured bulbs (festival lights across the street).
                            Cable(facades, a, b, (0.4f + (float)rnd.NextDouble() * 0.8f) * scale, 0.06f * scale, new Color(0.05f, 0.04f, 0.06f),
                                rnd.NextDouble() < 0.5 ? signs : null, _look.SignColors, rnd, scale);
                        }
                    }
                    x += w;
                    // The odd alley: a gap through which the next row (and the sky) shows.
                    if (r < StreetRows.Length - 1 && x < period - 6f * scale && rnd.NextDouble() < 0.22) x += (1.5f + (float)rnd.NextDouble() * 2f) * scale;
                }
                // Street floor between the deck's back edge and the nearest fronts (no void under the signs).
                if (r == 0) facades.Floor(new Vector3(period * 0.5f, FloorTop - 0.02f, (_look.DeckFar + z) * 0.5f), period, z - _look.DeckFar + 0.4f, PlasterUv, new Color(0.18f, 0.12f, 0.16f));

                var bodyMeshes = new Mesh[FacadeStyles];
                for (int s = 1; s < FacadeStyles; s++) if (bodies[s].Count > 0) bodyMeshes[s] = Own(bodies[s].Build("streetBodies" + s + "_" + r));
                var boardMesh = boards.Count > 0 ? Own(boards.Build("streetBoards" + r)) : null;
                var facadeMesh = Own(facades.Build("streetFacades" + r));
                var signMesh = signs.Count > 0 ? Own(signs.Build("streetSigns" + r)) : null;
                var blinkMesh = blink.Count > 0 ? Own(blink.Build("streetBlink" + r)) : null;
                var haloMesh = halos.Count > 0 ? Own(halos.Build("streetHalos" + r)) : null;
                for (int c = -1; c <= 1; c++)
                {
                    var inst = new GameObject("Strip " + c).transform;
                    inst.SetParent(_cityRows[r], false);
                    inst.localPosition = new Vector3(c * period, 0f, 0f);
                    // The nearest row is mirrored by the wet floor; the far rows are not (cost).
                    StreetPart("facades", inst, facadeMesh, _streetFacadeMat, r > 0);
                    for (int s = 1; s < FacadeStyles; s++) if (bodyMeshes[s] != null) StreetPart("bodies" + s, inst, bodyMeshes[s], facadeMats[s], r > 0);
                    if (boardMesh != null) StreetPart("boards", inst, boardMesh, _streetBoardMat, r > 0);
                    if (signMesh != null) StreetPart("signs", inst, signMesh, _streetSignMat, r > 0);
                    if (blinkMesh != null) StreetPart("blink", inst, blinkMesh, _streetBlinkMat, r > 0);
                    if (haloMesh != null) StreetPart("halos", inst, haloMesh, haloMat, true);
                }
            }
        }

        private static void StreetPart(string name, Transform parent, Mesh mesh, Material mat, bool hideFromMirror)
        {
            var go = MeshGo(name, parent, mesh, mat);
            NoShadow(go);
            if (hideFromMirror) Hidden(go);
        }

        /// <summary>Blinks the blinking sign set (called from Update): mostly on, short irregular drop-outs.</summary>
        private void StreetUpdate(float time)
        {
            if (_streetBlinkMat == null) return;
            float a = Mathf.Repeat(time * 0.7f, 1f), b = Mathf.Repeat(time * 1.9f + 0.3f, 1f);
            bool off = a > 0.86f || (b > 0.93f && a > 0.5f);
            _streetBlinkMat.SetColor("_Color", off ? new Color(0.12f, 0.12f, 0.12f, 1f) : Color.white);
        }

        private void StreetBuilding(System.Random rnd, UvQuads facades, UvQuads[] bodies, UvQuads boards, UvQuads signs, UvQuads blink, QuadBatch halos,
            float x, float w, float top, float z, float detail, float scale, bool near)
        {
            float R() => (float)rnd.NextDouble();
            float bottom = near ? FloorTop - 0.5f : -40f;
            // Building type: towers are glass offices or ribbed concrete; the rest plaster walk-ups, panel blocks or brick.
            bool tower = top - FloorTop > 11f + z * 0.12f;
            int style = tower ? (R() < 0.55f ? 3 : 4) : new[] { 0, 1, 1, 2, 2, 0 }[rnd.Next(6)];
            // Facade: tiled texture (8 units per tile), tinted per building (warm, never black; glass stays cool).
            var tints = new[] { new Color(0.85f, 0.62f, 0.7f), new Color(0.7f, 0.6f, 0.85f), new Color(0.9f, 0.7f, 0.6f), new Color(0.75f, 0.55f, 0.6f), new Color(0.6f, 0.55f, 0.75f) };
            var tint = (style == 3 ? new Color(0.9f, 0.9f, 1f) : tints[rnd.Next(tints.Length)]) * (0.75f + R() * 0.3f) * Mathf.Lerp(0.75f, 1f, detail);
            tint.a = 1f;
            float u0 = R(), v0 = R();
            bodies[style].Quad(new Vector3(x + w * 0.5f, (bottom + top) * 0.5f, z), w, top - bottom,
                new Rect(u0, v0, w / (8f * scale), (top - bottom) / (8f * scale)), tint);
            // Roof clutter on every building: AC boxes, vent stacks, a satellite dish.
            if (detail > 0.4f)
            {
                var kit = new Color(0.24f, 0.2f, 0.27f);
                int nk = 1 + rnd.Next(3);
                for (int k = 0; k < nk; k++)
                {
                    float kx = x + 0.6f * scale + R() * Mathf.Max(0.1f, w - 1.2f * scale);
                    switch (rnd.Next(3))
                    {
                        case 0: { float kw = (0.8f + R() * 0.6f) * scale, kh = (0.5f + R() * 0.4f) * scale; facades.Quad(new Vector3(kx, top + 0.5f * scale + kh * 0.5f, z + 0.3f), kw, kh, PlasterUv, kit); facades.Quad(new Vector3(kx, top + 0.5f * scale + kh * 0.5f, z + 0.25f), kw * 0.5f, kh * 0.5f, PlasterUv, kit * 0.6f); break; }
                        case 1: { float kh = (1f + R() * 1.4f) * scale; facades.Quad(new Vector3(kx, top + 0.5f * scale + kh * 0.5f, z + 0.3f), 0.22f * scale, kh, PlasterUv, kit * 1.2f); facades.Quad(new Vector3(kx, top + 0.5f * scale + kh, z + 0.28f), 0.36f * scale, 0.12f * scale, PlasterUv, kit); break; }
                        default: { float ds = (0.5f + R() * 0.3f) * scale; facades.Line(new Vector3(kx - ds * 0.5f, top + 0.6f * scale + ds, z + 0.3f), new Vector3(kx + ds * 0.4f, top + 0.6f * scale, z + 0.3f), ds * 0.7f, PlasterUv, kit * 1.4f); facades.Quad(new Vector3(kx, top + 0.55f * scale + ds * 0.25f, z + 0.32f), 0.08f * scale, ds * 0.5f, PlasterUv, kit); break; }
                    }
                }
            }

            // Big wall billboards on taller buildings (brands; some dead).
            if (detail > 0.55f && top - FloorTop > 9.5f * scale && R() < 0.5f)
            {
                int nb = R() < 0.3f ? 2 : 1;
                for (int k = 0; k < nb; k++)
                {
                    var cell = PickBoard(rnd);
                    float bw = Mathf.Min(w * 0.8f, (4f + R() * 3f) * scale), bh = bw * 0.5f;
                    float by = FloorTop + 6f * scale + bh * 0.5f + R() * Mathf.Max(0.1f, top - FloorTop - 8f * scale - bh);
                    float bx = x + w * 0.5f + (R() - 0.5f) * Mathf.Max(0f, w - bw);
                    bool deadB = R() < _look.DeadSigns;
                    facades.Quad(new Vector3(bx, by, z - 0.2f), bw + 0.3f * scale, bh + 0.3f * scale, PlasterUv, new Color(0.15f, 0.13f, 0.17f));   // frame
                    boards.Quad(new Vector3(bx, by, z - 0.25f), bw, bh, cell.Uv, deadB ? new Color(0.16f, 0.14f, 0.16f) : Color.white * 0.95f);
                    if (!deadB) halos.Billboard(new Vector3(bx, by, z - 0.6f), bw * 1.6f, bh * 2.2f, cell.Tint * 0.09f);
                }
            }
            // Roof parapet line and the odd LED outline.
            facades.Quad(new Vector3(x + w * 0.5f, top + 0.25f * scale, z - 0.05f), w, 0.5f * scale, PlasterUv, tint * 0.6f);
            if (R() < 0.45f * detail)
            {
                var led = _look.SignColors[rnd.Next(_look.SignColors.Length)] * 1.1f;
                signs.Quad(new Vector3(x + w * 0.5f, top + 0.55f * scale, z - 0.1f), w, 0.14f * scale, WhiteUv, led);
                signs.Quad(new Vector3(x + 0.07f * scale, (FloorTop + 4f + top) * 0.5f, z - 0.1f), 0.14f * scale, top - FloorTop - 4f, WhiteUv, led);
            }

            var dark = new Color(0.22f, 0.18f, 0.24f);   // metal: railings, pipes, pillars, roof kit (x plaster texel ~0.38)

            // Ground floor: a row of separate shops (3-4 units each) between dark pillars, each its own front
            // (lit store, bar, convenience, restaurant, shutter down, dark), its own header sign and maybe an awning.
            if (near || z < 40f)
            {
                float sh = 2.6f * scale, pillar = 0.28f * scale;
                int units = Mathf.Max(1, Mathf.RoundToInt((w - pillar) / (3.6f * scale)));
                float uw = (w - pillar) / units;
                facades.Quad(new Vector3(x + w * 0.5f, FloorTop + sh * 0.5f, z - 0.12f), w, sh + 0.2f * scale, PlasterUv, dark);
                for (int u = 0; u < units; u++)
                {
                    float ux = x + pillar + u * uw + (uw - pillar) * 0.5f;
                    var shop = Pick(rnd, 2);
                    bool shut = shop.Tint.r < 0.3f;   // shutter / dark variants carry a dark tint
                    signs.Quad(new Vector3(ux, FloorTop + sh * 0.5f, z - 0.15f), uw - pillar, sh, shop.Uv, Color.white * (shut ? 0.75f : 0.9f));
                    // Header board above the shop (dead on shut shops).
                    var head = Pick(rnd, 1);
                    float hh = 0.62f * scale;
                    signs.Quad(new Vector3(ux, FloorTop + sh + hh * 0.5f + 0.06f * scale, z - 0.2f), uw - pillar, hh, head.Uv,
                        shut ? new Color(0.15f, 0.12f, 0.14f) : Color.white * 0.95f);
                    if (!shut && R() < 0.55f)
                    {
                        var aw = _look.SignColors[rnd.Next(_look.SignColors.Length)] * 0.75f;
                        signs.Quad(new Vector3(ux, FloorTop + sh - 0.1f * scale, z - 0.55f), uw - pillar + 0.1f, 0.32f * scale, WhiteUv, aw);
                        signs.Quad(new Vector3(ux, FloorTop + sh - 0.3f * scale, z - 0.57f), uw - pillar + 0.1f, 0.08f * scale, WhiteUv, aw * 0.5f);
                    }
                    if (!shut) halos.Billboard(new Vector3(ux, FloorTop + sh * 0.45f, z - 1.2f), uw * 1.4f, sh * 1.8f, shop.Tint * 0.07f);
                }
            }

            // Facade kit: fire-escape balconies on a side, a drain pipe, roof water tank / antenna / rooftop billboard.
            if (detail > 0.6f)
            {
                if (R() < 0.45f)
                {
                    bool left = R() < 0.5f;
                    float bw = Mathf.Min(w * 0.45f, 3.2f * scale), bx = left ? x + bw * 0.5f + 0.3f : x + w - bw * 0.5f - 0.3f;
                    for (float by = FloorTop + 4.6f * scale; by < top - 2f; by += 2f * scale)
                    {
                        facades.Quad(new Vector3(bx, by, z - 0.6f), bw, 0.08f * scale, PlasterUv, dark);            // floor slat
                        facades.Quad(new Vector3(bx, by + 0.55f * scale, z - 0.62f), bw, 0.05f * scale, PlasterUv, dark); // rail
                        for (float rx = bx - bw * 0.5f; rx <= bx + bw * 0.5f + 0.01f; rx += 0.4f * scale)
                            facades.Quad(new Vector3(rx, by + 0.28f * scale, z - 0.62f), 0.04f * scale, 0.55f * scale, PlasterUv, dark);
                    }
                }
                if (R() < 0.6f)
                {
                    float px0 = R() < 0.5f ? x + 0.15f * scale : x + w - 0.15f * scale;
                    facades.Quad(new Vector3(px0, (FloorTop + sh0(scale) + top) * 0.5f, z - 0.08f), 0.12f * scale, top - FloorTop - sh0(scale), PlasterUv, dark * 1.6f);
                }
                double roof = R();
                if (roof < 0.3f)
                {
                    // Water tank on legs.
                    float tx = x + w * (0.25f + R() * 0.5f), tw = 1.6f * scale;
                    facades.Quad(new Vector3(tx, top + 1.6f * scale, z + 0.5f), tw, 1.6f * scale, PlasterUv, dark * 1.4f);
                    facades.Quad(new Vector3(tx - tw * 0.35f, top + 0.45f * scale, z + 0.5f), 0.08f * scale, 0.9f * scale, PlasterUv, dark);
                    facades.Quad(new Vector3(tx + tw * 0.35f, top + 0.45f * scale, z + 0.5f), 0.08f * scale, 0.9f * scale, PlasterUv, dark);
                }
                else if (roof < 0.55f)
                {
                    // Antenna mast with a red beacon.
                    float ax = x + w * (0.2f + R() * 0.6f), ah = (2.5f + R() * 3f) * scale;
                    facades.Quad(new Vector3(ax, top + ah * 0.5f, z + 0.3f), 0.07f * scale, ah, PlasterUv, dark);
                    signs.Quad(new Vector3(ax, top + ah, z + 0.25f), 0.18f * scale, 0.18f * scale, WhiteUv, new Color(1.4f, 0.08f, 0.06f));
                }
                else if (roof < 0.8f && w > 6f * scale)
                {
                    // Rooftop billboard on a scaffold frame (a brand; lit or dead).
                    var cell = PickBoard(rnd);
                    float bw2 = Mathf.Min(w * 0.75f, 8f * scale), bh2 = bw2 * 0.5f, by2 = top + 0.9f * scale + bh2 * 0.5f;
                    for (float lx = x + w * 0.5f - bw2 * 0.4f; lx <= x + w * 0.5f + bw2 * 0.4f + 0.01f; lx += bw2 * 0.4f)
                        facades.Quad(new Vector3(lx, top + 0.45f * scale, z + 0.2f), 0.08f * scale, 0.9f * scale, PlasterUv, dark);
                    bool deadBoard = R() < _look.DeadSigns * 1.5f;
                    boards.Quad(new Vector3(x + w * 0.5f, by2, z + 0.15f), bw2, bh2, cell.Uv, deadBoard ? new Color(0.14f, 0.12f, 0.13f) : Color.white * 0.95f);
                    if (!deadBoard) halos.Billboard(new Vector3(x + w * 0.5f, by2, z - 0.2f), bw2 * 1.5f, bh2 * 2.5f, cell.Tint * 0.1f);
                }
            }

            // Signs: blades along the building edges, boards across the floors, stacked from the shops to the roof.
            int n = Mathf.RoundToInt(detail * (9f + R() * 7f) * Mathf.Clamp(w / 7f, 0.6f, 1.6f));
            float yMin = FloorTop + 4f * scale, yMax = top - 1.5f * scale;
            for (int i = 0; i < n && yMax > yMin + 2f; i++)
            {
                bool bladeSign = R() < 0.55f;
                var cell = Pick(rnd, bladeSign ? 0 : 1);
                float sw, sh, cx;
                if (bladeSign)
                {
                    sw = (0.65f + R() * 0.4f) * scale;
                    sh = sw * 4f;
                    cx = R() < 0.7f ? (R() < 0.5f ? x + sw * 0.6f : x + w - sw * 0.6f) : x + sw + R() * Mathf.Max(0.1f, w - 2f * sw);
                }
                else
                {
                    sw = Mathf.Min(w - 0.8f * scale, (2f + R() * 2f) * scale);
                    sh = sw * 0.25f;
                    cx = x + w * 0.5f + (R() - 0.5f) * Mathf.Max(0f, w - sw - 0.4f);
                }
                float cy = yMin + sh * 0.5f + R() * Mathf.Max(0.1f, yMax - yMin - sh);
                float cz = z - (bladeSign ? 0.9f : 0.35f) * scale - i * 0.01f;
                if (bladeSign) // bracket to the wall
                    facades.Quad(new Vector3(cx, cy + sh * 0.5f + 0.12f * scale, cz + 0.4f * scale), sw * 0.9f, 0.12f * scale, PlasterUv, tint * 0.4f);
                if (R() < _look.DeadSigns)
                {
                    // Dead sign: the same panel, unlit and grimy, no halo (it casts no light).
                    var dead = new Color(0.16f, 0.13f, 0.15f, 1f) * (0.8f + R() * 0.5f);
                    signs.Quad(new Vector3(cx, cy, cz), sw, sh, cell.Uv, dead);
                    continue;
                }
                float bright = 0.85f + R() * 0.3f;   // around the bloom threshold: rich colour, little glow
                var target = R() < 0.15f ? blink : signs;
                target.Quad(new Vector3(cx, cy, cz), sw, sh, cell.Uv, Color.white * bright);
                halos.Billboard(new Vector3(cx, cy, cz - 0.3f), sw * 2.2f + 1.5f * scale, sh * 1.6f + 1.5f * scale, cell.Tint * (0.08f + 0.04f * bright));
            }
        }

        /// <summary>Height of the ground-floor shop band (shop front + header) at a row scale.</summary>
        private static float sh0(float scale) => 3.4f * scale;

        /// <summary>
        /// A sagging cable from <paramref name="a"/> to <paramref name="b"/> (segments of thin quads); with
        /// <paramref name="bulbs"/>, a string of coloured light bulbs hangs along it.
        /// </summary>
        private static void Cable(UvQuads q, Vector3 a, Vector3 b, float sag, float thick, Color col,
            UvQuads bulbs = null, Color[] palette = null, System.Random rnd = null, float scale = 1f)
        {
            int n = Mathf.Max(8, Mathf.RoundToInt((b - a).magnitude / (0.55f * scale)));
            Vector3 prev = a;
            for (int i = 1; i <= n; i++)
            {
                float t = i / (float)n;
                var p = Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4f * t * (1f - t));
                q.Line(prev, p, thick, PlasterUv, col);
                if (bulbs != null && palette != null && rnd != null && i < n)
                {
                    var bc = palette[rnd.Next(palette.Length)];
                    float s = 0.13f * scale;
                    bulbs.Quad(p + new Vector3(0f, -s * 0.8f, -0.02f), s, s, WhiteUv, bc * (rnd.NextDouble() < 0.12 ? 0.15f : 1f));
                }
                prev = p;
            }
        }

        /// <summary>
        /// The far city on the horizon (row <paramref name="index"/>, z 135): dark violet tower silhouettes of mixed
        /// height with sparse lit window dots and red aviation beacons, unlit and lightly fogged.
        /// </summary>
        private void FarSkyline(System.Random rnd, int index)
        {
            const float z = 135f;
            float period = Mathf.Ceil((PlaneDistance + z) * 1.2f / 8f) * 8f;
            var row = new GameObject("FarSkyline").transform;
            row.SetParent(_follow, false);
            _cityRows[index] = row;
            _cityPeriod[index] = period;
            var q = new UvQuads();
            float x = 0f;
            while (x < period - 3f)
            {
                float w = Mathf.Min(6f + (float)rnd.NextDouble() * 12f, period - x);
                float h = rnd.NextDouble() < 0.15 ? 34f + (float)rnd.NextDouble() * 12f : 12f + (float)rnd.NextDouble() * 18f;
                var body = new Color(0.12f, 0.09f, 0.22f) * (0.8f + (float)rnd.NextDouble() * 0.4f);
                body.a = 1f;
                q.Quad(new Vector3(x + w * 0.5f, (h - 30f) * 0.5f, z), w, h + 30f, WhiteUv, body);
                for (float wy = 2f; wy < h - 2f; wy += 2.2f)
                for (float wx = x + 1f; wx < x + w - 1f; wx += 1.8f)
                    if (rnd.NextDouble() < 0.22)
                    {
                        var wc = rnd.NextDouble() < 0.7 ? new Color(1f, 0.75f, 0.45f) : new Color(0.7f, 0.85f, 1f);
                        q.Quad(new Vector3(wx, wy, z - 0.1f), 0.7f, 0.9f, WhiteUv, wc * 0.75f);
                    }
                if (h > 30f) q.Quad(new Vector3(x + w * 0.5f, h + 0.6f, z - 0.1f), 0.9f, 0.9f, WhiteUv, new Color(1.4f, 0.1f, 0.08f));
                x += w + (rnd.NextDouble() < 0.3 ? 2f + (float)rnd.NextDouble() * 6f : 0f);
            }
            var mesh = Own(q.Build("farSkyline"));
            for (int c = -1; c <= 1; c++)
            {
                var inst = new GameObject("Strip " + c).transform;
                inst.SetParent(row, false);
                inst.localPosition = new Vector3(c * period, 0f, 0f);
                StreetPart("skyline", inst, mesh, _streetSignMat, true);
            }
        }

        private SignCell Pick(System.Random rnd, int kind)
        {
            int start = rnd.Next(_signCells.Count);
            for (int k = 0; k < _signCells.Count; k++)
            {
                var c = _signCells[(start + k) % _signCells.Count];
                if (c.Kind == kind) return c;
            }
            return _signCells[0];
        }

        /// <summary>UV rect of a plain plaster texel on the facade texture (flat facade-material strips and floor).</summary>
        private static readonly Rect PlasterUv = new Rect(2f / 128f, 4f / 128f, 0.5f / 128f, 0.5f / 128f);

        /// <summary>UV rect of the atlas's solid white texel block (for flat colour strips).</summary>
        private static readonly Rect WhiteUv = new Rect(508f / 512f, 508f / 512f, 2f / 512f, 2f / 512f);

        // ------------------------------------------------------------------------------------------------
        // Textures
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Tileable facade, 128 px = 8 units: plaster with grime, a 2-unit window grid (warm lit, cool dark, some with
        /// blinds), floor ledges every 3 units, and the odd AC unit. Point filtered to match the pixel art.
        /// </summary>
        private static Texture2D FacadeTexture(int seed)
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, name = "streetFacade" };
            var rnd = new System.Random(seed * 3 + 1);
            var px = new Color[N * N];
            for (int i = 0; i < px.Length; i++)
            {
                float n = (float)rnd.NextDouble() * 0.06f;
                px[i] = new Color(0.36f + n, 0.31f + n, 0.36f + n, 1f);
            }
            // Ledges every 3 units (48 px is not a divisor of 128, so use 32 px = 2 units per floor band + ledge).
            for (int y = 0; y < N; y += 32)
                for (int x = 0; x < N; x++) { px[y * N + x] = new Color(0.22f, 0.18f, 0.22f); px[((y + 1) % N) * N + x] = new Color(0.5f, 0.44f, 0.48f); }
            // Windows: 4 per tile horizontally (32 px pitch), 4 floors.
            for (int fy = 0; fy < 4; fy++)
            for (int fx = 0; fx < 4; fx++)
            {
                int x0 = fx * 32 + 6, y0 = fy * 32 + 8, ww = 20, wh = 18;
                double r = rnd.NextDouble();
                Color c = r < 0.5 ? new Color(1f, 0.78f, 0.48f) : r < 0.7 ? new Color(1f, 0.55f, 0.7f) : new Color(0.16f, 0.15f, 0.26f);
                for (int y = y0; y < y0 + wh; y++)
                for (int x = x0; x < x0 + ww; x++)
                {
                    var cc = c;
                    if (x == x0 || x == x0 + ww - 1 || y == y0 || y == y0 + wh - 1) cc = new Color(0.12f, 0.1f, 0.12f);
                    else if (x == x0 + ww / 2) cc *= 0.55f;                                  // mullion
                    else if (r < 0.7 && (y - y0) % 4 == 0 && rnd.NextDouble() < 0.5) cc *= 0.8f;   // blinds
                    cc.a = 1f;
                    px[y * N + x] = cc;
                }
                if (rnd.NextDouble() < 0.25) // AC unit under the window
                    for (int y = y0 - 6; y < y0 - 1; y++) for (int x = x0 + 2; x < x0 + 10; x++) px[y * N + x] = new Color(0.55f, 0.52f, 0.55f);
            }
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        /// <summary>
        /// Sign atlas 512x512: 32 vertical blades (32x128), 24 horizontal boards (128x32), 4 shop fronts (128x64) and a
        /// white block. Two sign styles: a filled colour panel with dark glyphs, and a dark panel with neon glyphs and
        /// border. Glyphs are procedural pseudo-Hangul built from strokes on a 5x5 grid.
        /// </summary>
        private static Texture2D SignAtlas(int seed, Color[] palette, List<SignCell> cells)
        {
            const int N = 512;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, true, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point, name = "streetSigns" };
            var px = new Color[N * N];
            var rnd = new System.Random(seed * 7 + 5);
            var extra = new[] { new Color(0.3f, 0.85f, 1f), new Color(0.4f, 1f, 0.55f), new Color(0.75f, 0.4f, 1f), new Color(1f, 0.55f, 0.15f) };
            Color PickColor()
            {
                if (palette != null && palette.Length > 0 && rnd.NextDouble() < 0.72) { var p = palette[rnd.Next(palette.Length)]; return new Color(Mathf.Clamp01(p.r / 1.4f), Mathf.Clamp01(p.g / 1.4f), Mathf.Clamp01(p.b / 1.4f), 1f); }
                return extra[rnd.Next(extra.Length)];
            }
            void Fill(int x0, int y0, int w, int h, Color c) { for (int y = y0; y < y0 + h; y++) for (int x = x0; x < x0 + w; x++) if (x >= 0 && y >= 0 && x < N && y < N) px[y * N + x] = c; }

            void Seg(int x0, int y0, int x1, int y1, int th, Color c)
            {
                int n = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));
                for (int i = 0; i <= n; i++)
                {
                    float t2 = n == 0 ? 0f : i / (float)n;
                    Fill(Mathf.RoundToInt(Mathf.Lerp(x0, x1, t2)), Mathf.RoundToInt(Mathf.Lerp(y0, y1, t2)), th, th, c);
                }
            }

            // One jamo-like consonant in the box (x, y, w, h), y up: ㄱ ㄴ ㄷ ㅁ ㅂ ㅅ ㅇ ㅈ ㄹ ㅎ shapes from thin strokes.
            void Consonant(int x, int y, int w, int h, int th, Color c)
            {
                int r = x + w - th, tp = y + h - th;
                switch (rnd.Next(10))
                {
                    case 0: Fill(x, tp, w, th, c); Fill(r, y, th, h, c); break;                                         // ㄱ
                    case 1: Fill(x, y, th, h, c); Fill(x, y, w, th, c); break;                                          // ㄴ
                    case 2: Fill(x, tp, w, th, c); Fill(x, y, th, h, c); Fill(x, y, w, th, c); break;                   // ㄷ
                    case 3: Fill(x, tp, w, th, c); Fill(x, y, w, th, c); Fill(x, y, th, h, c); Fill(r, y, th, h, c); break; // ㅁ
                    case 4: Fill(x, y, th, h, c); Fill(r, y, th, h, c); Fill(x, y + h / 2, w, th, c); Fill(x, y, w, th, c); break; // ㅂ
                    case 5: Seg(x + w / 2, tp, x, y, th, c); Seg(x + w / 2, tp, r, y, th, c); break;                     // ㅅ
                    case 6: { int cx = x + w / 2, cy = y + h / 2, rr = Mathf.Max(2, Mathf.Min(w, h) / 2 - th / 2); for (int a = 0; a < 32; a++) { float an = a / 32f * 6.283f; Fill(cx + Mathf.RoundToInt(Mathf.Cos(an) * rr) - th / 2, cy + Mathf.RoundToInt(Mathf.Sin(an) * rr) - th / 2, th, th, c); } break; } // ㅇ
                    case 7: Fill(x, tp, w, th, c); Seg(x + w / 2, tp, x, y, th, c); Seg(x + w / 2, tp - h / 3, r, y, th, c); break; // ㅈ
                    case 8: Fill(x, tp, w, th, c); Fill(r, y + h / 2, th, h / 2, c); Fill(x, y + h / 2, w, th, c); Fill(x, y, th, h / 2, c); Fill(x, y, w, th, c); break; // ㄹ
                    default: Fill(x + w / 4, tp, w / 2, th, c); Fill(x, tp - th * 2, w, th, c); { int cx = x + w / 2, cy = y + h / 3, rr = Mathf.Max(2, w / 3); for (int a = 0; a < 24; a++) { float an = a / 24f * 6.283f; Fill(cx + Mathf.RoundToInt(Mathf.Cos(an) * rr), cy + Mathf.RoundToInt(Mathf.Sin(an) * rr * 0.7f), th, th, c); } } break; // ㅎ
                }
            }

            // A Hangul-like syllable block: initial consonant + vowel (vertical ㅏㅓㅣ or horizontal ㅗㅜㅡ) + optional final.
            void Glyph(int gx, int gy, int size, Color c)
            {
                int th = Mathf.Max(1, Mathf.RoundToInt(size / 9f));
                bool final = rnd.NextDouble() < 0.45;
                int fh = final ? size * 2 / 5 : 0;                     // final consonant band at the bottom
                int by = gy + fh + (final ? th : 0), bh = size - fh - (final ? th : 0);
                if (rnd.NextDouble() < 0.6)
                {
                    // Vertical vowel on the right.
                    int cw = size * 3 / 5;
                    Consonant(gx, by + bh / 8, cw - th, bh * 3 / 4, th, c);
                    int vx = gx + size - th * 2;
                    Fill(vx, by, th, bh, c);
                    int tick = rnd.Next(3);
                    if (tick == 0) Fill(vx, by + bh / 2, th * 3, th, c);             // ㅏ
                    else if (tick == 1) Fill(vx - th * 2, by + bh / 2, th * 2, th, c); // ㅓ
                }
                else
                {
                    // Horizontal vowel under the consonant.
                    int ch = bh * 3 / 5;
                    Consonant(gx + size / 6, by + bh - ch, size * 2 / 3, ch - th, th, c);
                    int vy = by + th;
                    Fill(gx, vy, size, th, c);
                    int tick = rnd.Next(3);
                    if (tick == 0) Fill(gx + size / 2 - th / 2, vy, th, th * 3, c);       // ㅗ
                    else if (tick == 1) Fill(gx + size / 2 - th / 2, vy - th * 2, th, th * 2, c); // ㅜ
                }
                if (final) Consonant(gx + size / 6, gy, size * 2 / 3, fh, th, c);
            }

            void Sign(int x0, int y0, int w, int h, bool vertical)
            {
                var col = PickColor();
                bool filled = rnd.NextDouble() < 0.55;
                var bg = filled ? col : new Color(0.07f, 0.05f, 0.09f);
                var ink = filled ? (rnd.NextDouble() < 0.6 ? new Color(0.08f, 0.04f, 0.08f) : Color.white) : col;
                var border = filled ? Color.Lerp(col, Color.white, 0.6f) : col;
                Fill(x0, y0, w, h, border);
                Fill(x0 + 2, y0 + 2, w - 4, h - 4, bg);
                if (!filled && rnd.NextDouble() < 0.5) Fill(x0 + 4, y0 + 4, w - 8, h - 8, Color.Lerp(bg, col, 0.12f));
                int g = vertical ? w - 10 : h - 10;
                g = Mathf.Max(5, g);
                if (vertical)
                    for (int y = y0 + h - 6 - g; y >= y0 + 4; y -= g + 4) Glyph(x0 + (w - g) / 2, y, g, ink);
                else
                    for (int x = x0 + 6; x + g <= x0 + w - 5; x += g + 4) Glyph(x, y0 + (h - g) / 2, g, ink);
                cells.Add(new SignCell { Uv = new Rect(x0 / (float)N, y0 / (float)N, w / (float)N, h / (float)N), Kind = vertical ? 0 : 1, Tint = filled ? col : col * 0.6f });
            }

            for (int i = 0; i < N * N; i++) px[i] = new Color(0f, 0f, 0f, 1f);
            for (int row = 0; row < 2; row++) for (int c = 0; c < 16; c++) Sign(c * 32, row * 128, 32, 128, true);
            for (int row = 0; row < 6; row++) for (int c = 0; c < 4; c++) Sign(c * 128, 256 + row * 32, 128, 32, false);
            // Shop fronts, 64x64 each (one 3.6-unit shop): 0 store, 1 bar, 2 convenience, 3 restaurant, 4 shutter
            // down, 5 dark closed, 6 glass door. Tint (halo / "is shut") follows the kind: shut kinds are dark.
            var frame = new Color(0.1f, 0.08f, 0.1f);
            for (int c = 0; c < 7; c++)
            {
                int x0 = c * 64, y0 = 448, kind = c;
                Color lo, hi, tint;
                switch (kind)
                {
                    case 0: lo = new Color(1f, 0.62f, 0.32f); hi = new Color(1f, 0.92f, 0.7f); tint = new Color(1f, 0.75f, 0.45f); break;
                    case 1: lo = new Color(0.55f, 0.08f, 0.35f); hi = new Color(1f, 0.35f, 0.7f); tint = new Color(1f, 0.3f, 0.6f); break;
                    case 2: lo = new Color(0.75f, 0.9f, 0.95f); hi = new Color(0.95f, 1f, 1f); tint = new Color(0.7f, 0.95f, 1f); break;
                    case 3: lo = new Color(0.6f, 0.12f, 0.08f); hi = new Color(1f, 0.45f, 0.25f); tint = new Color(1f, 0.35f, 0.2f); break;
                    case 4: lo = new Color(0.32f, 0.3f, 0.34f); hi = new Color(0.42f, 0.4f, 0.44f); tint = new Color(0.2f, 0.2f, 0.2f); break;
                    case 5: lo = new Color(0.05f, 0.05f, 0.08f); hi = new Color(0.1f, 0.1f, 0.14f); tint = new Color(0.1f, 0.1f, 0.1f); break;
                    default: lo = new Color(0.9f, 0.7f, 0.45f); hi = new Color(1f, 0.85f, 0.6f); tint = new Color(1f, 0.8f, 0.55f); break;
                }
                for (int y = 0; y < 64; y++)
                {
                    var g2 = Color.Lerp(lo, hi, y / 63f);
                    for (int x = 0; x < 64; x++) px[(y0 + y) * N + x0 + x] = g2;
                }
                if (kind == 4)
                {
                    for (int y = 2; y < 62; y += 4) Fill(x0, y0 + y, 64, 1, new Color(0.2f, 0.19f, 0.22f));   // shutter ribs
                    for (int k = 0; k < 3; k++) { var tag = PickColor(); Fill(x0 + 6 + rnd.Next(44), y0 + 8 + rnd.Next(30), 6 + rnd.Next(10), 3 + rnd.Next(5), tag * 0.6f); } // graffiti
                }
                else if (kind != 5)
                {
                    // Shelves / tables / customers as silhouettes, window frame, door.
                    var sil = Color.Lerp(lo, Color.black, 0.65f);
                    for (int k = 0; k < 5; k++) Fill(x0 + 4 + rnd.Next(52), y0 + 3, 3 + rnd.Next(7), 8 + rnd.Next(16), sil);
                    if (kind == 0 || kind == 2) for (int y = 14; y < 50; y += 12) Fill(x0 + 3, y0 + y, 58, 1, sil);
                    if (kind == 3) for (int k = 0; k < 3; k++) { int lx = x0 + 10 + k * 20; Fill(lx, y0 + 46, 6, 8, new Color(1f, 0.25f, 0.15f)); Fill(lx + 2, y0 + 54, 2, 4, frame); } // lanterns
                    Fill(x0, y0, 64, 2, frame); Fill(x0, y0 + 62, 64, 2, frame); Fill(x0, y0, 2, 64, frame); Fill(x0 + 62, y0, 2, 64, frame);
                    int dx = kind == 6 ? 22 : (rnd.Next(2) == 0 ? 6 : 40);
                    Fill(x0 + dx, y0, 18, 44, Color.Lerp(lo, Color.black, kind == 6 ? 0.2f : 0.45f)); Fill(x0 + dx, y0 + 44, 18, 2, frame); Fill(x0 + dx + 8, y0, 2, 44, frame);
                }
                cells.Add(new SignCell { Uv = new Rect(x0 / (float)N, y0 / (float)N, 64f / N, 64f / N), Kind = 2, Tint = tint });
            }
            Fill(504, 504, 8, 8, Color.white);
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        // ------------------------------------------------------------------------------------------------
        // Mesh helper: quads with arbitrary UV rects and HDR vertex colours
        // ------------------------------------------------------------------------------------------------

        private sealed class UvQuads
        {
            private readonly List<Vector3> _v = new List<Vector3>();
            private readonly List<Vector3> _n = new List<Vector3>();
            private readonly List<Vector2> _u = new List<Vector2>();
            private readonly List<Color> _c = new List<Color>();
            private readonly List<int> _t = new List<int>();

            public int Count => _v.Count / 4;

            /// <summary>Camera-facing quad (normal -z) centred at <paramref name="c"/>.</summary>
            public void Quad(Vector3 c, float w, float h, Rect uv, Color col)
            {
                Add(c + new Vector3(-w / 2, -h / 2, 0), c + new Vector3(-w / 2, h / 2, 0), c + new Vector3(w / 2, h / 2, 0), c + new Vector3(w / 2, -h / 2, 0), Vector3.back, uv, col);
            }

            /// <summary>Thin camera-facing strip from <paramref name="a"/> to <paramref name="b"/> (in the XY plane at their z).</summary>
            public void Line(Vector3 a, Vector3 b, float thick, Rect uv, Color col)
            {
                var d = (Vector2)(b - a);
                if (d.sqrMagnitude < 1e-8f) return;
                var n = new Vector3(-d.y, d.x, 0f).normalized * (thick * 0.5f);
                Add(a - n, a + n, b + n, b - n, Vector3.back, uv, col);
            }

            /// <summary>Floor quad (normal +y) centred at <paramref name="c"/>, w (x) by d (z).</summary>
            public void Floor(Vector3 c, float w, float d, Rect uv, Color col)
            {
                Add(c + new Vector3(-w / 2, 0, -d / 2), c + new Vector3(-w / 2, 0, d / 2), c + new Vector3(w / 2, 0, d / 2), c + new Vector3(w / 2, 0, -d / 2), Vector3.up, uv, col);
            }

            private void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Rect uv, Color col)
            {
                int i = _v.Count;
                _v.Add(a); _v.Add(b); _v.Add(c); _v.Add(d);
                for (int k = 0; k < 4; k++) { _n.Add(n); _c.Add(col); }
                _u.Add(new Vector2(uv.xMin, uv.yMin)); _u.Add(new Vector2(uv.xMin, uv.yMax)); _u.Add(new Vector2(uv.xMax, uv.yMax)); _u.Add(new Vector2(uv.xMax, uv.yMin));
                _t.Add(i); _t.Add(i + 1); _t.Add(i + 2); _t.Add(i); _t.Add(i + 2); _t.Add(i + 3);
            }

            public Mesh Build(string name)
            {
                var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                m.SetVertices(_v); m.SetNormals(_n); m.SetUVs(0, _u); m.SetColors(_c); m.SetTriangles(_t, 0);
                m.RecalculateBounds();
                return m;
            }
        }
    }
}
