using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// The exhibit's sound: a looping ambient pad underneath, and a short cue for every
    /// interaction on top.
    ///
    /// Every clip has an override slot. Left empty, the sound is synthesised by
    /// <see cref="ProceduralAudio"/> at startup, so the module needs no audio assets; assign a file
    /// and that file is used instead, with no code change.
    ///
    /// Everything plays in 2D. On a fish-tank stereo display the viewer's head is tracked but their
    /// ears are not, so spatialised audio would drift away from the picture as they lean.
    /// </summary>
    public class AnatomyAudioDirector : MonoBehaviour {
        [Header("Sources")]
        [SerializeField, Tooltip("Looping background pad. Created on this object when left empty.")]
        private AudioSource musicSource;
        [SerializeField, Tooltip("One-shot interface cues. Created on this object when left empty.")]
        private AudioSource sfxSource;

        [Header("Levels")]
        [SerializeField, Range(0f, 1f), Tooltip("Level the background pad settles at.")]
        private float musicVolume = 0.16f;
        [SerializeField, Range(0f, 1f), Tooltip("Level interface cues play at.")]
        private float sfxVolume = 0.45f;
        [SerializeField, Tooltip("Seconds the pad takes to fade up when the exhibit starts.")]
        private float musicFadeInDuration = 3.5f;
        [SerializeField, Tooltip("Start the pad automatically. Turn off for a silent kiosk.")]
        private bool playMusicOnStart = true;

        [Header("Ducking")]
        [SerializeField, Range(0f, 1f), Tooltip("How far the pad drops under a prominent cue. 0 disables ducking.")]
        private float duckAmount = 0.45f;
        [SerializeField, Tooltip("Seconds the pad takes to recover after ducking.")]
        private float duckRecoverDuration = 0.7f;

        [Header("Music")]
        [SerializeField, Tooltip("Override for the background pad. Synthesised when empty.")]
        private AudioClip musicOverride;
        [SerializeField, Tooltip("Root note of the synthesised pad in hertz. 110 is a low A.")]
        private float padRootHz = 110f;
        [SerializeField, Tooltip("Loop length of the synthesised pad in seconds. Longer costs memory.")]
        private float padLoopSeconds = 16f;

        [Header("Cue Overrides")]
        [SerializeField, Tooltip("Pointer moving onto a badge.")] private AudioClip hoverOverride;
        [SerializeField, Tooltip("A part being selected.")] private AudioClip selectOverride;
        [SerializeField, Tooltip("Stepping to the next or previous part.")] private AudioClip navigateOverride;
        [SerializeField, Tooltip("Leaving a focused part.")] private AudioClip backOverride;
        [SerializeField, Tooltip("The eye opening.")] private AudioClip expandOverride;
        [SerializeField, Tooltip("The eye closing.")] private AudioClip collapseOverride;
        [SerializeField, Tooltip("The view returning home.")] private AudioClip resetOverride;

        private AudioClip _hoverClip;
        private AudioClip _selectClip;
        private AudioClip _navigateClip;
        private AudioClip _backClip;
        private AudioClip _expandClip;
        private AudioClip _collapseClip;
        private AudioClip _resetClip;

        private float _fadeProgress;
        private float _duckProgress;
        private float _lastHoverTime = -1f;

        /// <summary>
        /// Minimum gap between hover cues. The pointer can cross several badges in a moment, and
        /// without this the exhibit chatters.
        /// </summary>
        private const float HoverCooldown = 0.08f;

        private void Awake() {
            EnsureSources();
            BuildClips();
        }

        private void Start() {
            if (!playMusicOnStart) {
                return;
            }

            PlayMusic();
        }

        private void Update() {
            if (musicSource == null) {
                return;
            }

            if (_fadeProgress < 1f) {
                _fadeProgress = Mathf.MoveTowards(_fadeProgress, 1f,
                    musicFadeInDuration > 0f ? Time.deltaTime / musicFadeInDuration : 1f);
            }

            if (_duckProgress > 0f) {
                _duckProgress = Mathf.MoveTowards(_duckProgress, 0f,
                    duckRecoverDuration > 0f ? Time.deltaTime / duckRecoverDuration : 1f);
            }

            float duck = 1f - duckAmount * Mathf.SmoothStep(0f, 1f, _duckProgress);
            musicSource.volume = musicVolume * Mathf.SmoothStep(0f, 1f, _fadeProgress) * duck;
        }

        /// <summary>
        /// Starts the background pad, fading it up from silence.
        /// </summary>
        public void PlayMusic() {
            if (musicSource == null || musicSource.clip == null) {
                return;
            }

            _fadeProgress = 0f;
            musicSource.volume = 0f;
            musicSource.Play();
        }

        /// <summary>
        /// Stops the background pad immediately.
        /// </summary>
        public void StopMusic() {
            if (musicSource != null) {
                musicSource.Stop();
            }
        }

        public void PlayHover() {
            // Rate-limited rather than pitch-varied: a hover cue that changes pitch draws attention
            // to itself, and this one should sit just under notice.
            if (Time.unscaledTime - _lastHoverTime < HoverCooldown) {
                return;
            }

            _lastHoverTime = Time.unscaledTime;
            PlayCue(_hoverClip, 0.35f, 1f, false);
        }

        public void PlaySelect() {
            PlayCue(_selectClip, 1f, 1f, true);
        }

        /// <summary>
        /// Stepping through parts with the Next and Back buttons. The pitch rises when moving
        /// forward through the catalog and falls when moving back, so the direction is audible.
        /// </summary>
        public void PlayNavigate(bool forward) {
            PlayCue(_navigateClip, 0.7f, forward ? 1.06f : 0.94f, false);
        }

        public void PlayBack() {
            PlayCue(_backClip, 0.8f, 1f, false);
        }

        public void PlayExpand() {
            PlayCue(_expandClip, 0.9f, 1f, true);
        }

        public void PlayCollapse() {
            PlayCue(_collapseClip, 0.9f, 0.92f, true);
        }

        public void PlayReset() {
            PlayCue(_resetClip, 0.85f, 1f, false);
        }

        private void PlayCue(AudioClip clip, float volumeScale, float pitch, bool duck) {
            if (sfxSource == null || clip == null) {
                return;
            }

            // PlayOneShot ignores the source pitch, so it is set on the source and restored after.
            // Two cues in the same frame would otherwise fight over it.
            float previousPitch = sfxSource.pitch;
            sfxSource.pitch = pitch;
            sfxSource.PlayOneShot(clip, sfxVolume * volumeScale);
            sfxSource.pitch = previousPitch;

            if (duck && duckAmount > 0f) {
                _duckProgress = 1f;
            }
        }

        private void EnsureSources() {
            if (musicSource == null) {
                musicSource = gameObject.AddComponent<AudioSource>();
            }

            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;
            musicSource.volume = 0f;

            if (sfxSource == null) {
                sfxSource = gameObject.AddComponent<AudioSource>();
            }

            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
        }

        private void BuildClips() {
            if (musicSource.clip == null) {
                musicSource.clip = musicOverride != null
                    ? musicOverride
                    : ProceduralAudio.CreatePad(padRootHz, padLoopSeconds);
            }

            _hoverClip = hoverOverride != null ? hoverOverride : ProceduralAudio.CreateBlip(1560f, 0.05f);
            _selectClip = selectOverride != null ? selectOverride : ProceduralAudio.CreateChime(784f, 1.1f, 0.95f);
            _navigateClip = navigateOverride != null ? navigateOverride : ProceduralAudio.CreateBlip(1046f, 0.09f);
            _backClip = backOverride != null ? backOverride : ProceduralAudio.CreateThud(210f, 0.3f);
            _expandClip = expandOverride != null ? expandOverride : ProceduralAudio.CreateWhoosh(0.65f, true);
            _collapseClip = collapseOverride != null ? collapseOverride : ProceduralAudio.CreateWhoosh(0.5f, false);
            _resetClip = resetOverride != null ? resetOverride : ProceduralAudio.CreateThud(165f, 0.36f);
        }
    }
}
