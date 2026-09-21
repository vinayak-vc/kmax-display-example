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

        public EyePartDefinition(string displayName, string description, string partPath) {
            this.displayName = displayName;
            this.description = description;
            this.partPath = partPath;
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