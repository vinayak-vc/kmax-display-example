using KmaxXR;
using UnityEngine;

namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// Picks objects up with the pen and carries them, on a spring.
    ///
    /// <para><b>Two selection modes, deliberately.</b> <see cref="SelectionMode.TipProximity"/> is
    /// what the suite is designed around: you take hold of whatever the physical tip is actually
    /// inside, which only works if the tracked tip and the rendered object really do occupy the
    /// same point in the room. <c>S0-7</c> decides whether they do. If the answer is no, this
    /// field flips to <see cref="SelectionMode.Ray"/> and everything downstream keeps working -
    /// which is why both modes exist before the measurement rather than after it.</para>
    ///
    /// <para><b>The spring is the hard part.</b> A held object locked rigidly to the tip feels
    /// weightless and reads as a rendering artefact stuck to the pen rather than as an object in
    /// the hand. A little lag gives it mass. Too much and the object is visibly not where the hand
    /// is, which destroys the co-location the whole suite is built on. <see cref="followTime"/> is
    /// that tuning, it is the first thing to adjust on real hardware, and it will not be right on
    /// the first guess.</para>
    /// </summary>
    public class StylusGrab : MonoBehaviour {
        /// <summary>How a candidate object is chosen when the grab button goes down.</summary>
        public enum SelectionMode {
            /// <summary>Whatever the physical tip is inside. Requires accurate co-location.</summary>
            TipProximity,
            /// <summary>Whatever the pen's ray is resting on. Forgiving, but not co-located.</summary>
            Ray
        }

        [Header("References")]
        [SerializeField, Tooltip("The tracked tip. Found on this object or its children when empty.")]
        private StylusTip tip;
        [SerializeField, Tooltip("Haptics. Optional - without it the grab is silent to the hand.")]
        private StylusHaptics haptics;
        [SerializeField, Tooltip("The pen, for button reads. Found by id on first use when empty.")]
        private KmaxStylus stylus;

        [Header("Selection")]
        [SerializeField, Tooltip("How the object to grab is chosen. Tip proximity is the design " +
            "intent; ray is the fallback if S0-7 shows the tip cannot be trusted.")]
        private SelectionMode selection = SelectionMode.TipProximity;
        [SerializeField, Range(0, 2), Tooltip("Pen button held to grab. Index 0 is the front button.")]
        private int grabButton;

        [Header("Feel")]
        [SerializeField, Tooltip("Seconds the object lags behind the tip. This is the single most " +
            "important number in the suite: too low and the object feels weightless, too high and " +
            "it is visibly not where your hand is.")]
        private float followTime = 0.04f;
        [SerializeField, Tooltip("Speed cap in metres per second, so a fast flick cannot fling a " +
            "held object across the scene or through a wall.")]
        private float maxSpeed = 4f;
        [SerializeField, Tooltip("Angular speed cap in degrees per second.")]
        private float maxAngularSpeed = 1080f;
        [SerializeField, Tooltip("Keep the object's rotation locked to the pen's. Off lets a held " +
            "object hang and swing from the grab point, which suits some scenes and not others.")]
        private bool matchRotation = true;

        [Header("Development")]
        [SerializeField, Tooltip("With no pen tracked, grab with the left mouse button so the " +
            "scene can be exercised without hardware. Pairs with the tip's own mouse fallback.")]
        private bool mouseFallback = true;

        private KmaxStylus _stylus;
        private Grabbable _held;
        private Vector3 _holdLocalPoint;
        private Quaternion _holdLocalRotation = Quaternion.identity;

        /// <summary>The object currently held, or null.</summary>
        public Grabbable Held {
            get { return _held; }
        }

        /// <summary>True while something is held.</summary>
        public bool IsHolding {
            get { return _held != null; }
        }

        /// <summary>Which selection mode is live. Set by <c>S0-7</c>.</summary>
        public SelectionMode Selection {
            get { return selection; }
            set { selection = value; }
        }

        private void Awake() {
            if (tip == null) {
                tip = GetComponentInChildren<StylusTip>();
            }
            if (haptics == null) {
                haptics = GetComponentInChildren<StylusHaptics>();
            }
            if (tip == null) {
                Debug.LogError($"{nameof(StylusGrab)} on '{name}' has no {nameof(StylusTip)}. " +
                    "Nothing can be grabbed without one.", this);
                enabled = false;
            }
        }

        private void OnDisable() {
            if (_held != null) {
                ReleaseHeld();
            }
        }

        private void Update() {
            if (GrabPressed() && _held == null) {
                TryGrab();
                return;
            }
            if (GrabReleased() && _held != null) {
                ReleaseHeld();
            }
        }

        private void FixedUpdate() {
            if (_held == null) {
                return;
            }
            DriveHeld();
        }

        private void TryGrab() {
            Grabbable candidate = FindCandidate();
            if (candidate == null || candidate.IsHeld) {
                return;
            }

            Vector3 tipPosition = tip.Position;
            Transform held = candidate.transform;
            if (candidate.HoldAtContactPoint) {
                // Rotation only, deliberately. Transform.InverseTransformPoint also divides by
                // localScale, and the offset is put back with rotation alone - so on an object
                // built by scaling a unit mesh, which is how every block in Stack is made, the
                // grab offset comes back multiplied by one over the scale. At a block size of
                // 23 mm that is a factor of forty: a grab 1 mm off centre holds the block 40 mm
                // away from the hand, and the comfort clamp then pins it to the frame edge.
                _holdLocalPoint = Quaternion.Inverse(held.rotation) * (tipPosition - held.position);
            } else {
                _holdLocalPoint = Vector3.zero;
            }
            _holdLocalRotation = Quaternion.Inverse(tip.Rotation) * held.rotation;

            _held = candidate;
            _held.BeginHold();
            if (haptics != null) {
                haptics.Grab();
            }
        }

        private void ReleaseHeld() {
            Grabbable releasing = _held;
            _held = null;
            releasing.EndHold(tip != null ? tip.Velocity : Vector3.zero);
            if (haptics != null) {
                haptics.Release();
            }
        }

        /// <summary>
        /// Drives the held body towards the tip by setting velocity rather than position. Moving a
        /// body by position teleports it through whatever is in the way; a velocity lets the solver
        /// resolve contact, which is what makes a held block able to knock a tower over.
        /// </summary>
        private void DriveHeld() {
            Rigidbody body = _held.Body;
            Quaternion targetRotation = matchRotation
                ? tip.Rotation * _holdLocalRotation
                : body.rotation;

            Vector3 holdOffset = targetRotation * _holdLocalPoint;
            Vector3 targetPosition = tip.Position - holdOffset;
            if (_held.ClampToComfortVolume) {
                targetPosition = StereoVolume.ClampToComfort(targetPosition);
            }

            float step = Mathf.Max(followTime, Time.fixedDeltaTime);
            Vector3 toTarget = targetPosition - body.position;
            body.linearVelocity = Vector3.ClampMagnitude(toTarget / step, maxSpeed);

            if (!matchRotation) {
                return;
            }

            Quaternion delta = targetRotation * Quaternion.Inverse(body.rotation);
            float angle;
            Vector3 axis;
            delta.ToAngleAxis(out angle, out axis);
            if (float.IsInfinity(axis.x) || float.IsNaN(axis.x) || Mathf.Approximately(angle, 0f)) {
                body.angularVelocity = Vector3.zero;
                return;
            }
            if (angle > 180f) {
                angle -= 360f;
            }
            Vector3 angular = axis.normalized * (angle * Mathf.Deg2Rad / step);
            body.angularVelocity = Vector3.ClampMagnitude(angular, maxAngularSpeed * Mathf.Deg2Rad);
        }

        private Grabbable FindCandidate() {
            if (selection == SelectionMode.Ray) {
                KmaxStylus pen = Resolve();
                if (pen == null || pen.CurrentHitObject == null) {
                    return null;
                }
                return pen.CurrentHitObject.GetComponentInParent<Grabbable>();
            }

            // The nearest touched collider is not the answer. The tip is usually inside scenery as
            // well as the thing being reached for - a hand over a table touches the table - and
            // taking the nearest overall and then asking whether it happens to be grabbable means
            // a block resting on a surface can never be picked up. Search among the grabbables
            // only, and take the nearest of those.
            Grabbable best = null;
            float bestDistance = float.MaxValue;
            Vector3 tipPosition = tip.Position;
            System.Collections.Generic.IReadOnlyList<Collider> touching = tip.Touching;
            for (int i = 0; i < touching.Count; i++) {
                Collider candidate = touching[i];
                if (candidate == null) {
                    continue;
                }
                Grabbable grabbable = candidate.GetComponentInParent<Grabbable>();
                if (grabbable == null || grabbable.IsHeld) {
                    continue;
                }
                // The tip's overlap query uses one radius for everything; an object asking for
                // extra reach is checked here instead, against its own margin.
                float distance = Vector3.Distance(candidate.ClosestPoint(tipPosition), tipPosition);
                if (distance > tip.Radius + grabbable.GrabMargin) {
                    continue;
                }
                if (distance < bestDistance) {
                    bestDistance = distance;
                    best = grabbable;
                }
            }
            return best;
        }

        private bool GrabPressed() {
            KmaxStylus pen = Resolve();
            if (pen != null && pen.Visible) {
                return pen.GetButtonDown(grabButton);
            }
            return mouseFallback && Input.GetMouseButtonDown(0);
        }

        private bool GrabReleased() {
            KmaxStylus pen = Resolve();
            if (pen != null && pen.Visible) {
                return pen.GetButtonUp(grabButton);
            }
            return mouseFallback && Input.GetMouseButtonUp(0);
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
    }
}
