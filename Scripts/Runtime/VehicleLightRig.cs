using System;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// One switchable lamp on the car: the meshes that glow, how brightly, and any real light that
    /// should be cast alongside them.
    /// </summary>
    [Serializable]
    public class VehicleLightChannel {
        [SerializeField] private string channelName;
        [SerializeField, Tooltip("Meshes whose emission this channel drives.")]
        private Renderer[] renderers = new Renderer[0];
        [SerializeField, Tooltip("Real lights switched with the channel. Optional - most lamps only glow.")]
        private Light[] lights = new Light[0];
        [SerializeField, ColorUsage(false, true)] private Color emission = Color.white;
        [SerializeField, Tooltip("Blink rather than hold, for the indicators.")]
        private bool blinks;

        public VehicleLightChannel(string channelName, Renderer[] renderers, Light[] lights, Color emission, bool blinks) {
            this.channelName = channelName;
            this.renderers = renderers;
            this.lights = lights;
            this.emission = emission;
            this.blinks = blinks;
        }

        public string ChannelName {
            get { return channelName; }
        }

        public Renderer[] Renderers {
            get { return renderers; }
        }

        public Light[] Lights {
            get { return lights; }
        }

        public Color Emission {
            get { return emission; }
        }

        public bool Blinks {
            get { return blinks; }
        }
    }

    /// <summary>
    /// Switches the car's lamps on and off.
    ///
    /// The model has every lamp built as its own mesh - headlight emitters, running light glass,
    /// each taillight reflector, the indicators - so nothing has to be faked. What it does not have
    /// is any of them lit: they ship with ordinary materials and no light sources at all.
    ///
    /// Emission is driven on per-renderer material instances rather than on the shared assets. Many
    /// of these meshes share one material - every taillight frame uses <c>Taillight</c> - so
    /// writing to the asset would light the whole rear of the car whenever any one channel came on,
    /// and would leave the project's materials modified after play.
    /// </summary>
    public class VehicleLightRig : MonoBehaviour {
        [SerializeField] private VehicleLightChannel[] channels = new VehicleLightChannel[0];
        [SerializeField, Range(0.5f, 6f), Tooltip("Blinks per second for channels that blink.")]
        private float blinkRate = 1.4f;
        [SerializeField, Range(0.02f, 2f), Tooltip("Seconds a lamp takes to reach full brightness. " +
            "Filament lamps do not switch instantly and an instant change reads as a glitch.")]
        private float riseDuration = 0.16f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private Material[][] instances;
        /// <summary>
        /// Each light's authored intensity, captured before anything is switched on.
        ///
        /// The rig used to impose a single figure per light type, which is wrong twice over: a
        /// headlight and a courtesy lamp are nothing like each other, and the right number depends
        /// entirely on how big the model is in the scene. An interior lamp inside a car 0.26 m long
        /// is a few centimetres from every surface it lights, and inverse-square falloff turns
        /// anything near 1 into a blowtorch - which is exactly what it did once the glass stopped
        /// being opaque and let the light out.
        /// </summary>
        private float[][] fullIntensity;
        private float[] level;
        private float[] target;
        private bool[] requested;
        private bool isResolved;

        public int ChannelCount {
            get { return channels.Length; }
        }

        private void Awake() {
            Resolve();
        }

        private void Update() {
            if (!isResolved) {
                return;
            }

            // One shared phase, so every blinking lamp on the car flashes together.
            bool blinkOn = Mathf.Repeat(Time.time * blinkRate, 1f) < 0.5f;

            for (int i = 0; i < channels.Length; i++) {
                target[i] = requested[i] && (!channels[i].Blinks || blinkOn) ? 1f : 0f;

                if (Mathf.Approximately(level[i], target[i])) {
                    continue;
                }

                level[i] = Mathf.MoveTowards(level[i], target[i], Time.deltaTime / riseDuration);
                Apply(i);
            }
        }

        /// <summary>
        /// Index of a channel by name, or -1. Names rather than indices, because the build step and
        /// the ignition both refer to lamps by what they are.
        /// </summary>
        public int IndexOf(string channelName) {
            for (int i = 0; i < channels.Length; i++) {
                if (channels[i].ChannelName == channelName) {
                    return i;
                }
            }

            return -1;
        }

        public bool IsOn(int index) {
            if (index < 0 || index >= channels.Length) {
                return false;
            }

            return requested[index];
        }

        public void SetChannel(int index, bool on) {
            Resolve();

            if (index < 0 || index >= channels.Length) {
                Debug.LogError($"{nameof(VehicleLightRig)} was asked for light channel {index}, which does not exist.", this);
                return;
            }

            requested[index] = on;
        }

        public void SetChannel(string channelName, bool on) {
            int index = IndexOf(channelName);
            if (index < 0) {
                Debug.LogError($"{nameof(VehicleLightRig)} has no light channel called '{channelName}'.", this);
                return;
            }

            SetChannel(index, on);
        }

        public void SetAll(bool on) {
            Resolve();

            for (int i = 0; i < channels.Length; i++) {
                requested[i] = on;
            }
        }

        private void Apply(int index) {
            Material[] materials = instances[index];
            Color colour = channels[index].Emission * level[index];

            for (int i = 0; i < materials.Length; i++) {
                if (materials[i] == null) {
                    continue;
                }

                materials[i].SetColor(EmissionColorId, colour);
            }

            Light[] lights = channels[index].Lights;
            float[] full = fullIntensity[index];

            for (int i = 0; i < lights.Length; i++) {
                if (lights[i] == null) {
                    continue;
                }

                lights[i].enabled = level[index] > 0.001f;
                lights[i].intensity = full[i] * level[index];
            }
        }

        /// <summary>
        /// Takes a private copy of every material the channels touch and turns emission on in each.
        ///
        /// The keyword matters as much as the colour: URP compiles the emission out of the shader
        /// variant unless <c>_EMISSION</c> is enabled, so setting <c>_EmissionColor</c> on a
        /// material that shipped without emission does nothing at all.
        /// </summary>
        private void Resolve() {
            if (isResolved) {
                return;
            }

            isResolved = true;
            instances = new Material[channels.Length][];
            fullIntensity = new float[channels.Length][];
            level = new float[channels.Length];
            target = new float[channels.Length];
            requested = new bool[channels.Length];

            for (int i = 0; i < channels.Length; i++) {
                Renderer[] renderers = channels[i].Renderers;
                System.Collections.Generic.List<Material> materials = new System.Collections.Generic.List<Material>();

                for (int j = 0; j < renderers.Length; j++) {
                    if (renderers[j] == null) {
                        continue;
                    }

                    // .materials instantiates, which is what is wanted here - the copies are this
                    // renderer's alone and are discarded with the scene.
                    Material[] owned = renderers[j].materials;
                    for (int k = 0; k < owned.Length; k++) {
                        if (owned[k] == null) {
                            continue;
                        }

                        owned[k].EnableKeyword("_EMISSION");
                        owned[k].globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                        owned[k].SetColor(EmissionColorId, Color.black);
                        materials.Add(owned[k]);
                    }
                }

                instances[i] = materials.ToArray();

                Light[] lights = channels[i].Lights;
                fullIntensity[i] = new float[lights.Length];
                for (int j = 0; j < lights.Length; j++) {
                    if (lights[j] == null) {
                        continue;
                    }

                    fullIntensity[i][j] = lights[j].intensity;
                    lights[j].enabled = false;
                }
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Used only by the build step. The runtime view of the channels stays read-only.
        /// </summary>
        public void SetChannels(VehicleLightChannel[] value) {
            channels = value;
        }
#endif
    }
}
