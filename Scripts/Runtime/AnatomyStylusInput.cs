using KmaxXR;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Drives the exhibit from the Kmax stylus.
    ///
    /// The pen reports exactly three buttons - <see cref="KmaxStylus.StylusButtnLeft"/> (index 0,
    /// the front button), <see cref="KmaxStylus.StylusButtnRigth"/> (index 1) and
    /// <see cref="KmaxStylus.StylusButtnCenter"/> (index 2) - read through
    /// <c>IStylus.GetButton(0..2)</c>. They are mapped here as:
    ///
    /// <list type="bullet">
    /// <item><b>0, primary</b> - press to select, press and drag to orbit. This is also
    /// <see cref="KmaxStylus.PrimaryKey"/>, so the input module raises its click events.</item>
    /// <item><b>1, secondary</b> - tap to reset the view, matching the Reset button and the R key.</item>
    /// <item><b>2, centre</b> - hold and push or pull the pen to dolly in and out.</item>
    /// </list>
    ///
    /// Without this component the stylus moves the pointer but cannot navigate:
    /// <see cref="ViewerFlyController"/> reads <c>Input.GetMouseButton</c>, which a 6-DOF pen never
    /// sets. Both input paths stay live, so mouse and stylus behave identically.
    /// </summary>
    public class AnatomyStylusInput : MonoBehaviour {
        [Header("References")]
        [SerializeField, Tooltip("The pen. Found in the scene on first use when left empty.")]
        private KmaxStylus stylus;
        [SerializeField, Tooltip("Camera rig this orbits. Found on this object when left empty.")]
        private ViewerFlyController flyController;
        [SerializeField, Tooltip("Notified when the reset button is tapped, so focus and materials clear too.")]
        private EyeAnatomyController exhibitController;
        [SerializeField, Tooltip("Rig root, used to measure push and pull along the viewing axis.")]
        private Transform rigRoot;

        [Header("Button Mapping")]
        [SerializeField, Range(0, 2), Tooltip("Button that selects, and that orbits when dragged. Index 0 is the front button.")]
        private int orbitButton = 0;
        [SerializeField, Range(0, 2), Tooltip("Button that resets the view on a tap.")]
        private int resetButton = 1;
        [SerializeField, Range(0, 2), Tooltip("Button held to dolly by pushing and pulling the pen.")]
        private int dollyButton = 2;
        [SerializeField, Tooltip("Enable the push and pull dolly on the centre button.")]
        private bool enableDolly = true;

        [Header("Feel")]
        [SerializeField, Tooltip("Degrees orbited per pixel the aim point travels. Matches the mouse drag speed.")]
        private float orbitSpeed = 0.25f;
        [SerializeField, Tooltip("Pixels the aim point must travel before a press becomes a drag rather than a click.")]
        private float dragThreshold = 6f;
        [SerializeField, Tooltip("Metres dollied per metre the pen is pushed forward or pulled back.")]
        private float dollyGain = 1.6f;
        [SerializeField, Tooltip("Seconds a reset press may last and still count as a tap. Holding does nothing.")]
        private float tapMaxDuration = 0.6f;

        [Header("Feedback")]
        [SerializeField, Range(0, 100), Tooltip("Pen vibration strength when a reset tap is accepted.")]
        private int resetVibrationStrength = 40;
        [SerializeField, Tooltip("Pen vibration duration in seconds for a reset tap.")]
        private float resetVibrationDuration = 0.05f;

        private bool _isOrbitPressed;
        private bool _isOrbiting;
        private bool _orbitSuppressed;
        private Vector2 _lastAimPoint;
        private Vector2 _pressAimPoint;

        private float _resetPressTime = -1f;

        private bool _isDollying;
        private float _lastDollyDepth;

        /// <summary>
        /// True while the pen is actively dragging the view around, so other systems can hold off.
        /// </summary>
        public bool IsOrbiting {
            get { return _isOrbiting; }
        }

        private void Awake() {
            if (flyController == null) {
                flyController = GetComponent<ViewerFlyController>();
            }

            if (exhibitController == null) {
                exhibitController = GetComponent<EyeAnatomyController>();
            }

            if (flyController == null) {
                Debug.LogError($"{nameof(AnatomyStylusInput)} on '{name}' has no {nameof(flyController)} assigned; stylus navigation is disabled.", this);
                enabled = false;
            }
        }

        private void Update() {
            if (!TryResolveStylus()) {
                return;
            }

            UpdateOrbit();
            UpdateReset();
            UpdateDolly();
        }

        /// <summary>
        /// The pen is spawned with the rig and may not exist on the first frames, and it is absent
        /// entirely when no hardware is attached. Both cases degrade to the mouse path.
        /// </summary>
        private bool TryResolveStylus() {
            if (stylus == null) {
                stylus = KmaxPointer.PointerById(KmaxStylus.UniqueId) as KmaxStylus;
            }

            return stylus != null && stylus.Visible;
        }

        private void UpdateOrbit() {
            bool held = stylus.GetButton(orbitButton);

            if (!held) {
                _isOrbitPressed = false;
                _isOrbiting = false;
                _orbitSuppressed = false;
                return;
            }

            Vector2 aim = GetAimScreenPoint();

            if (!_isOrbitPressed) {
                _isOrbitPressed = true;
                _isOrbiting = false;
                _lastAimPoint = aim;
                _pressAimPoint = aim;

                // A press that lands on the UI belongs to the button under it. A press on the eye,
                // on a badge, or on empty space is the viewer reaching for the model.
                _orbitSuppressed = IsPressOnUI();
                return;
            }

            if (_orbitSuppressed) {
                return;
            }

            if (!_isOrbiting) {
                if ((aim - _pressAimPoint).magnitude < dragThreshold) {
                    _lastAimPoint = aim;
                    return;
                }

                _isOrbiting = true;
                // Start from the press point so the view does not jump by the threshold distance.
                _lastAimPoint = _pressAimPoint;
            }

            Vector2 delta = aim - _lastAimPoint;
            _lastAimPoint = aim;

            if (delta.sqrMagnitude <= Mathf.Epsilon) {
                return;
            }

            flyController.AddOrbitDelta(delta.x * orbitSpeed, -delta.y * orbitSpeed);
        }

        private void UpdateReset() {
            if (stylus.GetButtonDown(resetButton)) {
                _resetPressTime = Time.unscaledTime;
                return;
            }

            if (!stylus.GetButtonUp(resetButton) || _resetPressTime < 0f) {
                return;
            }

            bool wasTap = (Time.unscaledTime - _resetPressTime) <= tapMaxDuration;
            _resetPressTime = -1f;

            if (!wasTap) {
                return;
            }

            if (exhibitController != null) {
                exhibitController.ResetToHome();
            } else {
                flyController.ResetView(true);
            }

            stylus.VibrationOnce(resetVibrationDuration, resetVibrationStrength);
        }

        /// <summary>
        /// Pushing the pen towards the display dollies in, pulling it back dollies out. Measured
        /// along the rig's own viewing axis so it stays correct at every orbit angle.
        /// </summary>
        private void UpdateDolly() {
            if (!enableDolly) {
                return;
            }

            if (!stylus.GetButton(dollyButton)) {
                _isDollying = false;
                return;
            }

            float depth = GetPenDepth();

            if (!_isDollying) {
                _isDollying = true;
                _lastDollyDepth = depth;
                return;
            }

            float delta = depth - _lastDollyDepth;
            _lastDollyDepth = depth;

            if (Mathf.Approximately(delta, 0f)) {
                return;
            }

            flyController.AddDollyDelta(delta * dollyGain);
        }

        /// <summary>
        /// Where the pen is aiming, in screen pixels.
        ///
        /// Deliberately projected at a fixed distance along the ray rather than using
        /// <see cref="KmaxStylus.ScreenPosition"/>, which follows the hit point: crossing the edge
        /// of a part changes the hit distance, and the resulting jump would fling the view.
        /// </summary>
        private Vector2 GetAimScreenPoint() {
            Camera camera = stylus.EventCamera;
            if (camera == null) {
                return _lastAimPoint;
            }

            Pose pose = stylus.StartpointPose;
            Vector3 aimPoint = pose.position + stylus.RayLength * (pose.rotation * Vector3.forward);
            return camera.WorldToScreenPoint(aimPoint);
        }

        /// <summary>
        /// The pen's position along the rig's viewing axis, in metres.
        /// </summary>
        private float GetPenDepth() {
            Vector3 position = stylus.StartpointPose.position;
            if (rigRoot == null) {
                return position.z;
            }

            return rigRoot.InverseTransformPoint(position).z;
        }

        /// <summary>
        /// A UI hit comes from the canvas raycaster rather than the physics one, so it is the one
        /// case where <c>hitSomething</c> is true but <c>hit3D</c> is false.
        /// </summary>
        private bool IsPressOnUI() {
            KmaxStylus.PointerState state = stylus.pointerState;
            return state.hitSomething && !state.hit3D;
        }
    }
}
