using UnityEngine;
using C = Redline.Game.Atmosphere.AtmosphereConfig;

namespace Redline.Game.Atmosphere
{
    /// <summary>
    /// The night-city backdrop: a sky gradient, four parallax skyline layers fogged by distance,
    /// haze bands drifting between them, blinking aviation beacons on distant towers, a few hover
    /// lights crossing the second layer, and a low ground-fog band in front of the terrain.
    /// </summary>
    /// <example>
    /// <code>
    /// var city = new CitySkyline(backdropRoot, levelHeight: map.Height);
    /// city.Update(camera.transform.position, Time.deltaTime);   // every frame
    /// </code>
    /// </example>
    /// <remarks>
    /// Main-thread only. Everything is unlit (the sky and the far city are emissive or fog; lights
    /// would only darken them). Update does not allocate. About 30 sprites in total.
    /// </remarks>
    public sealed class CitySkyline
    {
        private const float SkyWidth = C.ViewHalfWidth * 2f + 4f;
        private const float SkyHeight = C.ViewHalfHeight * 2f + 4f;
        private const float FillDepth = 30f;
        private const float BeaconSize = 0.45f;
        private const float TrafficSize = 0.22f;
        private const float TrafficBandLow = 0.45f, TrafficBandHigh = 0.85f;

        private readonly Transform _sky;
        private readonly ParallaxStrip[] _layers = new ParallaxStrip[C.CityLayers];
        private readonly Transform[] _fills = new Transform[C.CityLayers];
        private readonly ParallaxStrip[] _haze = new ParallaxStrip[C.CityLayers];
        private readonly ParallaxStrip _groundFog;
        private readonly Transform _foundation;
        private readonly SpriteRenderer[] _beacons;
        private readonly int[] _beaconLayer;
        private readonly Vector2[] _beaconPos;
        private readonly SpriteRenderer[] _traffic = new SpriteRenderer[C.TrafficCount];
        private readonly float[] _trafficX = new float[C.TrafficCount];
        private readonly float[] _trafficY = new float[C.TrafficCount];
        private readonly float[] _trafficV = new float[C.TrafficCount];
        private float _time;

        /// <summary>Builds the backdrop under <paramref name="root"/> (which must sit at the world origin).</summary>
        public CitySkyline(Transform root, int levelHeight)
        {
            float mid = levelHeight * 0.5f;
            _sky = SpriteFactory.Create("Sky", root, AtmoArt.Sky, Color.white, C.SkyOrder).transform;
            var skyUnit = AtmoArt.Sky.bounds.size;
            _sky.localScale = new Vector3(SkyWidth / skyUnit.x, SkyHeight / skyUnit.y, 1f);

            int beaconCount = 0;
            for (int i = 0; i < C.CityLayers; i++)
            {
                var art = AtmoArt.City(i);
                _layers[i] = new ParallaxStrip(root, "City" + i, art.Sprite, art.Period, C.LayerFollowX[i],
                    C.LayerFollowY[i], C.LayerBaseY[i], mid, C.LayerOrder[i], Color.white);
                // Solid haze below each layer's base, so a camera low in the level never sees past it.
                var fill = SpriteFactory.Create("CityFill" + i, root, SpriteFactory.Square, AtmoArt.CityBaseColor(i), C.LayerOrder[i]);
                fill.transform.localScale = new Vector3(SkyWidth, FillDepth, 1f);
                _fills[i] = fill.transform;
                // A drifting haze band in front of each layer, sitting on its base.
                var fogColor = C.FogColor;
                fogColor.a = C.HazeAlpha[i];
                float fogScaleY = (C.LayerHeight[i] * 0.55f) / AtmoArt.FogUnitHeight;
                _haze[i] = new ParallaxStrip(root, "Haze" + i, AtmoArt.Fog, C.FogPeriod, C.LayerFollowX[i],
                    C.LayerFollowY[i], C.LayerBaseY[i] - C.LayerHeight[i] * 0.1f, mid, C.LayerOrder[i] + 1, fogColor,
                    new Vector3(1f, fogScaleY, 1f), C.HazeDrift[i]);
                beaconCount += art.Beacons.Length;
            }

            // One sprite per beacon: ToWorldX picks whichever strip copy shows it.
            _beacons = new SpriteRenderer[beaconCount];
            _beaconLayer = new int[beaconCount];
            _beaconPos = new Vector2[beaconCount];
            int k = 0;
            for (int i = 0; i < C.CityLayers; i++)
            {
                var art = AtmoArt.City(i);
                for (int b = 0; b < art.Beacons.Length; b++, k++)
                {
                    _beacons[k] = SpriteFactory.Create("Beacon", root, PixelArt.Glow, C.BeaconRed, C.LayerOrder[i]);
                    _beacons[k].transform.localScale = new Vector3(BeaconSize, BeaconSize, 1f);
                    _beaconLayer[k] = i;
                    _beaconPos[k] = art.Beacons[b];
                }
            }

            var rng = new AtmoArt.Rng(4242);
            for (int i = 0; i < C.TrafficCount; i++)
            {
                _traffic[i] = SpriteFactory.Create("HoverLight", root, PixelArt.Glow, C.TrafficLight, C.LayerOrder[1]);
                _traffic[i].transform.localScale = new Vector3(TrafficSize * 1.6f, TrafficSize, 1f);
                _trafficX[i] = rng.Range(0f, C.LayerPeriod[1]);
                _trafficY[i] = rng.Range(TrafficBandLow, TrafficBandHigh) * C.LayerHeight[1];
                float speed = rng.Range(C.TrafficSpeedMin, C.TrafficSpeedMax);
                _trafficV[i] = rng.Value() < 0.5f ? speed : -speed;
            }

            // Dark foundation under the level (y < 0): the near-black structure the level stands on.
            var foundation = SpriteFactory.Create("Foundation", root, SpriteFactory.Square, C.Foundation, C.FoundationOrder);
            foundation.transform.localScale = new Vector3(SkyWidth, FillDepth, 1f);
            _foundation = foundation.transform;

            var ground = C.FogColor;
            ground.a = C.GroundFogAlpha;
            _groundFog = new ParallaxStrip(root, "GroundFog", AtmoArt.Fog, C.FogPeriod, 0f, 0f,
                -C.GroundFogHeight * 0.5f + 1f - mid, mid, C.GroundFogOrder, ground,
                new Vector3(1f, C.GroundFogHeight / AtmoArt.FogUnitHeight, 1f), C.GroundFogDrift);
        }

        /// <summary>Moves every layer for the camera and animates beacons, hover lights and fog.</summary>
        public void Update(Vector3 camera, float dt)
        {
            _time += dt;
            _sky.localPosition = new Vector3(camera.x, camera.y, 0f);
            for (int i = 0; i < C.CityLayers; i++)
            {
                _layers[i].Update(camera, dt);
                _haze[i].Update(camera, dt);
                _fills[i].localPosition = new Vector3(camera.x, _layers[i].Bottom - FillDepth * 0.5f + 0.01f, 0f);
            }
            _groundFog.Update(camera, dt);
            _foundation.localPosition = new Vector3(camera.x, -FillDepth * 0.5f, 0f);

            for (int k = 0; k < _beacons.Length; k++)
            {
                var strip = _layers[_beaconLayer[k]];
                float x = strip.ToWorldX(_beaconPos[k].x);
                bool on = !float.IsNaN(x);
                _beacons[k].enabled = on;
                if (!on) continue;
                float phase = Mathf.Repeat(_time / C.BeaconPeriod + k * 0.37f, 1f);
                var c = C.BeaconRed;
                c.a *= phase < 0.18f ? 1f : 0.12f;
                _beacons[k].color = c;
                _beacons[k].transform.localPosition = new Vector3(x, strip.Bottom + _beaconPos[k].y, 0f);
            }

            var layer1 = _layers[1];
            for (int i = 0; i < C.TrafficCount; i++)
            {
                _trafficX[i] = Mathf.Repeat(_trafficX[i] + _trafficV[i] * dt, layer1.Period);
                float x = layer1.ToWorldX(_trafficX[i]);
                bool on = !float.IsNaN(x);
                _traffic[i].enabled = on;
                if (!on) continue;
                float bob = Mathf.Sin(_time * 0.7f + i * 1.9f) * 0.15f;
                _traffic[i].transform.localPosition = new Vector3(x, layer1.Bottom + _trafficY[i] + bob, 0f);
            }
        }
    }
}
