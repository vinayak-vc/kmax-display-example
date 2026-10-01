using System;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Moves the eye's parts between their assembled and exploded poses.
    /// Holds no UI and no selection state - it only knows how open the eye is.
    /// </summary>
    public class EyeExplodeView : MonoBehaviour {
        [SerializeField] private Transform modelRoot;
        [SerializeField] private EyeExplodePoseSet poseSet;
        [SerializeField, Range(0.1f, 5f)] private float transitionDuration = 1.05f;
        [SerializeField, Range(0f, 0.45f), Tooltip("Fraction of the transition duration used to stagger parts so they cascade rather than moving in robotic lockstep.")]
        private float cascadeFraction = 0.28f;
        [SerializeField, Range(0f, 0.25f), Tooltip("Curved arc height relative to each part's travel distance so parts travel along a graceful arc instead of a straight line.")]
        private float arcCurvature = 0.14f;
        [SerializeField, Range(0f, 0.2f), Tooltip("Subtle spring overshoot when parts reach their open pose.")]
        private float springOvershoot = 0.08f;

        private Transform[] parts;
        private Vector3[] closedPositions;
        private Vector3[] openPositions;
        private Vector3[] arcAxes;
        private int partCount;
        private float expansion;
        private float targetExpansion;
        private bool isTransitioning;
        private bool isResolved;

        /// <summary>
        /// Raised once a transition settles. The argument is true when the eye ended up open.
        /// </summary>
        public event Action<bool> TransitionCompleted;

        /// <summary>
        /// Raised the moment a transition is asked for, before any part has moved. The argument is
        /// true when the model is about to open.
        ///
        /// <see cref="TransitionCompleted"/> is too late for anything that has to stop before the
        /// parts separate - a model with its own animation driver has to be halted on the way out,
        /// not once the parts have already flown apart around it.
        /// </summary>
        public event Action<bool> TransitionStarted;

        /// <summary>
        /// True as soon as an opening transition starts, false as soon as a closing one does.
        /// </summary>
        public bool IsExpanded {
            get { return targetExpansion > 0.5f; }
        }

        public float Expansion {
            get { return expansion; }
        }

        private void Awake() {
            ResolveParts();
            SetExpansionImmediate(0f);
        }

        private void Update() {
            if (!isTransitioning) {
                return;
            }

            float step = Time.deltaTime / Mathf.Max(0.05f, transitionDuration);
            expansion = Mathf.MoveTowards(expansion, targetExpansion, step);
            ApplyExpansion(expansion);

            if (!Mathf.Approximately(expansion, targetExpansion)) {
                return;
            }

            isTransitioning = false;
            if (TransitionCompleted != null) {
                TransitionCompleted(IsExpanded);
            }
        }

        public void SetExpanded(bool expanded) {
            targetExpansion = expanded ? 1f : 0f;
            isTransitioning = !Mathf.Approximately(expansion, targetExpansion);

            if (TransitionStarted != null) {
                TransitionStarted(IsExpanded);
            }

            if (isTransitioning) {
                return;
            }

            if (TransitionCompleted != null) {
                TransitionCompleted(IsExpanded);
            }
        }

        public void Toggle() {
            SetExpanded(!IsExpanded);
        }

        /// <summary>
        /// Jumps straight to an expansion value. Used for the initial closed pose and for
        /// previewing the model outside play mode.
        /// </summary>
        public void SetExpansionImmediate(float value) {
            ResolveParts();
            expansion = Mathf.Clamp01(value);
            targetExpansion = expansion;
            isTransitioning = false;
            ApplyExpansion(expansion);
        }

        private void ApplyExpansion(float value) {
            if (partCount <= 0) {
                return;
            }

            // At exact endpoints (0 or 1), snap directly to the exact closed or open positions
            // so bounds and hotspot calculations see zero residual offset.
            if (value <= 0.0001f) {
                for (int i = 0; i < partCount; i++) {
                    parts[i].localPosition = closedPositions[i];
                }
                return;
            }

            if (value >= 0.9999f) {
                for (int i = 0; i < partCount; i++) {
                    parts[i].localPosition = openPositions[i];
                }
                return;
            }

            float staggerWindow = partCount > 1 ? Mathf.Clamp(cascadeFraction, 0f, 0.45f) : 0f;
            float partWindow = 1f - staggerWindow;

            for (int i = 0; i < partCount; i++) {
                float normalizedIndex = partCount > 1 ? i / (float)(partCount - 1) : 0f;
                float start = normalizedIndex * staggerWindow;
                float localT = partWindow > 0.0001f ? Mathf.Clamp01((value - start) / partWindow) : value;

                float eased = Mathf.SmoothStep(0f, 1f, localT);
                float spring = Mathf.Sin(localT * Mathf.PI) * springOvershoot * localT;
                Vector3 basePos = Vector3.LerpUnclamped(closedPositions[i], openPositions[i], eased + spring);

                // Graceful curved arc perpendicular to the straight travel line, vanishing at both endpoints (0 and 1).
                float arcBell = Mathf.Sin(localT * Mathf.PI);
                Vector3 arcOffset = arcAxes[i] * (arcCurvature * arcBell);
                parts[i].localPosition = basePos + arcOffset;
            }
        }

        private void ResolveParts() {
            if (isResolved) {
                return;
            }

            isResolved = true;
            partCount = 0;

            if (modelRoot == null) {
                Debug.LogError($"{nameof(EyeExplodeView)} on '{name}' has no {nameof(modelRoot)} assigned; the eye will not move.", this);
                return;
            }

            if (poseSet == null) {
                Debug.LogError($"{nameof(EyeExplodeView)} on '{name}' has no {nameof(poseSet)} assigned; the eye will not move.", this);
                return;
            }

            EyePartPose[] poses = poseSet.Poses;
            parts = new Transform[poses.Length];
            closedPositions = new Vector3[poses.Length];
            openPositions = new Vector3[poses.Length];
            arcAxes = new Vector3[poses.Length];

            for (int i = 0; i < poses.Length; i++) {
                Transform part = modelRoot.Find(poses[i].PartPath);
                if (part == null) {
                    Debug.LogError($"{nameof(EyeExplodeView)} could not resolve part path '{poses[i].PartPath}' under '{modelRoot.name}'. Rebake the pose set.", this);
                    continue;
                }

                Vector3 closed = poses[i].ClosedPosition;
                Vector3 open = poses[i].OpenPosition;
                Vector3 delta = open - closed;
                float distance = delta.magnitude;

                Vector3 arcAxis = Vector3.zero;
                if (distance > 0.0001f) {
                    Vector3 dir = delta / distance;
                    Vector3 reference = Mathf.Abs(dir.y) < 0.85f ? Vector3.up : Vector3.right;
                    arcAxis = Vector3.Cross(dir, reference).normalized * distance;
                    if ((partCount & 1) == 1) {
                        arcAxis = -arcAxis * 0.65f + Vector3.up * (distance * 0.55f);
                    } else {
                        arcAxis = arcAxis * 0.65f + Vector3.up * (distance * 0.55f);
                    }
                }

                parts[partCount] = part;
                closedPositions[partCount] = closed;
                openPositions[partCount] = open;
                arcAxes[partCount] = arcAxis;
                partCount++;
            }
        }
    }
}