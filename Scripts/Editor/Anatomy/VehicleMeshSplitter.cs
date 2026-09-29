using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample.Editor {
    /// <summary>
    /// Cuts a mirrored body panel into its left and right halves and saves each as its own mesh
    /// asset.
    ///
    /// The Volvo ships every panel that exists on both sides of the car as a single mesh: one
    /// <c>Door Front</c> 1.85 m wide holding both front doors, one <c>MIrror</c> holding both
    /// mirrors, and so on. A door cannot be hinged in that state - rotating the mesh swings both
    /// doors as one rigid body and the left one sweeps through the cabin.
    ///
    /// Splitting is exact rather than approximate here, because nothing straddles the centreline:
    /// the nearest triangle to x = 0 is 5.7 mm away on the body panels and 43 mm on the door
    /// interior cards. Sorting whole triangles by the sign of their centroid's x therefore never
    /// cuts an edge and never needs a new vertex.
    ///
    /// The halves are not required to match. Most of these panels do divide into two equal
    /// triangle counts, but the front door's interior card does not - the driver's door carries
    /// window and mirror switchgear the passenger's does not - and that cut is no less exact. The
    /// clearance is what makes it safe, not the symmetry.
    ///
    /// The source FBX is not modified. Both halves are new assets, so the import can be left alone
    /// and a re-run simply overwrites them.
    /// </summary>
    public static class VehicleMeshSplitter {
        /// <summary>
        /// Which half of a panel a mesh represents. Left and right are the car's own, which is why
        /// they are read off the sign of x rather than named for the viewer.
        /// </summary>
        public enum Side {
            Left,
            Right
        }

        /// <summary>
        /// Builds one side of <paramref name="source"/>.
        ///
        /// Every vertex stream the source carries is carried across, and the submesh structure is
        /// preserved exactly - these panels have up to five submeshes each (paint, gloss black,
        /// chrome, rubber, plastic) and the renderer's material array is indexed by submesh, so
        /// dropping or reordering one would repaint the door.
        /// </summary>
        public static Mesh BuildHalf(Mesh source, Side side) {
            if (source == null) {
                Debug.LogError($"{nameof(VehicleMeshSplitter)} was asked to split a null mesh.");
                return null;
            }

            if (!source.isReadable) {
                Debug.LogError($"{nameof(VehicleMeshSplitter)} cannot split '{source.name}' because it is not " +
                    "marked Read/Write in the model importer.");
                return null;
            }

            Vector3[] sourceVertices = source.vertices;
            Vector3[] sourceNormals = source.normals;
            Vector4[] sourceTangents = source.tangents;
            Vector2[] sourceUv = source.uv;
            Vector2[] sourceUv2 = source.uv2;
            Color[] sourceColors = source.colors;

            bool hasNormals = sourceNormals.Length == sourceVertices.Length;
            bool hasTangents = sourceTangents.Length == sourceVertices.Length;
            bool hasUv = sourceUv.Length == sourceVertices.Length;
            bool hasUv2 = sourceUv2.Length == sourceVertices.Length;
            bool hasColors = sourceColors.Length == sourceVertices.Length;

            // -1 until a vertex is first used by a kept triangle, then its index in the new mesh.
            int[] remap = new int[sourceVertices.Length];
            for (int i = 0; i < remap.Length; i++) {
                remap[i] = -1;
            }

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector4> tangents = new List<Vector4>();
            List<Vector2> uv = new List<Vector2>();
            List<Vector2> uv2 = new List<Vector2>();
            List<Color> colors = new List<Color>();
            List<int[]> submeshes = new List<int[]>();

            for (int submesh = 0; submesh < source.subMeshCount; submesh++) {
                int[] triangles = source.GetTriangles(submesh);
                List<int> kept = new List<int>();

                for (int i = 0; i < triangles.Length; i += 3) {
                    int a = triangles[i];
                    int b = triangles[i + 1];
                    int c = triangles[i + 2];

                    float centroidX = (sourceVertices[a].x + sourceVertices[b].x + sourceVertices[c].x) / 3f;
                    Side triangleSide = centroidX < 0f ? Side.Left : Side.Right;
                    if (triangleSide != side) {
                        continue;
                    }

                    for (int corner = 0; corner < 3; corner++) {
                        int index = triangles[i + corner];

                        if (remap[index] < 0) {
                            remap[index] = vertices.Count;
                            vertices.Add(sourceVertices[index]);

                            if (hasNormals) {
                                normals.Add(sourceNormals[index]);
                            }

                            if (hasTangents) {
                                tangents.Add(sourceTangents[index]);
                            }

                            if (hasUv) {
                                uv.Add(sourceUv[index]);
                            }

                            if (hasUv2) {
                                uv2.Add(sourceUv2[index]);
                            }

                            if (hasColors) {
                                colors.Add(sourceColors[index]);
                            }
                        }

                        kept.Add(remap[index]);
                    }
                }

                submeshes.Add(kept.ToArray());
            }

            Mesh half = new Mesh();
            half.name = source.name + (side == Side.Left ? " L" : " R");

            // Set before the vertices, or a mesh that needs 32-bit indices is silently truncated.
            half.indexFormat = vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;

            half.SetVertices(vertices);

            if (hasNormals) {
                half.SetNormals(normals);
            }

            if (hasTangents) {
                half.SetTangents(tangents);
            }

            if (hasUv) {
                half.SetUVs(0, uv);
            }

            if (hasUv2) {
                half.SetUVs(1, uv2);
            }

            if (hasColors) {
                half.SetColors(colors);
            }

            half.subMeshCount = submeshes.Count;
            for (int submesh = 0; submesh < submeshes.Count; submesh++) {
                half.SetTriangles(submeshes[submesh], submesh);
            }

            half.RecalculateBounds();

            if (!hasNormals) {
                half.RecalculateNormals();
            }

            return half;
        }

        /// <summary>
        /// Splits one panel and writes both halves into <paramref name="folder"/>, returning them
        /// through the out parameters. Existing assets at those paths are overwritten in place so
        /// anything already referencing them keeps its reference.
        /// </summary>
        public static bool SplitToAssets(Mesh source, string folder, out Mesh left, out Mesh right) {
            left = null;
            right = null;

            Mesh builtLeft = BuildHalf(source, Side.Left);
            Mesh builtRight = BuildHalf(source, Side.Right);

            if (builtLeft == null || builtRight == null) {
                return false;
            }

            if (builtLeft.vertexCount == 0 || builtRight.vertexCount == 0) {
                Debug.LogError($"{nameof(VehicleMeshSplitter)} split '{source.name}' into an empty half " +
                    $"({builtLeft.vertexCount} and {builtRight.vertexCount} vertices). That panel is not " +
                    "mirrored across the centreline and must not be split.");
                return false;
            }

            left = WriteOrReplace(builtLeft, folder + "/" + SanitiseName(source.name) + " L.asset");
            right = WriteOrReplace(builtRight, folder + "/" + SanitiseName(source.name) + " R.asset");
            return left != null && right != null;
        }

        /// <summary>
        /// Writes a mesh to a path, copying into the existing asset when there is one so that
        /// scenes and prefabs pointing at it survive a re-run.
        /// </summary>
        private static Mesh WriteOrReplace(Mesh mesh, string path) {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

            if (existing == null) {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            // Clear first: CopySerialized onto a mesh with more submeshes than the source leaves
            // the extra ones behind.
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(mesh);
            return existing;
        }

        /// <summary>
        /// The model names some panels with a trailing space, which is legal in a mesh name and
        /// not in a tidy file name.
        /// </summary>
        private static string SanitiseName(string name) {
            string trimmed = name.Trim();
            char[] invalid = System.IO.Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++) {
                trimmed = trimmed.Replace(invalid[i], '_');
            }

            return trimmed;
        }
    }
}
