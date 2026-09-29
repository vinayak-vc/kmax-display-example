using UnityEngine;
using UnityEngine.EventSystems;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// One corner of the scale box. Carries no logic of its own - it reports pointer state to the
    /// <see cref="EyeScaleBox"/> that built it, which owns the arithmetic.
    ///
    /// Both drag and hover are tracked. Hover matters as much as drag, because the view's own
    /// orbit has to stand down the moment the pointer is over a handle: the orbit drag threshold
    /// is smaller than the EventSystem's, so without that the view would start turning before the
    /// drag was ever recognised and the grab would be lost.
    /// </summary>
    public class EyeScaleHandle : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler {
        private EyeScaleBox _owner;

        /// <summary>
        /// True while the pointer rests on this handle.
        /// </summary>
        public bool IsHovered { get; private set; }

        /// <summary>
        /// True while this handle is being dragged.
        /// </summary>
        public bool IsDragging { get; private set; }

        public void Initialize(EyeScaleBox owner) {
            _owner = owner;
        }

        private void OnDisable() {
            // A handle switched off mid-grab would otherwise leave the box convinced it is still
            // being dragged, and the view's orbit suppressed for good.
            if (IsDragging && _owner != null) {
                _owner.EndScale();
            }

            IsDragging = false;
            IsHovered = false;
        }

        public void OnPointerEnter(PointerEventData eventData) {
            IsHovered = true;
        }

        public void OnPointerExit(PointerEventData eventData) {
            IsHovered = false;
        }

        public void OnBeginDrag(PointerEventData eventData) {
            if (_owner == null) {
                return;
            }

            IsDragging = true;
            _owner.BeginScale(eventData);
        }

        public void OnDrag(PointerEventData eventData) {
            if (_owner != null && IsDragging) {
                _owner.UpdateScale(eventData);
            }
        }

        public void OnEndDrag(PointerEventData eventData) {
            IsDragging = false;

            if (_owner != null) {
                _owner.EndScale();
            }
        }
    }
}
