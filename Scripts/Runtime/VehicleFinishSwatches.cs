using System;
using System.Collections.Generic;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// One material a set of swatches repaints, and what each swatch puts on it.
    ///
    /// Both arrays are indexed by swatch and either may be left empty: the car's paint is a colour
    /// with no texture, and its trim is a texture with no colour.
    /// </summary>
    [Serializable]
    public class VehicleFinishTarget {
        [SerializeField, Tooltip("The shared material to match on. Every renderer using it is repainted.")]
        private Material sourceMaterial;
        [SerializeField, Tooltip("Base map per swatch, in the same order as the swatch names. " +
            "Empty leaves the map alone; a null entry leaves it alone for that swatch only.")]
        private Texture2D[] baseMaps = new Texture2D[0];
        [SerializeField, Tooltip("Base colour per swatch, in the same order as the swatch names. " +
            "Empty leaves the colour alone.")]
        private Color[] baseColors = new Color[0];

        public VehicleFinishTarget(Material sourceMaterial, Texture2D[] baseMaps, Color[] baseColors) {
            this.sourceMaterial = sourceMaterial;
            this.baseMaps = baseMaps;
            this.baseColors = baseColors;
        }

        public Material SourceMaterial {
            get { return sourceMaterial; }
        }

        public Texture2D[] BaseMaps {
            get { return baseMaps; }
        }

        public Color[] BaseColors {
            get { return baseColors; }
        }
    }

    /// <summary>
    /// Swaps a model between finishes - the car's paint, and its interior trim - by rewriting the
    /// base map and base colour of a named set of materials.
    ///
    /// Presented through <see cref="IExhibitMachinery"/> so that <see cref="ExhibitFeaturePanel"/>
    /// drives it unchanged: a finish is exactly the shape that interface already describes, a run
    /// of mutually exclusive variants with a name each. One component serves both features, once
    /// per swatch set, because the only difference between paint and trim is the data.
    ///
    /// <see cref="SetTransparent"/> is a deliberate no-op. A finish has no see-through state, and
    /// the panels built for these swatches are given no transparency button, so it is never called.
    /// </summary>
    public class VehicleFinishSwatches : MonoBehaviour, IExhibitMachinery {
        [SerializeField, Tooltip("Root of the model whose renderers are repainted.")]
        private Transform modelRoot;
        [SerializeField, Tooltip("Optional. Told to re-read its captured materials once the private " +
            "copies are in place, or the first focus would restore the originals over them.")]
        private EyeFocusView focusView;
        [SerializeField, Tooltip("One per swatch, short enough for a button.")]
        private string[] swatchNames = new string[0];
        [SerializeField] private VehicleFinishTarget[] targets = new VehicleFinishTarget[0];

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// Suffix Unity appends when a renderer's materials are instantiated.
        /// </summary>
        private const string InstanceSuffix = " (Instance)";

        private List<Material>[] instances;
        private int selected;
        private bool isResolved;

        public int VariationCount {
            get { return swatchNames.Length; }
        }

        /// <summary>
        /// Which swatch is showing.
        /// </summary>
        public int SelectedSwatch {
            get { return selected; }
        }

        private void Awake() {
            Resolve();
        }

        public string GetVariationName(int index) {
            if (index < 0 || index >= swatchNames.Length) {
                Debug.LogError($"{nameof(VehicleFinishSwatches)} on '{name}' was asked for swatch {index}, " +
                    $"which does not exist.", this);
                return string.Empty;
            }

            return swatchNames[index];
        }

        public void ApplyVariation(int index) {
            Resolve();

            if (index < 0 || index >= swatchNames.Length) {
                Debug.LogError($"{nameof(VehicleFinishSwatches)} on '{name}' was asked to apply swatch " +
                    $"{index}, which does not exist.", this);
                return;
            }

            selected = index;

            for (int i = 0; i < targets.Length; i++) {
                Texture2D map = index < targets[i].BaseMaps.Length ? targets[i].BaseMaps[index] : null;
                bool hasColor = index < targets[i].BaseColors.Length;
                Color colour = hasColor ? targets[i].BaseColors[index] : Color.white;
                List<Material> owned = instances[i];

                for (int j = 0; j < owned.Count; j++) {
                    if (map != null && owned[j].HasProperty(BaseMapId)) {
                        owned[j].SetTexture(BaseMapId, map);
                    }

                    if (hasColor && owned[j].HasProperty(BaseColorId)) {
                        owned[j].SetColor(BaseColorId, colour);
                    }
                }
            }
        }

        /// <summary>
        /// Nothing to do: a finish has no casing to see through. Present because
        /// <see cref="IExhibitMachinery"/> declares it, and never called - the swatch panels are
        /// built without a transparency button.
        /// </summary>
        public void SetTransparent(bool transparent) {
        }

        /// <summary>
        /// Takes a private copy of every material the swatches touch.
        ///
        /// Written to copies rather than to the shared assets because the project's own material
        /// files would otherwise be left repainted after play, and because one material covers many
        /// renderers - the whole body shares <c>Car Paint</c> - so the asset is the wrong place to
        /// record a choice the exhibit makes.
        ///
        /// The copies come from <c>Renderer.materials</c>, which instantiates on first access and
        /// returns those same copies afterwards. <see cref="VehicleLightRig"/> reaches for its lamp
        /// materials the same way, so the two components end up writing to one set rather than each
        /// replacing the other's - the cabin lamp and the interior trim share the <c>Shell</c>
        /// material on <c>CeilingConsole</c>, and either order works.
        ///
        /// Which target a slot belongs to is matched by material name rather than by reference, for
        /// the same reason: whichever component instantiated first, the renderer now reports a copy
        /// and a reference test against the source asset would miss it.
        /// </summary>
        private void Resolve() {
            if (isResolved) {
                return;
            }

            isResolved = true;
            instances = new List<Material>[targets.Length];
            for (int i = 0; i < targets.Length; i++) {
                instances[i] = new List<Material>();
            }

            if (modelRoot == null) {
                Debug.LogError($"{nameof(VehicleFinishSwatches)} on '{name}' has no {nameof(modelRoot)} " +
                    "assigned; no swatch can be applied.", this);
                return;
            }

            Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
            bool replacedAny = false;

            for (int i = 0; i < renderers.Length; i++) {
                Material[] shared = renderers[i].sharedMaterials;
                Material[] owned = null;

                for (int j = 0; j < shared.Length; j++) {
                    int target = IndexOfTarget(shared[j]);
                    if (target < 0) {
                        continue;
                    }

                    if (owned == null) {
                        owned = renderers[i].materials;
                        replacedAny = true;
                    }

                    if (owned[j] != null) {
                        instances[target].Add(owned[j]);
                    }
                }
            }

            for (int i = 0; i < targets.Length; i++) {
                if (targets[i].SourceMaterial == null) {
                    Debug.LogError($"{nameof(VehicleFinishSwatches)} on '{name}' has a target with no " +
                        "source material; its swatches do nothing.", this);
                    continue;
                }

                if (instances[i].Count == 0) {
                    Debug.LogWarning($"{nameof(VehicleFinishSwatches)} on '{name}' found no renderer using " +
                        $"'{targets[i].SourceMaterial.name}' under '{modelRoot.name}'.", this);
                }
            }

            // The focus view captured each renderer's materials in its own Awake. Whatever it
            // captured there, the renderers now carry copies, so it has to be told - otherwise
            // ghosting a part and coming back out would restore the originals over every swatch.
            if (replacedAny && focusView != null) {
                focusView.RefreshCachedMaterials();
            }
        }

        private int IndexOfTarget(Material material) {
            if (material == null) {
                return -1;
            }

            string sourceName = SourceNameOf(material);
            for (int i = 0; i < targets.Length; i++) {
                Material source = targets[i].SourceMaterial;
                if (source != null && source.name == sourceName) {
                    return i;
                }
            }

            return -1;
        }

        private static string SourceNameOf(Material material) {
            string materialName = material.name;
            if (!materialName.EndsWith(InstanceSuffix, StringComparison.Ordinal)) {
                return materialName;
            }

            return materialName.Substring(0, materialName.Length - InstanceSuffix.Length);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Used only by the build step. The runtime view of the swatches stays read-only.
        /// </summary>
        public void SetSwatches(string[] names, VehicleFinishTarget[] value) {
            swatchNames = names;
            targets = value;
        }
#endif
    }
}
