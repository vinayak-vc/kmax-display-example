using System;
using System.Collections;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Starts and stops the car: the cabin wakes, the starter turns, the engine catches, the lamps
    /// come up and the idle settles in.
    ///
    /// Staged over a couple of seconds rather than switched, because the order is the whole point.
    /// A car does not light up all at once - the interior and the instruments come alive first,
    /// while the starter is still turning, and the exterior lamps follow once it has caught. Doing
    /// it in one step reads as a light switch rather than as a car starting.
    /// </summary>
    public class VehicleIgnition : MonoBehaviour {
        [Header("Systems")]
        [SerializeField] private VehicleLightRig lights;
        [SerializeField, Tooltip("Plays the starter and the catch. Positioned at the car, not the viewer.")]
        private AudioSource startSource;
        [SerializeField, Tooltip("Loops the idle. Sits at the tailpipe so the exhaust note has a place in the room.")]
        private AudioSource idleSource;

        [Header("Sequence")]
        [SerializeField, Tooltip("Lamps lit as soon as the key turns, before the engine catches.")]
        private string[] accessoryChannels = new string[0];
        [SerializeField, Tooltip("Lamps lit once the engine is running.")]
        private string[] runningChannels = new string[0];
        [SerializeField, Range(0f, 3f), Tooltip("Seconds between the starter beginning and the engine catching.")]
        private float catchDelay = 0.85f;
        [SerializeField, Range(0f, 3f), Tooltip("Seconds the idle takes to fade up once it has caught.")]
        private float idleFadeDuration = 0.6f;
        [SerializeField, Range(0f, 1f)] private float idleVolume = 0.5f;

        private bool isRunning;
        private Coroutine sequence;

        /// <summary>
        /// Raised when the car starts or stops. The argument is true while it is running.
        /// </summary>
        public event Action<bool> RunningChanged;

        public bool IsRunning {
            get { return isRunning; }
        }

        [Header("Audio")]
        [SerializeField, Tooltip("Override for the start sound. Synthesised when empty.")]
        private AudioClip startOverride;
        [SerializeField, Tooltip("Override for the idle loop. Synthesised when empty.")]
        private AudioClip idleOverride;
        [SerializeField, Range(8f, 60f), Tooltip("Combustion events per second at idle. A four-stroke " +
            "four fires twice per revolution, so 26 Hz is about 780 rpm.")]
        private float idleFiringHz = 26f;

        private void Awake() {
            // Synthesised rather than shipped, the same way the anatomy exhibit makes its cues:
            // a handful of clips that would otherwise be several megabytes of audio files.
            if (startSource != null && startSource.clip == null) {
                startSource.clip = startOverride != null ? startOverride : ProceduralAudio.CreateEngineStart();
            }

            if (idleSource != null && idleSource.clip == null) {
                idleSource.clip = idleOverride != null ? idleOverride : ProceduralAudio.CreateEngineIdle(idleFiringHz);
            }
        }

        private void Start() {
            if (lights != null) {
                lights.SetAll(false);
            }

            if (idleSource != null) {
                idleSource.volume = 0f;
                idleSource.loop = true;
            }
        }

        public void Toggle() {
            SetRunning(!isRunning);
        }

        public void SetRunning(bool running) {
            if (running == isRunning) {
                return;
            }

            isRunning = running;

            if (sequence != null) {
                StopCoroutine(sequence);
                sequence = null;
            }

            sequence = StartCoroutine(running ? StartSequence() : StopSequence());

            if (RunningChanged != null) {
                RunningChanged(isRunning);
            }
        }

        private IEnumerator StartSequence() {
            if (startSource != null && startSource.clip != null) {
                startSource.Play();
            }

            SetChannels(accessoryChannels, true);

            yield return new WaitForSeconds(catchDelay);

            SetChannels(runningChannels, true);

            if (idleSource != null && idleSource.clip != null) {
                if (!idleSource.isPlaying) {
                    idleSource.Play();
                }

                yield return FadeIdle(idleVolume, idleFadeDuration);
            }

            sequence = null;
        }

        private IEnumerator StopSequence() {
            SetChannels(runningChannels, false);

            if (idleSource != null) {
                yield return FadeIdle(0f, idleFadeDuration * 0.7f);
                idleSource.Stop();
            }

            // The cabin stays lit a moment after the engine dies, the way it does when the door is
            // still open and the courtesy lamps have not timed out.
            yield return new WaitForSeconds(0.35f);
            SetChannels(accessoryChannels, false);
            sequence = null;
        }

        private IEnumerator FadeIdle(float to, float duration) {
            if (idleSource == null) {
                yield break;
            }

            float from = idleSource.volume;
            float elapsed = 0f;

            while (elapsed < duration) {
                elapsed += Time.deltaTime;
                idleSource.volume = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            idleSource.volume = to;
        }

        private void SetChannels(string[] names, bool on) {
            if (lights == null) {
                return;
            }

            for (int i = 0; i < names.Length; i++) {
                int index = lights.IndexOf(names[i]);
                if (index >= 0) {
                    lights.SetChannel(index, on);
                }
            }
        }
    }
}
