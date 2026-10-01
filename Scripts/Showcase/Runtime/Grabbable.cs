using UnityEngine;
using UnityEngine.Events;

namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// Marks an object the pen can pick up, and carries the per-object part of how that feels.
    ///
    /// Held objects stay <b>dynamic rather than kinematic</b>. The instinct is to make a held
    /// object kinematic so it tracks the hand exactly, but then it passes through everything, and
    /// in a stacking scene the collision between the block in your hand and the tower you are
    /// building is the entire game. So <see cref="StylusGrab"/> drives the body by velocity and
    /// lets the physics engine resolve contact, and the cost is that a held object can be pushed
    /// off the hand by something solid - which is correct, and reads as weight.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Grabbable : MonoBehaviour {
        [Header("Feel")]
        [SerializeField, Tooltip("Extra reach for this object beyond the tip's own radius, in " +
            "metres. A large or awkward object can afford a little; a small one in a crowd " +
            "should have none, or it steals grabs from its neighbours.")]
        private float grabMargin = 0.004f;
        [SerializeField, Tooltip("Hold the object by the point the tip touched it, rather than by " +
            "its centre. On means a block picked up by one corner stays held by that corner, " +
            "which is what a hand does.")]
        private bool holdAtContactPoint = true;
        [SerializeField, Tooltip("Keep the object inside the comfort volume while it is held, so " +
            "it cannot be dragged into the viewer's face or out through the frame edge.")]
        private bool clampToComfortVolume = true;

        [Header("Release")]
        [SerializeField, Tooltip("Carry the tip's velocity into the object when it is let go, so " +
            "it can be thrown or placed gently. Off makes it drop dead from where it was.")]
        private bool inheritVelocityOnRelease = true;
        [SerializeField, Range(0f, 2f), Tooltip("Fraction of the tip's velocity handed over. " +
            "Below 1 because a tracked device reports faster peaks than the hand really moved.")]
        private float throwScale = 0.85f;

        [Header("Events")]
        [SerializeField, Tooltip("Raised when the pen takes hold of this object.")]
        private UnityEvent grabbed = new UnityEvent();
        [SerializeField, Tooltip("Raised when the pen lets go of this object.")]
        private UnityEvent released = new UnityEvent();

        private Rigidbody _body;
        private bool _wasUsingGravity;
        private float _originalDrag;
        private float _originalAngularDrag;
        private RigidbodyInterpolation _originalInterpolation;

        /// <summary>The body this drives. Never null - the component requires one.</summary>
        public Rigidbody Body {
            get {
                if (_body == null) {
                    _body = GetComponent<Rigidbody>();
                }
                return _body;
            }
        }

        /// <summary>True while the pen is holding this.</summary>
        public bool IsHeld { get; private set; }

        /// <summary>Extra reach beyond the tip radius, in metres.</summary>
        public float GrabMargin {
            get { return grabMargin; }
        }

        /// <summary>Whether the hold point is where the tip touched rather than the centre.</summary>
        public bool HoldAtContactPoint {
            get { return holdAtContactPoint; }
        }

        /// <summary>Whether this is kept inside the comfort volume while held.</summary>
        public bool ClampToComfortVolume {
            get { return clampToComfortVolume; }
        }

        /// <summary>Raised when the pen takes hold.</summary>
        public UnityEvent Grabbed {
            get { return grabbed; }
        }

        /// <summary>Raised when the pen lets go.</summary>
        public UnityEvent Released {
            get { return released; }
        }

        /// <summary>
        /// Called by <see cref="StylusGrab"/>. Stores the body settings the hold overrides so
        /// release can put them back exactly, rather than guessing at defaults.
        /// </summary>
        internal void BeginHold() {
            Rigidbody body = Body;
            _wasUsingGravity = body.useGravity;
            _originalDrag = body.linearDamping;
            _originalAngularDrag = body.angularDamping;
            _originalInterpolation = body.interpolation;

            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            IsHeld = true;
            grabbed.Invoke();
        }

        /// <summary>
        /// Called by <see cref="StylusGrab"/>. Restores everything <see cref="BeginHold"/> changed
        /// and optionally hands the tip's velocity over.
        /// </summary>
        internal void EndHold(Vector3 tipVelocity) {
            Rigidbody body = Body;
            body.useGravity = _wasUsingGravity;
            body.linearDamping = _originalDrag;
            body.angularDamping = _originalAngularDrag;
            body.interpolation = _originalInterpolation;

            if (inheritVelocityOnRelease) {
                body.linearVelocity = tipVelocity * throwScale;
            } else {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            IsHeld = false;
            released.Invoke();
        }
    }
}
