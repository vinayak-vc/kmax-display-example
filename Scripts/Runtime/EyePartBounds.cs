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

        public static bool IsMarker(Renderer renderer) {
            return renderer.GetComponentInParent<EyeHotspot>() != null;
        }
    }
}