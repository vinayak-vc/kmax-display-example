using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Owns the scene's particle effects.
    ///
    /// Two jobs. The ambient layers fill the volume around the eye with slow motes at several
    /// depths - on a fish-tank stereo display those are what sell the box as having real depth,
    /// because a single plane of particles reads as a flat backdrop however good the stereo is.
    /// The interaction effects then fire a burst, an expanding ring and a lift of sparks wherever
    /// the viewer just selected something.
    /// </summary>
    public class AnatomyParticleDirector : MonoBehaviour {
        [Header("Ambience")]
        [SerializeField, Tooltip("Slow drifting motes surrounding the eye in 3D depth. Runs continuously.")]
        private ParticleSystem ambientField;
        [SerializeField, Tooltip("Extra mote layers sitting nearer to and further from the viewer. " +
            "Parallax between the layers is what makes the volume read as deep rather than flat.")]
        private ParticleSystem[] depthLayers = new ParticleSystem[0];

        [Header("Interaction")]
        [SerializeField, Tooltip("One-shot burst played at the point that was just selected.")]
        private ParticleSystem focusBurst;
        [SerializeField, Tooltip("Expanding ring that marks the selection point. Billboarded to the viewer.")]
        private ParticleSystem popupRing;
        [SerializeField, Tooltip("Sparks that lift away from the selected structure.")]
        private ParticleSystem riseSparks;

        [Header("Counts")]
        [SerializeField, Range(10, 100), Tooltip("Particles emitted in each interaction burst.")]
        private int burstParticleCount = 45;
        [SerializeField, Range(1, 40), Tooltip("Particles emitted in each expanding ring.")]
        private int ringParticleCount = 18;
        [SerializeField, Range(0, 60), Tooltip("Sparks lifted on each selection.")]
        private int sparkParticleCount = 24;

        [Header("Response")]
        [SerializeField, Tooltip("Briefly lift the ambient layers when something is selected, so the " +
            "whole volume acknowledges the interaction rather than just the point that was touched.")]
        private bool swellAmbientOnSelect = true;
        [SerializeField, Range(1f, 6f), Tooltip("Emission multiplier at the peak of the swell.")]
        private float ambientSwellMultiplier = 2.4f;
        [SerializeField, Tooltip("Seconds the ambient swell takes to fall back to rest.")]
        private float ambientSwellDuration = 1.1f;

        private float[] _restEmissionRates;
        private float _swellProgress;
        private Camera _targetCamera;

        private void Start() {
            CacheRestEmission();

            if (ambientField != null && !ambientField.isPlaying) {
                ambientField.Play(true);
            }

            for (int i = 0; i < depthLayers.Length; i++) {
                if (depthLayers[i] != null && !depthLayers[i].isPlaying) {
                    depthLayers[i].Play(true);
                }
            }
        }

        private void Update() {
            if (_swellProgress <= 0f) {
                return;
            }

            _swellProgress = Mathf.MoveTowards(_swellProgress, 0f,
                ambientSwellDuration > 0f ? Time.deltaTime / ambientSwellDuration : 1f);

            ApplyEmissionMultiplier(Mathf.Lerp(1f, ambientSwellMultiplier, Mathf.SmoothStep(0f, 1f, _swellProgress)));
        }

        /// <summary>
        /// Plays the full selection effect - burst, ring and sparks - at a world position.
        /// </summary>
        public void PlayInteractionBurst(Vector3 worldPosition) {
            if (focusBurst != null) {
                focusBurst.transform.position = worldPosition;
                focusBurst.Emit(burstParticleCount);
            }

            if (popupRing != null) {
                popupRing.transform.position = worldPosition;
                FaceViewer(popupRing.transform);
                popupRing.Emit(ringParticleCount);
            }

            if (riseSparks != null) {
                riseSparks.transform.position = worldPosition;
                riseSparks.Emit(sparkParticleCount);
            }

            if (swellAmbientOnSelect) {
                _swellProgress = 1f;
            }
        }

        /// <summary>
        /// Plays the full selection effect at a world position.
        /// </summary>
        public void PlayFocusBurst(Vector3 worldPosition) {
            PlayInteractionBurst(worldPosition);
        }

        /// <summary>
        /// Plays only the expanding ring, without the burst. Used when the view flies to a part
        /// that was chosen from the Next and Back buttons rather than touched directly, where a
        /// full burst at a point the viewer did not press reads as a glitch.
        /// </summary>
        public void PlayPopupRing(Vector3 worldPosition) {
            if (popupRing == null) {
                return;
            }

            popupRing.transform.position = worldPosition;
            FaceViewer(popupRing.transform);
            popupRing.Emit(ringParticleCount);
        }

        /// <summary>
        /// Enables or disables every ambient layer.
        /// </summary>
        public void SetAmbientEnabled(bool enabled) {
            SetPlaying(ambientField, enabled);

            for (int i = 0; i < depthLayers.Length; i++) {
                SetPlaying(depthLayers[i], enabled);
            }
        }

        private static void SetPlaying(ParticleSystem system, bool enabled) {
            if (system == null) {
                return;
            }

            if (enabled) {
                if (!system.isPlaying) {
                    system.Play(true);
                }

                return;
            }

            system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>
        /// The ring is a flat shape, so it has to be turned to the viewer or it vanishes edge-on.
        /// </summary>
        private void FaceViewer(Transform target) {
            if (_targetCamera == null || !_targetCamera.isActiveAndEnabled) {
                _targetCamera = ResolveCamera();
            }

            if (_targetCamera != null) {
                target.rotation = _targetCamera.transform.rotation;
            }
        }

        private static Camera ResolveCamera() {
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

        private void CacheRestEmission() {
            _restEmissionRates = new float[depthLayers.Length + 1];
            _restEmissionRates[0] = ReadEmissionRate(ambientField);

            for (int i = 0; i < depthLayers.Length; i++) {
                _restEmissionRates[i + 1] = ReadEmissionRate(depthLayers[i]);
            }
        }

        private static float ReadEmissionRate(ParticleSystem system) {
            if (system == null) {
                return 0f;
            }

            return system.emission.rateOverTime.constant;
        }

        private void ApplyEmissionMultiplier(float multiplier) {
            if (_restEmissionRates == null) {
                return;
            }

            SetEmissionRate(ambientField, _restEmissionRates[0] * multiplier);

            for (int i = 0; i < depthLayers.Length; i++) {
                SetEmissionRate(depthLayers[i], _restEmissionRates[i + 1] * multiplier);
            }
        }

        private static void SetEmissionRate(ParticleSystem system, float rate) {
            if (system == null) {
                return;
            }

            ParticleSystem.EmissionModule emission = system.emission;
            ParticleSystem.MinMaxCurve curve = emission.rateOverTime;
            curve.constant = rate;
            emission.rateOverTime = curve;
        }
    }
}
