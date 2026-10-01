using KmaxXR;
using UnityEngine;

namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// Rate-limited haptics for the pen, and a shared vocabulary of cues for the whole suite.
    ///
    /// The rate limit is the reason this exists rather than every component calling
    /// <c>VibrationOnce</c> itself. The pen buzzes for as long as it is told to, and the events
    /// that want to buzz - a tip crossing a collider boundary, a block scraping another block -
    /// fire every few frames while the viewer holds still against a surface. Without a floor the
    /// pen hums continuously, which stops reading as feedback and starts reading as a fault. The
    /// eye exhibit found this on hardware and settled on roughly one pulse per quarter second.
    ///
    /// <para>The named cues matter for a different reason: four scenes that each invent their own
    /// strength and duration teach the viewer nothing, whereas four scenes where a grab always
    /// feels like a grab are learnable. Tune them here, once.</para>
    /// </summary>
    public class StylusHaptics : MonoBehaviour {
        [Header("References")]
        [SerializeField, Tooltip("The pen. Found by id on first use when left empty.")]
        private KmaxStylus stylus;

        [Header("Rate Limit")]
        [SerializeField, Tooltip("Minimum seconds between ordinary pulses. Cues marked important " +
            "ignore this - a grab has to land on the frame the viewer pressed the button.")]
        private float minInterval = 0.25f;

        [Header("Cues")]
        [SerializeField, Range(0, 100), Tooltip("A light tick: the tip has crossed onto something.")]
        private int tickStrength = 8;
        [SerializeField, Tooltip("Tick duration in seconds.")]
        private float tickDuration = 0.015f;
        [SerializeField, Range(0, 100), Tooltip("Taking hold of an object. Firmer than a tick, " +
            "because it confirms a deliberate action rather than reporting a passing contact.")]
        private int grabStrength = 32;
        [SerializeField, Tooltip("Grab duration in seconds.")]
        private float grabDuration = 0.04f;
        [SerializeField, Range(0, 100), Tooltip("Letting go. Lighter than the grab, so the pair " +
            "reads as a bracket rather than as two of the same event.")]
        private int releaseStrength = 18;
        [SerializeField, Tooltip("Release duration in seconds.")]
        private float releaseDuration = 0.025f;
        [SerializeField, Range(0, 100), Tooltip("A hard impact at full force. Scaled down by the " +
            "strength passed to Impact, so a gentle knock is gentle.")]
        private int impactStrength = 60;
        [SerializeField, Tooltip("Impact duration in seconds.")]
        private float impactDuration = 0.05f;
        [SerializeField, Range(0, 100), Tooltip("A sustained error buzz - the probe touching the wall.")]
        private int errorStrength = 45;
        [SerializeField, Tooltip("Error buzz duration in seconds.")]
        private float errorDuration = 0.12f;

        private KmaxStylus _stylus;
        private float _lastPulseTime = -1f;

        /// <summary>
        /// True when a pen is actually being tracked and can be vibrated.
        ///
        /// <b>This has to be checked before every call into the SDK's vibration path, not just
        /// once.</b> The <see cref="KmaxStylus"/> component exists in the scene whether or not a
        /// physical pen is connected, so a null check on it passes in the editor - but
        /// <c>PenTracker.Vibrate</c> goes on to send a command through <c>PNClient</c>, which has
        /// no connection without hardware and throws a NullReferenceException from inside the SDK.
        /// Every cue below is silently a no-op when this is false.
        /// </summary>
        public bool HasPen {
            get {
                KmaxStylus pen = Resolve();
                return pen != null && pen.Visible;
            }
        }

        private KmaxStylus Resolve() {
            if (_stylus != null) {
                return _stylus;
            }
            if (stylus != null) {
                _stylus = stylus;
                return _stylus;
            }
            _stylus = KmaxPointer.PointerById(KmaxStylus.UniqueId) as KmaxStylus;
            return _stylus;
        }

        /// <summary>
        /// The tip has arrived on something. Rate limited.
        /// </summary>
        public void Tick() {
            Pulse(tickDuration, tickStrength, false);
        }

        /// <summary>
        /// An object has been taken hold of. Always fires.
        /// </summary>
        public void Grab() {
            Pulse(grabDuration, grabStrength, true);
        }

        /// <summary>
        /// An object has been let go. Always fires.
        /// </summary>
        public void Release() {
            Pulse(releaseDuration, releaseStrength, true);
        }

        /// <summary>
        /// Something struck something, at <paramref name="strength01"/> of full force.
        /// Rate limited, because collisions arrive in bursts.
        /// </summary>
        public void Impact(float strength01) {
            int scaled = Mathf.RoundToInt(impactStrength * Mathf.Clamp01(strength01));
            if (scaled <= 0) {
                return;
            }
            Pulse(impactDuration, scaled, false);
        }

        /// <summary>
        /// The viewer has done the thing the scene is asking them not to do. Always fires - an
        /// error that only sometimes buzzes teaches nothing.
        /// </summary>
        public void Error() {
            Pulse(errorDuration, errorStrength, true);
        }

        /// <summary>
        /// Fires a pulse directly. Prefer the named cues; this is for a scene with a genuinely
        /// new kind of feedback.
        /// </summary>
        /// <param name="duration">Seconds.</param>
        /// <param name="strength">0 to 100.</param>
        /// <param name="important">Bypasses the rate limit.</param>
        public void Pulse(float duration, int strength, bool important) {
            KmaxStylus pen = Resolve();
            if (pen == null || !pen.Visible) {
                return;
            }
            if (!important && Time.unscaledTime - _lastPulseTime < minInterval) {
                return;
            }
            _lastPulseTime = Time.unscaledTime;
            pen.VibrationOnce(duration, Mathf.Clamp(strength, 0, 100));
        }

        /// <summary>
        /// Stops any sustained vibration. Called on disable so a scene change cannot leave the pen
        /// buzzing.
        /// </summary>
        public void Stop() {
            KmaxStylus pen = Resolve();
            if (pen == null || !pen.Visible) {
                return;
            }
            pen.StopVibration();
        }

        private void OnDisable() {
            Stop();
        }
    }
}
