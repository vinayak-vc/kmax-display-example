using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Builds the exhibit's music and interface sounds as audio clips at runtime.
    ///
    /// Synthesised rather than imported so the module ships with no audio assets and no licence
    /// question attached to them. <see cref="AnatomyAudioDirector"/> takes an override clip for
    /// every sound, so swapping in recorded audio later is an inspector edit and no code change.
    /// </summary>
    public static class ProceduralAudio {
        /// <summary>
        /// One voice of the pad: a frequency relative to the root, how loud it is, and how far it
        /// is detuned and panned.
        /// </summary>
        public struct PadVoice {
            public float Ratio;
            public float Gain;
            public float DetuneCents;
            public float Pan;

            public PadVoice(float ratio, float gain, float detuneCents, float pan) {
                Ratio = ratio;
                Gain = gain;
                DetuneCents = detuneCents;
                Pan = pan;
            }
        }

        /// <summary>
        /// A slow, warm stereo pad that loops without a seam.
        ///
        /// Seamlessness is the whole constraint here: every partial and every tremolo rate is
        /// snapped to a whole number of cycles across the loop, so the waveform and its envelope
        /// both arrive back exactly where they started. Without the snap the loop point clicks.
        /// </summary>
        /// <param name="rootHz">Fundamental of the chord, in hertz.</param>
        /// <param name="loopSeconds">Loop length. Longer is less repetitive but costs memory.</param>
        /// <param name="voices">Chord voicing. Null uses a calm suspended-ninth voicing.</param>
        public static AudioClip CreatePad(float rootHz = 110f, float loopSeconds = 16f, PadVoice[] voices = null) {
            if (voices == null) {
                // Root, fifth, octave, ninth and twelfth: open and unresolved, so it never sounds
                // like it is about to end - which matters for something playing all day.
                // Weighted towards the upper partials rather than the root. Most of a pad's warmth
                // lives in the fundamental, but a display's panel speakers roll that away entirely -
                // so the octave and the fifth above it carry the chord and the root only colours it.
                voices = new PadVoice[] {
                    new PadVoice(1.00f, 0.30f, 0f, -0.15f),
                    new PadVoice(1.50f, 0.34f, +4f, 0.35f),
                    new PadVoice(2.00f, 0.38f, -3f, -0.40f),
                    new PadVoice(2.25f, 0.22f, +6f, 0.55f),
                    new PadVoice(3.00f, 0.24f, -5f, -0.60f),
                    new PadVoice(4.00f, 0.15f, +7f, 0.25f)
                };
            }

            int sampleRate = GetSampleRate();
            int frames = Mathf.Max(1, Mathf.RoundToInt(sampleRate * loopSeconds));
            float actualLoop = frames / (float)sampleRate;
            float[] data = new float[frames * 2];

            for (int v = 0; v < voices.Length; v++) {
                PadVoice voice = voices[v];
                float frequency = rootHz * voice.Ratio * CentsToRatio(voice.DetuneCents);
                frequency = SnapToLoop(frequency, actualLoop);

                // Each voice breathes at its own slow rate, which keeps the chord moving without
                // anything ever arriving or departing.
                float tremoloHz = SnapToLoop(0.045f + v * 0.021f, actualLoop);
                float tremoloPhase = v * 0.9f;

                float leftGain = voice.Gain * Mathf.Sqrt(Mathf.Clamp01(0.5f - voice.Pan * 0.5f));
                float rightGain = voice.Gain * Mathf.Sqrt(Mathf.Clamp01(0.5f + voice.Pan * 0.5f));

                float angularStep = 2f * Mathf.PI * frequency / sampleRate;
                float tremoloStep = 2f * Mathf.PI * tremoloHz / sampleRate;

                for (int i = 0; i < frames; i++) {
                    float sample = Mathf.Sin(angularStep * i);
                    float tremolo = 0.72f + 0.28f * Mathf.Sin(tremoloStep * i + tremoloPhase);
                    sample *= tremolo;

                    data[i * 2] += sample * leftGain;
                    data[i * 2 + 1] += sample * rightGain;
                }
            }

            NormalisePeak(data, 0.72f);
            SoftClip(data);

            AudioClip clip = AudioClip.Create("AnatomyPad", frames, 2, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>
        /// A soft bell used for selecting a part: a few inharmonic partials under an exponential
        /// decay, which is what gives a struck body its shimmer rather than an organ tone.
        /// </summary>
        public static AudioClip CreateChime(float rootHz = 880f, float durationSeconds = 0.9f, float brightness = 1f) {
            float[] ratios = new float[] { 1f, 2.01f, 2.99f, 4.18f, 5.42f };
            float[] gains = new float[] { 1f, 0.46f, 0.28f, 0.15f, 0.08f };
            float[] decays = new float[] { 1f, 1.5f, 2.1f, 3.0f, 4.2f };

            int sampleRate = GetSampleRate();
            int frames = Mathf.Max(1, Mathf.RoundToInt(sampleRate * durationSeconds));
            float[] data = new float[frames];

            for (int p = 0; p < ratios.Length; p++) {
                float frequency = rootHz * ratios[p];
                if (frequency >= sampleRate * 0.45f) {
                    continue;
                }

                float gain = gains[p] * Mathf.Pow(brightness, p);
                float decayRate = decays[p] * 4.5f / Mathf.Max(durationSeconds, 0.001f);
                float angularStep = 2f * Mathf.PI * frequency / sampleRate;

                for (int i = 0; i < frames; i++) {
                    float t = i / (float)sampleRate;
                    data[i] += Mathf.Sin(angularStep * i) * gain * Mathf.Exp(-decayRate * t);
                }
            }

            ApplyFadeIn(data, sampleRate, 0.004f);
            NormalisePeak(data, 0.8f);
            return ToClip("AnatomyChime", data, 1, sampleRate);
        }

        /// <summary>
        /// A short pitched blip for hovers and navigation steps. Deliberately quiet and brief - it
        /// fires often, so anything with a tail becomes noise.
        /// </summary>
        public static AudioClip CreateBlip(float frequencyHz = 1320f, float durationSeconds = 0.07f) {
            int sampleRate = GetSampleRate();
            int frames = Mathf.Max(1, Mathf.RoundToInt(sampleRate * durationSeconds));
            float[] data = new float[frames];

            float angularStep = 2f * Mathf.PI * frequencyHz / sampleRate;
            float decayRate = 26f;

            for (int i = 0; i < frames; i++) {
                float t = i / (float)sampleRate;
                // A touch of second harmonic stops it sounding like a test tone.
                float sample = Mathf.Sin(angularStep * i) + 0.25f * Mathf.Sin(angularStep * 2f * i);
                data[i] = sample * Mathf.Exp(-decayRate * t);
            }

            ApplyFadeIn(data, sampleRate, 0.002f);
            ApplyFadeOut(data, sampleRate, 0.008f);
            NormalisePeak(data, 0.65f);
            return ToClip("AnatomyBlip", data, 1, sampleRate);
        }

        /// <summary>
        /// Air moving past: filtered noise swept by a resonant one-pole, used for the eye opening
        /// and for the camera flying to a part.
        /// </summary>
        /// <param name="rising">True sweeps the filter upward, false sweeps it down.</param>
        public static AudioClip CreateWhoosh(float durationSeconds = 0.55f, bool rising = true) {
            int sampleRate = GetSampleRate();
            int frames = Mathf.Max(1, Mathf.RoundToInt(sampleRate * durationSeconds));
            float[] data = new float[frames];

            // Deterministic so the exhibit sounds identical on every launch.
            Random.State previousState = Random.state;
            Random.InitState(20260925);

            float lowpass = 0f;
            float highpassMemory = 0f;
            float previousInput = 0f;

            for (int i = 0; i < frames; i++) {
                float t = i / (float)frames;
                float noise = Random.value * 2f - 1f;

                // Sweep the cutoff across the body of the sound, which is what reads as movement.
                float sweep = rising ? t : 1f - t;
                float cutoff = Mathf.Lerp(0.02f, 0.38f, sweep * sweep);

                lowpass += (noise - lowpass) * cutoff;

                // A gentle high-pass underneath keeps the rumble out of the display's speakers.
                highpassMemory = 0.92f * (highpassMemory + lowpass - previousInput);
                previousInput = lowpass;

                // Swell in and out so there is no edge at either end.
                float envelope = Mathf.Sin(t * Mathf.PI);
                data[i] = highpassMemory * envelope * envelope;
            }

            Random.state = previousState;

            NormalisePeak(data, 0.55f);
            return ToClip("AnatomyWhoosh", data, 1, sampleRate);
        }

        /// <summary>
        /// A low, short thud for returning and resetting - the sound of something settling back.
        /// </summary>
        public static AudioClip CreateThud(float frequencyHz = 190f, float durationSeconds = 0.3f) {
            int sampleRate = GetSampleRate();
            int frames = Mathf.Max(1, Mathf.RoundToInt(sampleRate * durationSeconds));
            float[] data = new float[frames];

            for (int i = 0; i < frames; i++) {
                float t = i / (float)sampleRate;
                // The pitch falls away as it decays, which is what makes a thud read as weight.
                float sweep = frequencyHz * Mathf.Exp(-6f * t);
                data[i] = Mathf.Sin(2f * Mathf.PI * sweep * t) * Mathf.Exp(-11f * t);
            }

            ApplyFadeIn(data, sampleRate, 0.003f);
            NormalisePeak(data, 0.7f);
            return ToClip("AnatomyThud", data, 1, sampleRate);
        }

        /// <summary>
        /// A seamless four-cylinder idle loop.
        ///
        /// Built from the firing rate rather than from an engine note: a four-stroke four fires
        /// twice per revolution, so an idle around 780 rpm puts a combustion pulse every 26 Hz, and
        /// that pulse train is what the ear identifies as a particular engine. Each event is a
        /// sharp attack decaying exponentially, gating both a harmonic stack for the body of the
        /// note and filtered noise for the rasp in the exhaust.
        ///
        /// The noise is generated once per firing cycle and replayed for every cycle, which is both
        /// truer - each combustion event really is much like the last - and what makes the clip
        /// loop without a click, since every component is then periodic at the firing rate.
        /// </summary>
        public static AudioClip CreateEngineIdle(float firingHz = 26f, float loopSeconds = 2f) {
            int sampleRate = GetSampleRate();
            float snapped = SnapToLoop(firingHz, loopSeconds);
            int frames = Mathf.Max(1, Mathf.RoundToInt(sampleRate * loopSeconds));
            float[] data = new float[frames * 2];

            int cycleFrames = Mathf.Max(1, Mathf.RoundToInt(sampleRate / snapped));
            float[] cycleNoise = new float[cycleFrames];
            System.Random random = new System.Random(20260929);
            float filtered = 0f;
            for (int i = 0; i < cycleFrames; i++) {
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                // One-pole low pass: unfiltered noise reads as hiss rather than as exhaust.
                filtered += (white - filtered) * 0.12f;
                cycleNoise[i] = filtered;
            }

            for (int i = 0; i < frames; i++) {
                float t = i / (float)sampleRate;
                float phase = t * snapped;
                float withinCycle = phase - Mathf.Floor(phase);
                float pulse = Mathf.Exp(-withinCycle * 7f);

                float body = 0f;
                for (int harmonic = 1; harmonic <= 7; harmonic++) {
                    body += Mathf.Sin(2f * Mathf.PI * snapped * harmonic * t) / harmonic;
                }

                float rasp = cycleNoise[i % cycleFrames];
                float sample = (body * 0.22f + rasp * 0.55f) * pulse;

                // A slow wobble, because a real idle never holds perfectly steady.
                sample *= 1f + 0.06f * Mathf.Sin(2f * Mathf.PI * SnapToLoop(1.5f, loopSeconds) * t);

                data[i * 2] = sample;
                data[i * 2 + 1] = sample * 0.92f;
            }

            NormalisePeak(data, 0.55f);
            SoftClip(data);
            return ToClip("VehicleIdle", data, 2, sampleRate);
        }

        /// <summary>
        /// The starter turning over, the engine catching, and the blip of revs as it settles.
        ///
        /// Three overlapping stages rather than three clips, so the catch lands in the middle of
        /// the starter rather than after it - which is what a start actually sounds like.
        /// </summary>
        public static AudioClip CreateEngineStart(float durationSeconds = 2.2f) {
            int sampleRate = GetSampleRate();
            int frames = Mathf.Max(1, Mathf.RoundToInt(sampleRate * durationSeconds));
            float[] data = new float[frames];

            System.Random random = new System.Random(19661014);
            float filtered = 0f;
            float crankEnd = durationSeconds * 0.45f;
            float catchAt = durationSeconds * 0.38f;

            for (int i = 0; i < frames; i++) {
                float t = i / (float)sampleRate;
                float sample = 0f;

                // Starter: a slow chug plus the gear whine that rides on it, both fading as the
                // engine takes over.
                if (t < crankEnd) {
                    float crankFade = 1f - Mathf.Clamp01(t / crankEnd);
                    float chug = Mathf.Sin(2f * Mathf.PI * 9.5f * t);
                    float chugGate = Mathf.Max(0f, chug);
                    float whine = Mathf.Sin(2f * Mathf.PI * (1450f - 220f * t) * t) * 0.18f;
                    sample += (chugGate * 0.5f + whine) * crankFade;
                }

                // The engine catching: firing rate flares above idle and settles back down to it.
                if (t > catchAt) {
                    float since = t - catchAt;
                    float flare = Mathf.Exp(-since * 2.2f);
                    float firing = 26f + 34f * flare;
                    float withinCycle = (t * firing) - Mathf.Floor(t * firing);
                    float pulse = Mathf.Exp(-withinCycle * 7f);

                    float body = 0f;
                    for (int harmonic = 1; harmonic <= 6; harmonic++) {
                        body += Mathf.Sin(2f * Mathf.PI * firing * harmonic * t) / harmonic;
                    }

                    float white = (float)(random.NextDouble() * 2.0 - 1.0);
                    filtered += (white - filtered) * 0.12f;

                    float rise = Mathf.Clamp01(since * 6f);
                    sample += (body * 0.22f + filtered * 0.55f) * pulse * rise;
                }

                data[i] = sample;
            }

            ApplyFadeIn(data, sampleRate, 0.01f);
            ApplyFadeOut(data, sampleRate, 0.12f);
            NormalisePeak(data, 0.8f);
            SoftClip(data);
            return ToClip("VehicleStart", data, 1, sampleRate);
        }

        private static int GetSampleRate() {
            int rate = AudioSettings.outputSampleRate;
            return rate > 0 ? rate : 48000;
        }

        /// <summary>
        /// Rounds a frequency to the nearest whole number of cycles across the loop, so the clip
        /// wraps without a discontinuity.
        /// </summary>
        private static float SnapToLoop(float frequency, float loopSeconds) {
            if (loopSeconds <= 0f) {
                return frequency;
            }

            float cycles = Mathf.Max(1f, Mathf.Round(frequency * loopSeconds));
            return cycles / loopSeconds;
        }

        private static float CentsToRatio(float cents) {
            return Mathf.Pow(2f, cents / 1200f);
        }

        private static void NormalisePeak(float[] data, float target) {
            float peak = 0f;
            for (int i = 0; i < data.Length; i++) {
                float magnitude = Mathf.Abs(data[i]);
                if (magnitude > peak) {
                    peak = magnitude;
                }
            }

            if (peak <= Mathf.Epsilon) {
                return;
            }

            float scale = target / peak;
            for (int i = 0; i < data.Length; i++) {
                data[i] *= scale;
            }
        }

        /// <summary>
        /// Rounds off anything that still pokes past full scale, rather than letting it clip hard.
        /// </summary>
        private static void SoftClip(float[] data) {
            for (int i = 0; i < data.Length; i++) {
                data[i] = (float)System.Math.Tanh(data[i]);
            }
        }

        private static void ApplyFadeIn(float[] data, int sampleRate, float seconds) {
            int fade = Mathf.Min(data.Length, Mathf.RoundToInt(sampleRate * seconds));
            for (int i = 0; i < fade; i++) {
                data[i] *= i / (float)fade;
            }
        }

        private static void ApplyFadeOut(float[] data, int sampleRate, float seconds) {
            int fade = Mathf.Min(data.Length, Mathf.RoundToInt(sampleRate * seconds));
            for (int i = 0; i < fade; i++) {
                data[data.Length - 1 - i] *= i / (float)fade;
            }
        }

        private static AudioClip ToClip(string name, float[] data, int channels, int sampleRate) {
            AudioClip clip = AudioClip.Create(name, data.Length / channels, channels, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
