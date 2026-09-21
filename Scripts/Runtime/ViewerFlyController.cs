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

        private bool _isAnimatedReset;
        private float _resetElapsed;
        private float _resetFromYaw;
        private float _resetFromPitch;
        private float _resetFromDistance;

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

            if (_isAnimatedReset) {
                UpdateAnimatedReset();
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
            _targetYaw = 0f;
            _targetPitch = 0f;
            _targetDistance = defaultDistance;
            focalCenter = Vector3.zero;

            _isPointerPressed = false;
            _isPointerDragging = false;

            if (animated) {
                _isAnimatedReset = true;
                _resetElapsed = 0f;
                _resetFromYaw = _currentYaw;
                _resetFromPitch = _currentPitch;
                _resetFromDistance = _currentDistance;
            } else {
                _isAnimatedReset = false;
                _currentYaw = 0f;
                _currentPitch = 0f;
                _currentDistance = defaultDistance;
                _yawVelocity = 0f;
                _pitchVelocity = 0f;
                _distanceVelocity = 0f;
                UpdateRigTransformImmediate();
            }
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

        private void UpdateAnimatedReset() {
            _resetElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_resetElapsed / resetDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            _currentYaw = Mathf.Lerp(_resetFromYaw, 0f, eased);
            _currentPitch = Mathf.Lerp(_resetFromPitch, 0f, eased);
            _currentDistance = Mathf.Lerp(_resetFromDistance, defaultDistance, eased);

            _targetYaw = _currentYaw;
            _targetPitch = _currentPitch;
            _targetDistance = _currentDistance;

            if (t >= 1f) {
                _isAnimatedReset = false;
                _currentYaw = 0f;
                _currentPitch = 0f;
                _currentDistance = defaultDistance;
                _targetYaw = 0f;
                _targetPitch = 0f;
                _targetDistance = defaultDistance;
                _yawVelocity = 0f;
                _pitchVelocity = 0f;
                _distanceVelocity = 0f;
            }
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

        private bool IsPointerOverUI() {
            if (EventSystem.current == null) {
                return false;
            }

            return EventSystem.current.IsPointerOverGameObject();
        }
    }
}