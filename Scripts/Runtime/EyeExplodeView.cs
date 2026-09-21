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
        [SerializeField, Range(0.1f, 5f)] private float transitionDuration = 0.9f;

        private Transform[] parts;
        private Vector3[] closedPositions;
        private Vector3[] openPositions;
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

            float step = Time.deltaTime / transitionDuration;
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
            float eased = Mathf.SmoothStep(0f, 1f, value);
            for (int i = 0; i < partCount; i++) {
                parts[i].localPosition = Vector3.Lerp(closedPositions[i], openPositions[i], eased);
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

            for (int i = 0; i < poses.Length; i++) {
                Transform part = modelRoot.Find(poses[i].PartPath);
                if (part == null) {
                    Debug.LogError($"{nameof(EyeExplodeView)} could not resolve part path '{poses[i].PartPath}' under '{modelRoot.name}'. Rebake the pose set.", this);
                    continue;
                }

                parts[partCount] = part;
                closedPositions[partCount] = poses[i].ClosedPosition;
                openPositions[partCount] = poses[i].OpenPosition;
                partCount++;
            }
        }
    }
}