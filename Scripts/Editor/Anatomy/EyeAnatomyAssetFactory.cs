using UnityEditor;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample.Editor {
    /// <summary>
    /// Creates the handful of assets the interaction upgrades need - the stylus tip mesh and the
    /// two materials that dress the stylus - and returns existing ones untouched on a re-run.
    ///
    /// The materials are copied from <c>AnatomyMote.mat</c> rather than built from a shader by
    /// name. URP transparency depends on a set of keywords, blend factors and a render queue that
    /// all have to agree, and copying a material that already renders correctly in this project is
    /// far more reliable than setting eight properties and hoping the keyword state follows.
    /// </summary>
    public static class EyeAnatomyAssetFactory {
        private const string ModuleRoot = "Assets/Games/kmax-display-example";
        private const string MaterialsFolder = ModuleRoot + "/Materials";
        private const string ModelFolder = ModuleRoot + "/Model";
        private const string MoteMaterialPath = MaterialsFolder + "/AnatomyMote.mat";

        public const string BeamMaterialPath = MaterialsFolder + "/StylusBeam.mat";
        public const string TipMaterialPath = MaterialsFolder + "/StylusTip.mat";
        public const string TipMeshPath = ModelFolder + "/StylusTip.asset";
        public const string FocusHighlightMaterialPath = MaterialsFolder + "/FocusHighlight.mat";
        public const string EnvironmentSkyboxPath = MaterialsFolder + "/AnatomyEnvironment.mat";

        /// <summary>
        /// Stand-in for a focused part whose own material is too transparent to read.
        ///
        /// Translucent rather than opaque, and that took some finding. An opaque version is
        /// certainly visible, but the lens is a biconvex disc viewed down its own axis: almost
        /// every normal you can see points at the camera, so diffuse shading has nothing to grade
        /// across and it renders as a flat pale sticker no matter how it is lit. Raising the
        /// smoothness makes it worse, not better - a near-mirror reflecting a nearly uniform grey
        /// sky returns the same value at every normal.
        ///
        /// Letting the structures behind show through is what actually reads as an optical body,
        /// and it is far closer to the source art's intent than a solid blob. The alpha is well
        /// above the 58% the model ships with, which is the level that was invisible.
        /// </summary>
        public static Material GetOrCreateFocusHighlightMaterial() {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(FocusHighlightMaterialPath);
            if (existing != null) {
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) {
                Debug.LogError($"{nameof(EyeAnatomyAssetFactory)} could not find the URP Lit shader; transparent parts will not be rescued.");
                return null;
            }

            Material material = new Material(shader);

            // URP decides transparency from a set of floats and a keyword that all have to agree;
            // setting the colour's alpha alone leaves the material opaque.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            material.SetColor("_BaseColor", new Color(0.76f, 0.88f, 0.98f, 0.74f));
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.85f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(0.05f, 0.09f, 0.12f));
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            AssetDatabase.CreateAsset(material, FocusHighlightMaterialPath);
            return material;
        }

        /// <summary>
        /// A grey gradient sky used for ambient light and reflections only - the cameras keep
        /// clearing to a solid colour, so it is never actually seen.
        ///
        /// That split is deliberate. The eye's wet surfaces need something in the environment to
        /// reflect or they read as dead matte, but a visible sky on a stereo panel raises the
        /// average screen brightness and with it the crosstalk between the two eyes.
        /// </summary>
        public static Material GetOrCreateEnvironmentSkybox() {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(EnvironmentSkyboxPath);
            if (existing != null) {
                return existing;
            }

            Shader shader = Shader.Find("Skybox/Procedural");
            if (shader == null) {
                Debug.LogError($"{nameof(EyeAnatomyAssetFactory)} could not find the Skybox/Procedural shader; the scene will have no environment reflections.");
                return null;
            }

            Material material = new Material(shader);
            material.SetFloat("_SunDisk", 0f);
            // A pronounced sky-to-ground gradient rather than a flat grey: smooth surfaces mirror
            // this, and a uniform environment reflects identically at every normal, which flattens
            // exactly the curved surfaces it is meant to bring out.
            material.SetFloat("_AtmosphereThickness", 0.75f);
            material.SetColor("_SkyTint", new Color(0.50f, 0.56f, 0.68f));
            material.SetColor("_GroundColor", new Color(0.07f, 0.075f, 0.09f));
            material.SetFloat("_Exposure", 0.95f);

            AssetDatabase.CreateAsset(material, EnvironmentSkyboxPath);
            return material;
        }

        /// <summary>
        /// Additive, untextured material for the stylus line renderer.
        /// A line renderer supplies its gradient through vertex colours, so the shader has to be
        /// one that reads them - which is why this is the particle unlit shader and not plain unlit.
        /// </summary>
        public static Material GetOrCreateBeamMaterial() {
            return GetOrCreateMoteVariant(BeamMaterialPath, Color.white, false);
        }

        /// <summary>
        /// Material for the pointed tip. Tinted per-frame through a property block, so the colour
        /// set here only matters before the first frame.
        /// </summary>
        public static Material GetOrCreateTipMaterial() {
            return GetOrCreateMoteVariant(TipMaterialPath, new Color(0.55f, 0.90f, 1f, 1f), false);
        }

        private static Material GetOrCreateMoteVariant(string path, Color color, bool keepTexture) {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) {
                return existing;
            }

            Material source = AssetDatabase.LoadAssetAtPath<Material>(MoteMaterialPath);
            if (source == null) {
                Debug.LogError($"{nameof(EyeAnatomyAssetFactory)} could not find '{MoteMaterialPath}' to copy; the stylus will render with the default material.");
                return null;
            }

            if (!AssetDatabase.CopyAsset(MoteMaterialPath, path)) {
                Debug.LogError($"{nameof(EyeAnatomyAssetFactory)} could not copy '{MoteMaterialPath}' to '{path}'.");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) {
                return null;
            }

            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);

            if (!keepTexture) {
                // The mote texture is a soft dot. Tiled along a line or wrapped on a cone it reads
                // as a smear, so these variants are left plain white.
                material.SetTexture("_BaseMap", null);
                material.SetTexture("_MainTex", null);
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// A cone whose apex sits at the local origin with its body running out along +Y.
        ///
        /// That layout is what lets <c>AnatomyStylusBeam</c> place the tip by setting its position
        /// to the hit point and rotating +Y onto the surface normal: the point lands exactly on the
        /// geometry and the body stands out of it, rather than the cone straddling the surface.
        /// </summary>
        public static Mesh GetOrCreateTipMesh() {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(TipMeshPath);
            if (existing != null) {
                return existing;
            }

            Mesh mesh = BuildCone(16, 0.34f, 1f);
            mesh.name = "StylusTip";
            AssetDatabase.CreateAsset(mesh, TipMeshPath);
            return mesh;
        }

        private static Mesh BuildCone(int segments, float radius, float height) {
            // Apex, one ring of base vertices, and a centre for the cap.
            Vector3[] vertices = new Vector3[segments + 2];
            vertices[0] = Vector3.zero;

            for (int i = 0; i < segments; i++) {
                float angle = (i / (float)segments) * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
            }

            vertices[segments + 1] = new Vector3(0f, height, 0f);

            int[] triangles = new int[segments * 6];
            int t = 0;

            for (int i = 0; i < segments; i++) {
                int current = i + 1;
                int next = (i + 1) % segments + 1;

                // Side, wound so the outside faces out from the apex.
                triangles[t++] = 0;
                triangles[t++] = next;
                triangles[t++] = current;

                // Cap.
                triangles[t++] = segments + 1;
                triangles[t++] = current;
                triangles[t++] = next;
            }

            Mesh mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
