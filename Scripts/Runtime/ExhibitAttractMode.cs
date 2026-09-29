using KmaxXR;
using TMPro;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Demonstrates the exhibit to an empty room.
    ///
    /// After a stretch with no input the eye opens itself and begins touring its structures, the
    /// camera flying to each one and drifting gently while it sits there, with a prompt inviting
    /// whoever is walking past to take over. Any input hands control straight back.
    ///
    /// This is the piece a standing exhibit most needs and is easiest to leave out. A display
    /// showing a static model reads as a screensaver from three metres away; a moving one reads as
    /// something worth crossing the room for - and slow orbital motion is the only thing that
    /// conveys stereo depth to someone who is not yet close enough to be head-tracked.
    /// </summary>
    public class ExhibitAttractMode : MonoBehaviour {
        [Header("References")]
        [SerializeField, Tooltip("Drives the tour. Found on this object when left empty.")]
        private EyeAnatomyController exhibitController;
        [SerializeField, Tooltip("Orbited while idle. Found on this object when left empty.")]
        private ViewerFlyController flyController;
        [SerializeField, Tooltip("Only read, to report whether the eye is open. The controller does " +
            "the opening itself as part of stepping to a structure.")]
        private EyeExplodeView explodeView;
        [SerializeField, Tooltip("Optional. Faded up while idle to invite a passer-by.")]
        private TextMeshProUGUI promptLabel;

        [Header("Timing")]
        [SerializeField, Tooltip("Seconds without input before the tour begins.")]
        private float idleDelay = 30f;
        [SerializeField, Tooltip("Seconds spent on each structure before moving to the next.")]
        private float dwellPerPart = 5.5f;
        [SerializeField, Tooltip("Seconds the prompt takes to fade in and out.")]
        private float promptFadeDuration = 1.2f;

        [Header("Motion")]
        [SerializeField, Tooltip("Degrees per second the view drifts while resting on a structure. " +
            "Slow on purpose - this reads as presence, and anything faster reads as a screensaver.")]
        private float driftSpeed = 5f;

        [Header("Input Sensitivity")]
        [SerializeField, Tooltip("Pixels the mouse must move to count as someone arriving.")]
        private float mouseWakeThreshold = 8f;
        [SerializeField, Tooltip("Degrees the pen must turn to count as someone arriving. The pen " +
            "jitters slightly even at rest, so this cannot be zero.")]
        private float penWakeThreshold = 3f;

        private bool _isAttracting;
        private float _lastActivityTime;
        private float _nextPartTime;
        private float _promptAlpha;

        private Vector3 _lastMousePosition;
        private Quaternion _lastPenRotation = Quaternion.identity;
        private bool _hasPenReference;

        /// <summary>
        /// True while the exhibit is showing itself off rather than responding to someone.
        /// </summary>
        public bool IsAttracting {
            get { return _isAttracting; }
        }

        private void Awake() {
            if (exhibitController == null) {
                exhibitController = GetComponent<EyeAnatomyController>();
            }

            if (flyController == null) {
                flyController = GetComponent<ViewerFlyController>();
            }

            if (explodeView == null) {
                explodeView = GetComponent<EyeExplodeView>();
            }

            if (exhibitController == null || flyController == null) {
                Debug.LogError($"{nameof(ExhibitAttractMode)} on '{name}' is missing the controller or the " +
                    "fly controller; the attract loop is disabled.", this);
                enabled = false;
                return;
            }

            _lastMousePosition = Input.mousePosition;
            _lastActivityTime = Time.unscaledTime;
        }

        private void Start() {
            SetPromptAlpha(0f);
        }

        private void Update() {
            if (DetectActivity()) {
                _lastActivityTime = Time.unscaledTime;

                if (_isAttracting) {
                    Stop();
                }
            } else if (!_isAttracting && Time.unscaledTime - _lastActivityTime >= idleDelay) {
                Begin();
            }

            if (_isAttracting) {
                UpdateTour();
            }

            UpdatePrompt();
        }

        /// <summary>
        /// Starts the tour immediately, without waiting out the idle delay.
        /// </summary>
        [ContextMenu("Begin")]
        public void Begin() {
            if (_isAttracting) {
                return;
            }

            _isAttracting = true;

            // Deliberately does not open the eye here. SelectNextPart already opens it and queues
            // the selection until the explode settles - and that completion is also when the
            // controller caches the per-part view directions the camera flight needs. Expanding
            // first makes the controller take its immediate path instead, the directions are not
            // ready yet, and every flight is silently skipped.
            _nextPartTime = 0f;
        }

        /// <summary>
        /// Hands control back.
        ///
        /// Deliberately leaves the view wherever the tour had reached. Snapping home the moment
        /// someone touches the pen would take away the very thing that drew them over.
        /// </summary>
        [ContextMenu("Stop")]
        public void Stop() {
            _isAttracting = false;
        }

        private void UpdateTour() {
            if (Time.unscaledTime >= _nextPartTime) {
                _nextPartTime = Time.unscaledTime + Mathf.Max(1f, dwellPerPart);
                exhibitController.SelectNextPart();
                return;
            }

            // Drift only between flights. Any orbit delta cancels a flight in progress, so drifting
            // through one would cut every camera move short and the tour would never arrive.
            if (!flyController.IsFlying && driftSpeed != 0f) {
                flyController.AddOrbitDelta(driftSpeed * Time.deltaTime, 0f);
            }
        }

        /// <summary>
        /// True on any sign of a person: mouse, keyboard, scroll, or the pen being pressed or moved.
        /// </summary>
        private bool DetectActivity() {
            if (Input.anyKeyDown || Input.GetMouseButton(0) || Input.GetMouseButton(1)) {
                return true;
            }

            if (!Mathf.Approximately(Input.mouseScrollDelta.y, 0f)) {
                return true;
            }

            Vector3 mouse = Input.mousePosition;
            if ((mouse - _lastMousePosition).sqrMagnitude >= mouseWakeThreshold * mouseWakeThreshold) {
                _lastMousePosition = mouse;
                return true;
            }

            _lastMousePosition = mouse;
            return DetectPenActivity();
        }

        private bool DetectPenActivity() {
            KmaxStylus stylus = KmaxPointer.PointerById(KmaxStylus.UniqueId) as KmaxStylus;
            if (stylus == null || !stylus.Visible) {
                _hasPenReference = false;
                return false;
            }

            if (stylus.AnyButtonPressed) {
                return true;
            }

            Quaternion rotation = stylus.StartpointPose.rotation;

            if (!_hasPenReference) {
                _lastPenRotation = rotation;
                _hasPenReference = true;
                return false;
            }

            // A tracked pen is never perfectly still, so this asks for a deliberate movement rather
            // than any change at all - otherwise the exhibit could never go idle with a pen on the
            // desk beside it.
            if (Quaternion.Angle(_lastPenRotation, rotation) < penWakeThreshold) {
                return false;
            }

            _lastPenRotation = rotation;
            return true;
        }

        private void UpdatePrompt() {
            if (promptLabel == null) {
                return;
            }

            float target = _isAttracting ? 1f : 0f;
            float step = promptFadeDuration > 0f ? Time.unscaledDeltaTime / promptFadeDuration : 1f;
            _promptAlpha = Mathf.MoveTowards(_promptAlpha, target, step);

            // A slow breath on top of the fade, so the invitation reads as alive rather than as a
            // caption someone forgot to remove.
            float breath = 0.78f + 0.22f * Mathf.Sin(Time.unscaledTime * 1.6f);
            SetPromptAlpha(_promptAlpha * breath);
        }

        private void SetPromptAlpha(float alpha) {
            if (promptLabel == null) {
                return;
            }

            bool visible = alpha > 0.01f;
            if (promptLabel.gameObject.activeSelf != visible) {
                promptLabel.gameObject.SetActive(visible);
            }

            Color color = promptLabel.color;
            color.a = alpha;
            promptLabel.color = color;
        }
    }
}
