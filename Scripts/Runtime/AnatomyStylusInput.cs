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

        /// <summary>
        /// How a held orbit button turns into rotation.
        /// </summary>
        public enum OrbitMode {
            /// <summary>Screen-space travel of the aim point, scaled to degrees. Mouse parity.</summary>
            ScreenDrag,
            /// <summary>The pen's own change in aim angle, applied one for one. Feels physical.</summary>
            WristTurn
        }

        [Header("Feel")]
        [SerializeField, Tooltip("How a held orbit button becomes rotation. Wrist turn maps the pen's " +
            "own rotation onto the view one for one, which is the gesture a 6-DOF wand invites - " +
            "screen drag reproduces the mouse exactly.")]
        private OrbitMode orbitMode = OrbitMode.WristTurn;
        [SerializeField, Tooltip("Degrees orbited per pixel the aim point travels. Screen drag mode only.")]
        private float orbitSpeed = 0.25f;
        [SerializeField, Tooltip("Pixels the aim point must travel before a press becomes a drag rather than a click.")]
        private float dragThreshold = 6f;
        [SerializeField, Range(0.25f, 4f), Tooltip("Degrees of view rotation per degree of pen rotation. " +
            "1 is literally one for one; a little above that saves the wrist on a wide turn.")]
        private float wristTurnGain = 1.35f;
        [SerializeField, Tooltip("Degrees the pen must turn before a press becomes a drag rather than a click.")]
        private float wristDragThreshold = 1.5f;
        [SerializeField, Tooltip("Flip the wrist turn so the model follows the pen like a held object, " +
            "rather than orbiting the view around it. Off keeps it consistent with the mouse.")]
        private bool invertWristTurn;
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
        private Vector2 _lastAimAngles;
        private Vector2 _pressAimAngles;

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
            Vector2 angles = GetAimAngles();

            if (!_isOrbitPressed) {
                _isOrbitPressed = true;
                _isOrbiting = false;
                _lastAimPoint = aim;
                _pressAimPoint = aim;
                _lastAimAngles = angles;
                _pressAimAngles = angles;

                // A press that lands on the UI belongs to the button under it. A press on the eye,
                // on a badge, or on empty space is the viewer reaching for the model.
                _orbitSuppressed = IsPressOnUI();
                return;
            }

            if (_orbitSuppressed) {
                return;
            }

            if (orbitMode == OrbitMode.WristTurn) {
                UpdateWristTurn(angles);
                return;
            }

            UpdateScreenDrag(aim);
        }

        /// <summary>
        /// Rotation from how far the aim point travelled across the screen, in pixels. Identical in
        /// feel to a mouse drag, which is what makes the two input paths interchangeable.
        /// </summary>
        private void UpdateScreenDrag(Vector2 aim) {
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

        /// <summary>
        /// Rotation from how far the pen itself turned, in degrees, applied one for one.
        ///
        /// This is the gesture a tracked wand actually invites: take hold of the model and turn
        /// your wrist. Screen-space dragging works, but it scales by the projection - the same
        /// hand movement rotates by a different amount depending on how far the camera is dollied -
        /// where an angle is an angle at any distance.
        /// </summary>
        private void UpdateWristTurn(Vector2 angles) {
            if (!_isOrbiting) {
                float travelled = Mathf.Max(
                    Mathf.Abs(Mathf.DeltaAngle(_pressAimAngles.x, angles.x)),
                    Mathf.Abs(Mathf.DeltaAngle(_pressAimAngles.y, angles.y)));

                if (travelled < wristDragThreshold) {
                    _lastAimAngles = angles;
                    return;
                }

                _isOrbiting = true;
                _lastAimAngles = _pressAimAngles;
            }

            // DeltaAngle rather than subtraction, so a pen crossing the +/-180 seam does not
            // register as most of a full turn.
            float yaw = Mathf.DeltaAngle(_lastAimAngles.x, angles.x);
            float pitch = Mathf.DeltaAngle(_lastAimAngles.y, angles.y);
            _lastAimAngles = angles;

            if (Mathf.Approximately(yaw, 0f) && Mathf.Approximately(pitch, 0f)) {
                return;
            }

            float gain = wristTurnGain * (invertWristTurn ? -1f : 1f);
            flyController.AddOrbitDelta(yaw * gain, pitch * gain);
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
        /// Where the pen is aiming, as yaw and pitch in degrees, measured in the rig's own space.
        ///
        /// Rig space matters: the pen hangs off the rig, so its world rotation turns with the view.
        /// Measuring in world space would feed the orbit back into its own input and the model
        /// would keep spinning after the hand stopped.
        /// </summary>
        private Vector2 GetAimAngles() {
            Pose pose = stylus.StartpointPose;
            Quaternion local = rigRoot != null
                ? Quaternion.Inverse(rigRoot.rotation) * pose.rotation
                : pose.rotation;

            Vector3 forward = local * Vector3.forward;
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            return new Vector2(yaw, pitch);
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
