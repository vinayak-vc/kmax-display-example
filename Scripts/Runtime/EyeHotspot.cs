using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// A subtle, numbered interaction badge pinned to one part of the eye.
    /// Faces the active camera, displays the part index (1 to 18), gently breathes to make
    /// the display feel alive, and highlights smoothly when hovered or selected.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class EyeHotspot : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler {
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

        private MaterialPropertyBlock _propertyBlock;
        private int _partIndex = -1;
        private bool _isSelected;
        private bool _isHovered;
        private float _hoverProgress;
        private Camera _targetCamera;
        private Vector3 _initialVisualScale = Vector3.one;

        public event Action<EyeHotspot> Clicked;

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

        private void Update() {
            UpdateBillboarding();
            UpdateAnimation();
        }

        public void Initialize(int index, Camera camera = null) {
            _partIndex = index;
            if (camera != null) {
                _targetCamera = camera;
            }

            if (numberText != null) {
                numberText.text = (index + 1).ToString();
            }

            UpdateBillboarding();
            RefreshVisualsImmediate();
        }

        public void SetSelected(bool selected) {
            _isSelected = selected;
            RefreshVisualsImmediate();
        }

        public void OnPointerClick(PointerEventData eventData) {
            if (Clicked != null) {
                Clicked(this);
            }
        }

        public void OnPointerEnter(PointerEventData eventData) {
            _isHovered = true;
        }

        public void OnPointerExit(PointerEventData eventData) {
            _isHovered = false;
        }

        private void UpdateBillboarding() {
            if (_targetCamera == null || !_targetCamera.isActiveAndEnabled) {
                _targetCamera = ResolveCamera();
            }

            if (visualRoot != null) {
                if (_targetCamera != null) {
                    visualRoot.rotation = _targetCamera.transform.rotation;
                } else {
                    visualRoot.rotation = Quaternion.identity;
                }
            }
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

            // Gentle organic pulse offset by partIndex so markers breathe organically
            float phaseOffset = _partIndex >= 0 ? _partIndex * 0.45f : 0f;
            float pulse = 1f + pulseAmplitude * Mathf.Sin(Time.time * pulseSpeed + phaseOffset);
            float scaleMultiplier = Mathf.Lerp(1f, hoverScale, _hoverProgress) * pulse;

            visualRoot.localScale = _initialVisualScale * scaleMultiplier;

            RefreshColors();
        }

        private void RefreshVisualsImmediate() {
            RefreshColors();
        }

        private void RefreshColors() {
            Color badgeColor = idleBadgeColor;
            Color textColor = idleTextColor;

            if (_isSelected) {
                badgeColor = selectedBadgeColor;
                textColor = selectedTextColor;
            } else if (_hoverProgress > 0f) {
                badgeColor = Color.Lerp(idleBadgeColor, hoverBadgeColor, _hoverProgress);
                textColor = Color.Lerp(idleTextColor, hoverTextColor, _hoverProgress);
            }

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