using UnityEngine;

namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// Stack: pick blocks up off the bench with the pen tip and build a tower on the platform.
    ///
    /// This is the scene the whole suite is built to reach, because it is the one that cannot be
    /// done without stereo. Judging whether a block is above the tower or in front of it is a pure
    /// depth question, the failure is immediate and legible, and nobody has to be told what the
    /// task is.
    ///
    /// <para><b>The platform sits at zero parallax and the held block pops out.</b> The instinct is
    /// the other way round - pop out the thing you are aiming at - and it is wrong. The screen
    /// plane is the most comfortable and most precisely resolved place on the display, which is
    /// exactly what a target needs; the pop-out belongs on the object in your hand, where the
    /// depth contrast is doing work instead of decoration.</para>
    ///
    /// <para><b>Gravity is lowered, and not for feel alone.</b> The blocks are about 40 mm because
    /// that is what fits the comfort volume, but they are standing in for objects five times that
    /// size. Earth gravity on a 40 mm block drops it its own height in 90 ms, which reads as
    /// frantic and is genuinely too fast to place anything into. Dividing g by the same factor the
    /// world is scaled down by restores the timing of the object being portrayed.</para>
    /// </summary>
    public class StackGame : MonoBehaviour {
        [Header("References")]
        [SerializeField, Tooltip("Surface blocks are stacked on. Its top face is where height is " +
            "measured from.")]
        private Transform platform;
        [SerializeField, Tooltip("Parent of the blocks. All StackBlocks under it are found at startup.")]
        private Transform blockRoot;
        [SerializeField, Tooltip("Drives the grab. Watched to know when a run has begun.")]
        private StylusGrab grab;
        [SerializeField, Tooltip("Pulsed when a held block knocks something. Optional.")]
        private StylusHaptics haptics;
        [SerializeField, Tooltip("Chimes when a block is placed. Optional.")]
        private ShowcaseAudio audioCues;
        [SerializeField, Tooltip("Lifecycle. Told when a run starts and when the tower falls.")]
        private ShowcaseScene scene;

        [Header("Physics")]
        [SerializeField, Tooltip("Downward acceleration in metres per second squared. Earth is " +
            "9.81; this scene runs far lower because its blocks are a scale model of larger ones. " +
            "Applied globally while the scene is loaded and restored when it unloads.")]
        private float gravity = 2f;
        [SerializeField, Tooltip("Metres below the platform top at which a block counts as lost " +
            "off the bench and is returned to its start.")]
        private float killDrop = 0.25f;

        [Header("Scoring")]
        [SerializeField, Tooltip("Metres the settled height must fall from its peak to count as a " +
            "collapse rather than a block shifting.")]
        private float toppleDrop = 0.025f;
        [SerializeField, Tooltip("Metres the tower must reach before a collapse is worth calling. " +
            "Below this a run has not really started and a knocked block should not end it.")]
        private float minimumTowerHeight = 0.05f;
        [SerializeField, Tooltip("Metres beyond the platform edge that still counts as part of the " +
            "tower. A block overhanging slightly is still stacked; one sitting on the bench is not.")]
        private float platformMargin = 0.01f;

        [Header("Attract")]
        [SerializeField, Tooltip("Seconds between idle nudges. While nobody is here a block is " +
            "tossed gently, which says the blocks are loose and liftable without any instructions.")]
        private float nudgeInterval = 4f;
        [SerializeField, Tooltip("Upward impulse of an idle nudge.")]
        private float nudgeImpulse = 0.0025f;

        private StackBlock[] _blocks;
        private Vector3 _originalGravity;
        private float _peakHeight;
        private float _bestHeight;
        private float _nextNudge;
        private bool _wasHolding;
        private bool _homeMarked;

        /// <summary>Height of the settled tower above the platform, in metres.</summary>
        public float CurrentHeight { get; private set; }

        /// <summary>Best height reached since the scene last reset.</summary>
        public float BestHeight {
            get { return _bestHeight; }
        }

        /// <summary>How many blocks are currently settled above the platform.</summary>
        public int StackedCount { get; private set; }

        private void Awake() {
            _originalGravity = Physics.gravity;
            Physics.gravity = new Vector3(0f, -Mathf.Abs(gravity), 0f);

            if (blockRoot == null) {
                blockRoot = transform;
            }
            _blocks = blockRoot.GetComponentsInChildren<StackBlock>(true);
            for (int i = 0; i < _blocks.Length; i++) {
                _blocks[i].Bind(this);
            }
            if (scene == null) {
                scene = FindFirstObjectByType<ShowcaseScene>();
            }
            if (grab == null) {
                grab = FindFirstObjectByType<StylusGrab>();
            }
        }

        private void OnDestroy() {
            // Gravity is a project-wide setting, not a scene one. Leaving it lowered would quietly
            // change the physics of every other scene loaded afterwards in the same session.
            Physics.gravity = _originalGravity;
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
            if (state == ShowcaseState.Attract) {
                ResetBlocks();
            }
        }

        private void Update() {
            RecoverLostBlocks();
            Measure();
            WatchForStart();
            WatchForCollapse();
            Attract();
            MarkHomeOnce();
        }

        /// <summary>
        /// Pulses the pen when a held block strikes something. Called by <see cref="StackBlock"/>.
        /// </summary>
        public void ReportKnock(float strength01) {
            if (haptics != null) {
                haptics.Impact(strength01);
            }
        }

        /// <summary>Returns every block to its starting arrangement and clears the score.</summary>
        public void ResetBlocks() {
            if (_blocks == null) {
                return;
            }
            for (int i = 0; i < _blocks.Length; i++) {
                _blocks[i].ReturnHome();
            }
            _peakHeight = 0f;
            _bestHeight = 0f;
            CurrentHeight = 0f;
            StackedCount = 0;
        }

        /// <summary>
        /// The first scatter settles under physics rather than being authored block by block, so
        /// the arrangement a reset returns to is recorded once it has come to rest.
        /// </summary>
        private void MarkHomeOnce() {
            if (_homeMarked || _blocks == null || Time.timeSinceLevelLoad < 1.5f) {
                return;
            }
            for (int i = 0; i < _blocks.Length; i++) {
                if (!_blocks[i].IsSettled) {
                    return;
                }
            }
            for (int i = 0; i < _blocks.Length; i++) {
                _blocks[i].MarkHome();
            }
            _homeMarked = true;
        }

        private float PlatformTop() {
            if (platform == null) {
                return 0f;
            }
            Renderer renderer = platform.GetComponentInChildren<Renderer>();
            if (renderer != null) {
                return renderer.bounds.max.y;
            }
            return platform.position.y;
        }

        /// <summary>
        /// True when a point is over the platform, ignoring height.
        ///
        /// Height alone is not enough to decide what is part of the tower. The bench top sits
        /// slightly below the platform top, so every block still lying where it started reads as
        /// being above the platform - which made a fresh scene score ten stacked blocks before
        /// anyone had touched anything, and would have let a block nudged on the bench register as
        /// a collapse.
        /// </summary>
        private bool IsOverPlatform(Vector3 point) {
            if (platform == null) {
                return true;
            }
            Renderer renderer = platform.GetComponentInChildren<Renderer>();
            if (renderer == null) {
                return true;
            }
            Bounds footprint = renderer.bounds;
            return point.x >= footprint.min.x - platformMargin
                && point.x <= footprint.max.x + platformMargin
                && point.z >= footprint.min.z - platformMargin
                && point.z <= footprint.max.z + platformMargin;
        }

        private void Measure() {
            if (_blocks == null) {
                return;
            }
            float top = PlatformTop();
            float highest = top;
            int stacked = 0;
            for (int i = 0; i < _blocks.Length; i++) {
                StackBlock block = _blocks[i];
                if (!block.IsSettled) {
                    continue;
                }
                if (!IsOverPlatform(block.transform.position)) {
                    continue;
                }
                float blockTop = block.TopY;
                if (blockTop <= top + 0.001f) {
                    continue;
                }
                stacked++;
                if (blockTop > highest) {
                    highest = blockTop;
                }
            }
            StackedCount = stacked;
            CurrentHeight = Mathf.Max(0f, highest - top);
            if (CurrentHeight > _peakHeight) {
                _peakHeight = CurrentHeight;
            }
            if (CurrentHeight > _bestHeight) {
                _bestHeight = CurrentHeight;
            }
        }

        private void WatchForStart() {
            bool holding = grab != null && grab.IsHolding;
            if (holding && !_wasHolding) {
                if (scene != null) {
                    scene.BeginPlay();
                }
                if (audioCues != null) {
                    audioCues.PlayChime(1.2f);
                }
            }
            if (!holding && _wasHolding && audioCues != null) {
                audioCues.PlayChime(0.9f);
            }
            _wasHolding = holding;
        }

        private void WatchForCollapse() {
            if (scene == null || scene.State != ShowcaseState.Playing) {
                return;
            }
            if (grab != null && grab.IsHolding) {
                return;
            }
            if (_peakHeight < minimumTowerHeight) {
                return;
            }
            if (CurrentHeight > _peakHeight - toppleDrop) {
                return;
            }
            scene.Resolve();
        }

        /// <summary>
        /// While nobody is here, a block is tossed gently every few seconds. It says the blocks are
        /// loose and can be picked up, using the physics already in the scene, without a caption
        /// and without a rehearsed demonstration that would have to be maintained.
        /// </summary>
        private void Attract() {
            if (scene == null || scene.State != ShowcaseState.Attract || _blocks == null
                || _blocks.Length == 0) {
                return;
            }
            if (Time.time < _nextNudge) {
                return;
            }
            _nextNudge = Time.time + nudgeInterval;
            StackBlock block = _blocks[Random.Range(0, _blocks.Length)];
            if (!block.IsSettled) {
                return;
            }
            block.Body.AddForce(Vector3.up * nudgeImpulse, ForceMode.Impulse);
            block.Body.AddTorque(new Vector3(
                Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f))
                * nudgeImpulse * 0.15f, ForceMode.Impulse);
        }

        private void RecoverLostBlocks() {
            if (_blocks == null) {
                return;
            }
            float floor = PlatformTop() - killDrop;
            for (int i = 0; i < _blocks.Length; i++) {
                if (_blocks[i].IsHeld || _blocks[i].transform.position.y >= floor) {
                    continue;
                }
                _blocks[i].ReturnHome();
            }
        }
    }
}
