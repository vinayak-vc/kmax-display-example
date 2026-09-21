using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Owns the scene's particle effects. Drives ambient stereoscopic floating motes to make the
    /// display feel alive and triggers dynamic interaction bursts whenever an interaction point is selected.
    /// </summary>
    public class AnatomyParticleDirector : MonoBehaviour {
        [SerializeField, Tooltip("Slow drifting motes surrounding the eye in 3D depth. Runs continuously.")]
        private ParticleSystem ambientField;
        [SerializeField, Tooltip("One-shot burst played at the interaction point that was just selected.")]
        private ParticleSystem focusBurst;
        [SerializeField, Tooltip("Number of particles emitted in each interaction burst.")]
        [Range(10, 100)] private int burstParticleCount = 45;

        private void Start() {
            if (ambientField != null && !ambientField.isPlaying) {
                ambientField.Play(true);
            }
        }

        /// <summary>
        /// Plays a burst of radiant interaction particles at the specified world position.
        /// </summary>
        public void PlayFocusBurst(Vector3 worldPosition) {
            PlayInteractionBurst(worldPosition);
        }

        /// <summary>
        /// Plays a burst of radiant interaction particles at the specified world position.
        /// </summary>
        public void PlayInteractionBurst(Vector3 worldPosition) {
            if (focusBurst == null) {
                return;
            }

            focusBurst.transform.position = worldPosition;
            focusBurst.Emit(burstParticleCount);
        }

        /// <summary>
        /// Enables or disables the ambient floating depth motes.
        /// </summary>
        public void SetAmbientEnabled(bool enabled) {
            if (ambientField == null) {
                return;
            }

            if (enabled) {
                if (!ambientField.isPlaying) {
                    ambientField.Play(true);
                }
                return;
            }

            ambientField.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}