using System;

namespace Redline.Game.Audio
{
    /// <summary>
    /// Real-time synthwave track in A minor (Am - F - C - G, one bar each, 4-bar loop) at
    /// <see cref="AudioMix.MusicBpm"/>. Three layers follow the player's speed tier
    /// (design/game-brief.md, driving synthwave): tier 1 = drums + bass, tier 2 adds a 16th-note
    /// arpeggio, tier 3 adds a saw lead. Every layer is triggered from one sample-accurate step
    /// clock, so they stay in sync whatever their volume; layers only fade, they never restart.
    /// </summary>
    /// <remarks>
    /// Threading: <see cref="Render"/> runs on the audio thread and owns all synth state. The main
    /// thread only writes the volatile targets through <see cref="SetLayers"/>,
    /// <see cref="SetDucked"/> and <see cref="SetSampleRate"/>. Pure C#, no Unity API, no
    /// allocation after construction.
    /// </remarks>
    public sealed class MusicSequencer
    {
        private const int StepsPerBar = 16;
        private const int Steps = 64;
        private const int Rest = -1;
        private const int Hold = -2;
        /// <summary>Delay buffer length per channel: enough for 4 sixteenths at 192 kHz.</summary>
        private const int DelayCapacity = 120000;

        // Song data. Bass roots per bar (A1, F1, C2, G1) and three arpeggio chord tones per bar.
        private static readonly int[] BassRoots = { 33, 29, 36, 31 };
        private static readonly int[] BassPattern = { 0, 0, 12, 0, 0, 12, 0, 12 };
        private static readonly int[] ChordTones = { 57, 60, 64, 57, 60, 65, 55, 60, 64, 55, 59, 62 };
        private static readonly int[] ArpPattern = { 0, 1, 2, 3, 4, 3, 2, 1 };
        private static readonly int[] Lead =
        {
            76, Hold, Hold, Hold, 72, Hold, 74, Hold, 76, Hold, Hold, Hold, 79, Hold, 76, Hold,
            77, Hold, Hold, Hold, 76, Hold, 72, Hold, 69, Hold, Hold, Hold, Hold, Hold, Rest, Rest,
            72, Hold, Hold, Hold, 76, Hold, 79, Hold, 84, Hold, Hold, Hold, 83, Hold, 79, Hold,
            81, Hold, Hold, Hold, 79, Hold, Hold, Hold, 74, Hold, Hold, Hold, 71, Hold, Rest, Rest
        };

        // Internal layer levels (headroom is restored by the bus gain).
        private const float KickLevel = 0.85f;
        private const float SnareLevel = 0.45f;
        private const float HatLevel = 0.16f;
        private const float BassLevel = 0.42f;
        private const float ArpLevel = 0.27f;
        private const float LeadLevel = 0.34f;
        private const float DelaySend = 0.4f;
        private const float DelayFeedback = 0.38f;
        private const float DelayReturn = 0.45f;
        private const float PumpDepth = 0.4f;

        // Main thread -> audio thread.
        private volatile float _arpTarget;
        private volatile float _leadTarget;
        private volatile float _duckTarget = 1f;
        private volatile int _pendingRate;

        // Clock (audio thread).
        private int _rate;
        private float _invRate;
        private double _samplesPerStep;
        private double _stepPhase;
        private int _step;

        // Layer gains (audio thread).
        private float _baseGain;
        private float _arpGain;
        private float _leadGain;
        private float _duck = 1f;
        private float _userGain = 1f;

        private Rng _rng = new Rng(4242);

        // Kick.
        private float _kickT = -1f;
        private float _kickPhase;
        // Snare.
        private float _snareT = -1f;
        private float _snareVel;
        private Osc _snareTone;
        private Svf _snareFilter;
        // Hat.
        private float _hatT = -1f;
        private float _hatVel;
        private float _hatTau;
        private Svf _hatFilter;
        // Bass.
        private float _bassT = -1f;
        private float _bassHz;
        private float _bassGate;
        private Osc _bassA, _bassB, _bassSub;
        private Svf _bassFilter;
        // Arp.
        private float _arpT = -1f;
        private float _arpHz;
        private Osc _arpOsc;
        private Svf _arpFilter;
        // Lead.
        private float _leadT;
        private float _leadHz;
        private float _leadTargetHz;
        private bool _leadGate;
        private float _leadEnv;
        private Osc _leadA, _leadB, _leadSub;
        private Svf _leadFilter;
        // Stereo delay.
        private readonly float[] _delayL = new float[DelayCapacity];
        private readonly float[] _delayR = new float[DelayCapacity];
        private int _delayPos;
        private int _delayLenL;
        private int _delayLenR;
        private float _fbL, _fbR;

        /// <summary>Creates the sequencer for an output sample rate. Allocates its delay buffers here, once.</summary>
        public MusicSequencer(int sampleRate)
        {
            ApplyRate(sampleRate > 0 ? sampleRate : 48000);
            _stepPhase = _samplesPerStep; // first step triggers on the first sample
        }

        /// <summary>Main thread: which tier layers should be audible (they crossfade, never restart).</summary>
        public void SetLayers(bool arpeggio, bool lead)
        {
            _arpTarget = arpeggio ? 1f : 0f;
            _leadTarget = lead ? 1f : 0f;
        }

        /// <summary>Main thread: ducks the whole track to <see cref="AudioMix.GameOverDuck"/> (game over).</summary>
        public void SetDucked(bool ducked) => _duckTarget = ducked ? AudioMix.GameOverDuck : 1f;

        /// <summary>Main thread: ducks the whole track to a level multiplier (1 = no duck), e.g. <see cref="AudioMix.PauseDuck"/>.</summary>
        public void SetDuck(float level) => _duckTarget = level < 0f ? 0f : level > 1f ? 1f : level;

        /// <summary>Main thread: the output sample rate changed (applied at the next audio block).</summary>
        public void SetSampleRate(int sampleRate)
        {
            if (sampleRate > 0) _pendingRate = sampleRate;
        }

        /// <summary>
        /// Audio thread: overwrites an interleaved buffer with the next block of music, already
        /// scaled by <see cref="AudioMix.MusicBus"/>, <see cref="AudioMix.Master"/> and the player's music and master volumes.
        /// </summary>
        public void Render(float[] data, int channels)
        {
            int pending = _pendingRate;
            if (pending > 0 && pending != _rate) ApplyRate(pending);
            if (channels < 1) channels = 1;

            float arpTarget = _arpTarget, leadTarget = _leadTarget, duckTarget = _duckTarget;
            float upStart = _invRate / AudioMix.MusicStartFadeSeconds;
            float up = _invRate / AudioMix.LayerFadeInSeconds;
            float down = _invRate / AudioMix.LayerFadeOutSeconds;
            float duckStep = _invRate / AudioMix.DuckSeconds;
            const float bus = AudioMix.MusicBus * AudioMix.Master;
            float userTarget = AudioMix.UserMaster * AudioMix.UserMusic;
            float userStep = _invRate / AudioMix.UserVolumeSeconds;

            for (int i = 0; i + channels <= data.Length; i += channels)
            {
                if (_stepPhase >= _samplesPerStep)
                {
                    _stepPhase -= _samplesPerStep;
                    TriggerStep();
                }
                _stepPhase += 1.0;

                _baseGain = Ramp(_baseGain, 1f, upStart, upStart);
                _arpGain = Ramp(_arpGain, arpTarget, up, down);
                _leadGain = Ramp(_leadGain, leadTarget, up, down);
                _duck = Ramp(_duck, duckTarget, duckStep, duckStep);
                _userGain = Ramp(_userGain, userTarget, userStep, userStep);

                RenderFrame(out float l, out float r);
                float g = bus * _duck * _userGain;
                l = Dsp.SoftClip(l) * g;
                r = Dsp.SoftClip(r) * g;

                if (channels == 1)
                {
                    data[i] = 0.5f * (l + r);
                }
                else
                {
                    data[i] = l;
                    data[i + 1] = r;
                    for (int c = 2; c < channels; c++) data[i + c] = 0f;
                }
            }
        }

        // ------------------------------------------------------------------ sequencing

        private void TriggerStep()
        {
            int s = _step;
            _step = (_step + 1) % Steps;
            int bar = s / StepsPerBar;
            int pos = s % StepsPerBar;

            // Drums: four on the floor, snare on 2 and 4, offbeat open hats over soft 16ths, a fill at the loop end.
            if (pos % 4 == 0) _kickT = 0f;
            if (pos == 4 || pos == 12) TriggerSnare(1f);
            else if (bar == 3 && pos == 14) TriggerSnare(0.45f);
            else if (bar == 3 && pos == 15) TriggerSnare(0.7f);
            if (pos % 4 == 2) TriggerHat(1f, 0.06f);
            else if ((pos & 1) == 1) TriggerHat(0.4f, 0.018f);

            // Bass: driving eighths with octave jumps.
            if ((pos & 1) == 0)
            {
                _bassHz = Dsp.MidiToHz(BassRoots[bar] + BassPattern[pos / 2]);
                _bassT = 0f;
                _bassGate = (float)(2.0 * _samplesPerStep * _invRate * 0.8);
            }

            // Arpeggio: every sixteenth, chord tones up over two octaves and back.
            int idx = ArpPattern[pos % ArpPattern.Length];
            int note = idx < 3 ? ChordTones[bar * 3 + idx] : ChordTones[bar * 3 + idx - 3] + 12;
            _arpHz = Dsp.MidiToHz(note + 12);
            _arpT = 0f;

            // Lead: a melody note, a tie or a rest.
            int lead = Lead[s];
            if (lead >= 0)
            {
                _leadTargetHz = Dsp.MidiToHz(lead);
                if (!_leadGate || _leadEnv < 0.05f) _leadHz = _leadTargetHz;
                _leadGate = true;
                _leadT = 0f;
            }
            else if (lead == Rest)
            {
                _leadGate = false;
            }
        }

        private void TriggerSnare(float vel)
        {
            _snareT = 0f;
            _snareVel = vel;
        }

        private void TriggerHat(float vel, float tau)
        {
            _hatT = 0f;
            _hatVel = vel;
            _hatTau = tau;
        }

        // ------------------------------------------------------------------ synthesis

        private void RenderFrame(out float left, out float right)
        {
            float rate = _rate;
            float dt = _invRate;

            // Kick: pitch-dropping sine with a click. Its envelope also drives the sidechain pump.
            float kick = 0f, pump = 1f;
            if (_kickT >= 0f)
            {
                float t = _kickT;
                float hz = 45f + 110f * MathF.Exp(-t / 0.035f);
                _kickPhase += hz * dt;
                if (_kickPhase >= 1f) _kickPhase -= 1f;
                kick = MathF.Sin(Dsp.TwoPi * _kickPhase) * MathF.Exp(-t / 0.2f) + _rng.Next() * 0.3f * MathF.Exp(-t / 0.002f);
                pump = 1f - PumpDepth * MathF.Exp(-t / 0.09f);
                _kickT += dt;
                if (_kickT > 0.7f)
                {
                    _kickT = -1f;
                    _kickPhase = 0f;
                }
            }

            // Snare: body tone plus a bright noise burst with a slightly gated tail.
            float snare = 0f;
            if (_snareT >= 0f)
            {
                float t = _snareT;
                _snareFilter.Process(_rng.Next(), 3000f, 0.9f, rate);
                float noise = (_snareFilter.Band * 0.6f + _snareFilter.High * 0.5f) * MathF.Exp(-t / 0.12f);
                float tone = _snareTone.Sine(Dsp.Glide(240f, 185f, t, 0.02f), rate) * MathF.Exp(-t / 0.06f) * 0.6f;
                float gate = t < 0.18f ? 1f : MathF.Exp(-(t - 0.18f) / 0.02f);
                snare = (noise + tone) * gate * _snareVel;
                _snareT += dt;
                if (_snareT > 0.35f) _snareT = -1f;
            }

            // Hat: high-passed noise.
            float hat = 0f;
            if (_hatT >= 0f)
            {
                _hatFilter.Process(_rng.Next(), 9000f, 0.9f, rate);
                hat = _hatFilter.High * MathF.Exp(-_hatT / _hatTau) * _hatVel;
                _hatT += dt;
                if (_hatT > 0.3f) _hatT = -1f;
            }

            // Bass: two detuned saws and a sub pulse through a low-pass with a pluck envelope.
            float bass = 0f;
            if (_bassT >= 0f)
            {
                float t = _bassT;
                float x = 0.5f * (_bassA.Saw(_bassHz, rate) + _bassB.Saw(_bassHz * 1.006f, rate)) + 0.35f * _bassSub.Square(_bassHz * 0.5f, rate, 0.5f);
                x = _bassFilter.Process(x, 150f + 1300f * MathF.Exp(-t / 0.08f), 0.55f, rate);
                float amp = MathF.Min(1f, t / 0.003f) * (0.6f + 0.4f * MathF.Exp(-t / 0.08f));
                if (t > _bassGate) amp *= MathF.Exp(-(t - _bassGate) / 0.012f);
                bass = x * amp;
                _bassT += dt;
                if (_bassT > _bassGate + 0.1f) _bassT = -1f;
            }

            // Arp: plucky pulse with a filter envelope.
            float arp = 0f;
            if (_arpT >= 0f)
            {
                float t = _arpT;
                float x = _arpOsc.Square(_arpHz, rate, 0.5f);
                x = _arpFilter.Process(x, 800f + 3800f * MathF.Exp(-t / 0.05f), 0.5f, rate);
                arp = x * MathF.Min(1f, t / 0.002f) * MathF.Exp(-t / 0.1f);
                _arpT += dt;
                if (_arpT > 0.6f) _arpT = -1f;
            }

            // Lead: detuned saw pair with a sub, glide, delayed vibrato and an ASR envelope.
            float lead = 0f;
            {
                float coef = 1f - MathF.Exp(-dt / 0.03f);
                _leadHz += (_leadTargetHz - _leadHz) * coef;
                float envTarget = _leadGate ? 1f : 0f;
                float envTau = _leadGate ? 0.01f : 0.12f;
                _leadEnv += (envTarget - _leadEnv) * (1f - MathF.Exp(-dt / envTau));
                if (_leadEnv > 1e-4f && _leadHz > 0f)
                {
                    float t = _leadT;
                    float vibDepth = 0.005f * MathF.Max(0f, MathF.Min(1f, (t - 0.2f) / 0.3f));
                    float hz = _leadHz * (1f + vibDepth * MathF.Sin(Dsp.TwoPi * 5.5f * t));
                    float x = 0.5f * (_leadA.Saw(hz * 1.004f, rate) + _leadB.Saw(hz * 0.996f, rate)) + 0.25f * _leadSub.Square(hz * 0.5f, rate, 0.5f);
                    x = _leadFilter.Process(x, 1800f + 2400f * MathF.Exp(-t / 0.25f), 0.45f, rate);
                    lead = x * _leadEnv;
                }
                _leadT += dt;
            }

            float b = _baseGain;
            float a = Dsp.Smooth(_arpGain);
            float ld = Dsp.Smooth(_leadGain);
            float drums = (kick * KickLevel + snare * SnareLevel + hat * HatLevel) * b;
            float bassOut = bass * BassLevel * pump * b;
            float arpOut = arp * ArpLevel * a * pump;
            float leadOut = lead * LeadLevel * ld * (0.85f + 0.15f * pump);

            // Ping-pong delay fed by the arp and the lead, with darkened feedback.
            float send = (arpOut + leadOut) * DelaySend;
            int readL = _delayPos - _delayLenL;
            if (readL < 0) readL += DelayCapacity;
            int readR = _delayPos - _delayLenR;
            if (readR < 0) readR += DelayCapacity;
            float dl = _delayL[readL];
            float dr = _delayR[readR];
            _fbL += (dr - _fbL) * 0.35f;
            _fbR += (dl - _fbR) * 0.35f;
            _delayL[_delayPos] = send + _fbL * DelayFeedback;
            _delayR[_delayPos] = _fbR * DelayFeedback;
            if (++_delayPos >= DelayCapacity) _delayPos = 0;

            float centre = drums + bassOut + leadOut;
            left = centre + arpOut * 0.8f + dl * DelayReturn;
            right = centre + arpOut * 1.2f + dr * DelayReturn;
        }

        // ------------------------------------------------------------------ helpers

        private void ApplyRate(int rate)
        {
            _rate = rate;
            _invRate = 1f / rate;
            _samplesPerStep = rate * 60.0 / (AudioMix.MusicBpm * 4.0);
            _delayLenL = Math.Min(DelayCapacity - 1, (int)(_samplesPerStep * 3.0));
            _delayLenR = Math.Min(DelayCapacity - 1, (int)(_samplesPerStep * 4.0));
            if (_stepPhase > _samplesPerStep) _stepPhase = _samplesPerStep;
        }

        private static float Ramp(float value, float target, float up, float down)
        {
            if (value < target) return MathF.Min(target, value + up);
            if (value > target) return MathF.Max(target, value - down);
            return value;
        }
    }
}
