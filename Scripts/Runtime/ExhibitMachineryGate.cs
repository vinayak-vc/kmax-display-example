using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Halts a model's own animation driver while the model is pulled apart, and starts it again
    /// once it is fully back together.
    ///
    /// The engine exhibit needs this because <c>Enginei4</c> drives its pistons, rods and valves
    /// by writing their local positions every frame. Those writes are harmless on their own -
    /// <see cref="EyeExplodeView"/> only ever moves the group nodes above them - but the connecting
    /// rods aim at targets parented under the pistons, so as soon as the pistons travel away from
    /// the block the rods swing across the gap to keep pointing at them and the teardown reads as
    /// broken rather than as an exploded view.
    ///
    /// The driver is held as a plain <see cref="MonoBehaviour"/> rather than as its own type on
    /// purpose. <c>Enginei4</c> lives in <c>Assembly-CSharp</c>, which already references this
    /// assembly, so naming the type here would close a reference cycle and neither assembly would
    /// compile. Toggling <c>enabled</c> needs no type knowledge, and it leaves the driver free to
    /// be any behaviour whose <c>Update</c> should pause with the explode.
    /// </summary>
    public class ExhibitMachineryGate : MonoBehaviour {
        [SerializeField, Tooltip("The explode view whose state decides whether the machinery runs.")]
        private EyeExplodeView explodeView;
        [SerializeField, Tooltip("The behaviour driving the model's own animation. Disabled while " +
            "the model is apart. Typically the Enginei4 component on the model root.")]
        private MonoBehaviour machinery;
        [SerializeField, Tooltip("Run the machinery whenever the model is assembled. Clear this to " +
            "leave the model still until something else starts it.")]
        private bool runWhenAssembled = true;

        private void Awake() {
            if (explodeView == null) {
                Debug.LogError($"{nameof(ExhibitMachineryGate)} on '{name}' has no {nameof(explodeView)} assigned; the machinery will never be paused.", this);
                enabled = false;
                return;
            }

            if (machinery == null) {
                Debug.LogError($"{nameof(ExhibitMachineryGate)} on '{name}' has no {nameof(machinery)} assigned; there is nothing to pause.", this);
                enabled = false;
                return;
            }

            explodeView.TransitionStarted += OnTransitionStarted;
            explodeView.TransitionCompleted += OnTransitionCompleted;

            // The exhibit loads assembled, so the driver starts in whichever state this gate is
            // configured for rather than in whatever state the scene happened to be saved in.
            SetMachineryRunning(runWhenAssembled && !explodeView.IsExpanded);
        }

        private void OnDestroy() {
            if (explodeView == null) {
                return;
            }

            explodeView.TransitionStarted -= OnTransitionStarted;
            explodeView.TransitionCompleted -= OnTransitionCompleted;
        }

        /// <summary>
        /// Stops the driver as the model starts to open. Closing is deliberately not handled here:
        /// the parts are still apart for the whole of the closing transition, so the driver stays
        /// off until they have actually arrived.
        /// </summary>
        private void OnTransitionStarted(bool expanding) {
            if (!expanding) {
                return;
            }

            SetMachineryRunning(false);
        }

        private void OnTransitionCompleted(bool expanded) {
            if (expanded) {
                return;
            }

            SetMachineryRunning(runWhenAssembled);
        }

        private void SetMachineryRunning(bool running) {
            if (machinery == null) {
                return;
            }

            machinery.enabled = running;
        }
    }
}
