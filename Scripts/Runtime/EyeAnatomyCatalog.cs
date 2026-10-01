using System;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// One labelled, clickable part of the eye model.
    /// </summary>
    [Serializable]
    public class EyePartDefinition {
        [SerializeField] private string displayName;
        [SerializeField, TextArea(1, 3)] private string description;
        [SerializeField, Tooltip("Transform path of the part, relative to the model root.")]
        private string partPath;
        [SerializeField, Tooltip("Leave the rest of the model opaque while this part is focused. " +
            "Off by default, which fades everything else - right for a part that would otherwise be " +
            "buried, wrong for one that is already on the outside. On a car, ghosting the bodywork " +
            "to show a wheel throws away the paint the viewer just chose.")]
        private bool keepOthersSolid;

        public EyePartDefinition(string displayName, string description, string partPath)
            : this(displayName, description, partPath, false) {
        }

        public EyePartDefinition(string displayName, string description, string partPath, bool keepOthersSolid) {
            this.displayName = displayName;
            this.description = description;
            this.partPath = partPath;
            this.keepOthersSolid = keepOthersSolid;
        }

        public string DisplayName {
            get { return displayName; }
        }

        public string Description {
            get { return description; }
        }

        public string PartPath {
            get { return partPath; }
        }

        /// <summary>
        /// True when focusing this part should leave the rest of the model as it is.
        ///
        /// Serialised as the negative - "keep solid" rather than "ghost" - so that catalogues
        /// written before this existed deserialise it as false and keep fading, which is what they
        /// have always done. A bool added to a serialised class comes back false whatever you would
        /// have liked the default to be.
        /// </summary>
        public bool KeepOthersSolid {
            get { return keepOthersSolid; }
        }
    }

    /// <summary>
    /// The labels and descriptions shown when a hotspot is selected. Editing this asset is the
    /// only thing needed to correct a label or reword a description - no code change.
    /// </summary>
    [CreateAssetMenu(fileName = "EyeAnatomyCatalog", menuName = "Kmax Display Example/Eye Anatomy Catalog")]
    public class EyeAnatomyCatalog : ScriptableObject {
        [SerializeField] private EyePartDefinition[] parts = new EyePartDefinition[0];

        public EyePartDefinition[] Parts {
            get { return parts; }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Used only by editor tooling. The runtime view of this asset stays read-only.
        /// </summary>
        public void SetParts(EyePartDefinition[] value) {
            parts = value;
        }
#endif
    }
}