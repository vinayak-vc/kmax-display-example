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
        private float musicVolume = 0.45f;
        [SerializeField, Range(0f, 1f), Tooltip("Level interface cues play at.")]
        private float sfxVolume = 0.45f;
        [SerializeField, Tooltip("Seconds the pad takes to fade up when the exhibit starts. Long " +
            "enough not to announce itself, short enough that someone checking whether there is " +
            "music at all does not conclude there is none.")]
        private float musicFadeInDuration = 1.5f;
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
        [SerializeField, Tooltip("Root note of the synthesised pad in hertz. Deliberately not down at " +
            "110: a low A is below what a display's own panel speakers reproduce, so the pad was " +
            "playing correctly and simply could not be heard. 196 is a G below middle C, which puts " +
            "the whole chord in a band small speakers actually carry.")]
        private float padRootHz = 196f;
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
        private AudioClip _clickClip;
        private AudioClip _selectClip;
        private AudioClip _navigateClip;
        private AudioClip _backClip;
        private AudioClip _expandClip;
        private AudioClip _collapseClip;
        private AudioClip _resetClip;

        private float _fadeProgress;
        private float _duckProgress;
        private float _lastHoverTime = -1f;
        private int _lastClickCueFrame = -1;

        /// <summary>
        /// Minimum gap between hover cues. The pointer can cross several badges in a moment, and
        /// without this the exhibit chatters.
        /// </summary>
        private const float HoverCooldown = 0.08f;

        private static AnatomyAudioDirector _persistentInstance;

        public static AnatomyAudioDirector Instance {
            get { return _persistentInstance; }
        }

        private void Awake() {
            if (_persistentInstance != null && _persistentInstance != this) {
                AudioSource[] duplicateSources = GetComponents<AudioSource>();
                for (int i = 0; i < duplicateSources.Length; i++) {
                    if (duplicateSources[i] != null) {
                        duplicateSources[i].Stop();
                        Destroy(duplicateSources[i]);
                    }
                }

                enabled = false;
                return;
            }

            _persistentInstance = this;
            if (transform.parent != null) {
                transform.SetParent(null);
            }

            DontDestroyOnLoad(gameObject);
            EnsureSources();
            BuildClips();
        }

        private void OnDestroy() {
            if (_persistentInstance == this) {
                _persistentInstance = null;
            }
        }

        private void Start() {
            if (_persistentInstance != null && _persistentInstance != this) {
                return;
            }

            if (!playMusicOnStart) {
                return;
            }

            if (musicSource == null || !musicSource.isPlaying) {
                PlayMusic();
            }
        }

        private void Update() {
            if (_persistentInstance != null && _persistentInstance != this) {
                return;
            }

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
            if (_persistentInstance != null && _persistentInstance != this) {
                _persistentInstance.PlayMusic();
                return;
            }

            if (musicSource == null || musicSource.clip == null) {
                return;
            }

            if (musicSource.isPlaying) {
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
            if (_persistentInstance != null && _persistentInstance != this) {
                _persistentInstance.StopMusic();
                return;
            }

            if (musicSource != null) {
                musicSource.Stop();
            }
        }

        public void PlayHover() {
            if (_persistentInstance != null && _persistentInstance != this) {
                _persistentInstance.PlayHover();
                return;
            }

            // Rate-limited rather than pitch-varied: a hover cue that changes pitch draws attention
            // to itself, and this one should sit just under notice.
            if (Time.unscaledTime - _lastHoverTime < HoverCooldown) {
                return;
            }

            _lastHoverTime = Time.unscaledTime;
            PlayCue(_hoverClip, 0.38f, 1f, false);
            PulseStylus(0.010f, 7);
        }

        /// <summary>
        /// Plays a crisp, modern glass/marimba UI button click cue.
        /// </summary>
        public void PlayClick() {
            if (_persistentInstance != null && _persistentInstance != this) {
                _persistentInstance.PlayClick();
                return;
            }

            if (_lastClickCueFrame == Time.frameCount) {
                return;
            }

            _lastClickCueFrame = Time.frameCount;
            PlayCue(_clickClip, 0.65f, 1f, false);
            PulseStylus(0.016f, 12);
        }

        public void PlaySelect() {
            if (_persistentInstance != null && _persistentInstance != this) {
                _persistentInstance.PlaySelect();
                return;
            }

            _lastClickCueFrame = Time.frameCount;
            PlayCue(_selectClip, 0.85f, 1f, true);
            PulseStylus(0.025f, 18);
        }

        /// <summary>
        /// Stepping through parts with the Next and Back buttons. The pitch rises when moving
        /// forward through the catalog and falls when moving back, so the direction is audible.
        /// </summary>
        public void PlayNavigate(bool forward) {
            if (_persistentInstance != null && _persistentInstance != this) {
                _persistentInstance.PlayNavigate(forward);
                return;
            }

            _lastClickCueFrame = Time.frameCount;
            PlayCue(_navigateClip, 0.68f, forward ? 1.08f : 0.94f, false);
            PulseStylus(0.018f, 14);
        }

        public void PlayBack() {
            if (_persistentInstance != null && _persistentInstance != this) {
                _persistentInstance.PlayBack();
                return;
            }

            _lastClickCueFrame = Time.frameCount;
            PlayCue(_backClip, 0.78f, 1f, false);
            PulseStylus(0.022f, 15);
        }

        public void PlayExpand() {
            if (_persistentInstance != null && _persistentInstance != this) {
                _persistentInstance.PlayExpand();
                return;
            }

            PlayCue(_expandClip, 0.85f, 1f, true);
            PulseStylus(0.035f, 20);
        }

        public void PlayCollapse() {
            if (_persistentInstance != null && _persistentInstance != this) {
                _persistentInstance.PlayCollapse();
                return;
            }

            PlayCue(_collapseClip, 0.85f, 0.92f, true);
            PulseStylus(0.030f, 16);
        }

        public void PlayReset() {
            if (_persistentInstance != null && _persistentInstance != this) {
                _persistentInstance.PlayReset();
                return;
            }

            _lastClickCueFrame = Time.frameCount;
            PlayCue(_resetClip, 0.80f, 1f, false);
            PulseStylus(0.035f, 18);
        }

        private static void PulseStylus(float duration, int strength) {
            KmaxXR.KmaxStylus stylus = KmaxXR.KmaxPointer.PointerById(KmaxXR.KmaxStylus.UniqueId) as KmaxXR.KmaxStylus;
            if (stylus != null && stylus.Visible) {
                stylus.VibrationOnce(duration, strength);
            }
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

            _hoverClip = hoverOverride != null ? hoverOverride : ProceduralAudio.CreateBlip(680f, 0.052f);
            _clickClip = ProceduralAudio.CreateUiClick(587.33f, 0.085f);
            _selectClip = selectOverride != null ? selectOverride : ProceduralAudio.CreateChime(523.25f, 0.72f, 0.95f);
            _navigateClip = navigateOverride != null ? navigateOverride : ProceduralAudio.CreateUiClick(659.25f, 0.080f);
            _backClip = backOverride != null ? backOverride : ProceduralAudio.CreateThud(392f, 0.24f);
            _expandClip = expandOverride != null ? expandOverride : ProceduralAudio.CreateWhoosh(0.65f, true);
            _collapseClip = collapseOverride != null ? collapseOverride : ProceduralAudio.CreateWhoosh(0.5f, false);
            _resetClip = resetOverride != null ? resetOverride : ProceduralAudio.CreateThud(349.23f, 0.26f);
        }
    }
}
