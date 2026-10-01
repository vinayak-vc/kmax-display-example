using System;
using System.Collections.Generic;
using KmaxXR;
using UnityEngine;

namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// The pen's physical tip, as a tracked point in the world that things can be touched with.
    ///
    /// This is the component the whole suite is built on, and it is a different idea from the one
    /// the existing exhibits use. They treat the stylus as a ray-caster - a 3D mouse that points
    /// at things across a distance. This treats it as a finger: the tip has a position, a radius
    /// and a velocity, and it interacts with whatever it is actually inside. That only works
    /// because the Kmax tracks the viewer's eyes and converges the frustum on them, so a rendered
    /// object at -0.08 m and the physical pen tip occupy the same point in the room.
    ///
    /// <para><b>Contact is found by <see cref="Physics.OverlapSphereNonAlloc"/> each frame, not by
    /// trigger callbacks.</b> A kinematic trigger moving at hand speed tunnels through thin
    /// colliders and reports enter and exit in the wrong order when two overlap on one frame. An
    /// explicit query costs one call and is deterministic, and the grab system needs the full
    /// current set anyway rather than a stream of events.</para>
    ///
    /// <para><see cref="tipOffset"/> is the number <c>S0-2</c> measures on the hardware: the gap
    /// between the pose the SDK reports and where the physical point of the pen actually is. It
    /// defaults to zero, which is almost certainly wrong, and every co-located interaction in the
    /// suite is only as accurate as this value.</para>
    /// </summary>
    public class StylusTip : MonoBehaviour {
        private const int MaxOverlaps = 16;

        [Header("References")]
        [SerializeField, Tooltip("The pen. Found by id on first use when left empty.")]
        private KmaxStylus stylus;
        [SerializeField, Tooltip("Visual for the tip, moved to the tracked point. Optional.")]
        private Transform tipVisual;

        [Header("Calibration")]
        [SerializeField, Tooltip("Distance from the pose the SDK reports to the physical point of " +
            "the pen, along the pen's forward axis, in metres. This is what S0-2 measures on the " +
            "hardware. Everything co-located in this suite is only as good as this number.")]
        private float tipOffset;
        [SerializeField, Tooltip("Sideways correction, if the physical point is not on the pen's " +
            "own axis. Usually zero.")]
        private Vector3 lateralOffset = Vector3.zero;

        [Header("Contact")]
        [SerializeField, Tooltip("Radius of the tip in metres. Small enough to be precise, large " +
            "enough that the viewer does not have to be perfect. 4 mm is about a pen nib.")]
        private float radius = 0.004f;
        [SerializeField, Tooltip("Layers the tip can touch.")]
        private LayerMask layers = ~0;
        [SerializeField, Tooltip("Seconds of smoothing on the reported velocity. Raw frame-to-frame " +
            "velocity from a tracked device is far too noisy to throw an object with.")]
        private float velocitySmoothing = 0.05f;

        [Header("Development")]
        [SerializeField, Tooltip("With no pen tracked, drive the tip from the mouse so the scene " +
            "can be exercised without the hardware. The mouse moves it across the glass; the " +
            "scroll wheel moves it in depth. Development aid - it is not co-location and it will " +
            "not tell you whether a grab feels right.")]
        private bool mouseFallback = true;
        [SerializeField, Tooltip("Depth the mouse fallback starts at, as a fraction of the pop-out " +
            "budget. 0 is the screen plane, 1 is fully forward.")]
        private float mouseFallbackDepth = 0.4f;
        [SerializeField, Tooltip("Camera used by the mouse fallback. Resolved from the rig when empty.")]
        private Camera fallbackCamera;

        private KmaxStylus _stylus;
        private readonly Collider[] _overlaps = new Collider[MaxOverlaps];
        private readonly List<Collider> _touching = new List<Collider>(MaxOverlaps);
        private readonly List<Collider> _previous = new List<Collider>(MaxOverlaps);
        private Vector3 _position;
        private Quaternion _rotation = Quaternion.identity;
        private Vector3 _velocity;
        private Vector3 _lastPosition;
        private bool _hasLastPosition;
        private bool _tracked;
        private float _mouseDepth;

        /// <summary>
        /// Raised when the tip arrives on a collider it was not touching last frame.
        /// </summary>
        public event Action<Collider> TouchBegan;

        /// <summary>
        /// Raised when the tip leaves a collider it was touching last frame. Also raised for
        /// everything still held when the component is disabled.
        /// </summary>
        public event Action<Collider> TouchEnded;

        /// <summary>World position of the physical tip, calibration applied.</summary>
        public Vector3 Position {
            get { return _position; }
        }

        /// <summary>Orientation of the pen.</summary>
        public Quaternion Rotation {
            get { return _rotation; }
        }

        /// <summary>Smoothed velocity in metres per second. Use this to throw things.</summary>
        public Vector3 Velocity {
            get { return _velocity; }
        }

        /// <summary>Radius of the tip in metres.</summary>
        public float Radius {
            get { return radius; }
        }

        /// <summary>
        /// True when a real pen is being tracked. False when the mouse fallback is driving the
        /// tip, so a scene can refuse to score a run that was not done on the hardware.
        /// </summary>
        public bool IsTracked {
            get { return _tracked; }
        }

        /// <summary>
        /// Signed parallax depth of the tip. Negative is in front of the glass.
        /// </summary>
        public float Depth {
            get { return StereoVolume.DepthOf(_position); }
        }

        /// <summary>Colliders the tip is currently inside. Do not hold on to this list.</summary>
        public IReadOnlyList<Collider> Touching {
            get { return _touching; }
        }

        private void Awake() {
            _mouseDepth = Mathf.Clamp01(mouseFallbackDepth);
        }

        private void OnDisable() {
            for (int i = _touching.Count - 1; i >= 0; i--) {
                RaiseTouchEnded(_touching[i]);
            }
            _touching.Clear();
            _previous.Clear();
            _hasLastPosition = false;
        }

        private void Update() {
            UpdatePose();
            UpdateVelocity();
            UpdateContacts();
            if (tipVisual != null) {
                tipVisual.SetPositionAndRotation(_position, _rotation);
            }
        }

        private void UpdatePose() {
            KmaxStylus pen = Resolve();
            if (pen != null && pen.Visible) {
                _tracked = true;
                Pose pose = pen.StartpointPose;
                _rotation = pose.rotation;
                _position = pose.position + _rotation * (Vector3.forward * tipOffset + lateralOffset);
                return;
            }
            _tracked = false;
            if (mouseFallback) {
                UpdateMouseFallback();
            }
        }

        /// <summary>
        /// Puts the tip under the mouse at a depth the scroll wheel controls, so the scenes can be
        /// exercised in the editor before the hardware is available. This is not co-location and
        /// it cannot answer whether an interaction feels right - it only proves the logic runs.
        /// </summary>
        private void UpdateMouseFallback() {
            Camera camera = ResolveFallbackCamera();
            if (camera == null || !StereoVolume.IsReady) {
                return;
            }
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > Mathf.Epsilon) {
                _mouseDepth = Mathf.Clamp01(_mouseDepth + scroll * 0.05f);
            }

            Transform plane = StereoVolume.ScreenTransform;
            float targetZ = Mathf.Lerp(0f, -StereoVolume.PopOutLimit, _mouseDepth);
            Vector3 planePoint = plane.TransformPoint(new Vector3(0f, 0f, targetZ));
            Vector3 planeNormal = plane.forward;

            Ray ray = camera.ScreenPointToRay(Input.mousePosition);
            float denominator = Vector3.Dot(ray.direction, planeNormal);
            if (Mathf.Abs(denominator) < 1e-6f) {
                return;
            }
            float distance = Vector3.Dot(planePoint - ray.origin, planeNormal) / denominator;
            if (distance <= 0f) {
                return;
            }
            _position = ray.GetPoint(distance);
            _rotation = Quaternion.LookRotation(plane.forward, plane.up);
        }

        private void UpdateVelocity() {
            float delta = Time.unscaledDeltaTime;
            if (!_hasLastPosition || delta <= 0f) {
                _lastPosition = _position;
                _hasLastPosition = true;
                return;
            }
            Vector3 instant = (_position - _lastPosition) / delta;
            float blend = velocitySmoothing <= 0f ? 1f : 1f - Mathf.Exp(-delta / velocitySmoothing);
            _velocity = Vector3.Lerp(_velocity, instant, blend);
            _lastPosition = _position;
        }

        private void UpdateContacts() {
            _previous.Clear();
            for (int i = 0; i < _touching.Count; i++) {
                _previous.Add(_touching[i]);
            }
            _touching.Clear();

            int count = Physics.OverlapSphereNonAlloc(_position, radius, _overlaps, layers,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++) {
                Collider hit = _overlaps[i];
                if (hit == null) {
                    continue;
                }
                _touching.Add(hit);
                if (!_previous.Contains(hit)) {
                    RaiseTouchBegan(hit);
                }
            }
            for (int i = 0; i < _previous.Count; i++) {
                Collider gone = _previous[i];
                if (gone == null || _touching.Contains(gone)) {
                    continue;
                }
                RaiseTouchEnded(gone);
            }
        }

        /// <summary>
        /// The collider nearest the tip among those it is currently inside, or null. Distance is
        /// measured to the collider's closest surface point, so a large object the tip is deep
        /// inside does not beat a small one it is just touching.
        /// </summary>
        public Collider NearestTouched() {
            Collider nearest = null;
            float best = float.MaxValue;
            for (int i = 0; i < _touching.Count; i++) {
                Collider candidate = _touching[i];
                if (candidate == null) {
                    continue;
                }
                float distance = Vector3.SqrMagnitude(candidate.ClosestPoint(_position) - _position);
                if (distance < best) {
                    best = distance;
                    nearest = candidate;
                }
            }
            return nearest;
        }

        /// <summary>
        /// True when the tip is inside the comfort volume. A scene can use this to stop scoring
        /// while the viewer has wandered out of the box.
        /// </summary>
        public bool InsideVolume() {
            return StereoVolume.Contains(_position);
        }

        private void RaiseTouchBegan(Collider other) {
            if (TouchBegan != null) {
                TouchBegan(other);
            }
        }

        private void RaiseTouchEnded(Collider other) {
            if (TouchEnded != null) {
                TouchEnded(other);
            }
        }

        private KmaxStylus Resolve() {
            if (_stylus != null) {
                return _stylus;
            }
            if (stylus != null) {
                _stylus = stylus;
                return _stylus;
            }
            _stylus = KmaxPointer.PointerById(KmaxStylus.UniqueId) as KmaxStylus;
            return _stylus;
        }

        private Camera ResolveFallbackCamera() {
            if (fallbackCamera != null) {
                return fallbackCamera;
            }
            KmaxStylus pen = Resolve();
            if (pen != null && pen.EventCamera != null) {
                fallbackCamera = pen.EventCamera;
                return fallbackCamera;
            }
            fallbackCamera = StereoVolume.CenterCamera;
            return fallbackCamera;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos() {
            Gizmos.color = _tracked ? new Color(0.3f, 1f, 0.5f, 0.9f) : new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(Application.isPlaying ? _position : transform.position, radius);
        }
#endif
    }
}
