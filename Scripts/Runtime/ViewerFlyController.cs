using UnityEngine;
using UnityEngine.EventSystems;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Spherical orbit camera navigation for the Kmax XRRig.
    /// Always maintains the eye model (or focused part) in the exact center of the screen
    /// across all angles and distances.
    ///
    /// - Mouse drag (Left or Right click): Orbits yaw and pitch around the center.
    /// - W / S: Flies forward / backward in orbit (dollies toward or away from the center).
    /// - A / D: Flies in an orbital circle left / right around the center.
    /// - Q / E: Flies in an orbital arc down / up around the center.
    /// - Arrow Keys: Orbit yaw (Left/Right) and pitch (Up/Down).
    /// - Left Shift: 2.5x flight speed boost.
    /// - Scroll Wheel: Dollies in / out toward the center.
    /// - R key / Reset button / Back button: Smoothly restores starting pose (0, 0, 0).
    /// </summary>
    public class ViewerFlyController : MonoBehaviour {
        [Header("Rig & Controller References")]
        [SerializeField, Tooltip("The XRRig root. Position and rotation are driven in orbit.")]
        private Transform rigRoot;
        [SerializeField, Tooltip("Main exhibit controller to notify on reset.")]
        private EyeAnatomyController exhibitController;
        [SerializeField, Tooltip("Enable orbit flying for the XRRig.")]
        private bool enableFly = true;

        [Header("Orbit Targets & Limits")]
        [SerializeField, Tooltip("Target point in world coordinates to orbit around.")]
        private Vector3 focalCenter = Vector3.zero;
        [SerializeField, Tooltip("Default distance between camera and focal center in metres.")]
        private float defaultDistance = 0.5f;
        [SerializeField, Tooltip("Minimum distance to focal center in metres.")]
        private float minDistance = 0.12f;
        [SerializeField, Tooltip("Maximum distance to focal center in metres.")]
        private float maxDistance = 1.2f;

        [Header("Speeds")]
        [SerializeField, Tooltip("Degrees turned per pixel of mouse drag.")]
        private float mouseOrbitSpeed = 0.25f;
        [SerializeField, Tooltip("Degrees turned per second with keyboard (A/D/Q/E/Arrows).")]
        private float keyOrbitSpeed = 80f;
        [SerializeField, Tooltip("Metres per second when flying forward/backward (W/S).")]
        private float keyDollySpeed = 0.35f;
        [SerializeField, Tooltip("Metres per mouse scroll wheel notch.")]
        private float scrollDollySpeed = 0.05f;
        [SerializeField, Tooltip("Speed boost multiplier when holding Shift.")]
        private float boostMultiplier = 2.5f;

        [Header("Pitch Clamps")]
        [SerializeField, Tooltip("Minimum pitch angle to prevent flipping upside down.")]
        private float minPitch = -80f;
        [SerializeField, Tooltip("Maximum pitch angle to prevent flipping upside down.")]
        private float maxPitch = 80f;

        [Header("Smoothing")]
        [SerializeField, Tooltip("Smooth damping duration for rotation.")]
        private float orbitSmoothTime = 0.06f;
        [SerializeField, Tooltip("Smooth damping duration for distance.")]
        private float dollySmoothTime = 0.08f;
        [SerializeField, Tooltip("Duration of animated reset in seconds.")]
        private float resetDuration = 0.45f;

        [Header("Keys")]
        [SerializeField] private KeyCode resetKey = KeyCode.R;
        [SerializeField] private KeyCode boostKey = KeyCode.LeftShift;

        private float _currentYaw;
        private float _currentPitch;
        private float _currentDistance;
        private float _targetYaw;
        private float _targetPitch;
        private float _targetDistance;

        private float _yawVelocity;
        private float _pitchVelocity;
        private float _distanceVelocity;

        private Vector2 _lastMousePosition;
        private bool _isPointerPressed;
        private bool _isPointerDragging;
        private float _dragThreshold = 4f;

        // One animated flight serves both the reset to home and the fly-to used when a part is
        // picked from the Next / Back buttons, so there is a single place that eases the camera.
        private bool _isFlying;
        private float _flightElapsed;
        private float _flightDuration;
        private float _flightFromYaw;
        private float _flightFromPitch;
        private float _flightFromDistance;
        private float _flightToYaw;
        private float _flightToPitch;
        private float _flightToDistance;

        public Vector3 FocalCenter {
            get { return focalCenter; }
        }

        public float Distance {
            get { return _currentDistance; }
        }

        private void Awake() {
            if (rigRoot == null) {
                Debug.LogError($"{nameof(ViewerFlyController)} on '{name}' has no {nameof(rigRoot)} assigned; flying is disabled.", this);
                enabled = false;
                return;
            }

            if (exhibitController == null) {
                exhibitController = GetComponent<EyeAnatomyController>();
            }

            _currentYaw = 0f;
            _currentPitch = 0f;
            _currentDistance = defaultDistance;
            _targetYaw = 0f;
            _targetPitch = 0f;
            _targetDistance = defaultDistance;

            UpdateRigTransformImmediate();
        }

        private void Update() {
            if (Input.GetKeyDown(resetKey)) {
                if (exhibitController != null) {
                    exhibitController.ResetToHome();
                } else {
                    ResetView(true);
                }

                return;
            }

            if (!enableFly) {
                return;
            }

            // Grabbing the view during a flight takes it over immediately. Without this, clicking
            // Next twice in a row or reaching for the model mid-reset feels like a dead control.
            if (_isFlying && WantsManualControl()) {
                CancelFlight();
            }

            if (_isFlying) {
                UpdateFlight();
            } else {
                UpdateMouseOrbit();
                UpdateKeyboardFlight();
                UpdateScrollDolly();
                ApplySmoothing();
            }

            ApplyRigTransform();
        }

        /// <summary>
        /// Sets a custom focal center to orbit around (e.g. when inspecting a focused part).
        /// </summary>
        public void SetFocalCenter(Vector3 worldCenter) {
            focalCenter = worldCenter;
        }

        /// <summary>
        /// Resets the focal center back to world origin.
        /// </summary>
        public void ResetFocalCenter() {
            focalCenter = Vector3.zero;
        }

        /// <summary>
        /// Smoothly or immediately restores the camera rig to the authored home position and rotation.
        /// </summary>
        public void ResetView(bool animated = true) {
            focalCenter = Vector3.zero;
            FlyTo(0f, 0f, defaultDistance, animated);
        }

        /// <summary>
        /// Eases the camera to an absolute orbit pose. Used by the reset and by the part navigator,
        /// which picks an angle that shows the chosen structure rather than whatever the viewer
        /// happened to be looking from.
        /// </summary>
        /// <param name="yaw">Target yaw in degrees.</param>
        /// <param name="pitch">Target pitch in degrees, clamped to the configured limits.</param>
        /// <param name="distance">Target distance from the focal centre in metres.</param>
        /// <param name="animated">False snaps straight there.</param>
        /// <param name="duration">Flight time in seconds. Negative uses the configured default.</param>
        public void FlyTo(float yaw, float pitch, float distance, bool animated = true, float duration = -1f) {
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            distance = Mathf.Clamp(distance, minDistance, maxDistance);

            _targetYaw = yaw;
            _targetPitch = pitch;
            _targetDistance = distance;

            _isPointerPressed = false;
            _isPointerDragging = false;

            if (animated) {
                _isFlying = true;
                _flightElapsed = 0f;
                _flightDuration = duration > 0f ? duration : resetDuration;
                _flightFromYaw = _currentYaw;
                _flightFromPitch = _currentPitch;
                _flightFromDistance = _currentDistance;
                _flightToYaw = yaw;
                _flightToPitch = pitch;
                _flightToDistance = distance;
            } else {
                _isFlying = false;
                _currentYaw = yaw;
                _currentPitch = pitch;
                _currentDistance = distance;
                _yawVelocity = 0f;
                _pitchVelocity = 0f;
                _distanceVelocity = 0f;
                UpdateRigTransformImmediate();
            }
        }

        /// <summary>
        /// Orbits by a relative amount from an external input source - the stylus drag.
        /// Any flight in progress yields immediately, because the viewer taking hold of the view
        /// should never have to wait for an animation to finish.
        /// </summary>
        public void AddOrbitDelta(float yawDegrees, float pitchDegrees) {
            if (!enableFly) {
                return;
            }

            CancelFlight();
            _targetYaw += yawDegrees;
            _targetPitch = Mathf.Clamp(_targetPitch + pitchDegrees, minPitch, maxPitch);
        }

        /// <summary>
        /// Dollies by a relative amount from an external input source. Positive moves closer,
        /// matching a pen pushed towards the display.
        /// </summary>
        public void AddDollyDelta(float metres) {
            if (!enableFly) {
                return;
            }

            CancelFlight();
            _targetDistance = Mathf.Clamp(_targetDistance - metres, minDistance, maxDistance);
        }

        /// <summary>
        /// Drops out of an animated flight and hands control back to the smoothed targets from
        /// wherever the camera currently is, so there is no snap.
        /// </summary>
        private void CancelFlight() {
            if (!_isFlying) {
                return;
            }

            _isFlying = false;
            _targetYaw = _currentYaw;
            _targetPitch = _currentPitch;
            _targetDistance = _currentDistance;
        }

        private void UpdateMouseOrbit() {
            bool isHeld = Input.GetMouseButton(0) || Input.GetMouseButton(1);
            Vector2 mousePosition = Input.mousePosition;

            if (isHeld) {
                if (!_isPointerPressed) {
                    if (IsPointerOverUI()) {
                        return;
                    }

                    _isPointerPressed = true;
                    _isPointerDragging = false;
                    _lastMousePosition = mousePosition;
                } else {
                    Vector2 delta = mousePosition - _lastMousePosition;
                    if (!_isPointerDragging && delta.magnitude >= _dragThreshold) {
                        _isPointerDragging = true;
                    }

                    if (_isPointerDragging) {
                        _lastMousePosition = mousePosition;
                        _targetYaw += delta.x * mouseOrbitSpeed;
                        _targetPitch -= delta.y * mouseOrbitSpeed;
                        _targetPitch = Mathf.Clamp(_targetPitch, minPitch, maxPitch);
                    }
                }
            } else {
                _isPointerPressed = false;
                _isPointerDragging = false;
            }
        }

        private void UpdateKeyboardFlight() {
            float forward = 0f;
            float strafe = 0f;
            float rise = 0f;

            if (Input.GetKey(KeyCode.W)) {
                forward += 1f;
            }

            if (Input.GetKey(KeyCode.S)) {
                forward -= 1f;
            }

            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) {
                strafe += 1f;
            }

            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) {
                strafe -= 1f;
            }

            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.UpArrow)) {
                rise += 1f;
            }

            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.DownArrow)) {
                rise -= 1f;
            }

            if (Mathf.Approximately(forward, 0f) && Mathf.Approximately(strafe, 0f) && Mathf.Approximately(rise, 0f)) {
                return;
            }

            float multiplier = Input.GetKey(boostKey) ? boostMultiplier : 1f;
            float dt = Time.deltaTime;

            _targetDistance -= forward * keyDollySpeed * multiplier * dt;
            _targetDistance = Mathf.Clamp(_targetDistance, minDistance, maxDistance);

            _targetYaw += strafe * keyOrbitSpeed * multiplier * dt;
            _targetPitch += rise * keyOrbitSpeed * multiplier * dt;
            _targetPitch = Mathf.Clamp(_targetPitch, minPitch, maxPitch);
        }

        private void UpdateScrollDolly() {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f)) {
                return;
            }

            if (IsPointerOverUI()) {
                return;
            }

            _targetDistance -= scroll * scrollDollySpeed;
            _targetDistance = Mathf.Clamp(_targetDistance, minDistance, maxDistance);
        }

        private void ApplySmoothing() {
            float dt = Time.deltaTime;
            _currentYaw = Mathf.SmoothDamp(_currentYaw, _targetYaw, ref _yawVelocity, orbitSmoothTime, Mathf.Infinity, dt);
            _currentPitch = Mathf.SmoothDamp(_currentPitch, _targetPitch, ref _pitchVelocity, orbitSmoothTime, Mathf.Infinity, dt);
            _currentDistance = Mathf.SmoothDamp(_currentDistance, _targetDistance, ref _distanceVelocity, dollySmoothTime, Mathf.Infinity, dt);
        }

        private void UpdateFlight() {
            _flightElapsed += Time.deltaTime;
            float t = _flightDuration > 0f ? Mathf.Clamp01(_flightElapsed / _flightDuration) : 1f;
            float eased = Mathf.SmoothStep(0f, 1f, t);

            // Yaw is interpolated the short way round, so flying from 350 to 10 degrees crosses
            // zero rather than sweeping the long way back through the whole model.
            _currentYaw = Mathf.LerpAngle(_flightFromYaw, _flightToYaw, eased);
            _currentPitch = Mathf.Lerp(_flightFromPitch, _flightToPitch, eased);
            _currentDistance = Mathf.Lerp(_flightFromDistance, _flightToDistance, eased);

            _targetYaw = _currentYaw;
            _targetPitch = _currentPitch;
            _targetDistance = _currentDistance;

            if (t < 1f) {
                return;
            }

            _isFlying = false;
            _currentYaw = _flightToYaw;
            _currentPitch = _flightToPitch;
            _currentDistance = _flightToDistance;
            _targetYaw = _flightToYaw;
            _targetPitch = _flightToPitch;
            _targetDistance = _flightToDistance;
            _yawVelocity = 0f;
            _pitchVelocity = 0f;
            _distanceVelocity = 0f;
        }

        private void ApplyRigTransform() {
            if (rigRoot == null) {
                return;
            }

            Quaternion rot = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
            Vector3 pos = focalCenter + rot * new Vector3(0f, 0f, 0.5f - _currentDistance);
            rigRoot.SetPositionAndRotation(pos, rot);
        }

        private void UpdateRigTransformImmediate() {
            if (rigRoot == null) {
                return;
            }

            Quaternion rot = Quaternion.Euler(_currentPitch, _currentYaw, 0f);
            Vector3 pos = focalCenter + rot * new Vector3(0f, 0f, 0.5f - _currentDistance);
            rigRoot.SetPositionAndRotation(pos, rot);
        }

        /// <summary>
        /// True when the viewer is reaching for the view with the mouse, the wheel or the flight
        /// keys, which should take precedence over any flight still playing out.
        ///
        /// The stylus does not need checking here: its input arrives through
        /// <see cref="AddOrbitDelta"/> and <see cref="AddDollyDelta"/>, which cancel the flight
        /// themselves.
        /// </summary>
        private bool WantsManualControl() {
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) ||
                !Mathf.Approximately(Input.mouseScrollDelta.y, 0f)) {
                // A press on a button is aimed at that button, not at the view behind it.
                return !IsPointerOverUI();
            }

            return Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.S) ||
                Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.D) ||
                Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.E) ||
                Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.DownArrow) ||
                Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.RightArrow);
        }

        private bool IsPointerOverUI() {
            if (EventSystem.current == null) {
                return false;
            }

            return EventSystem.current.IsPointerOverGameObject();
        }
    }
}