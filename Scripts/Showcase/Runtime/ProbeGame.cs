using UnityEngine;

namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// Probe: thread the pen tip along the tunnel without touching the wall.
    ///
    /// This is the hardest claim the suite makes. Stack shows that co-location is good enough to
    /// pick things up; Probe asks whether it is good enough to <b>work in</b>, at a tolerance of a
    /// centimetre or two, continuously, with an immediate penalty for being wrong. If the tracked
    /// tip and the rendered geometry are genuinely in the same place, this is a game. If they are
    /// not, it is impossible, and no amount of tuning will hide that.
    ///
    /// <para>The tunnel runs from in front of the glass back into the volume, so the viewer reaches
    /// through the screen plane to start and pushes away from themselves to finish. Pop-out is not
    /// decoration here - the mouth is where their hand has to be.</para>
    /// </summary>
    public class ProbeGame : MonoBehaviour {
        [Header("References")]
        [SerializeField, Tooltip("The tunnel. Found on this object or its children when empty.")]
        private ProbePath path;
        [SerializeField, Tooltip("The tracked tip. Found in the scene when empty.")]
        private StylusTip tip;
        [SerializeField, Tooltip("Buzzes on contact. Optional.")]
        private StylusHaptics haptics;
        [SerializeField, Tooltip("Chimes on entry and completion. Optional.")]
        private ShowcaseAudio audioCues;
        [SerializeField, Tooltip("Lifecycle. Told when a run starts and finishes.")]
        private ShowcaseScene scene;
        [SerializeField, Tooltip("Marker that demonstrates the run while nobody is here.")]
        private Transform attractMarker;

        [Header("Rules")]
        [SerializeField, Range(0.01f, 0.3f), Tooltip("How near the mouth the tip must be, as a " +
            "fraction of the tunnel, for a run to start.")]
        private float entryProgress = 0.06f;
        [SerializeField, Range(0.8f, 1f), Tooltip("Progress that counts as finishing.")]
        private float finishProgress = 0.97f;
        [SerializeField, Tooltip("Seconds added to the clock for each contact with the wall.")]
        private float penaltySeconds = 2f;
        [SerializeField, Tooltip("Minimum seconds between penalties, so resting against the wall " +
            "costs a steady trickle rather than one per frame.")]
        private float penaltyCooldown = 0.5f;

        [Header("Difficulty")]
        [SerializeField, Range(0f, 1f), Tooltip("Starting difficulty. Tightens the lumen and " +
            "widens the bends.")]
        private float startDifficulty;
        [SerializeField, Range(0f, 0.5f), Tooltip("Difficulty added for each completed run.")]
        private float difficultyStep = 0.2f;

        [Header("Feedback")]
        [SerializeField, Tooltip("Tunnel colour while the tip is clear of the wall.")]
        private Color safeColor = new Color(0.35f, 0.62f, 0.78f);
        [SerializeField, Tooltip("Tunnel colour at the instant of contact.")]
        private Color contactColor = new Color(1f, 0.25f, 0.2f);
        [SerializeField, Tooltip("Tunnel colour once a run is finished.")]
        private Color finishedColor = new Color(0.4f, 0.85f, 0.5f);
        [SerializeField, Tooltip("Seconds the contact flash takes to fade.")]
        private float flashFade = 0.45f;

        [Header("Attract")]
        [SerializeField, Tooltip("Seconds the demonstration marker takes to travel the tunnel.")]
        private float attractTravelSeconds = 6f;
        [SerializeField, Tooltip("Seconds a finished run stays on screen before a fresh, tighter " +
            "tunnel is offered. The player stays where they are - only walking away resets the " +
            "difficulty and the best time.")]
        private float replayDelay = 4f;

        private MeshRenderer _tunnel;
        private MaterialPropertyBlock _properties;
        private float _difficulty;
        private float _elapsed;
        private float _penalties;
        private float _lastPenalty = -99f;
        private float _flash;
        private float _finishedAt;
        private int _contacts;
        private bool _running;
        private bool _finished;
        private float _bestTime = -1f;
        private int _seed;

        /// <summary>Clock for the current run, penalties included.</summary>
        public float RunTime {
            get { return _elapsed + _penalties; }
        }

        /// <summary>Best completed time this session, or -1 if none.</summary>
        public float BestTime {
            get { return _bestTime; }
        }

        /// <summary>Wall contacts in the current run.</summary>
        public int Contacts {
            get { return _contacts; }
        }

        /// <summary>True while a run is underway.</summary>
        public bool IsRunning {
            get { return _running; }
        }

        /// <summary>Current difficulty, 0 easiest.</summary>
        public float Difficulty {
            get { return _difficulty; }
        }

        private void Awake() {
            if (path == null) {
                path = GetComponentInChildren<ProbePath>();
            }
            if (tip == null) {
                tip = FindFirstObjectByType<StylusTip>();
            }
            if (scene == null) {
                scene = FindFirstObjectByType<ShowcaseScene>();
            }
            _properties = new MaterialPropertyBlock();
            if (path != null) {
                _tunnel = path.GetComponent<MeshRenderer>();
            }
            _difficulty = startDifficulty;
            _seed = Random.Range(int.MinValue, int.MaxValue);
        }

        private void Start() {
            Regenerate();
        }

        private void OnEnable() {
            if (scene != null) {
                scene.StateChanged += OnStateChanged;
            }
        }

        private void OnDisable() {
            if (scene != null) {
                scene.StateChanged -= OnStateChanged;
            }
        }

        private void OnStateChanged(ShowcaseState state) {
            if (state != ShowcaseState.Attract) {
                return;
            }
            // A fresh visitor starts from the beginning, on an easy tunnel, with no inherited score.
            _difficulty = startDifficulty;
            _bestTime = -1f;
            Regenerate();
        }

        /// <summary>Builds a new tunnel and clears the run.</summary>
        public void Regenerate() {
            if (path == null) {
                return;
            }
            _seed = Random.Range(int.MinValue, int.MaxValue);
            path.Generate(_seed, _difficulty);
            ResetRun();
        }

        private void ResetRun() {
            _running = false;
            _finished = false;
            _elapsed = 0f;
            _penalties = 0f;
            _contacts = 0;
            _flash = 0f;
        }

        private void Update() {
            if (path == null || !path.IsBuilt || tip == null) {
                return;
            }

            float distance;
            float progress;
            if (!path.TryGetNearest(tip.Position, out distance, out progress)) {
                return;
            }
            bool insideLumen = distance <= path.LumenRadius;

            if (_finished) {
                UpdateAttract(false);
                ApplyColour(finishedColor);
                // A fresh tunnel, one step harder, for the same player. Deliberately not routed
                // through the scene's own reset: that returns to Attract, which is the signal that
                // somebody new has walked up, and it clears the difficulty and the best time. A
                // player who has just finished a run has not gone anywhere.
                if (Time.time - _finishedAt >= replayDelay) {
                    Regenerate();
                }
                return;
            }

            if (!_running) {
                bool atMouth = insideLumen && progress <= entryProgress;
                if (atMouth) {
                    StartRun();
                } else {
                    UpdateAttract(true);
                }
            }

            if (_running) {
                UpdateAttract(false);
                _elapsed += Time.deltaTime;
                if (!insideLumen) {
                    RegisterContact();
                }
                if (insideLumen && progress >= finishProgress) {
                    FinishRun();
                }
            }

            _flash = Mathf.Max(0f, _flash - Time.deltaTime / Mathf.Max(0.01f, flashFade));
            ApplyColour(Color.Lerp(safeColor, contactColor, _flash));
        }

        private void StartRun() {
            _running = true;
            _elapsed = 0f;
            _penalties = 0f;
            _contacts = 0;
            if (scene != null) {
                scene.BeginPlay();
            }
            if (audioCues != null) {
                audioCues.PlayChime(1.3f);
            }
        }

        private void RegisterContact() {
            _flash = 1f;
            if (Time.time - _lastPenalty < penaltyCooldown) {
                return;
            }
            _lastPenalty = Time.time;
            _contacts++;
            _penalties += penaltySeconds;
            if (haptics != null) {
                haptics.Error();
            }
            if (audioCues != null) {
                audioCues.PlayChime(0.55f);
            }
        }

        private void FinishRun() {
            _running = false;
            _finished = true;
            _finishedAt = Time.time;
            float total = RunTime;
            if (_bestTime < 0f || total < _bestTime) {
                _bestTime = total;
            }
            // Each clean run tightens the next tunnel, so the scene stays interesting to someone
            // who is good at it without ever being impossible for someone who is not.
            _difficulty = Mathf.Clamp01(_difficulty + difficultyStep);
            if (audioCues != null) {
                audioCues.PlayChime(1.8f);
            }
            if (haptics != null) {
                haptics.Grab();
            }
            if (scene != null) {
                scene.Resolve();
            }
        }

        /// <summary>
        /// Runs a marker up and down the tunnel while nobody is playing. It demonstrates the task
        /// exactly - go in at the mouth, follow the middle, come out the far end - with no caption
        /// and nothing to maintain.
        /// </summary>
        private void UpdateAttract(bool active) {
            if (attractMarker == null) {
                return;
            }
            if (!active) {
                if (attractMarker.gameObject.activeSelf) {
                    attractMarker.gameObject.SetActive(false);
                }
                return;
            }
            if (!attractMarker.gameObject.activeSelf) {
                attractMarker.gameObject.SetActive(true);
            }
            float cycle = Mathf.Max(0.5f, attractTravelSeconds);
            float t = Mathf.PingPong(Time.time / cycle, 1f);
            attractMarker.position = path.SampleAt(t);
            float size = path.LumenRadius * 0.45f;
            attractMarker.localScale = new Vector3(size, size, size);
        }

        private void ApplyColour(Color color) {
            if (_tunnel == null) {
                return;
            }
            _properties.Clear();
            _properties.SetColor("_BaseColor", color);
            _properties.SetColor("_Color", color);
            _tunnel.SetPropertyBlock(_properties);
        }
    }
}
