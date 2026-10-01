using KmaxXR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Visualises the stylus as a tapered beam ending in a pointed tip that lands on whatever the
    /// ray hits - the eye's mesh colliders, a badge, or the UI.
    ///
    /// This replaces the SDK's <c>StylusRay</c> rather than extending it, because the exhibit needs
    /// three things that one does not do: the tip is oriented to the surface normal so it reads as
    /// touching the anatomy rather than floating inside it, the beam recolours on hit and per
    /// stylus button press so the viewer knows which action/selection will land, and hits drive haptics.
    ///
    /// <see cref="KmaxStylus"/> finds this through <c>GetComponent&lt;IPointerVisualize&gt;()</c> on
    /// its serialised <c>stylus</c> transform, so this component must sit on that same object.
    /// </summary>
    public class AnatomyStylusBeam : MonoBehaviour, IPointerVisualize {
        private static readonly int[] ColorPropertyIds = new int[] {
            Shader.PropertyToID("_BaseColor"),
            Shader.PropertyToID("_Color")
        };

        private const int BeamSegmentCount = 24;

        [Header("Beam")]
        [SerializeField, Tooltip("Line drawn from the pen to the hit point. Drawn in local space.")]
        private LineRenderer beam;
        [SerializeField, Tooltip("Width of the beam at the pen, in metres.")]
        private float beamStartWidth = 0.0018f;
        [SerializeField, Tooltip("Width of the beam at the far end, in metres. Tapering reads as depth.")]
        private float beamEndWidth = 0.0006f;
        [SerializeField, Range(0f, 0.35f), Tooltip("How strongly the beam curves into a graceful arc toward the hit surface normal.")]
        private float beamCurveStrength = 0.18f;

        [Header("Tip")]
        [SerializeField, Tooltip("Pointed cone placed at the hit point and aimed along the surface normal.")]
        private Transform tip;
        [SerializeField, Tooltip("Renderer of the tip, recoloured to match the beam state.")]
        private Renderer tipRenderer;
        [SerializeField, Tooltip("Size of the tip in metres while the ray is hitting nothing.")]
        private float tipSize = 0.0065f;
        [SerializeField, Tooltip("Multiplier applied to the tip while it rests on something hittable.")]
        private float tipHitScale = 1.42f;
        [SerializeField, Tooltip("Align the tip to the surface it lands on. Off keeps it aimed along the beam.")]
        private bool alignTipToSurface = true;

        [Header("Colours")]
        [SerializeField, Tooltip("Beam colour while the ray is hitting nothing.")]
        private Color idleColor = new Color(0.32f, 0.72f, 1.00f, 0.48f);
        [SerializeField, Tooltip("Beam colour while the ray rests on a surface or selectable target.")]
        private Color hitColor = new Color(0.20f, 0.94f, 1.00f, 0.92f);
        [SerializeField, Tooltip("1st press colour: Stylus Button 0 (Primary / Orbit) pressed when not on a selectable item.")]
        private Color button0PressColor = new Color(1.00f, 0.78f, 0.18f, 1.00f);
        [SerializeField, Tooltip("2nd press colour: Stylus Button 1 (Secondary / Reset) pressed.")]
        private Color button1PressColor = new Color(0.88f, 0.32f, 1.00f, 1.00f);
        [SerializeField, Tooltip("3rd press colour: Stylus Button 2 (Centre / Dolly) pressed.")]
        private Color button2PressColor = new Color(1.00f, 0.36f, 0.24f, 1.00f);
        [SerializeField, Tooltip("4th press colour: Stylus Select Button (Button 0) pressed while beaming on a selectable UI button or 3D item.")]
        private Color selectElementPressColor = new Color(0.14f, 0.98f, 0.48f, 1.00f);
        [SerializeField, Tooltip("Seconds the colour takes to cross-fade between idle/hover states.")]
        private float colorBlendTime = 0.08f;
        [SerializeField, Tooltip("Seconds the colour takes to snap to a pressed button state.")]
        private float pressColorBlendTime = 0.02f;

        [Header("Haptics")]
        [SerializeField, Tooltip("Pulse the pen when the ray moves onto a new object.")]
        private bool vibrateOnHitEnter = true;
        [SerializeField, Range(0, 100), Tooltip("Vibration strength, 0 to 100. Kept low: this fires " +
            "as feedback, not as an alert, and the pen is held against the fingertips.")]
        private int hitVibrationStrength = 8;
        [SerializeField, Tooltip("Vibration duration in seconds.")]
        private float hitVibrationDuration = 0.015f;
        [SerializeField, Tooltip("Minimum seconds between pulses. Every structure carries its own " +
            "collider, so sweeping the beam across the eye crosses a boundary every few frames - " +
            "without a floor here the pen buzzes continuously rather than ticking on arrival.")]
        private float minVibrationInterval = 0.25f;

        private KmaxStylus _stylus;
        private MaterialPropertyBlock _propertyBlock;
        private GameObject _lastHitObject;
        private float _lastVibrationTime = -1f;
        private Color _currentColor;
        private Color _currentAccentColor;
        private float _tipScaleProgress;
        private float _originalRayLength = 1f;
        private float _viewScale = 1f;
        private Vector3 _smoothedLocalEnd = new Vector3(0f, 0f, 0.4f);
        private Vector3 _localEndVelocity;
        private readonly Gradient _beamGradient = new Gradient();
        private readonly GradientColorKey[] _colorKeys = new GradientColorKey[3];
        private readonly GradientAlphaKey[] _alphaKeys = new GradientAlphaKey[3];
        private readonly AnimationCurve _widthCurve = new AnimationCurve();

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
                // Local space keeps the beam anchored to the pen while drawing a smooth 24-segment Bezier arc.
                beam.useWorldSpace = false;
                beam.positionCount = BeamSegmentCount;
                beam.numCornerVertices = 4;
                beam.numCapVertices = 4;
                beam.startWidth = beamStartWidth;
                beam.endWidth = beamEndWidth;
                for (int i = 0; i < BeamSegmentCount; i++) {
                    float t = i / (float)(BeamSegmentCount - 1);
                    beam.SetPosition(i, new Vector3(0f, 0f, t * 0.4f));
                }
            }

            _originalRayLength = _stylus != null ? _stylus.RayLength : 1f;
            _currentColor = idleColor;
            _currentAccentColor = hitColor;
            ApplyColor(_currentColor, _currentAccentColor, false);
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
            bool isSelectable = IsSelectableTarget(hit);

            bool button0Pressed = _stylus.GetButton(0);
            bool button1Pressed = _stylus.GetButton(1);
            bool button2Pressed = _stylus.GetButton(2);
            bool anyPressed = button0Pressed || button1Pressed || button2Pressed;

            UpdateBeam(isHitting, isSelectable, anyPressed);
            UpdateTip(isHitting, isSelectable, anyPressed);
            UpdateColor(isHitting, isSelectable, button0Pressed, button1Pressed, button2Pressed);
            UpdateHaptics(isSelectable ? hit : null);
        }

        /// <summary>
        /// Returns true when <paramref name="hit"/> belongs to an interactive UI control (such as a
        /// Button or Swatch) or a selectable 3D exhibit element (part, hotspot badge, or scale handle).
        /// </summary>
        public static bool IsSelectableTarget(GameObject hit) {
            if (hit == null) {
                return false;
            }

            Selectable uiSelectable = hit.GetComponentInParent<Selectable>();
            if (uiSelectable != null && uiSelectable.IsInteractable()) {
                return true;
            }

            if (hit.GetComponentInParent<EyeHotspot>() != null ||
                hit.GetComponentInParent<EyePartPicker>() != null ||
                hit.GetComponentInParent<EyeScaleHandle>() != null) {
                return true;
            }

            if (hit.GetComponentInParent<IPointerClickHandler>() != null ||
                hit.GetComponentInParent<IPointerDownHandler>() != null ||
                hit.GetComponentInParent<IDragHandler>() != null) {
                return true;
            }

            return false;
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

        private void UpdateBeam(bool isHitting, bool isSelectable, bool anyPressed) {
            if (beam == null) {
                return;
            }

            if (beam.positionCount != BeamSegmentCount) {
                beam.positionCount = BeamSegmentCount;
            }

            Vector3 targetLocalEnd = beam.transform.InverseTransformPoint(_stylus.PointerPosition);
            float distance = targetLocalEnd.magnitude;

            // Smoothly spring the far control point so quick wrist flicks create a fluid whip-curve
            // while the tip itself still lands accurately on the target.
            _smoothedLocalEnd = Vector3.SmoothDamp(
                _smoothedLocalEnd,
                targetLocalEnd,
                ref _localEndVelocity,
                isHitting ? 0.022f : 0.040f,
                Mathf.Infinity,
                Time.unscaledDeltaTime);

            Vector3 p0 = Vector3.zero;
            Vector3 p3 = targetLocalEnd;

            // Control point P1 leaves the pen straight along local +Z (forward).
            float forwardTangent = distance * 0.42f;
            Vector3 p1 = new Vector3(0f, 0f, forwardTangent);

            // Control point P2 bends gracefully toward the hit surface normal and trailing lag,
            // eliminating the stiff straight-line rod look.
            Vector3 localNormal = Vector3.back;
            if (isHitting) {
                Vector3 worldNormal = _stylus.pointerState.result.worldNormal;
                if (worldNormal.sqrMagnitude > Mathf.Epsilon) {
                    localNormal = beam.transform.InverseTransformDirection(worldNormal).normalized;
                }
            }

            Vector3 lagOffset = (_smoothedLocalEnd - targetLocalEnd) * 1.65f;
            float normalBend = isHitting ? distance * beamCurveStrength : distance * 0.06f;
            Vector3 p2 = Vector3.Lerp(p0, p3, 0.68f) + localNormal * normalBend + lagOffset;

            // Subtle living energy ripple along the arc when hovering a selectable target or pressing a button.
            float waveAmplitude = (isSelectable || anyPressed) ? distance * 0.008f : distance * 0.0025f;
            float waveTime = Time.unscaledTime * (anyPressed ? 12f : 5.5f);

            for (int i = 0; i < BeamSegmentCount; i++) {
                float t = i / (float)(BeamSegmentCount - 1);
                Vector3 pos = EvaluateCubicBezier(p0, p1, p2, p3, t);

                // Taper wave to zero at both endpoints so P0 and P3 stay pinned.
                float envelope = Mathf.Sin(t * Mathf.PI);
                float wave = Mathf.Sin(t * Mathf.PI * 2.5f - waveTime) * waveAmplitude * envelope;
                pos.y += wave;
                pos.x += Mathf.Cos(t * Mathf.PI * 2.0f - waveTime * 0.8f) * waveAmplitude * 0.55f * envelope;

                beam.SetPosition(i, pos);
            }

            // Dynamic width pulse travelling down the beam
            float pulseWidth = anyPressed ? 1.28f : (isSelectable ? 1.14f : 1f);
            beam.startWidth = beamStartWidth * _viewScale * pulseWidth;
            beam.endWidth = beamEndWidth * _viewScale * (isSelectable ? 1.35f : 1f);
        }

        private static Vector3 EvaluateCubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t) {
            float u = 1f - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;
            return uuu * p0 + 3f * uu * t * p1 + 3f * u * tt * p2 + ttt * p3;
        }

        private void UpdateTip(bool isHitting, bool isSelectable, bool anyPressed) {
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

            float targetProgress = isSelectable ? 1.18f : (isHitting ? 1f : 0f);
            if (anyPressed) {
                targetProgress = 1.32f;
            }

            _tipScaleProgress = Mathf.MoveTowards(_tipScaleProgress, targetProgress, Time.unscaledDeltaTime * 11f);
            float pulse = isSelectable ? 1f + 0.08f * Mathf.Sin(Time.unscaledTime * 8f) : 1f;
            ApplyTipScale(Mathf.Lerp(1f, tipHitScale, _tipScaleProgress) * pulse);
        }

        private void ApplyTipScale(float multiplier) {
            if (tip == null) {
                return;
            }

            tip.localScale = Vector3.one * (tipSize * _viewScale * multiplier);
        }

        private void UpdateColor(
            bool isHitting,
            bool isSelectable,
            bool button0Pressed,
            bool button1Pressed,
            bool button2Pressed) {
            Color targetPrimary = idleColor;
            Color targetAccent = new Color(0.55f, 0.88f, 1.00f, 0.70f);
            bool anyPressed = button0Pressed || button1Pressed || button2Pressed;

            if (button0Pressed && isSelectable) {
                // 4th colour: Beaming on a selectable UI button or 3D element while pressing Select (Button 0).
                targetPrimary = selectElementPressColor;
                targetAccent = new Color(0.55f, 1.00f, 0.82f, 1.00f);
            } else if (button0Pressed) {
                // 1st button colour: Primary / Orbit button (Button 0).
                targetPrimary = button0PressColor;
                targetAccent = new Color(1.00f, 0.92f, 0.48f, 1.00f);
            } else if (button1Pressed) {
                // 2nd button colour: Secondary / Reset button (Button 1).
                targetPrimary = button1PressColor;
                targetAccent = new Color(0.48f, 0.82f, 1.00f, 1.00f);
            } else if (button2Pressed) {
                // 3rd button colour: Centre / Dolly button (Button 2).
                targetPrimary = button2PressColor;
                targetAccent = new Color(1.00f, 0.72f, 0.30f, 1.00f);
            } else if (isSelectable) {
                // Vibrant cyan-to-emerald hint when hovering a selectable target so the viewer knows it's clickable.
                targetPrimary = hitColor;
                targetAccent = new Color(0.28f, 1.00f, 0.76f, 0.98f);
            } else if (isHitting) {
                targetPrimary = hitColor;
                targetAccent = new Color(0.65f, 0.85f, 1.00f, 0.90f);
            }

            float blendDuration = anyPressed ? pressColorBlendTime : colorBlendTime;
            float step = blendDuration > 0f ? Time.unscaledDeltaTime / blendDuration : 1f;
            _currentColor = Color.Lerp(_currentColor, targetPrimary, Mathf.Clamp01(step));
            _currentAccentColor = Color.Lerp(_currentAccentColor, targetAccent, Mathf.Clamp01(step));
            ApplyColor(_currentColor, _currentAccentColor, isSelectable || anyPressed);
        }

        private void ApplyColor(Color primary, Color accent, bool activeTarget) {
            if (beam != null) {
                float shimmer = 0.5f + 0.3f * Mathf.Sin(Time.unscaledTime * 7f);
                _colorKeys[0] = new GradientColorKey(primary, 0f);
                _colorKeys[1] = new GradientColorKey(Color.Lerp(primary, accent, 0.55f), shimmer);
                _colorKeys[2] = new GradientColorKey(accent, 1f);

                _alphaKeys[0] = new GradientAlphaKey(primary.a, 0f);
                _alphaKeys[1] = new GradientAlphaKey(Mathf.Max(primary.a, accent.a) * 0.92f, 0.65f);
                _alphaKeys[2] = new GradientAlphaKey(activeTarget ? Mathf.Min(1f, primary.a * 0.95f) : primary.a * 0.32f, 1f);

                _beamGradient.SetKeys(_colorKeys, _alphaKeys);
                beam.colorGradient = _beamGradient;
            }

            if (tipRenderer != null && _propertyBlock != null) {
                Color solid = Color.Lerp(primary, accent, 0.35f);
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

            if (!vibrateOnHitEnter || hit == null) {
                return;
            }

            if (Time.unscaledTime - _lastVibrationTime < minVibrationInterval) {
                return;
            }

            _lastVibrationTime = Time.unscaledTime;
            _stylus.VibrationOnce(hitVibrationDuration, hitVibrationStrength);
        }
    }
}
