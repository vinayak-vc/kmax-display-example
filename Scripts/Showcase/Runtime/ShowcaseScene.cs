using System;
using UnityEngine;
using UnityEngine.Events;

namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// The lifecycle every scene in the suite shares, and the idle reset that lets it run
    /// unattended.
    ///
    /// Four states rather than two, because the difference between someone having arrived and
    /// someone having started matters. A viewer who has picked the pen up and is turning it over
    /// is <see cref="ShowcaseState.Engaged"/> - the attract loop must stop, but a timer must not
    /// start, or every score is ruined by the seconds spent working out what the scene is. The
    /// clock starts at <see cref="ShowcaseState.Playing"/>, which the scene declares when the task
    /// actually begins.
    ///
    /// <para>This is a plain component with events rather than a base class to inherit from. A
    /// scene's own logic subscribes to the transitions it cares about and ignores the rest, which
    /// keeps the four scenes from having to share a shape they do not have.</para>
    /// </summary>
    public class ShowcaseScene : MonoBehaviour {
        [Header("References")]
        [SerializeField, Tooltip("Watched for movement to decide whether anybody is here. Found " +
            "in the scene on first use when left empty.")]
        private StylusTip tip;

        [Header("Idle")]
        [SerializeField, Tooltip("Seconds without activity before the scene gives up and returns " +
            "to its attract loop. The eye and the car both use 30; a task-based scene can afford " +
            "longer, because a viewer thinking is not a viewer who has left.")]
        private float idleDelay = 45f;
        [SerializeField, Tooltip("Metres the tip must move to count as someone being here.")]
        private float tipWakeThreshold = 0.01f;
        [SerializeField, Tooltip("Pixels the mouse must move to count as someone being here. " +
            "Kept so the scene can be exercised without the pen.")]
        private float mouseWakeThreshold = 8f;

        [Header("Resolution")]
        [SerializeField, Tooltip("Seconds a finished run stays on screen before the scene resets " +
            "itself. Long enough to read a score, short enough that the next visitor gets a clean " +
            "scene.")]
        private float resolvedHoldSeconds = 8f;
        [SerializeField, Tooltip("Reset automatically once the hold has elapsed. Off leaves the " +
            "result up until something calls ResetScene.")]
        private bool autoResetWhenResolved = true;

        [Header("Events")]
        [SerializeField, Tooltip("Entering attract: nobody is here, demonstrate the scene.")]
        private UnityEvent attractBegan = new UnityEvent();
        [SerializeField, Tooltip("Somebody has arrived. Stop the attract loop.")]
        private UnityEvent engaged = new UnityEvent();
        [SerializeField, Tooltip("The task has actually started. Start any clock here.")]
        private UnityEvent playBegan = new UnityEvent();
        [SerializeField, Tooltip("The task has finished. Show the result.")]
        private UnityEvent resolved = new UnityEvent();
        [SerializeField, Tooltip("Put the scene back to its starting state.")]
        private UnityEvent sceneReset = new UnityEvent();

        private ShowcaseState _state = ShowcaseState.Attract;
        private float _lastActivityTime;
        private float _resolvedAt;
        private float _playStartedAt;
        private Vector3 _lastTipPosition;
        private Vector3 _lastMousePosition;
        private bool _hasTipSample;

        /// <summary>Raised on every state change, with the state just entered.</summary>
        public event Action<ShowcaseState> StateChanged;

        /// <summary>Where the scene is in its lifecycle.</summary>
        public ShowcaseState State {
            get { return _state; }
        }

        /// <summary>
        /// Seconds since the task started, or zero when it has not. Frozen once resolved, so a
        /// scene can read the final time after the fact.
        /// </summary>
        public float PlayDuration {
            get {
                if (_state == ShowcaseState.Playing) {
                    return Time.time - _playStartedAt;
                }
                if (_state == ShowcaseState.Resolved) {
                    return _resolvedAt - _playStartedAt;
                }
                return 0f;
            }
        }

        private void Awake() {
            if (tip == null) {
                tip = FindFirstObjectByType<StylusTip>();
            }
            _lastActivityTime = Time.time;
            _lastMousePosition = Input.mousePosition;
        }

        private void Start() {
            EnterState(ShowcaseState.Attract);
        }

        private void Update() {
            if (DetectActivity()) {
                _lastActivityTime = Time.time;
                if (_state == ShowcaseState.Attract) {
                    EnterState(ShowcaseState.Engaged);
                }
            }

            if (_state == ShowcaseState.Resolved && autoResetWhenResolved
                && Time.time - _resolvedAt >= resolvedHoldSeconds) {
                ResetScene();
                return;
            }

            if (_state != ShowcaseState.Attract && Time.time - _lastActivityTime >= idleDelay) {
                ResetScene();
            }
        }

        /// <summary>
        /// Somebody is here. Called automatically when the tip or the mouse moves; public so a
        /// scene can also engage on something of its own, like a button press.
        /// </summary>
        public void Engage() {
            _lastActivityTime = Time.time;
            if (_state == ShowcaseState.Attract) {
                EnterState(ShowcaseState.Engaged);
            }
        }

        /// <summary>
        /// The task has begun - the first block lifted, the probe entered. Starts the clock.
        /// Ignored if the scene is already playing.
        /// </summary>
        public void BeginPlay() {
            if (_state == ShowcaseState.Playing) {
                return;
            }
            _playStartedAt = Time.time;
            EnterState(ShowcaseState.Playing);
        }

        /// <summary>
        /// The task has finished. Freezes <see cref="PlayDuration"/> and starts the hold before
        /// the automatic reset.
        /// </summary>
        public void Resolve() {
            if (_state == ShowcaseState.Resolved) {
                return;
            }
            _resolvedAt = Time.time;
            EnterState(ShowcaseState.Resolved);
        }

        /// <summary>
        /// Puts the scene back to its starting state and returns to the attract loop. Not named
        /// Reset, which Unity reserves for the editor message raised when a component is added.
        /// </summary>
        public void ResetScene() {
            sceneReset.Invoke();
            EnterState(ShowcaseState.Attract);
        }

        private void EnterState(ShowcaseState next) {
            _state = next;
            switch (next) {
                case ShowcaseState.Attract:
                    attractBegan.Invoke();
                    break;
                case ShowcaseState.Engaged:
                    engaged.Invoke();
                    break;
                case ShowcaseState.Playing:
                    playBegan.Invoke();
                    break;
                case ShowcaseState.Resolved:
                    resolved.Invoke();
                    break;
            }
            if (StateChanged != null) {
                StateChanged(next);
            }
        }

        private bool DetectActivity() {
            bool active = false;

            if (tip != null) {
                Vector3 position = tip.Position;
                if (!_hasTipSample) {
                    _lastTipPosition = position;
                    _hasTipSample = true;
                } else if (Vector3.Distance(position, _lastTipPosition) >= tipWakeThreshold) {
                    _lastTipPosition = position;
                    active = true;
                }
            }

            Vector3 mouse = Input.mousePosition;
            if (Vector3.Distance(mouse, _lastMousePosition) >= mouseWakeThreshold) {
                _lastMousePosition = mouse;
                active = true;
            }

            return active;
        }
    }
}
