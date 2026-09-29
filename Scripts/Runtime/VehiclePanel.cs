using System;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// One hinged panel of a vehicle - a door, the hood, the trunk lid, the sunroof.
    ///
    /// Sits on an empty placed at the hinge line with the panel's geometry parented under it, so
    /// the panel swings about a real axis rather than about its own mesh origin. The car's panels
    /// all share one origin at the model root, so hinging them in place is not possible; the build
    /// step creates these pivots and reparents the geometry onto them without moving it.
    ///
    /// Angle is driven rather than physics-simulated. A door on an exhibit has to end up at a known
    /// angle every time, and has to be able to close again from wherever the viewer left it.
    /// </summary>
    public class VehiclePanel : MonoBehaviour {
        [SerializeField, Tooltip("Axis the panel turns about, in this pivot's local space.")]
        private Vector3 hingeAxis = Vector3.up;
        [SerializeField, Tooltip("Angle in degrees at the open extreme. Sign sets which way it swings.")]
        private float openAngle = 65f;
        [SerializeField, Range(0.1f, 5f), Tooltip("Seconds from shut to fully open.")]
        private float duration = 1.1f;
        [SerializeField, Tooltip("Open on load. Off for everything except a panel meant to start ajar.")]
        private bool startOpen;

        private Quaternion closedRotation;
        private float openness;
        private float target;
        private bool isMoving;
        private bool isResolved;

        /// <summary>
        /// Raised when the panel settles. The argument is true when it ended up open.
        /// </summary>
        public event Action<bool> Settled;

        /// <summary>
        /// True from the moment an opening move starts until a closing one does.
        /// </summary>
        public bool IsOpen {
            get { return target > 0.5f; }
        }

        /// <summary>
        /// 0 shut, 1 fully open, and everything between while it is moving.
        /// </summary>
        public float Openness {
            get { return openness; }
        }

        public float OpenAngle {
            get { return openAngle; }
        }

        private void Awake() {
            Resolve();
            SetOpennessImmediate(startOpen ? 1f : 0f);
        }

        private void Update() {
            if (!isMoving) {
                return;
            }

            openness = Mathf.MoveTowards(openness, target, Time.deltaTime / duration);
            Apply(openness);

            if (!Mathf.Approximately(openness, target)) {
                return;
            }

            isMoving = false;

            if (Settled != null) {
                Settled(IsOpen);
            }
        }

        public void SetOpen(bool open) {
            Resolve();
            target = open ? 1f : 0f;
            isMoving = !Mathf.Approximately(openness, target);

            if (isMoving) {
                return;
            }

            if (Settled != null) {
                Settled(IsOpen);
            }
        }

        public void Toggle() {
            SetOpen(!IsOpen);
        }

        /// <summary>
        /// Jumps straight to an angle, for the initial pose and for previewing outside play mode.
        /// </summary>
        public void SetOpennessImmediate(float value) {
            Resolve();
            openness = Mathf.Clamp01(value);
            target = openness;
            isMoving = false;
            Apply(openness);
        }

        /// <summary>
        /// Re-reads the shut pose from the transform as it stands. The build step calls this after
        /// placing the pivot, so a rebuild does not bake in whatever angle the panel was left at.
        /// </summary>
        public void CaptureClosedRotation() {
            closedRotation = transform.localRotation;
            isResolved = true;
            openness = 0f;
            target = 0f;
            isMoving = false;
        }

        private void Apply(float value) {
            // Eased rather than linear: a door that starts and stops abruptly reads as a jump cut,
            // and on a stereo panel abrupt motion at close range is genuinely uncomfortable.
            float eased = Mathf.SmoothStep(0f, 1f, value);
            transform.localRotation = closedRotation * Quaternion.AngleAxis(openAngle * eased, hingeAxis);
        }

        private void Resolve() {
            if (isResolved) {
                return;
            }

            isResolved = true;
            closedRotation = transform.localRotation;

            if (hingeAxis.sqrMagnitude <= Mathf.Epsilon) {
                Debug.LogError($"{nameof(VehiclePanel)} on '{name}' has a zero {nameof(hingeAxis)}; it cannot turn.", this);
                hingeAxis = Vector3.up;
            }

            hingeAxis = hingeAxis.normalized;
        }
    }
}
