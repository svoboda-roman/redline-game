using System;

namespace Redline.Game.Audio
{
    /// <summary>
    /// Generates every Redline sound effect as mono PCM, from code only (no audio assets).
    /// Neon/synth character per design/game-brief.md: band-limited saw and pulse tones, filtered
    /// noise sweeps, pitch-dropping sine thumps. Pure C# so it can also be rendered offline.
    /// </summary>
    /// <remarks>Called once at startup; each call allocates the returned buffer and nothing else.</remarks>
    public static class SynthClips
    {
        /// <summary>
        /// Renders one sound at <paramref name="rate"/> Hz, normalised to <see cref="AudioMix.ClipPeak"/>
        /// with short edge fades so it never clicks. Returns an empty buffer for ids without a sound.
        /// </summary>
        public static float[] Render(SfxId id, int rate)
        {
            float[] buf;
            switch (id)
            {
                case SfxId.Swing: buf = Swing(rate); break;
                case SfxId.Hit1: buf = Hit(rate, 1); break;
                case SfxId.Hit2: buf = Hit(rate, 2); break;
                case SfxId.Hit3: buf = Hit(rate, 3); break;
                case SfxId.Kill1: buf = Kill(rate, 1); break;
                case SfxId.Kill2: buf = Kill(rate, 2); break;
                case SfxId.Kill3: buf = Kill(rate, 3); break;
                case SfxId.Clang: buf = Clang(rate); break;
                case SfxId.HookAttach: buf = HookAttach(rate); break;
                case SfxId.HookRelease: buf = HookRelease(rate); break;
                case SfxId.Jump: buf = Jump(rate); break;
                case SfxId.WallJump: buf = WallJump(rate); break;
                case SfxId.Land: buf = Land(rate); break;
                case SfxId.Slide: buf = Slide(rate); break;
                case SfxId.Hurt: buf = Hurt(rate); break;
                case SfxId.WaveStart: buf = WaveStart(rate); break;
                case SfxId.Victory: buf = Victory(rate); break;
                case SfxId.GameOver: buf = GameOver(rate); break;
                case SfxId.BruteWindup: buf = BruteWindup(rate); break;
                case SfxId.BruteCrash: buf = BruteCrash(rate); break;
                case SfxId.DroneWindup: buf = DroneWindup(rate); break;
                case SfxId.OrbSpawn: buf = OrbSpawn(rate); break;
                case SfxId.OrbPickup: buf = OrbPickup(rate); break;
                case SfxId.OrbExpire: buf = OrbExpire(rate); break;
                default: return new float[0];
            }
            Finish(buf, rate);
            return buf;
        }

        // ------------------------------------------------------------------ combat

        /// <summary>Sword swoosh: a band-pass noise sweep up and back down, with a faint neon edge.</summary>
        private static float[] Swing(int rate)
        {
            const float len = 0.17f;
            var buf = Alloc(len, rate);
            var rng = new Rng(11);
            var f = new Svf();
            var o = new Osc();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate, u = t / len;
                float arc = MathF.Sin(MathF.PI * MathF.Min(1f, u * 1.15f));
                f.Process(rng.Next(), 700f + 5200f * arc * arc, 0.55f, rate);
                float edge = o.Saw(Dsp.Glide(500f, 1400f, t, 0.06f), rate) * 0.06f;
                buf[i] = (f.Band + edge) * MathF.Pow(MathF.Max(0f, arc), 0.8f);
            }
            return buf;
        }

        /// <summary>Strike landing: pitch-dropping thump, a noise click and a grit layer; more drive per tier.</summary>
        private static float[] Hit(int rate, int tier)
        {
            float len = 0.12f + 0.05f * tier;
            var buf = Alloc(len, rate);
            var rng = new Rng((uint)(100 + tier));
            var body = new Osc();
            var grit = new Osc();
            var hp = new Svf();
            var lp = new Svf();
            float drive = 1.2f + 0.9f * tier;
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate;
                float hz = Dsp.Glide(170f + 50f * tier, 55f, t, 0.035f);
                float thump = body.Sine(hz, rate) * Dsp.Decay(t, 0.045f + 0.02f * tier);
                hp.Process(rng.Next(), 3500f, 0.9f, rate);
                float click = hp.High * Dsp.Decay(t, 0.007f) * 0.7f;
                float g = lp.Process(grit.Square(hz * 2.01f, rate, 0.5f), 2200f + 800f * tier, 0.8f, rate);
                float gritAmp = 0.12f * tier * Dsp.Decay(t, 0.03f);
                buf[i] = Dsp.SoftClip((thump + click + g * gritAmp) * drive);
            }
            return buf;
        }

        /// <summary>Kill: downward saw zap, sub boom and noise burst; tier 3 adds a crushed sparkle.</summary>
        private static float[] Kill(int rate, int tier)
        {
            float len = 0.3f + 0.1f * tier;
            var buf = Alloc(len, rate);
            var rng = new Rng((uint)(200 + tier));
            var zapA = new Osc();
            var zapB = new Osc();
            var boom = new Osc();
            var s1 = new Osc();
            var s2 = new Osc();
            var zf = new Svf();
            var nf = new Svf();
            float drive = 1.4f + 0.6f * tier;
            float held = 0f;
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate;
                float zhz = Dsp.Glide(1100f + 300f * tier, 70f, t, 0.06f);
                float zap = 0.5f * (zapA.Saw(zhz, rate) + zapB.Saw(zhz * 1.012f, rate));
                zap = zf.Process(zap, Dsp.Glide(7000f, 350f, t, 0.08f), 0.5f, rate) * Dsp.Decay(t, 0.1f + 0.02f * tier);
                float b = boom.Sine(Dsp.Glide(95f, 38f, t, 0.08f), rate) * Dsp.AttackDecay(t, 0.002f, 0.1f + 0.05f * tier) * (0.7f + 0.2f * tier);
                float nz = nf.Process(rng.Next(), Dsp.Glide(4000f, 400f, t, 0.05f), 0.9f, rate) * Dsp.Decay(t, 0.05f + 0.02f * tier) * 0.8f;
                float x = Dsp.SoftClip((zap * 0.8f + b + nz) * drive);
                if (tier >= 3)
                {
                    float sparkle = (s1.Sine(1760f, rate) + 0.7f * s2.Sine(2637f, rate)) * Dsp.AttackDecay(t, 0.01f, 0.12f) * 0.22f;
                    // Sample-and-hold every 3 samples and 12-level quantise: a little bit-crush grit.
                    if (i % 3 == 0) held = MathF.Round(x * 6f) / 6f;
                    x = 0.6f * x + 0.4f * held + sparkle;
                }
                buf[i] = x;
            }
            return buf;
        }

        /// <summary>Armour clang: inharmonic metallic partials over a bright transient.</summary>
        private static float[] Clang(int rate)
        {
            const float len = 0.55f;
            var buf = Alloc(len, rate);
            var rng = new Rng(300);
            var p0 = new Osc();
            var p1 = new Osc();
            var p2 = new Osc();
            var p3 = new Osc();
            var p4 = new Osc();
            var tf = new Svf();
            const float baseHz = 540f;
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate;
                float ring =
                    p0.Sine(baseHz, rate) * Dsp.Decay(t, 0.34f) +
                    p1.Sine(baseHz * 2.32f, rate) * 0.7f * Dsp.Decay(t, 0.24f) +
                    p2.Sine(baseHz * 3.86f, rate) * 0.5f * Dsp.Decay(t, 0.17f) +
                    p3.Sine(baseHz * 5.41f, rate) * 0.35f * Dsp.Decay(t, 0.11f) +
                    p4.Sine(baseHz * 7.13f, rate) * 0.25f * Dsp.Decay(t, 0.08f);
                tf.Process(rng.Next(), 4500f, 0.4f, rate);
                float hitT = tf.Band * Dsp.Decay(t, 0.008f) * 0.8f;
                buf[i] = Dsp.SoftClip((ring * 0.6f + hitT) * 1.3f) * MathF.Min(1f, t / 0.0008f);
            }
            return buf;
        }

        // ------------------------------------------------------------------ hook and movement

        /// <summary>Hook attach: a fast rising pulse zip over a low thunk.</summary>
        private static float[] HookAttach(int rate)
        {
            const float len = 0.15f;
            var buf = Alloc(len, rate);
            var zip = new Osc();
            var thunk = new Osc();
            var lp = new Svf();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate;
                float zhz = Dsp.Glide(2600f, 600f, 0.05f - MathF.Min(t, 0.05f), 0.02f);
                float z = lp.Process(zip.Square(zhz, rate, 0.3f), 5000f, 0.7f, rate) * Dsp.AttackDecay(t, 0.002f, 0.03f) * 0.6f;
                float th = thunk.Sine(Dsp.Glide(160f, 70f, t, 0.03f), rate) * Dsp.AttackDecay(t, 0.002f, 0.05f);
                buf[i] = Dsp.SoftClip((z + th) * 1.3f);
            }
            return buf;
        }

        /// <summary>Hook release: an airy noise whoosh opening up, with a faint rising whistle.</summary>
        private static float[] HookRelease(int rate)
        {
            const float len = 0.3f;
            var buf = Alloc(len, rate);
            var rng = new Rng(400);
            var f = new Svf();
            var w = new Osc();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate, u = t / len;
                f.Process(rng.Next(), 300f + 3500f * u, 0.7f, rate);
                float amp = MathF.Pow(MathF.Sin(MathF.PI * u), 0.8f);
                buf[i] = (f.Band + w.Sine(400f + 600f * u, rate) * 0.08f) * amp;
            }
            return buf;
        }

        /// <summary>Jump: a short rising pulse blip.</summary>
        private static float[] Jump(int rate)
        {
            const float len = 0.11f;
            var buf = Alloc(len, rate);
            var o = new Osc();
            var lp = new Svf();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate;
                float hz = Dsp.Glide(320f, 720f, t, 0.03f);
                buf[i] = lp.Process(o.Square(hz, rate, 0.25f), 3200f, 0.9f, rate) * Dsp.AttackDecay(t, 0.003f, 0.045f);
            }
            return buf;
        }

        /// <summary>Wall jump: two quick rising blips and a tick off the wall.</summary>
        private static float[] WallJump(int rate)
        {
            const float len = 0.16f;
            var buf = Alloc(len, rate);
            var rng = new Rng(500);
            var a = new Osc();
            var b = new Osc();
            var lp = new Svf();
            var hp = new Svf();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate;
                float first = a.Square(Dsp.Glide(440f, 880f, t, 0.025f), rate, 0.3f) * Dsp.AttackDecay(t, 0.002f, 0.03f);
                float t2 = t - 0.05f;
                float second = t2 > 0f ? b.Square(Dsp.Glide(660f, 1320f, t2, 0.025f), rate, 0.3f) * Dsp.AttackDecay(t2, 0.002f, 0.04f) : 0f;
                hp.Process(rng.Next(), 5000f, 0.9f, rate);
                float tick = hp.High * Dsp.Decay(t, 0.006f) * 0.5f;
                buf[i] = lp.Process(first + second, 4000f, 0.9f, rate) + tick;
            }
            return buf;
        }

        /// <summary>Landing: a soft low thud with a little grit.</summary>
        private static float[] Land(int rate)
        {
            const float len = 0.13f;
            var buf = Alloc(len, rate);
            var rng = new Rng(600);
            var o = new Osc();
            var lp = new Svf();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate;
                float thud = o.Sine(Dsp.Glide(120f, 48f, t, 0.03f), rate) * Dsp.AttackDecay(t, 0.002f, 0.04f);
                float grit = lp.Process(rng.Next(), 900f, 1f, rate) * Dsp.Decay(t, 0.03f) * 0.5f;
                buf[i] = Dsp.SoftClip((thud + grit) * 1.4f);
            }
            return buf;
        }

        /// <summary>Slide: a band-passed scrape with a fast flutter, falling in pitch.</summary>
        private static float[] Slide(int rate)
        {
            const float len = 0.34f;
            var buf = Alloc(len, rate);
            var rng = new Rng(700);
            var f = new Svf();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate, u = t / len;
                f.Process(rng.Next(), Dsp.Glide(2600f, 1100f, t, 0.15f), 0.35f, rate);
                float flutter = 1f - 0.3f * (0.5f + 0.5f * MathF.Sin(Dsp.TwoPi * 32f * t));
                float amp = MathF.Min(1f, t / 0.02f) * (1f - u * u);
                buf[i] = f.Band * flutter * amp;
            }
            return buf;
        }

        // ------------------------------------------------------------------ player and run state

        /// <summary>Player hurt: a harsh detuned saw drop with buzz.</summary>
        private static float[] Hurt(int rate)
        {
            const float len = 0.38f;
            var buf = Alloc(len, rate);
            var rng = new Rng(800);
            var a = new Osc();
            var b = new Osc();
            var lp = new Svf();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate;
                float hz = Dsp.Glide(240f, 95f, t, 0.12f);
                float x = 0.5f * (a.Saw(hz, rate) + b.Saw(hz * 1.06f, rate)) + rng.Next() * 0.25f;
                x = lp.Process(x, Dsp.Glide(3500f, 500f, t, 0.12f), 0.6f, rate);
                float buzz = 1f - 0.4f * (0.5f + 0.5f * MathF.Sin(Dsp.TwoPi * 28f * t));
                buf[i] = Dsp.SoftClip(x * 3f) * buzz * Dsp.AttackDecay(t, 0.003f, 0.14f);
            }
            return buf;
        }

        /// <summary>Wave start: a rising three-note saw stab (A minor) over a short noise riser.</summary>
        private static float[] WaveStart(int rate)
        {
            const float len = 0.62f;
            var buf = Alloc(len, rate);
            var rng = new Rng(900);
            var nf = new Svf();
            AddStab(buf, rate, 69f, 0.00f, 0.08f);
            AddStab(buf, rate, 72f, 0.09f, 0.08f);
            AddStab(buf, rate, 76f, 0.18f, 0.22f);
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate;
                float riser = t < 0.22f ? t / 0.22f : Dsp.Decay(t - 0.22f, 0.05f);
                buf[i] += nf.Process(rng.Next(), 500f + 6000f * MathF.Min(1f, t / 0.22f), 0.8f, rate) * riser * 0.25f;
            }
            return buf;
        }

        /// <summary>Victory: a bright C-major arpeggio that blooms into a held chord, with echoes.</summary>
        private static float[] Victory(int rate)
        {
            const float len = 2.0f;
            var buf = Alloc(len, rate);
            AddStab(buf, rate, 72f, 0.00f, 0.1f);
            AddStab(buf, rate, 76f, 0.12f, 0.1f);
            AddStab(buf, rate, 79f, 0.24f, 0.1f);
            AddStab(buf, rate, 84f, 0.36f, 0.9f);
            AddStab(buf, rate, 72f, 0.36f, 0.9f);
            AddStab(buf, rate, 76f, 0.36f, 0.9f);
            AddStab(buf, rate, 79f, 0.36f, 0.9f);
            AddEcho(buf, (int)(0.18f * rate), 0.3f);
            AddEcho(buf, (int)(0.27f * rate), 0.2f);
            return buf;
        }

        /// <summary>Game over: a long descending detuned saw with the filter closing, over a sub drone.</summary>
        private static float[] GameOver(int rate)
        {
            const float len = 1.8f;
            var buf = Alloc(len, rate);
            var rng = new Rng(1000);
            var a = new Osc();
            var b = new Osc();
            var sub = new Osc();
            var lp = new Svf();
            var rumble = new Svf();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate, u = t / len;
                float hz = Dsp.Glide(220f, 52f, t, 0.5f);
                float x = 0.5f * (a.Saw(hz, rate) + b.Saw(hz * 0.993f, rate));
                x = lp.Process(x, Dsp.Glide(3200f, 180f, t, 0.45f), 0.5f, rate);
                float s = sub.Sine(55f, rate) * 0.5f * MathF.Min(1f, t / 0.2f);
                float r = rumble.Process(rng.Next(), 250f, 1f, rate) * 0.5f;
                float trem = 1f - 0.25f * (0.5f + 0.5f * MathF.Sin(Dsp.TwoPi * Dsp.Glide(9f, 2f, t, 0.6f) * t));
                buf[i] = (x + s + r) * trem * MathF.Min(1f, t / 0.005f) * (1f - u) * (1f - u);
            }
            return buf;
        }

        // ------------------------------------------------------------------ enemies

        /// <summary>Brute charge telegraph: a rising low growl with a speeding flutter.</summary>
        private static float[] BruteWindup(int rate)
        {
            const float len = 0.55f;
            var buf = Alloc(len, rate);
            var a = new Osc();
            var b = new Osc();
            var lp = new Svf();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate, u = t / len;
                float hz = 55f + 60f * u;
                float x = 0.5f * (a.Saw(hz, rate) + b.Saw(hz * 1.02f, rate));
                x = lp.Process(x, 400f + 1400f * u, 0.4f, rate);
                float flutter = 0.6f + 0.4f * MathF.Sin(Dsp.TwoPi * (8f * t + 12f * t * t / len));
                buf[i] = Dsp.SoftClip(x * 2f) * flutter * MathF.Min(1f, t / 0.05f) * MathF.Min(1f, (len - t) / 0.06f);
            }
            return buf;
        }

        /// <summary>Brute wall crash: a heavy boom, a noise blast and a low metallic ring.</summary>
        private static float[] BruteCrash(int rate)
        {
            const float len = 0.65f;
            var buf = Alloc(len, rate);
            var rng = new Rng(1100);
            var boom = new Osc();
            var r1 = new Osc();
            var r2 = new Osc();
            var nf = new Svf();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate;
                float b = boom.Sine(Dsp.Glide(80f, 30f, t, 0.1f), rate) * Dsp.AttackDecay(t, 0.002f, 0.22f);
                float n = nf.Process(rng.Next(), Dsp.Glide(2500f, 200f, t, 0.08f), 0.9f, rate) * Dsp.Decay(t, 0.15f);
                float ring = (r1.Sine(180f, rate) + 0.6f * r2.Sine(180f * 2.7f, rate)) * Dsp.Decay(t, 0.18f) * 0.3f;
                buf[i] = Dsp.SoftClip((b + n + ring) * 2f);
            }
            return buf;
        }

        /// <summary>Drone dive telegraph: a rising whine with vibrato.</summary>
        private static float[] DroneWindup(int rate)
        {
            const float len = 0.45f;
            var buf = Alloc(len, rate);
            var a = new Osc();
            var b = new Osc();
            var lp = new Svf();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate, u = t / len;
                float hz = (900f + 900f * u) * (1f + 0.02f * MathF.Sin(Dsp.TwoPi * 12f * t));
                float x = a.Sine(hz, rate) + 0.3f * lp.Process(b.Square(hz * 0.5f, rate, 0.5f), 2500f, 0.9f, rate);
                buf[i] = x * MathF.Min(1f, t / 0.1f) * MathF.Min(1f, (len - t) / 0.05f);
            }
            return buf;
        }

        // ------------------------------------------------------------------ orbs (mapped by event name; see RedlineAudio)

        /// <summary>Orb appears: two soft glassy bell tones gliding up.</summary>
        private static float[] OrbSpawn(int rate)
        {
            const float len = 0.4f;
            var buf = Alloc(len, rate);
            var a = new Osc();
            var b = new Osc();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate;
                float g = Dsp.Glide(0.94f, 1f, t, 0.04f);
                buf[i] = (a.Triangle(1318.5f * g, rate) * Dsp.AttackDecay(t, 0.004f, 0.12f) +
                          b.Sine(1975.5f * g, rate) * 0.6f * Dsp.AttackDecay(t, 0.03f, 0.1f));
            }
            return buf;
        }

        /// <summary>Orb collected: a quick B5-E6 pulse arpeggio with a sparkle.</summary>
        private static float[] OrbPickup(int rate)
        {
            const float len = 0.26f;
            var buf = Alloc(len, rate);
            var a = new Osc();
            var b = new Osc();
            var s = new Osc();
            var lp = new Svf();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate;
                float n1 = a.Square(987.8f, rate, 0.5f) * (t < 0.05f ? Dsp.AttackDecay(t, 0.002f, 0.04f) : 0f);
                float t2 = t - 0.05f;
                float n2 = t2 > 0f ? b.Square(1318.5f, rate, 0.5f) * Dsp.AttackDecay(t2, 0.002f, 0.08f) : 0f;
                float sp = s.Sine(2637f, rate) * Dsp.AttackDecay(MathF.Max(0f, t2), 0.01f, 0.06f) * (t2 > 0f ? 0.3f : 0f);
                buf[i] = lp.Process(n1 + n2, 6000f, 0.9f, rate) * 0.7f + sp;
            }
            return buf;
        }

        /// <summary>Orb vanished: a small falling triangle blip that fades out.</summary>
        private static float[] OrbExpire(int rate)
        {
            const float len = 0.22f;
            var buf = Alloc(len, rate);
            var a = new Osc();
            for (int i = 0; i < buf.Length; i++)
            {
                float t = i / (float)rate;
                buf[i] = a.Triangle(Dsp.Glide(1100f, 520f, t, 0.06f), rate) * Dsp.AttackDecay(t, 0.003f, 0.07f);
            }
            return buf;
        }

        // ------------------------------------------------------------------ helpers

        private static float[] Alloc(float seconds, int rate) => new float[Math.Max(1, (int)(seconds * rate))];

        /// <summary>Mixes a detuned two-saw synth note with a filter envelope into the buffer.</summary>
        private static void AddStab(float[] buf, int rate, float midi, float start, float tau)
        {
            float hz = Dsp.MidiToHz(midi);
            var a = new Osc();
            var b = new Osc();
            var lp = new Svf();
            int from = (int)(start * rate);
            for (int i = from; i < buf.Length; i++)
            {
                float t = (i - from) / (float)rate;
                float vib = 1f + 0.003f * MathF.Sin(Dsp.TwoPi * 5.5f * t) * MathF.Min(1f, t / 0.3f);
                float x = 0.5f * (a.Saw(hz * vib * 1.004f, rate) + b.Saw(hz * vib * 0.996f, rate));
                x = lp.Process(x, 1200f + 4500f * Dsp.Decay(t, 0.12f), 0.6f, rate);
                buf[i] += x * Dsp.AttackDecay(t, 0.004f, tau) * 0.5f;
            }
        }

        /// <summary>Adds one non-recursive echo <paramref name="delay"/> samples late.</summary>
        private static void AddEcho(float[] buf, int delay, float gain)
        {
            for (int i = buf.Length - 1; i >= delay; i--) buf[i] += buf[i - delay] * gain;
        }

        /// <summary>Removes DC, fades the edges and normalises the peak to <see cref="AudioMix.ClipPeak"/>.</summary>
        private static void Finish(float[] buf, int rate)
        {
            int n = buf.Length;
            if (n == 0) return;
            double mean = 0;
            for (int i = 0; i < n; i++) mean += buf[i];
            float dc = (float)(mean / n);
            int fadeIn = Math.Min(n, Math.Max(1, rate / 2000));
            int fadeOut = Math.Min(n, Math.Max(1, rate / 200));
            float peak = 0f;
            for (int i = 0; i < n; i++)
            {
                float x = buf[i] - dc;
                if (i < fadeIn) x *= i / (float)fadeIn;
                int left = n - 1 - i;
                if (left < fadeOut) x *= left / (float)fadeOut;
                buf[i] = x;
                float a = MathF.Abs(x);
                if (a > peak) peak = a;
            }
            if (peak < 1e-6f) return;
            float k = AudioMix.ClipPeak / peak;
            for (int i = 0; i < n; i++) buf[i] *= k;
        }
    }
}
