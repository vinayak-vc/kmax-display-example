using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Gives a UGUI button the same interaction feel the 3D badges have: it swells on hover,
    /// compresses on press and springs back on release.
    ///
    /// On a stereo display the buttons sit on a world-space canvas at a fixed depth, so there is no
    /// cursor shadow or hover highlight to tell the viewer the pointer has arrived. Scale is the
    /// cue that survives being looked at with two eyes from an angle.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class UiButtonMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler {
        [Header("Scale")]
        [SerializeField, Range(1f, 1.4f), Tooltip("Scale held while the pointer rests on the button.")]
        private float hoverScale = 1.07f;
        [SerializeField, Range(0.6f, 1f), Tooltip("Scale compressed to at the moment of the press.")]
        private float pressScale = 0.93f;
        [SerializeField, Tooltip("Seconds the scale takes to settle. Smaller is snappier.")]
        private float scaleSmoothTime = 0.07f;

        [Header("Lift")]
        [SerializeField, Tooltip("Canvas units the button rises while hovered. On a stereo panel a " +
            "small rise reads as the control coming to meet the pointer.")]
        private float hoverLift = 7f;

        [Header("Tint")]
        [SerializeField, Tooltip("Graphic tinted on hover. Left empty, no tinting happens.")]
        private Graphic tintTarget;
        [SerializeField, Tooltip("Colour the graphic takes while hovered.")]
        private Color hoverTint = new Color(0.62f, 0.88f, 1f, 1f);
        [SerializeField, Tooltip("Seconds the tint takes to cross-fade.")]
        private float tintBlendTime = 0.1f;

        [Header("Press Flash")]
        [SerializeField, Tooltip("Flare the button on press. The compression alone is easy to miss " +
            "with a wand, where there is no cursor resting on the control to watch.")]
        private bool pressFlash = true;
        [SerializeField, Tooltip("Colour flashed at the instant of the press.")]
        private Color pressFlashTint = new Color(0.90f, 0.97f, 1f, 1f);
        [SerializeField, Tooltip("Seconds the flash takes to fall away.")]
        private float pressFlashDecay = 0.26f;

        [Header("Attention")]
        [SerializeField, Tooltip("Breathe gently when idle. Worth it for the one button that starts " +
            "the experience, distracting on every button at once.")]
        private bool idlePulse;
        [SerializeField, Range(0f, 0.08f), Tooltip("Depth of the idle breathing.")]
        private float idlePulseAmplitude = 0.02f;
        [SerializeField, Range(0.2f, 3f), Tooltip("Speed of the idle breathing.")]
        private float idlePulseSpeed = 1.4f;

        private RectTransform _rectTransform;
        private Selectable _selectable;
        private Vector3 _restScale = Vector3.one;
        private Vector2 _restAnchoredPosition;
        private Color _restTint = Color.white;

        private float _hoverProgress;
        private float _pressProgress;
        private float _flashProgress;
        private float _currentScale = 1f;
        private float _scaleVelocity;
        private float _currentLift;
        private float _liftVelocity;
        private bool _isHovered;
        private bool _isPressed;

        /// <summary>
        /// Raised when the pointer moves onto or off the button. True means it moved on.
        /// Lets the controller sound a hover cue without this class knowing about audio.
        /// </summary>
        public event Action<bool> HoverChanged;

        private void Awake() {
            _rectTransform = GetComponent<RectTransform>();
            _selectable = GetComponent<Selectable>();
            _restScale = _rectTransform.localScale;
            _restAnchoredPosition = _rectTransform.anchoredPosition;

            if (tintTarget != null) {
                _restTint = tintTarget.color;
            }
        }

        /// <summary>
        /// The Back and Next buttons are switched off and on as the flow changes, so the state is
        /// cleared here - otherwise a button hidden mid-hover comes back still swollen.
        /// </summary>
        private void OnEnable() {
            _isHovered = false;
            _isPressed = false;
            _hoverProgress = 0f;
            _pressProgress = 0f;
            _flashProgress = 0f;
            _currentScale = 1f;
            _scaleVelocity = 0f;
            _currentLift = 0f;
            _liftVelocity = 0f;

            if (_rectTransform != null) {
                _rectTransform.localScale = _restScale;
                _rectTransform.anchoredPosition = _restAnchoredPosition;
            }

            if (tintTarget != null) {
                tintTarget.color = _restTint;
            }
        }

        private void Update() {
            float dt = Time.unscaledDeltaTime;
            bool interactable = _selectable == null || _selectable.IsInteractable();

            _hoverProgress = Mathf.MoveTowards(_hoverProgress, (_isHovered && interactable) ? 1f : 0f, dt * 9f);
            _pressProgress = Mathf.MoveTowards(_pressProgress, (_isPressed && interactable) ? 1f : 0f, dt * 16f);

            float target = Mathf.Lerp(1f, hoverScale, _hoverProgress);
            target *= Mathf.Lerp(1f, pressScale, _pressProgress);

            if (idlePulse && interactable) {
                // Only breathe while resting, so the pulse never fights the hover.
                float rest = 1f - _hoverProgress;
                target *= 1f + idlePulseAmplitude * rest * Mathf.Sin(Time.unscaledTime * idlePulseSpeed);
            }

            _currentScale = Mathf.SmoothDamp(_currentScale, target, ref _scaleVelocity, scaleSmoothTime, Mathf.Infinity, dt);
            _rectTransform.localScale = _restScale * _currentScale;

            // The lift eases out under the press, so pressing pushes the button back down again.
            float targetLift = hoverLift * _hoverProgress * (1f - _pressProgress);
            _currentLift = Mathf.SmoothDamp(_currentLift, targetLift, ref _liftVelocity, scaleSmoothTime, Mathf.Infinity, dt);
            _rectTransform.anchoredPosition = _restAnchoredPosition + new Vector2(0f, _currentLift);

            if (_flashProgress > 0f) {
                _flashProgress = Mathf.MoveTowards(_flashProgress, 0f,
                    pressFlashDecay > 0f ? dt / pressFlashDecay : 1f);
            }

            if (tintTarget != null) {
                Color settled = Color.Lerp(_restTint, hoverTint, _hoverProgress);
                float step = tintBlendTime > 0f ? dt / tintBlendTime : 1f;
                tintTarget.color = Color.Lerp(tintTarget.color, settled, Mathf.Clamp01(step));

                if (pressFlash && _flashProgress > 0f) {
                    // Applied after the blend rather than through it, so the flash is instant on the
                    // way in and only the decay is eased.
                    tintTarget.color = Color.Lerp(tintTarget.color, pressFlashTint,
                        Mathf.SmoothStep(0f, 1f, _flashProgress));
                }
            }
        }

        public void OnPointerEnter(PointerEventData eventData) {
            if (_isHovered) {
                return;
            }

            _isHovered = true;

            if (HoverChanged != null) {
                HoverChanged(true);
            }
        }

        public void OnPointerExit(PointerEventData eventData) {
            _isPressed = false;

            if (!_isHovered) {
                return;
            }

            _isHovered = false;

            if (HoverChanged != null) {
                HoverChanged(false);
            }
        }

        public void OnPointerDown(PointerEventData eventData) {
            _isPressed = true;
            _flashProgress = 1f;
        }

        public void OnPointerUp(PointerEventData eventData) {
            _isPressed = false;
        }
    }
}
