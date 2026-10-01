using UnityEngine;

namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// Synthesises the suite's interaction cues, so the assembly ships no audio files and needs
    /// no reference to the exhibit stack.
    ///
    /// The eye and the car already have a procedural audio helper, and reusing it would have been
    /// the obvious move - but it lives in <c>KmaxDisplayExample</c>, and referencing that
    /// assembly to get one static class would drag the whole anatomy exhibit along behind the
    /// showcase core. Since the core is meant to be lifted into the next Kmax project intact,
    /// forty lines of sine wave is the cheaper dependency.
    ///
    /// <para>One clip, played with a pitch offset per hit. Generating a clip per pitch would be
    /// both slower and worse - real objects of different sizes ring at different pitches, which is
    /// exactly what varying playback rate does.</para>
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class ShowcaseAudio : MonoBehaviour {
        private const int SampleRate = 44100;

        [Header("Chime")]
        [SerializeField, Tooltip("Base frequency in hertz. Panel speakers reproduce very little " +
            "below about 200 Hz, which the eye exhibit found on hardware - keep this well clear.")]
        private float chimeFrequency = 880f;
        [SerializeField, Tooltip("Length of the chime in seconds.")]
        private float chimeDuration = 0.28f;
        [SerializeField, Tooltip("How fast the chime dies away. Higher is shorter and drier.")]
        private float chimeDecay = 14f;
        [SerializeField, Range(0f, 1f), Tooltip("Level of the octave above the root. A little " +
            "makes it read as a struck object rather than a test tone.")]
        private float chimeOvertone = 0.35f;
        [SerializeField, Range(0f, 1f), Tooltip("Playback volume for a chime.")]
        private float chimeVolume = 0.45f;

        private AudioSource _source;
        private AudioClip _chime;

        private void Awake() {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _chime = BuildChime();
        }

        /// <summary>
        /// Plays the chime at a pitch offset. <paramref name="pitch"/> is a multiplier: 1 is the
        /// base, 2 is an octave up.
        /// </summary>
        public void PlayChime(float pitch) {
            if (_source == null || _chime == null) {
                return;
            }
            _source.pitch = Mathf.Clamp(pitch, 0.25f, 4f);
            _source.PlayOneShot(_chime, chimeVolume);
        }

        /// <summary>
        /// Plays the chime at a pitch chosen from how far in front of the glass something is.
        /// Nearer rings higher, which gives the burst an audible depth cue to go with the visual
        /// one - useful precisely because a flat recording of this scene cannot carry the stereo.
        /// </summary>
        /// <param name="worldPoint">Where the event happened.</param>
        /// <param name="lowest">Pitch at the back of the volume.</param>
        /// <param name="highest">Pitch at the front of it.</param>
        public void PlayChimeAtDepth(Vector3 worldPoint, float lowest, float highest) {
            float depth = StereoVolume.DepthOf(worldPoint);
            float span = StereoVolume.PopOutLimit + StereoVolume.DepthLimit;
            float t = span <= 0f
                ? 0.5f
                : Mathf.Clamp01((StereoVolume.DepthLimit - depth) / span);
            PlayChime(Mathf.Lerp(lowest, highest, t));
        }

        private AudioClip BuildChime() {
            int samples = Mathf.Max(1, Mathf.RoundToInt(SampleRate * chimeDuration));
            float[] data = new float[samples];
            float root = Mathf.PI * 2f * chimeFrequency;
            float octave = root * 2f;
            for (int i = 0; i < samples; i++) {
                float t = i / (float)SampleRate;
                float envelope = Mathf.Exp(-t * chimeDecay);
                float value = Mathf.Sin(root * t) + chimeOvertone * Mathf.Sin(octave * t);
                // A short fade in stops the attack from clicking, which at this duration is
                // otherwise the loudest part of the sound.
                float attack = Mathf.Clamp01(t / 0.004f);
                data[i] = value * envelope * attack * 0.5f;
            }
            AudioClip clip = AudioClip.Create("ShowcaseChime", samples, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
