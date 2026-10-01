using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Measures the geometry of one eye part. Hotspot markers are parented to the part they
    /// label, so they have to be excluded or they inflate the part they are measuring - a
    /// marker floating in front of the lens more than doubles its apparent depth.
    /// </summary>
    public static class EyePartBounds {
        public static bool TryGet(Transform part, out Bounds bounds) {
            bounds = new Bounds(part.position, Vector3.zero);
            Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true);
            bool found = false;

            for (int i = 0; i < renderers.Length; i++) {
                if (IsMarker(renderers[i])) {
                    continue;
                }

                if (!found) {
                    bounds = renderers[i].bounds;
                    found = true;
                    continue;
                }

                bounds.Encapsulate(renderers[i].bounds);
            }

            return found;
        }

        /// <summary>
        /// Measures the same geometry in <paramref name="space"/>'s own axes rather than the
        /// world's.
        ///
        /// A world-space box is axis-aligned to the world, so it grows the moment the model is
        /// turned: the car rests at 45 degrees and its world box measures 0.25 x 0.25 m in plan
        /// where the car itself is 0.10 x 0.26. Anything that means "the model's own silhouette"
        /// has to measure in the model's axes, or it describes a box with the corners empty.
        ///
        /// Every corner of every renderer is transformed rather than the box centre alone, because
        /// a part may be rotated relative to the space being measured in - on this car the door
        /// halves hang off hinges of their own.
        /// </summary>
        public static bool TryGetLocal(Transform part, Transform space, out Bounds bounds) {
            bounds = new Bounds(Vector3.zero, Vector3.zero);
            Renderer[] renderers = part.GetComponentsInChildren<Renderer>(true);
            Matrix4x4 toSpace = space.worldToLocalMatrix;
            bool found = false;

            for (int i = 0; i < renderers.Length; i++) {
                if (IsMarker(renderers[i])) {
                    continue;
                }

                Matrix4x4 toLocal = toSpace * renderers[i].localToWorldMatrix;
                Bounds local = renderers[i].localBounds;

                for (int corner = 0; corner < 8; corner++) {
                    Vector3 offset = new Vector3(
                        (corner & 1) == 0 ? -local.extents.x : local.extents.x,
                        (corner & 2) == 0 ? -local.extents.y : local.extents.y,
                        (corner & 4) == 0 ? -local.extents.z : local.extents.z);
                    Vector3 point = toLocal.MultiplyPoint3x4(local.center + offset);

                    if (!found) {
                        bounds = new Bounds(point, Vector3.zero);
                        found = true;
                        continue;
                    }

                    bounds.Encapsulate(point);
                }
            }

            return found;
        }

        public static bool IsMarker(Renderer renderer) {
            return renderer.GetComponentInParent<EyeHotspot>() != null;
        }
    }
}