using System.Collections.Generic;
using Redline.Core;
using Redline.Game.Render3D.Actors3D;
using UnityEngine;

namespace Redline.Game
{
    /// <summary>
    /// Draws the shock-floor tiles ('E'): a dark metal grate strip per tile with a cyan arc on top that crackles
    /// while <see cref="RedlineSim.ShockLive"/> and dims to a faint warning glow while the current is off.
    /// </summary>
    public sealed class ShockFloorView
    {
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ParamsId = Shader.PropertyToID("_Params");
        private static readonly Color Live = new Color(0.35f, 0.85f, 1f) * 3.2f;
        private static readonly Color Idle = new Color(0.35f, 0.85f, 1f) * 0.35f;

        private readonly Transform _root;
        private readonly List<MeshRenderer> _grates = new List<MeshRenderer>();
        private readonly List<MeshRenderer> _arcs = new List<MeshRenderer>();
        private readonly List<Vector2> _cells = new List<Vector2>();
        private readonly MaterialPropertyBlock _mpb = new MaterialPropertyBlock();

        public ShockFloorView(Transform parent, TileMap map)
        {
            _root = new GameObject("ShockFloor").transform;
            _root.SetParent(parent, false);
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                if (map.Get(x, y) != Tile.Shock) continue;
                _cells.Add(new Vector2(x, y));
                var g = Actors3DAssets.CreateQuad("grate", _root, Actors3DAssets.FxSolid, false, false);
                g.transform.localPosition = new Vector3(x + 0.5f, y + 0.06f, 0f);
                g.transform.localScale = new Vector3(1f, 0.12f, 1f);
                Set(g, new Color(0.08f, 0.09f, 0.12f), 0f);
                g.enabled = true;
                _grates.Add(g);
                var a = Actors3DAssets.CreateQuad("arc", _root, Actors3DAssets.FxAdditive, false, false);
                a.transform.localPosition = new Vector3(x + 0.5f, y + 0.22f, -0.02f);
                a.transform.localScale = new Vector3(1.05f, 0.42f, 1f);
                a.enabled = true;
                _arcs.Add(a);
            }
        }

        /// <summary>Crackle while live (random flicker per tile), faint warning glow while off.</summary>
        public void Sync(RedlineSim sim, float time)
        {
            bool live = sim != null && sim.ShockLive;
            for (int i = 0; i < _arcs.Count; i++)
            {
                float flick = live ? 0.55f + 0.45f * Mathf.PerlinNoise(time * 22f, i * 3.1f) : 0.6f + 0.4f * Mathf.Sin(time * 3f + i);
                Set(_arcs[i], (live ? Live : Idle) * flick, 1f);
                float h = live ? 0.3f + 0.25f * Mathf.PerlinNoise(i * 1.7f, time * 15f) : 0.18f;
                var t = _arcs[i].transform;
                t.localScale = new Vector3(1.05f, h, 1f);
                t.localPosition = new Vector3(_cells[i].x + 0.5f, _cells[i].y + 0.08f + h * 0.5f, -0.02f);
            }
        }

        private void Set(MeshRenderer r, Color c, float shape)
        {
            _mpb.Clear();
            _mpb.SetColor(ColorId, new Color(c.r, c.g, c.b, 1f));
            _mpb.SetVector(ParamsId, new Vector4(shape, 0.2f, 0f, 0f));
            r.SetPropertyBlock(_mpb);
        }

        public void Dispose()
        {
            if (_root != null) Object.Destroy(_root.gameObject);
        }
    }
}
