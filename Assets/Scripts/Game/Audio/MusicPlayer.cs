using UnityEngine;

namespace Redline.Game.Audio
{
    /// <summary>
    /// Hosts <see cref="MusicSequencer"/> on the audio thread: an AudioSource plays a silent looping
    /// carrier clip and <see cref="OnAudioFilterRead"/> replaces it with the synthesised track.
    /// Created by <see cref="RedlineAudio"/>.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class MusicPlayer : MonoBehaviour
    {
        private volatile MusicSequencer _sequencer;
        private AudioSource _source;
        private AudioClip _carrier;
        private int _rate;

        /// <summary>The running sequencer (main thread may call its Set* methods).</summary>
        public MusicSequencer Sequencer => _sequencer;

        /// <summary>Builds the sequencer for the current output rate and starts playback.</summary>
        public void Init()
        {
            _rate = AudioSettings.outputSampleRate;
            var sequencer = new MusicSequencer(_rate);

            _source = GetComponent<AudioSource>();
            int carrierRate = _rate > 0 ? _rate : 48000;
            _carrier = AudioClip.Create("redline_music_carrier", carrierRate, 1, carrierRate, false);
            _carrier.SetData(new float[carrierRate], 0);
            _source.clip = _carrier;
            _source.loop = true;
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.dopplerLevel = 0f;
            _source.priority = 0; // never virtualised in favour of an effect
            _source.volume = 1f;

            _sequencer = sequencer;
            _source.Play();
        }

        private void Update()
        {
            var sequencer = _sequencer;
            if (sequencer == null) return;
            int rate = AudioSettings.outputSampleRate;
            if (rate != _rate && rate > 0)
            {
                _rate = rate;
                sequencer.SetSampleRate(rate);
            }
            // An audio device change stops sources; pick the track back up.
            if (!_source.isPlaying) _source.Play();
        }

        // Audio thread: no Unity API calls here.
        private void OnAudioFilterRead(float[] data, int channels)
        {
            var sequencer = _sequencer;
            if (sequencer == null)
            {
                System.Array.Clear(data, 0, data.Length);
                return;
            }
            sequencer.Render(data, channels);
        }

        private void OnDestroy()
        {
            _sequencer = null;
            if (_carrier != null) Destroy(_carrier);
        }
    }
}
