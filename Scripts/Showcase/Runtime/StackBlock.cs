using UnityEngine;

namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// One block in Stack: where it started, whether it has come to rest, and what it hit.
    ///
    /// The collision reporting is the reason this is a component rather than state held in an
    /// array by <see cref="StackGame"/> - <c>OnCollisionEnter</c> has to be on the body that
    /// collides. It pays for itself twice over, because the knock a viewer feels through the pen
    /// when the block in their hand touches the tower is one of the few cues telling them they
    /// have arrived, and it arrives before they can see the contact.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(Grabbable))]
    public class StackBlock : MonoBehaviour {
        [SerializeField, Tooltip("Metres per second below which the block counts as at rest. " +
            "Scoring only counts settled blocks, so a tower mid-collapse does not read as a score.")]
        private float settleSpeed = 0.02f;
        [SerializeField, Tooltip("Seconds below the settle speed before the block is called settled.")]
        private float settleTime = 0.25f;
        [SerializeField, Tooltip("Relative speed of a collision that counts as a knock worth " +
            "feeling, in metres per second.")]
        private float knockSpeed = 0.15f;
        [SerializeField, Tooltip("Relative speed treated as a full-strength knock.")]
        private float knockFullSpeed = 1.2f;

        private Rigidbody _body;
        private Grabbable _grabbable;
        private StackGame _game;
        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private float _restingSince = -1f;

        /// <summary>The body. Never null - required by the component.</summary>
        public Rigidbody Body {
            get {
                if (_body == null) {
                    _body = GetComponent<Rigidbody>();
                }
                return _body;
            }
        }

        /// <summary>True while the pen is holding this block.</summary>
        public bool IsHeld {
            get {
                if (_grabbable == null) {
                    _grabbable = GetComponent<Grabbable>();
                }
                return _grabbable != null && _grabbable.IsHeld;
            }
        }

        /// <summary>
        /// True when the block is not held and has been below the settle speed long enough to
        /// count. Scoring a tower that is still falling would record a height that never existed.
        /// </summary>
        public bool IsSettled {
            get {
                if (IsHeld) {
                    return false;
                }
                return _restingSince >= 0f && Time.time - _restingSince >= settleTime;
            }
        }

        /// <summary>World-space top of the block's renderer bounds.</summary>
        public float TopY {
            get {
                Renderer renderer = GetComponentInChildren<Renderer>();
                if (renderer == null) {
                    return transform.position.y;
                }
                return renderer.bounds.max.y;
            }
        }

        private void Awake() {
            _body = GetComponent<Rigidbody>();
            _grabbable = GetComponent<Grabbable>();
            _homePosition = transform.position;
            _homeRotation = transform.rotation;
        }

        /// <summary>
        /// Told by <see cref="StackGame"/> at startup, so blocks authored into the scene do not
        /// have to carry a reference each.
        /// </summary>
        public void Bind(StackGame game) {
            _game = game;
        }

        /// <summary>
        /// Records where the block should return to on a reset. Called by the game once the
        /// opening scatter has settled, so reset returns blocks to a tidy arrangement rather than
        /// to wherever they happened to be authored.
        /// </summary>
        public void MarkHome() {
            _homePosition = transform.position;
            _homeRotation = transform.rotation;
        }

        /// <summary>Puts the block back where it started, at rest.</summary>
        public void ReturnHome() {
            Body.linearVelocity = Vector3.zero;
            Body.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(_homePosition, _homeRotation);
            _restingSince = -1f;
        }

        private void FixedUpdate() {
            if (IsHeld) {
                _restingSince = -1f;
                return;
            }
            bool slow = Body.linearVelocity.sqrMagnitude < settleSpeed * settleSpeed
                && Body.angularVelocity.sqrMagnitude < 1f;
            if (!slow) {
                _restingSince = -1f;
                return;
            }
            if (_restingSince < 0f) {
                _restingSince = Time.time;
            }
        }

        private void OnCollisionEnter(Collision collision) {
            if (_game == null) {
                return;
            }
            float speed = collision.relativeVelocity.magnitude;
            if (speed < knockSpeed) {
                return;
            }
            StackBlock other = collision.collider.GetComponentInParent<StackBlock>();
            // Only a contact the viewer is responsible for is worth feeling. A tower settling on
            // its own would otherwise buzz the pen in someone's hand who is not touching anything.
            bool involvesHand = IsHeld || (other != null && other.IsHeld);
            if (!involvesHand) {
                return;
            }
            float span = Mathf.Max(0.0001f, knockFullSpeed - knockSpeed);
            _game.ReportKnock(Mathf.Clamp01((speed - knockSpeed) / span));
        }
    }
}
