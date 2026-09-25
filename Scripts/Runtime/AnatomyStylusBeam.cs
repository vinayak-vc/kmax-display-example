using KmaxXR;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Visualises the stylus as a tapered beam ending in a pointed tip that lands on whatever the
    /// ray hits - the eye's mesh colliders, a badge, or the UI.
    ///
    /// This replaces the SDK's <c>StylusRay</c> rather than extending it, because the exhibit needs
    /// three things that one does not do: the tip is oriented to the surface normal so it reads as
    /// touching the anatomy rather than floating inside it, the beam recolours on hit so the viewer
    /// knows when a press will land, and hits drive haptics.
    ///
    /// <see cref="KmaxStylus"/> finds this through <c>GetComponent&lt;IPointerVisualize&gt;()</c> on
    /// its serialised <c>stylus</c> transform, so this component must sit on that same object.
    /// </summary>
    public class AnatomyStylusBeam : MonoBehaviour, IPointerVisualize {
        private static readonly int[] ColorPropertyIds = new int[] {
            Shader.PropertyToID("_BaseColor"),
            Shader.PropertyToID("_Color")
        };

        [Header("Beam")]
        [SerializeField, Tooltip("Line drawn from the pen to the hit point. Drawn in local space.")]
        private LineRenderer beam;
        [SerializeField, Tooltip("Width of the beam at the pen, in metres.")]
        private float beamStartWidth = 0.0016f;
        [SerializeField, Tooltip("Width of the beam at the far end, in metres. Tapering reads as depth.")]
        private float beamEndWidth = 0.0005f;

        [Header("Tip")]
        [SerializeField, Tooltip("Pointed cone placed at the hit point and aimed along the surface normal.")]
        private Transform tip;
        [SerializeField, Tooltip("Renderer of the tip, recoloured to match the beam state.")]
        private Renderer tipRenderer;
        [SerializeField, Tooltip("Size of the tip in metres while the ray is hitting nothing.")]
        private float tipSize = 0.006f;
        [SerializeField, Tooltip("Multiplier applied to the tip while it rests on something hittable.")]
        private float tipHitScale = 1.35f;
        [SerializeField, Tooltip("Align the tip to the surface it lands on. Off keeps it aimed along the beam.")]
        private bool alignTipToSurface = true;

        [Header("Colours")]
        [SerializeField, Tooltip("Beam colour while the ray is hitting nothing.")]
        private Color idleColor = new Color(0.45f, 0.72f, 0.95f, 0.30f);
        [SerializeField, Tooltip("Beam colour while the ray rests on something interactive.")]
        private Color hitColor = new Color(0.55f, 0.90f, 1f, 0.85f);
        [SerializeField, Tooltip("Beam colour while a stylus button is held down.")]
        private Color pressColor = new Color(1f, 0.82f, 0.35f, 1f);
        [SerializeField, Tooltip("Seconds the colour takes to cross-fade between states.")]
        private float colorBlendTime = 0.09f;

        [Header("Haptics")]
        [SerializeField, Tooltip("Pulse the pen when the ray moves onto a new object.")]
        private bool vibrateOnHitEnter = true;
        [SerializeField, Range(0, 100), Tooltip("Vibration strength, 0 to 100.")]
        private int hitVibrationStrength = 22;
        [SerializeField, Tooltip("Vibration duration in seconds.")]
        private float hitVibrationDuration = 0.02f;

        private KmaxStylus _stylus;
        private MaterialPropertyBlock _propertyBlock;
        private GameObject _lastHitObject;
        private Color _currentColor;
        private float _tipScaleProgress;
        private float _originalRayLength = 1f;
        private float _viewScale = 1f;

        /// <summary>
        /// True while the ray is resting on a collider or a UI element.
        /// </summary>
        public bool IsHitting {
            get { return _stylus != null && _stylus.CurrentHitObject != null; }
        }

        public void InitVisualization(KmaxPointer pointer) {
            _stylus = pointer as KmaxStylus;
            _propertyBlock = new MaterialPropertyBlock();

            if (beam == null) {
                beam = GetComponentInChildren<LineRenderer>(true);
            }

            if (tip != null && tipRenderer == null) {
                tipRenderer = tip.GetComponentInChildren<Renderer>(true);
            }

            if (beam == null) {
                Debug.LogError($"{nameof(AnatomyStylusBeam)} on '{name}' has no {nameof(beam)} assigned; the stylus will have no visible ray.", this);
            } else {
                // Local space keeps the beam rigidly attached to the pen; only the far point moves.
                beam.useWorldSpace = false;
                beam.positionCount = 2;
                beam.startWidth = beamStartWidth;
                beam.endWidth = beamEndWidth;
                beam.SetPosition(0, Vector3.zero);
            }

            _originalRayLength = _stylus != null ? _stylus.RayLength : 1f;
            _currentColor = idleColor;
            ApplyColor(_currentColor);
            ApplyTipScale(1f);
        }

        public void UpdateVisualization(KmaxPointer pointer) {
            _stylus = pointer as KmaxStylus;
            if (_stylus == null) {
                return;
            }

            FollowViewScale();

            GameObject hit = _stylus.CurrentHitObject;
            bool isHitting = hit != null;
            bool isPressed = _stylus.AnyButtonPressed;

            UpdateBeam();
            UpdateTip(isHitting);
            UpdateColor(isHitting, isPressed);
            UpdateHaptics(hit);
        }

        /// <summary>
        /// The virtual screen can be rescaled at runtime. The beam and tip follow it so they keep
        /// the same apparent thickness on the display rather than growing with the world.
        /// </summary>
        private void FollowViewScale() {
            float scale = XRRig.ViewScale;
            if (Mathf.Approximately(scale, _viewScale)) {
                return;
            }

            _viewScale = scale;
            _stylus.RayLength = _originalRayLength * scale;

            if (beam != null) {
                beam.startWidth = beamStartWidth * scale;
                beam.endWidth = beamEndWidth * scale;
            }
        }

        private void UpdateBeam() {
            if (beam == null) {
                return;
            }

            // PointerPosition is the hit point when something was hit and the ray's far end when
            // nothing was, so the beam always terminates where the tip sits.
            beam.SetPosition(1, beam.transform.InverseTransformPoint(_stylus.PointerPosition));
        }

        private void UpdateTip(bool isHitting) {
            if (tip == null) {
                return;
            }

            tip.position = _stylus.PointerPosition;

            // The cone is authored pointing along +Y, so its up axis is aimed back out of the
            // surface. The point then rests on the geometry instead of burying itself in it.
            Vector3 outward = -transform.forward;
            if (alignTipToSurface && isHitting) {
                Vector3 normal = _stylus.pointerState.result.worldNormal;
                if (normal.sqrMagnitude > Mathf.Epsilon) {
                    outward = normal;
                }
            }

            tip.rotation = Quaternion.FromToRotation(Vector3.up, outward);

            _tipScaleProgress = Mathf.MoveTowards(_tipScaleProgress, isHitting ? 1f : 0f, Time.deltaTime * 10f);
            ApplyTipScale(Mathf.Lerp(1f, tipHitScale, _tipScaleProgress));
        }

        private void ApplyTipScale(float multiplier) {
            if (tip == null) {
                return;
            }

            tip.localScale = Vector3.one * (tipSize * _viewScale * multiplier);
        }

        private void UpdateColor(bool isHitting, bool isPressed) {
            Color target = idleColor;
            if (isPressed) {
                target = pressColor;
            } else if (isHitting) {
                target = hitColor;
            }

            float step = colorBlendTime > 0f ? Time.deltaTime / colorBlendTime : 1f;
            _currentColor = Color.Lerp(_currentColor, target, Mathf.Clamp01(step));
            ApplyColor(_currentColor);
        }

        private void ApplyColor(Color color) {
            if (beam != null) {
                // The far end fades out so the beam reads as reaching rather than as a solid rod.
                Color far = color;
                far.a *= 0.25f;
                beam.startColor = color;
                beam.endColor = far;
            }

            if (tipRenderer != null && _propertyBlock != null) {
                Color solid = color;
                solid.a = 1f;
                tipRenderer.GetPropertyBlock(_propertyBlock);
                for (int i = 0; i < ColorPropertyIds.Length; i++) {
                    _propertyBlock.SetColor(ColorPropertyIds[i], solid);
                }
                tipRenderer.SetPropertyBlock(_propertyBlock);
            }
        }

        private void UpdateHaptics(GameObject hit) {
            if (hit == _lastHitObject) {
                return;
            }

            _lastHitObject = hit;

            if (vibrateOnHitEnter && hit != null) {
                _stylus.VibrationOnce(hitVibrationDuration, hitVibrationStrength);
            }
        }
    }
}
