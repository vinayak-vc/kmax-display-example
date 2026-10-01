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
        public static AudioClip CreatePad(float rootHz = 110f, float loopSeconds = 24f, PadVoice[] voices = null) {
            int sampleRate = GetSampleRate();
            if (voices == null) {
                float duration = Mathf.Max(16f, loopSeconds);
                float[] soothingData = BuildSoothingSamples(sampleRate, duration);
                int soothingFrames = soothingData.Length / 2;
                AudioClip soothingClip = AudioClip.Create("SoothingAmbientPad", soothingFrames, 2, sampleRate, false);
                soothingClip.SetData(soothingData, 0);
                return soothingClip;
            }

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
        /// Synthesizes a seamless, soothing stereo ambient soundtrack: four slow-breathing warm
        /// ninth chords (Fmaj9 - Cmaj9 - Dm9 - Am9) crossfading across the loop boundary with
        /// gentle felt-bell pentatonic notes and stereo echo.
        /// </summary>
        public static float[] BuildSoothingSamples(int sampleRate, float loopSeconds = 24f) {
            int frames = Mathf.Max(1, Mathf.RoundToInt(sampleRate * loopSeconds));
            float actualLoop = frames / (float)sampleRate;
            float[] data = new float[frames * 2];

            float[][] chords = new float[][] {
                // Fmaj9: F3, A3, C4, E4, G4
                new float[] { 174.61f, 220.00f, 261.63f, 329.63f, 392.00f },
                // Cmaj9: C3, G3, B3, D4, E4
                new float[] { 130.81f, 196.00f, 246.94f, 293.66f, 329.63f },
                // Dm9: D3, A3, C4, E4, F4
                new float[] { 146.83f, 220.00f, 261.63f, 329.63f, 349.23f },
                // Am9: A2, E3, G3, B3, C4
                new float[] { 110.00f, 164.81f, 196.00f, 246.94f, 261.63f }
            };

            float[] voiceGains = new float[] { 0.26f, 0.24f, 0.22f, 0.18f, 0.14f };
            float[] voicePans = new float[] { -0.15f, 0.28f, -0.35f, 0.42f, -0.22f };

            int chordCount = chords.Length;
            float chordDuration = actualLoop / chordCount;
            float crossfade = chordDuration * 0.38f;

            for (int c = 0; c < chordCount; c++) {
                float chordStart = c * chordDuration;
                float[] notes = chords[c];

                for (int v = 0; v < notes.Length; v++) {
                    float freqA = SnapToLoop(notes[v] * CentsToRatio(-2.5f + v * 1.1f), actualLoop);
                    float freqB = SnapToLoop(notes[v] * CentsToRatio(2.5f - v * 0.9f), actualLoop);
                    float stepA = 2f * Mathf.PI * freqA / sampleRate;
                    float stepB = 2f * Mathf.PI * freqB / sampleRate;
                    float lfoStep = 2f * Mathf.PI * SnapToLoop(0.08f + v * 0.03f, actualLoop) / sampleRate;
                    float lfoPhase = c * 1.3f + v * 0.7f;

                    float gain = voiceGains[v];
                    float pan = voicePans[v];
                    float leftGain = gain * Mathf.Sqrt(Mathf.Clamp01(0.5f - pan * 0.5f));
                    float rightGain = gain * Mathf.Sqrt(Mathf.Clamp01(0.5f + pan * 0.5f));

                    for (int i = 0; i < frames; i++) {
                        float t = i / (float)sampleRate;
                        float env = ChordEnvelope(t, chordStart, chordDuration, crossfade, actualLoop);
                        if (env <= 0.0001f) {
                            continue;
                        }

                        float warmTone = 0.52f * (Mathf.Sin(stepA * i) + Mathf.Sin(stepB * i))
                            + 0.14f * Mathf.Sin(stepA * 2f * i);
                        float breath = 0.82f + 0.18f * Mathf.Sin(lfoStep * i + lfoPhase);
                        float sample = warmTone * breath * env;

                        data[i * 2] += sample * leftGain;
                        data[i * 2 + 1] += sample * rightGain;
                    }
                }
            }

            // Gentle, sparse felt-bell pentatonic notes with soft stereo echo.
            float[] bellTimes = new float[] { 1.1f, 4.1f, 7.1f, 10.1f, 13.1f, 16.1f, 19.1f, 22.1f };
            float[] bellNotes = new float[] { 329.63f, 392.00f, 440.00f, 329.63f, 293.66f, 349.23f, 329.63f, 261.63f };
            float[] bellPans = new float[] { -0.32f, 0.30f, -0.24f, 0.34f, -0.28f, 0.26f, -0.30f, 0.22f };

            for (int b = 0; b < bellTimes.Length; b++) {
                if (bellTimes[b] >= actualLoop) {
                    continue;
                }

                AddSoftBellNote(data, sampleRate, frames, bellTimes[b], bellNotes[b], bellPans[b], 0.13f);
                AddSoftBellNote(data, sampleRate, frames, bellTimes[b] + 0.48f, bellNotes[b], -bellPans[b] * 0.8f, 0.05f);
            }

            NormalisePeak(data, 0.58f);
            SoftClip(data);
            return data;
        }

        private static float ChordEnvelope(float t, float start, float duration, float fade, float loopLength) {
            float rel = t - start;
            if (rel < -fade) {
                rel += loopLength;
            } else if (rel > loopLength - fade) {
                rel -= loopLength;
            }

            if (rel < -fade || rel > duration + fade) {
                return 0f;
            }

            if (rel < fade) {
                float u = Mathf.Clamp01((rel + fade) / (2f * fade));
                return 0.5f - 0.5f * Mathf.Cos(u * Mathf.PI);
            }

            if (rel > duration - fade) {
                float u = Mathf.Clamp01((rel - (duration - fade)) / (2f * fade));
                return 0.5f + 0.5f * Mathf.Cos(u * Mathf.PI);
            }

            return 1f;
        }

        private static void AddSoftBellNote(
            float[] data,
            int sampleRate,
            int totalFrames,
            float startTime,
            float frequencyHz,
            float pan,
            float amplitude) {
            float noteDuration = 3.2f;
            int noteFrames = Mathf.RoundToInt(sampleRate * noteDuration);
            int startFrame = Mathf.RoundToInt(sampleRate * startTime);
            int attackFrames = Mathf.Max(1, Mathf.RoundToInt(sampleRate * 0.045f));

            float leftGain = amplitude * Mathf.Sqrt(Mathf.Clamp01(0.5f - pan * 0.5f));
            float rightGain = amplitude * Mathf.Sqrt(Mathf.Clamp01(0.5f + pan * 0.5f));
            float step1 = 2f * Mathf.PI * frequencyHz / sampleRate;
            float step2 = 2f * Mathf.PI * (frequencyHz * 2f) / sampleRate;

            for (int i = 0; i < noteFrames; i++) {
                float dt = i / (float)sampleRate;
                float attack = i < attackFrames
                    ? 0.5f - 0.5f * Mathf.Cos((i / (float)attackFrames) * Mathf.PI)
                    : 1f;
                float decay = Mathf.Exp(-1.55f * dt);
                float fadeOut = dt > noteDuration - 0.25f
                    ? Mathf.Clamp01((noteDuration - dt) / 0.25f)
                    : 1f;

                float tone = Mathf.Sin(step1 * i) + 0.22f * Mathf.Sin(step2 * i) * Mathf.Exp(-2.8f * dt);
                float sample = tone * attack * decay * fadeOut;

                int frameIndex = (startFrame + i) % totalFrames;
                if (frameIndex < 0) {
                    frameIndex += totalFrames;
                }

                data[frameIndex * 2] += sample * leftGain;
                data[frameIndex * 2 + 1] += sample * rightGain;
            }
        }

        /// <summary>
        /// A warm, modern harmonic crystal chime used when selecting a 3D part or scene tile:
        /// blends a soft felt transient with a lush major-ninth micro-arpeggio (root, fifth, major
        /// seventh, ninth) so selection feels rewarding and contemporary.
        /// </summary>
        public static AudioClip CreateChime(float rootHz = 523.25f, float durationSeconds = 0.75f, float brightness = 1f) {
            float[] notes = new float[] {
                rootHz,
                rootHz * 1.25f,
                rootHz * 1.498f,
                rootHz * 1.875f
            };
            float[] noteDelays = new float[] { 0f, 0.022f, 0.046f, 0.072f };
            float[] noteGains = new float[] { 0.85f, 0.62f, 0.50f, 0.38f };

            int sampleRate = GetSampleRate();
            int frames = Mathf.Max(1, Mathf.RoundToInt(sampleRate * durationSeconds));
            float[] data = new float[frames];

            for (int n = 0; n < notes.Length; n++) {
                float freq = notes[n];
                if (freq >= sampleRate * 0.45f) {
                    continue;
                }

                int startFrame = Mathf.RoundToInt(noteDelays[n] * sampleRate);
                float gain = noteGains[n] * Mathf.Lerp(0.8f, 1.1f, brightness);
                float step1 = 2f * Mathf.PI * freq / sampleRate;
                float step2 = 2f * Mathf.PI * (freq * 2f) / sampleRate;
                float step3 = 2f * Mathf.PI * (freq * 3f) / sampleRate;

                for (int i = startFrame; i < frames; i++) {
                    int local = i - startFrame;
                    float t = local / (float)sampleRate;
                    float attack = Mathf.Clamp01(t / 0.0045f);
                    // Soft pitch envelope at the very start gives a tactile glass-mallet strike.
                    float pitchBend = 1f + 0.012f * Mathf.Exp(-65f * t);
                    float phase1 = step1 * local * pitchBend;
                    float tone = Mathf.Sin(phase1) * Mathf.Exp(-5.2f * t)
                        + 0.28f * Mathf.Sin(step2 * local) * Mathf.Exp(-9.5f * t)
                        + 0.09f * Mathf.Sin(step3 * local) * Mathf.Exp(-15f * t);
                    data[i] += tone * gain * attack;
                }
            }

            ApplyFadeIn(data, sampleRate, 0.002f);
            ApplyFadeOut(data, sampleRate, 0.03f);
            NormalisePeak(data, 0.76f);
            SoftClip(data);
            return ToClip("AnatomyChime", data, 1, sampleRate);
        }

        /// <summary>
        /// A modern, warm micro-tap for hover and step navigation. Instead of an old-fashioned pure
        /// sine beep, this synthesizes a soft wooden/glass tactile impulse with a subtle pitch drop
        /// and fast exponential damping.
        /// </summary>
        public static AudioClip CreateBlip(float frequencyHz = 680f, float durationSeconds = 0.055f) {
            int sampleRate = GetSampleRate();
            int frames = Mathf.Max(1, Mathf.RoundToInt(sampleRate * durationSeconds));
            float[] data = new float[frames];

            // Keep hover/step frequencies in the warm, modern mid-band (520 - 880 Hz) even if caller passes higher.
            float baseFreq = Mathf.Clamp(frequencyHz * 0.52f, 440f, 880f);
            float phase = 0f;
            float phaseFifth = 0f;

            for (int i = 0; i < frames; i++) {
                float t = i / (float)sampleRate;
                // Subtle downward pitch transient gives a physical "tap" rather than an electronic beep.
                float instFreq = baseFreq * (1f + 0.38f * Mathf.Exp(-140f * t));
                phase += 2f * Mathf.PI * instFreq / sampleRate;
                phaseFifth += 2f * Mathf.PI * (instFreq * 1.498f) / sampleRate;

                float body = Mathf.Sin(phase) * Mathf.Exp(-48f * t);
                float shimmer = 0.32f * Mathf.Sin(phaseFifth) * Mathf.Exp(-75f * t);
                float clickTransient = 0.22f * Mathf.Sin(2f * Mathf.PI * 2100f * t) * Mathf.Exp(-240f * t);
                data[i] = body + shimmer + clickTransient;
            }

            ApplyFadeIn(data, sampleRate, 0.0012f);
            ApplyFadeOut(data, sampleRate, 0.008f);
            NormalisePeak(data, 0.58f);
            return ToClip("AnatomyBlip", data, 1, sampleRate);
        }

        /// <summary>
        /// Synthesizes a modern, crisp spatial-UI button click: a warm two-stage glass/marimba
        /// tactile tap (low-mid body + crisp fifth chime) that feels responsive and contemporary.
        /// </summary>
        public static AudioClip CreateUiClick(float rootHz = 587.33f, float durationSeconds = 0.085f) {
            int sampleRate = GetSampleRate();
            int frames = Mathf.Max(1, Mathf.RoundToInt(sampleRate * durationSeconds));
            float[] data = new float[frames];

            float phase1 = 0f;
            float phase2 = 0f;
            int secondTapStart = Mathf.RoundToInt(0.014f * sampleRate);

            for (int i = 0; i < frames; i++) {
                float t = i / (float)sampleRate;
                float freq1 = rootHz * (1f + 0.45f * Mathf.Exp(-160f * t));
                phase1 += 2f * Mathf.PI * freq1 / sampleRate;

                // First stage: warm tactile press transient
                float tap1 = (Mathf.Sin(phase1) + 0.25f * Mathf.Sin(phase1 * 2f)) * Mathf.Exp(-42f * t);

                // Second stage (14ms later): bright fifth harmonic release bloom
                float tap2 = 0f;
                if (i >= secondTapStart) {
                    float t2 = (i - secondTapStart) / (float)sampleRate;
                    float freq2 = (rootHz * 1.498f) * (1f + 0.15f * Mathf.Exp(-120f * t2));
                    phase2 += 2f * Mathf.PI * freq2 / sampleRate;
                    float attack2 = Mathf.Clamp01(t2 / 0.002f);
                    tap2 = 0.65f * (Mathf.Sin(phase2) + 0.20f * Mathf.Sin(phase2 * 2f)) * attack2 * Mathf.Exp(-34f * t2);
                }

                data[i] = tap1 + tap2;
            }

            ApplyFadeIn(data, sampleRate, 0.0012f);
            ApplyFadeOut(data, sampleRate, 0.012f);
            NormalisePeak(data, 0.72f);
            SoftClip(data);
            return ToClip("AnatomyUiClick", data, 1, sampleRate);
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
        /// A warm, modern downward two-note marimba/glass settle for returning and resetting.
        /// </summary>
        public static AudioClip CreateThud(float frequencyHz = 392f, float durationSeconds = 0.24f) {
            int sampleRate = GetSampleRate();
            int frames = Mathf.Max(1, Mathf.RoundToInt(sampleRate * durationSeconds));
            float[] data = new float[frames];

            float highNote = Mathf.Clamp(frequencyHz * 1.8f, 340f, 520f);
            float lowNote = highNote * 0.749f; // Descending fourth
            int secondStart = Mathf.RoundToInt(0.032f * sampleRate);

            for (int i = 0; i < frames; i++) {
                float t = i / (float)sampleRate;
                float tone1 = Mathf.Sin(2f * Mathf.PI * highNote * t) * Mathf.Exp(-24f * t);
                float tone2 = 0f;
                if (i >= secondStart) {
                    float t2 = (i - secondStart) / (float)sampleRate;
                    float attack2 = Mathf.Clamp01(t2 / 0.003f);
                    tone2 = 0.85f * (Mathf.Sin(2f * Mathf.PI * lowNote * t2)
                        + 0.22f * Mathf.Sin(2f * Mathf.PI * (lowNote * 2f) * t2))
                        * attack2 * Mathf.Exp(-16f * t2);
                }

                data[i] = tone1 * 0.55f + tone2;
            }

            ApplyFadeIn(data, sampleRate, 0.002f);
            ApplyFadeOut(data, sampleRate, 0.02f);
            NormalisePeak(data, 0.68f);
            SoftClip(data);
            return ToClip("AnatomyThud", data, 1, sampleRate);
        }

        /// <summary>
        /// A seamless idle loop for a modern turbocharged four, which is what an S90 has: a
        /// two-litre inline-four in every variant it was sold with.
        ///
        /// Built from the firing rate rather than from an engine note. A four-stroke four fires
        /// twice per revolution, so an idle around 780 rpm puts a combustion pulse every 26 Hz, and
        /// that pulse train is what the ear identifies as a particular engine.
        ///
        /// What makes it read as *this* engine rather than a generic one is the balance. A large
        /// saloon's exhaust is muffled and low: the note is mostly second order with a soft attack,
        /// the harmonics fall away as 1/n squared rather than 1/n, and there is very little rasp.
        /// Underneath sits a component at half the firing rate - once per crank revolution - which
        /// is where the low beat of a four at idle comes from, and a small per-cylinder variation,
        /// because four cylinders are never quite identical and a perfectly even pulse train sounds
        /// synthetic within a second of hearing it.
        ///
        /// The noise is generated once per firing cycle and replayed for every cycle, which is both
        /// truer - each combustion event really is much like the last - and what makes the clip
        /// loop without a click. The firing rate is snapped so the loop holds a whole multiple of
        /// four cycles, which is what keeps the half-rate component and the per-cylinder table
        /// periodic across the loop point as well.
        /// </summary>
        public static AudioClip CreateEngineIdle(float firingHz = 26f, float loopSeconds = 2f) {
            int sampleRate = GetSampleRate();
            float snapped = SnapToLoop(firingHz, loopSeconds, 4);
            int frames = Mathf.Max(1, Mathf.RoundToInt(sampleRate * loopSeconds));
            float[] data = new float[frames * 2];

            int cycleFrames = Mathf.Max(1, Mathf.RoundToInt(sampleRate / snapped));
            float[] cycleNoise = new float[cycleFrames];
            System.Random random = new System.Random(20260929);
            float filtered = 0f;
            for (int i = 0; i < cycleFrames; i++) {
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                // One-pole low pass, tighter than a naturally aspirated engine's would be: a
                // silenced turbo four has almost no top end, and unfiltered noise reads as hiss.
                filtered += (white - filtered) * 0.06f;
                cycleNoise[i] = filtered;
            }

            // Four cylinders, never quite matched. Averages to 1, so the level is unaffected.
            float[] cylinderGain = new float[] { 1.06f, 0.95f, 1.02f, 0.97f };

            for (int i = 0; i < frames; i++) {
                float t = i / (float)sampleRate;
                float phase = t * snapped;
                float withinCycle = phase - Mathf.Floor(phase);
                int cylinder = Mathf.FloorToInt(phase) & 3;

                // Softer than a crack: this engine is behind a silencer and two metres of pipe.
                float pulse = Mathf.Exp(-withinCycle * 4.5f) * cylinderGain[cylinder];

                float body = 0f;
                for (int harmonic = 1; harmonic <= 5; harmonic++) {
                    body += Mathf.Sin(2f * Mathf.PI * snapped * harmonic * t) / (harmonic * harmonic);
                }

                // Once per crank revolution: the low beat under the firing note.
                float beat = Mathf.Sin(2f * Mathf.PI * snapped * 0.5f * t) * 0.5f;

                float rasp = cycleNoise[i % cycleFrames];
                float sample = (body * 0.46f + beat * 0.30f + rasp * 0.24f) * pulse;

                // A slow wobble, because a real idle never holds perfectly steady.
                sample *= 1f + 0.05f * Mathf.Sin(2f * Mathf.PI * SnapToLoop(1.5f, loopSeconds) * t);

                data[i * 2] = sample;
                data[i * 2 + 1] = sample * 0.94f;
            }

            NormalisePeak(data, 0.5f);
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

        /// <summary>
        /// As above, but rounded to a whole multiple of <paramref name="multiple"/> cycles.
        ///
        /// Anything in the waveform that repeats over several firing cycles - the half-rate beat
        /// under a four's idle, the per-cylinder variation - is only periodic across the loop point
        /// if the loop holds a whole number of those groups, not just a whole number of cycles.
        /// </summary>
        private static float SnapToLoop(float frequency, float loopSeconds, int multiple) {
            if (loopSeconds <= 0f || multiple <= 1) {
                return SnapToLoop(frequency, loopSeconds);
            }

            float groups = Mathf.Max(1f, Mathf.Round(frequency * loopSeconds / multiple));
            return groups * multiple / loopSeconds;
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
