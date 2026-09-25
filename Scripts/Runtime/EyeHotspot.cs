using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// A subtle, numbered interaction badge pinned to one part of the eye.
    /// Faces the active camera, displays the part index (1 to 18), and carries the exhibit's
    /// interaction feel: a staggered pop-in when the eye opens, a lift and glow on hover, a punch
    /// on press, and a slow halo pulse while selected.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class EyeHotspot : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler,
        IPointerExitHandler, IPointerDownHandler, IPointerUpHandler {
        private static readonly int[] ColorPropertyIds = new int[] {
            Shader.PropertyToID("_BaseColor"),
            Shader.PropertyToID("_Color")
        };

        [Header("Visual Components")]
        [SerializeField, Tooltip("Transform holding the billboarded badge quad and text.")]
        private Transform visualRoot;
        [SerializeField, Tooltip("Renderer for the circular badge background.")]
        private Renderer badgeRenderer;
        [SerializeField, Tooltip("TextMeshPro displaying the badge number.")]
        private TextMeshPro numberText;
        [SerializeField, Tooltip("Optional halo ring that pulses outward while this badge is selected.")]
        private Transform selectionHalo;
        [SerializeField, Tooltip("Optional renderer of the halo ring.")]
        private Renderer haloRenderer;

        [Header("Badge Colors")]
        [SerializeField] private Color idleBadgeColor = new Color(0.9f, 0.95f, 1f, 0.85f);
        [SerializeField] private Color hoverBadgeColor = new Color(0.5f, 0.85f, 1f, 1f);
        [SerializeField] private Color selectedBadgeColor = new Color(1f, 0.82f, 0.35f, 1f);

        [Header("Text Colors")]
        [SerializeField] private Color idleTextColor = new Color(1f, 1f, 1f, 0.95f);
        [SerializeField] private Color hoverTextColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private Color selectedTextColor = new Color(1f, 0.95f, 0.75f, 1f);

        [Header("Animation & Alive Feel")]
        [SerializeField, Range(1f, 1.5f)] private float hoverScale = 1.18f;
        [SerializeField, Range(0f, 0.1f)] private float pulseAmplitude = 0.035f;
        [SerializeField, Range(0.5f, 5f)] private float pulseSpeed = 2.2f;
        [SerializeField, Range(1f, 1.6f), Tooltip("Extra scale a selected badge holds over an idle one.")]
        private float selectedScale = 1.22f;
        [SerializeField, Tooltip("Metres the badge lifts towards the viewer while hovered.")]
        private float hoverLift = 0.0025f;

        [Header("Pop-in")]
        [SerializeField, Tooltip("Scale up from nothing when the eye opens, rather than appearing all at once.")]
        private bool animatePopIn = true;
        [SerializeField, Tooltip("Seconds one badge takes to pop in.")]
        private float popInDuration = 0.34f;
        [SerializeField, Tooltip("Seconds of delay added per badge index, so they arrive as a sweep.")]
        private float popInStagger = 0.035f;
        [SerializeField, Range(1f, 1.8f), Tooltip("How far past full size the pop-in overshoots before settling.")]
        private float popInOvershoot = 1.25f;

        [Header("Press")]
        [SerializeField, Range(0.5f, 1f), Tooltip("Scale the badge compresses to at the moment of the press.")]
        private float pressScale = 0.82f;
        [SerializeField, Tooltip("Seconds the press punch takes to recover.")]
        private float pressRecoverDuration = 0.28f;

        [Header("Sizing")]
        [SerializeField, Tooltip("Hold a constant on-screen size while the model scales up during focus. " +
            "Without this a badge grows with the part it is pinned to and swamps the view.")]
        private bool maintainWorldSize = true;
        [SerializeField, Tooltip("Diameter in metres the badge keeps, whatever the model is scaled to.")]
        private float worldDiameter = 0.01f;

        [Header("Input")]
        [SerializeField, Tooltip("Pixels the pointer may travel between press and release and still count " +
            "as a click. Beyond this the viewer was dragging the view, not picking a part.")]
        private float clickDragTolerance = 12f;

        private MaterialPropertyBlock _propertyBlock;
        private int _partIndex = -1;
        private bool _isSelected;
        private bool _isHovered;
        private float _hoverProgress;
        private float _selectProgress;
        private Camera _targetCamera;
        private Vector3 _initialVisualScale = Vector3.one;
        private Vector3 _initialVisualLocalPosition;

        private float _popInProgress = 1f;
        private float _popInDelayRemaining;
        private float _pressProgress;
        private int _lastClickFrame = -1;

        public event Action<EyeHotspot> Clicked;

        /// <summary>
        /// Raised when the pointer moves onto or off this badge. True means it moved on.
        /// Exists so the controller can sound a hover cue without this class knowing about audio.
        /// </summary>
        public event Action<EyeHotspot, bool> HoverChanged;

        public int PartIndex {
            get { return _partIndex; }
        }

        private void Awake() {
            if (visualRoot == null) {
                visualRoot = transform.Find("Visuals");
                if (visualRoot == null) {
                    visualRoot = transform;
                }
            }

            _initialVisualScale = visualRoot.localScale;
            _initialVisualLocalPosition = visualRoot.localPosition;

            if (badgeRenderer == null) {
                badgeRenderer = visualRoot.GetComponentInChildren<Renderer>();
            }

            if (numberText == null) {
                numberText = visualRoot.GetComponentInChildren<TextMeshPro>();
            }

            _propertyBlock = new MaterialPropertyBlock();
            _targetCamera = ResolveCamera();

            if (visualRoot != null) {
                visualRoot.rotation = _targetCamera != null ? _targetCamera.transform.rotation : Quaternion.identity;
            }

            RefreshVisualsImmediate();
        }

        /// <summary>
        /// Badges are switched off and on as the eye opens and closes, so the pop-in is armed here
        /// rather than in Awake - it should replay every time the eye opens.
        /// </summary>
        private void OnEnable() {
            if (!animatePopIn) {
                _popInProgress = 1f;
                _popInDelayRemaining = 0f;
                return;
            }

            _popInProgress = 0f;
            _popInDelayRemaining = Mathf.Max(0f, _partIndex) * popInStagger;
            _pressProgress = 0f;
            _hoverProgress = 0f;
            _isHovered = false;
            ApplyTransformState();
        }

        private void Update() {
            UpdateWorldSize();
            UpdateBillboarding();
            UpdateAnimation();
        }

        /// <summary>
        /// Sets the on-screen diameter this badge holds regardless of the model's scale.
        /// </summary>
        public void ConfigureSize(float diameterInMetres) {
            worldDiameter = diameterInMetres;
            UpdateWorldSize();
        }

        public void Initialize(int index, Camera camera = null) {
            _partIndex = index;
            if (camera != null) {
                _targetCamera = camera;
            }

            if (numberText != null) {
                numberText.text = (index + 1).ToString();
            }

            if (animatePopIn && isActiveAndEnabled) {
                _popInDelayRemaining = index * popInStagger;
                _popInProgress = 0f;
            }

            UpdateBillboarding();
            RefreshVisualsImmediate();
        }

        public void SetSelected(bool selected) {
            _isSelected = selected;
            RefreshVisualsImmediate();
        }

        public void OnPointerDown(PointerEventData eventData) {
            _pressProgress = 1f;
        }

        public void OnPointerUp(PointerEventData eventData) {
            SetHovered(false);
        }

        public void OnPointerClick(PointerEventData eventData) {
            // The same press can arrive twice in one frame when the Kmax driver emulates the mouse
            // alongside the stylus pointer. One selection per frame is always the right answer.
            if (Time.frameCount == _lastClickFrame) {
                return;
            }

            // A press that travelled was the viewer dragging the view around, not picking a part.
            if (eventData != null &&
                (eventData.position - eventData.pressPosition).magnitude > clickDragTolerance) {
                return;
            }

            _lastClickFrame = Time.frameCount;

            if (Clicked != null) {
                Clicked(this);
            }
        }

        public void OnPointerEnter(PointerEventData eventData) {
            SetHovered(true);
        }

        public void OnPointerExit(PointerEventData eventData) {
            SetHovered(false);
        }

        private void SetHovered(bool hovered) {
            if (_isHovered == hovered) {
                return;
            }

            _isHovered = hovered;

            if (HoverChanged != null) {
                HoverChanged(this, hovered);
            }
        }

        /// <summary>
        /// Counter-scales against the part this badge is pinned to, so focusing a part - which
        /// scales the whole model - leaves every badge the same size on screen.
        /// </summary>
        private void UpdateWorldSize() {
            if (!maintainWorldSize) {
                return;
            }

            Transform parent = transform.parent;
            float parentScale = parent != null ? parent.lossyScale.x : 1f;
            if (parentScale <= Mathf.Epsilon) {
                return;
            }

            transform.localScale = Vector3.one * (worldDiameter / parentScale);
        }

        private void UpdateBillboarding() {
            if (_targetCamera == null || !_targetCamera.isActiveAndEnabled) {
                _targetCamera = ResolveCamera();
            }

            if (visualRoot == null) {
                return;
            }

            visualRoot.rotation = _targetCamera != null ? _targetCamera.transform.rotation : Quaternion.identity;
        }

        private Camera ResolveCamera() {
            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled) {
                return main;
            }

            Camera[] all = Camera.allCameras;
            for (int i = 0; i < all.Length; i++) {
                if (all[i] != null && all[i].isActiveAndEnabled) {
                    return all[i];
                }
            }

            return null;
        }

        private void UpdateAnimation() {
            float dt = Time.deltaTime;

            _hoverProgress = Mathf.MoveTowards(_hoverProgress, _isHovered ? 1f : 0f, dt * 6f);
            _selectProgress = Mathf.MoveTowards(_selectProgress, _isSelected ? 1f : 0f, dt * 5f);

            if (_popInDelayRemaining > 0f) {
                _popInDelayRemaining -= dt;
            } else if (_popInProgress < 1f) {
                _popInProgress = Mathf.MoveTowards(_popInProgress, 1f, popInDuration > 0f ? dt / popInDuration : 1f);
            }

            if (_pressProgress > 0f) {
                _pressProgress = Mathf.MoveTowards(_pressProgress, 0f, pressRecoverDuration > 0f ? dt / pressRecoverDuration : 1f);
            }

            ApplyTransformState();
            UpdateHalo();
            RefreshColors();
        }

        private void ApplyTransformState() {
            if (visualRoot == null) {
                return;
            }

            // Offset by index so the badges breathe independently rather than in lockstep.
            float phaseOffset = _partIndex >= 0 ? _partIndex * 0.45f : 0f;
            float pulse = 1f + pulseAmplitude * Mathf.Sin(Time.time * pulseSpeed + phaseOffset);

            float scale = Mathf.Lerp(1f, hoverScale, _hoverProgress);
            scale *= Mathf.Lerp(1f, selectedScale, _selectProgress);
            scale *= pulse;
            scale *= EvaluatePopIn(_popInProgress);
            scale *= Mathf.Lerp(1f, pressScale, EvaluatePress(_pressProgress));

            visualRoot.localScale = _initialVisualScale * scale;

            // Lift towards the viewer on hover. Local -Z is towards the viewer on this rig, and the
            // badge is billboarded, so its own back axis always points at the camera.
            float lift = hoverLift * Mathf.Max(_hoverProgress, _selectProgress);
            visualRoot.localPosition = _initialVisualLocalPosition;
            if (lift > 0f) {
                visualRoot.position -= visualRoot.forward * lift;
            }
        }

        /// <summary>
        /// Eases the pop-in past full size and back, so a badge arrives with a little weight.
        /// </summary>
        private float EvaluatePopIn(float t) {
            if (t >= 1f) {
                return 1f;
            }

            if (t <= 0f) {
                return 0f;
            }

            float eased = Mathf.SmoothStep(0f, 1f, t);
            float overshoot = Mathf.Sin(t * Mathf.PI) * (popInOvershoot - 1f);
            return eased + overshoot;
        }

        /// <summary>
        /// Turns the press timer into a punch that is deepest at the moment of contact.
        /// </summary>
        private float EvaluatePress(float t) {
            return t <= 0f ? 0f : Mathf.Sin(t * Mathf.PI * 0.5f);
        }

        private void UpdateHalo() {
            if (selectionHalo == null) {
                return;
            }

            bool visible = _selectProgress > 0.01f;
            if (selectionHalo.gameObject.activeSelf != visible) {
                selectionHalo.gameObject.SetActive(visible);
            }

            if (!visible) {
                return;
            }

            // A ring that swells and fades on a loop, reading as a slow radar ping on the selection.
            float ping = Mathf.Repeat(Time.time * 0.8f, 1f);
            selectionHalo.localScale = Vector3.one * Mathf.Lerp(1f, 2.1f, ping) * _selectProgress;

            if (haloRenderer != null && _propertyBlock != null) {
                Color halo = selectedBadgeColor;
                halo.a = (1f - ping) * 0.55f * _selectProgress;
                haloRenderer.GetPropertyBlock(_propertyBlock);
                for (int i = 0; i < ColorPropertyIds.Length; i++) {
                    _propertyBlock.SetColor(ColorPropertyIds[i], halo);
                }
                haloRenderer.SetPropertyBlock(_propertyBlock);
            }
        }

        private void RefreshVisualsImmediate() {
            RefreshColors();
        }

        private void RefreshColors() {
            Color badgeColor = idleBadgeColor;
            Color textColor = idleTextColor;

            if (_hoverProgress > 0f) {
                badgeColor = Color.Lerp(idleBadgeColor, hoverBadgeColor, _hoverProgress);
                textColor = Color.Lerp(idleTextColor, hoverTextColor, _hoverProgress);
            }

            if (_selectProgress > 0f) {
                badgeColor = Color.Lerp(badgeColor, selectedBadgeColor, _selectProgress);
                textColor = Color.Lerp(textColor, selectedTextColor, _selectProgress);
            }

            // Fade in with the pop-in so a badge does not flash at full opacity before it has grown.
            float popAlpha = Mathf.Clamp01(_popInProgress * 1.4f);
            badgeColor.a *= popAlpha;
            textColor.a *= popAlpha;

            if (badgeRenderer != null && _propertyBlock != null) {
                badgeRenderer.GetPropertyBlock(_propertyBlock);
                for (int i = 0; i < ColorPropertyIds.Length; i++) {
                    _propertyBlock.SetColor(ColorPropertyIds[i], badgeColor);
                }
                badgeRenderer.SetPropertyBlock(_propertyBlock);
            }

            if (numberText != null) {
                numberText.color = textColor;
            }
        }
    }
}
