using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Gives the eye's meshes colliders, so the stylus ray has something to land on and each
    /// structure can be picked directly rather than only through its badge.
    ///
    /// The model arrives from glTFast as mesh filters and renderers with no colliders at all,
    /// which is why the stylus beam used to pass straight through the eye.
    /// </summary>
    public static class EyePartColliders {
        /// <summary>
        /// What happened while fitting colliders to a part, so the caller can report once for the
        /// whole model rather than eighteen times.
        /// </summary>
        public struct Result {
            public int MeshColliders;
            public int BoxFallbacks;
            public int Skipped;

            public void Add(Result other) {
                MeshColliders += other.MeshColliders;
                BoxFallbacks += other.BoxFallbacks;
                Skipped += other.Skipped;
            }
        }

        /// <summary>
        /// Ensures every mesh under <paramref name="part"/> has a collider.
        ///
        /// A mesh collider is used where the mesh allows it, because per-triangle accuracy is what
        /// makes pointing at a thin structure like the optic nerve feel true. Where the source mesh
        /// was imported without read/write access a mesh collider cannot be baked at runtime, so a
        /// box sized to the renderer's own bounds stands in - less precise, but it still stops the
        /// beam on the geometry instead of letting it sail through.
        /// </summary>
        /// <param name="part">Root of the structure to fit.</param>
        /// <param name="convex">Convex mesh colliders. Leave false: raycasting does not need it,
        /// and convex hulls of a hollow shell like the sclera bear no resemblance to the shape.</param>
        public static Result Fit(Transform part, bool convex = false) {
            Result result = new Result();

            MeshFilter[] filters = part.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++) {
                MeshFilter filter = filters[i];

                // Badges carry their own sphere collider and must not gain a second one, or the
                // beam would hit the badge quad rather than the badge's own pick volume.
                if (filter.GetComponentInParent<EyeHotspot>() != null) {
                    continue;
                }

                if (filter.GetComponent<Collider>() != null) {
                    continue;
                }

                Mesh mesh = filter.sharedMesh;
                if (mesh == null) {
                    result.Skipped++;
                    continue;
                }

                if (mesh.isReadable) {
                    MeshCollider meshCollider = filter.gameObject.AddComponent<MeshCollider>();
                    meshCollider.sharedMesh = mesh;
                    meshCollider.convex = convex;
                    result.MeshColliders++;
                    continue;
                }

                if (!TryAddBoxFallback(filter)) {
                    result.Skipped++;
                    continue;
                }

                result.BoxFallbacks++;
            }

            return result;
        }

        /// <summary>
        /// Logs one summary for the whole model, naming the fix if any part had to fall back.
        /// </summary>
        public static void Report(Result result, Object context) {
            if (result.BoxFallbacks == 0 && result.Skipped == 0) {
                return;
            }

            Debug.LogWarning(
                $"{nameof(EyePartColliders)} fitted {result.MeshColliders} mesh collider(s), " +
                $"{result.BoxFallbacks} box fallback(s) and skipped {result.Skipped}. " +
                "Box fallbacks mean those meshes are not marked Read/Write in the model importer, " +
                "so the stylus will hit a bounding box rather than the surface. Enable Read/Write " +
                "on Model/EyeAnatomy.glb to get exact hits.", context);
        }

        private static bool TryAddBoxFallback(MeshFilter filter) {
            Renderer renderer = filter.GetComponent<Renderer>();
            if (renderer == null) {
                return false;
            }

            // localBounds is the mesh's own bounds, so the box tracks the part as it is moved and
            // scaled by the explode and focus views without needing to be refitted.
            Bounds local = filter.sharedMesh.bounds;
            BoxCollider box = filter.gameObject.AddComponent<BoxCollider>();
            box.center = local.center;
            box.size = local.size;
            return true;
        }
    }
}
