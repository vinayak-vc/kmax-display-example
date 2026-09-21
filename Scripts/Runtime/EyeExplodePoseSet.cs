using System;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// The two extreme local positions of one animated part of the eye.
    /// </summary>
    [Serializable]
    public class EyePartPose {
        [SerializeField] private string partPath;
        [SerializeField] private Vector3 closedPosition;
        [SerializeField] private Vector3 openPosition;

        public EyePartPose(string partPath, Vector3 closedPosition, Vector3 openPosition) {
            this.partPath = partPath;
            this.closedPosition = closedPosition;
            this.openPosition = openPosition;
        }

        public string PartPath {
            get { return partPath; }
        }

        public Vector3 ClosedPosition {
            get { return closedPosition; }
        }

        public Vector3 OpenPosition {
            get { return openPosition; }
        }
    }

    /// <summary>
    /// Assembled and exploded poses baked out of the model's imported animation clips.
    /// The clips themselves are a continuous pulse rather than a one-shot explode, so the two
    /// extremes are baked and interpolated instead of replayed. Regenerate with
    /// Kmax/Eye Anatomy/Bake Explode Poses.
    /// </summary>
    [CreateAssetMenu(fileName = "EyeExplodePoses", menuName = "Kmax Display Example/Eye Explode Pose Set")]
    public class EyeExplodePoseSet : ScriptableObject {
        [SerializeField] private EyePartPose[] poses = new EyePartPose[0];

        public EyePartPose[] Poses {
            get { return poses; }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Used only by the bake tool. The runtime view of this asset stays read-only.
        /// </summary>
        public void SetPoses(EyePartPose[] value) {
            poses = value;
        }
#endif
    }
}