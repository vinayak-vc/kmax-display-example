using KmaxXR;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Orbit, turntable, pan, and zoom manipulator for the 3D model.
    /// Drives the parent pivot of the model so that rotation stays centered on the active focal point
    /// (the eye globe center in overview, or the focused part center when inspecting a part).
    /// </summary>
    public class EyeManipulator : MonoBehaviour {
        [SerializeField, Tooltip("Parent of the model. Rotated and panned.")]
        private Transform pivot;
        [SerializeField, Tooltip("Supplies the screen-relative axes for camera-relative pan and pitch.")]
        private Camera referenceCamera;

        [Header("Rotation Settings")]
        [SerializeField, Tooltip("Degrees turned per pixel of drag.")]
        private float dragRotateSpeed = 0.35f;
        [SerializeField, Tooltip("Pixels of movement before a press counts as a drag rather than a click.")]
        private float dragThreshold = 4f;
        [SerializeField, Tooltip("Minimum pitch in degrees to prevent flipping upside-down.")]
        private float minPitch = -75f;
        [SerializeField, Tooltip("Maximum pitch in degrees to prevent flipping upside-down.")]
        private float maxPitch = 75f;
        [SerializeField, Tooltip("Smoothing duration for rotation damping in seconds.")]
        private float rotationSmoothTime = 0.08f;

        [Header("Pan Settings")]
        [SerializeField, Tooltip("Metres panned per pixel of middle or right-click drag.")]
        private float mousePanSpeed = 0.0006f;
        [SerializeField, Tooltip("Metres per second when panning with keyboard.")]
        private float keyPanSpeed = 0.06f;
        [SerializeField, Tooltip("Maximum pan extent from center in metres (X = horizontal, Y = vertical).")]
        private Vector2 panLimits = new Vector2(0.18f, 0.12f);
        [SerializeField, Tooltip("Smoothing duration for pan damping in seconds.")]
        private float panSmoothTime = 0.08f;

        [Header("Zoom Settings")]
        [SerializeField, Tooltip("Zoom scale step per mouse scroll wheel tick.")]
        private float scrollZoomSpeed = 0.08f;
        [SerializeField, Tooltip("Minimum zoom multiplier.")]
        private float minZoom = 0.6f;
        [SerializeField, Tooltip("Maximum zoom multiplier.")]
        private float maxZoom = 2.0f;
        [SerializeField, Tooltip("Smoothing duration for zoom damping in seconds.")]
        private float zoomSmoothTime = 0.08f;

        [Header("Keyboard")]
        [SerializeField] private float keyRotateSpeed = 90f;
        [SerializeField, Tooltip("Held with the arrow keys to pan instead of rotate.")]
        private KeyCode panModifier = KeyCode.LeftShift;

        [Header("Coordination")]
        [SerializeField, Tooltip("When true, ViewerFlyController drives camera orbit, so model pointer manipulation is bypassed.")]
        private bool disablePointerManipulation = true;

        private Quaternion _restRotation;
        private Vector3 _restPosition;
        private Vector3 _restScale;

        private Vector3 _focalPoint;
        private bool _hasCustomFocalPoint;

        private float _currentYaw;
        private float _currentPitch;
        private float _targetYaw;
        private float _targetPitch;
        private float _yawVelocity;
        private float _pitchVelocity;

        private Vector3 _currentPan;
        private Vector3 _targetPan;
        private Vector3 _panVelocity;

        private float _currentZoom = 1f;
        private float _targetZoom = 1f;
        private float _zoomVelocity;

        private Vector2 _lastPointerPosition;
        private bool _isPrimaryPressed;
        private bool _isPrimaryDragging;
        private bool _isSecondaryPressed;
        private bool _isSecondaryDragging;

        private bool _isAnimatedReset;
        private float _resetDuration = 0.45f;
        private float _resetElapsed;
        private float _resetFromYaw;
        private float _resetFromPitch;
        private Vector3 _resetFromPan;
        private float _resetFromZoom;

        private void Awake() {
            if (pivot == null) {
                Debug.LogError($"{nameof(EyeManipulator)} on '{name}' has no {nameof(pivot)} assigned; manipulation is disabled.", this);
                enabled = false;
                return;
            }

            _restRotation = pivot.rotation;
            _restPosition = pivot.position;
            _restScale = pivot.localScale;
            _focalPoint = _restPosition;

            _currentYaw = 0f;
            _currentPitch = 0f;
            _targetYaw = 0f;
            _targetPitch = 0f;
            _currentPan = Vector3.zero;
            _targetPan = Vector3.zero;
            _currentZoom = 1f;
            _targetZoom = 1f;
        }

        private void Update() {
            if (_isAnimatedReset) {
                UpdateAnimatedReset();
            } else if (!disablePointerManipulation) {
                UpdatePointer();
                UpdateKeyboard();
                UpdateScrollZoom();
                ApplySmoothing();
            }

            ApplyTransform();
        }

        /// <summary>
        /// Sets a custom focal point in world space (e.g. when inspecting a focused part).
        /// Rotation will orbit around this point.
        /// </summary>
        public void SetFocalPoint(Vector3 worldPoint) {
            _focalPoint = worldPoint;
            _hasCustomFocalPoint = true;
        }

        /// <summary>
        /// Clears custom focal point, returning rotation orbit to the default rest position.
        /// </summary>
        public void ClearFocalPoint() {
            _focalPoint = _restPosition;
            _hasCustomFocalPoint = false;
        }

        /// <summary>
        /// Resets rotation, pan, and zoom to their default forward-facing state.
        /// </summary>
        public void ResetTransform(bool animated = true) {
            _targetYaw = 0f;
            _targetPitch = 0f;
            _targetPan = Vector3.zero;
            _targetZoom = 1f;

            _isPrimaryPressed = false;
            _isPrimaryDragging = false;
            _isSecondaryPressed = false;
            _isSecondaryDragging = false;

            if (animated) {
                _isAnimatedReset = true;
                _resetElapsed = 0f;
                _resetFromYaw = _currentYaw;
                _resetFromPitch = _currentPitch;
                _resetFromPan = _currentPan;
                _resetFromZoom = _currentZoom;
            } else {
                _isAnimatedReset = false;
                _currentYaw = 0f;
                _currentPitch = 0f;
                _currentPan = Vector3.zero;
                _currentZoom = 1f;
                _yawVelocity = 0f;
                _pitchVelocity = 0f;
                _panVelocity = Vector3.zero;
                _zoomVelocity = 0f;
                ApplyTransform();
            }
        }

        private void UpdatePointer() {
            Vector2 position;
            bool primaryHeld;
            bool secondaryHeld;
            if (!TryReadPointer(out position, out primaryHeld, out secondaryHeld)) {
                _isPrimaryPressed = false;
                _isPrimaryDragging = false;
                _isSecondaryPressed = false;
                _isSecondaryDragging = false;
                return;
            }

            // Primary drag: Turntable Orbit
            if (primaryHeld) {
                if (!_isPrimaryPressed) {
                    _isPrimaryPressed = true;
                    _isPrimaryDragging = false;
                    _lastPointerPosition = position;

                    if (IsPointerOverUI()) {
                        _isPrimaryPressed = false;
                        return;
                    }
                } else {
                    Vector2 delta = position - _lastPointerPosition;
                    if (!_isPrimaryDragging && delta.magnitude >= dragThreshold) {
                        _isPrimaryDragging = true;
                    }

                    if (_isPrimaryDragging) {
                        _lastPointerPosition = position;
                        _targetYaw += delta.x * dragRotateSpeed;
                        _targetPitch -= delta.y * dragRotateSpeed;
                        _targetPitch = Mathf.Clamp(_targetPitch, minPitch, maxPitch);
                    }
                }
            } else {
                _isPrimaryPressed = false;
                _isPrimaryDragging = false;
            }

            // Secondary drag (right/middle click): Pan
            if (secondaryHeld) {
                if (!_isSecondaryPressed) {
                    _isSecondaryPressed = true;
                    _isSecondaryDragging = false;
                    _lastPointerPosition = position;

                    if (IsPointerOverUI()) {
                        _isSecondaryPressed = false;
                        return;
                    }
                } else {
                    Vector2 delta = position - _lastPointerPosition;
                    if (!_isSecondaryDragging && delta.magnitude >= dragThreshold) {
                        _isSecondaryDragging = true;
                    }

                    if (_isSecondaryDragging) {
                        _lastPointerPosition = position;
                        Vector3 right = GetCameraRight();
                        Vector3 up = GetCameraUp();
                        Vector3 panDelta = right * (delta.x * mousePanSpeed) + up * (delta.y * mousePanSpeed);
                        _targetPan += panDelta;
                        _targetPan.x = Mathf.Clamp(_targetPan.x, -panLimits.x, panLimits.x);
                        _targetPan.y = Mathf.Clamp(_targetPan.y, -panLimits.y, panLimits.y);
                    }
                }
            } else {
                _isSecondaryPressed = false;
                _isSecondaryDragging = false;
            }
        }

        private void UpdateKeyboard() {
            float horizontal = 0f;
            float vertical = 0f;

            if (Input.GetKey(KeyCode.LeftArrow)) {
                horizontal -= 1f;
            }
            if (Input.GetKey(KeyCode.RightArrow)) {
                horizontal += 1f;
            }
            if (Input.GetKey(KeyCode.DownArrow)) {
                vertical -= 1f;
            }
            if (Input.GetKey(KeyCode.UpArrow)) {
                vertical += 1f;
            }

            if (Mathf.Approximately(horizontal, 0f) && Mathf.Approximately(vertical, 0f)) {
                return;
            }

            if (Input.GetKey(panModifier)) {
                Vector3 right = GetCameraRight();
                Vector3 up = GetCameraUp();
                Vector3 panDelta = (right * horizontal + up * vertical) * (keyPanSpeed * Time.deltaTime);
                _targetPan += panDelta;
                _targetPan.x = Mathf.Clamp(_targetPan.x, -panLimits.x, panLimits.x);
                _targetPan.y = Mathf.Clamp(_targetPan.y, -panLimits.y, panLimits.y);
                return;
            }

            _targetYaw += horizontal * keyRotateSpeed * Time.deltaTime;
            _targetPitch -= vertical * keyRotateSpeed * Time.deltaTime;
            _targetPitch = Mathf.Clamp(_targetPitch, minPitch, maxPitch);
        }

        private void UpdateScrollZoom() {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f)) {
                return;
            }

            if (IsPointerOverUI() || Input.GetMouseButton(1)) {
                return;
            }

            _targetZoom += scroll * scrollZoomSpeed;
            _targetZoom = Mathf.Clamp(_targetZoom, minZoom, maxZoom);
        }

        private void ApplySmoothing() {
            float dt = Time.deltaTime;
            _currentYaw = Mathf.SmoothDamp(_currentYaw, _targetYaw, ref _yawVelocity, rotationSmoothTime, Mathf.Infinity, dt);
            _currentPitch = Mathf.SmoothDamp(_currentPitch, _targetPitch, ref _pitchVelocity, rotationSmoothTime, Mathf.Infinity, dt);
            _currentPan = Vector3.SmoothDamp(_currentPan, _targetPan, ref _panVelocity, panSmoothTime, Mathf.Infinity, dt);
            _currentZoom = Mathf.SmoothDamp(_currentZoom, _targetZoom, ref _zoomVelocity, zoomSmoothTime, Mathf.Infinity, dt);
        }

        private void UpdateAnimatedReset() {
            _resetElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_resetElapsed / _resetDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            _currentYaw = Mathf.Lerp(_resetFromYaw, 0f, eased);
            _currentPitch = Mathf.Lerp(_resetFromPitch, 0f, eased);
            _currentPan = Vector3.Lerp(_resetFromPan, Vector3.zero, eased);
            _currentZoom = Mathf.Lerp(_resetFromZoom, 1f, eased);

            if (t >= 1f) {
                _isAnimatedReset = false;
                _currentYaw = 0f;
                _currentPitch = 0f;
                _currentPan = Vector3.zero;
                _currentZoom = 1f;
                _yawVelocity = 0f;
                _pitchVelocity = 0f;
                _panVelocity = Vector3.zero;
                _zoomVelocity = 0f;
            }
        }

        private void ApplyTransform() {
            if (pivot == null) {
                return;
            }

            Quaternion targetRotation = Quaternion.Euler(_currentPitch, _currentYaw, 0f) * _restRotation;
            Vector3 activeFocal = _hasCustomFocalPoint ? _focalPoint : _restPosition;

            // Invariance math: ensures activeFocal stays invariant when rotating around it
            Vector3 basePosition = activeFocal - targetRotation * (Quaternion.Inverse(_restRotation) * (activeFocal - _restPosition));
            Vector3 finalPosition = basePosition + _currentPan;

            pivot.position = finalPosition;
            pivot.rotation = targetRotation;
            pivot.localScale = _restScale * _currentZoom;
        }

        private Vector3 GetCameraRight() {
            if (referenceCamera != null) {
                return referenceCamera.transform.right;
            }

            return Vector3.right;
        }

        private Vector3 GetCameraUp() {
            if (referenceCamera != null) {
                return referenceCamera.transform.up;
            }

            return Vector3.up;
        }

        private bool TryReadPointer(out Vector2 position, out bool primaryHeld, out bool secondaryHeld) {
            if (KmaxPointer.Enable) {
                foreach (KmaxPointer pointer in KmaxPointer.Pointers) {
                    position = pointer.ScreenPosition;
                    primaryHeld = pointer.GetButton(0);
                    secondaryHeld = false;
                    return true;
                }
            }

            if (Input.mousePresent) {
                position = Input.mousePosition;
                primaryHeld = Input.GetMouseButton(0);
                secondaryHeld = false;
                return true;
            }

            position = Vector2.zero;
            primaryHeld = false;
            secondaryHeld = false;
            return false;
        }

        private bool IsPointerOverUI() {
            if (EventSystem.current == null) {
                return false;
            }

            return EventSystem.current.IsPointerOverGameObject();
        }
    }
}