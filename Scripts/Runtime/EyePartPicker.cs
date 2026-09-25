using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Makes one anatomical structure selectable by pointing at the geometry itself, not just at
    /// its numbered badge.
    ///
    /// Added by <see cref="EyeAnatomyController"/> to each catalogued part once that part has
    /// colliders. The badges stay the primary affordance - they are what carry the number and the
    /// hover state - but with the stylus in hand, touching the structure you are looking at is the
    /// obvious gesture, and it is the one that works for parts whose badge is buried behind a shell.
    /// </summary>
    public class EyePartPicker : MonoBehaviour, IPointerClickHandler {
        [SerializeField, Tooltip("Pixels the pointer may travel between press and release and still " +
            "count as a pick. Beyond this the viewer was dragging the view around.")]
        private float clickDragTolerance = 12f;

        private int _partIndex = -1;
        private int _lastClickFrame = -1;

        /// <summary>
        /// Raised when this structure is picked. The argument is the part's index in the catalog.
        /// </summary>
        public event Action<int> Picked;

        public int PartIndex {
            get { return _partIndex; }
        }

        public void Initialize(int partIndex) {
            _partIndex = partIndex;
        }

        public void OnPointerClick(PointerEventData eventData) {
            if (_partIndex < 0) {
                return;
            }

            // The same press can arrive twice in one frame when the Kmax driver emulates the mouse
            // alongside the stylus pointer.
            if (Time.frameCount == _lastClickFrame) {
                return;
            }

            // A press that travelled was the viewer orbiting the view, not picking a structure.
            if (eventData != null &&
                (eventData.position - eventData.pressPosition).magnitude > clickDragTolerance) {
                return;
            }

            _lastClickFrame = Time.frameCount;

            if (Picked != null) {
                Picked(_partIndex);
            }
        }
    }
}
