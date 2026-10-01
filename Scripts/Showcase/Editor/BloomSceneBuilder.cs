using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ViitorCloud.KmaxShowcase.Editor {
    /// <summary>
    /// Authors the Bloom scene, start to finish, from an empty project state.
    ///
    /// The rig, event system, tip and diagnostics come from
    /// <see cref="ShowcaseBuildUtility"/>; everything below is what makes this scene Bloom rather
    /// than one of the other three.
    /// </summary>
    public static class BloomSceneBuilder {
        private const string ScenePath =
            ShowcaseBuildUtility.ModuleRoot + "/Scenes/Showcase/Bloom.unity";
        private const string MoteMaterialPath =
            ShowcaseBuildUtility.ModuleRoot + "/Materials/Showcase/BloomAdditive.mat";

        [MenuItem("Kmax/Showcase/Set Up Bloom")]
        public static void SetUp() {
            Scene scene = ShowcaseBuildUtility.EnsureScene(ScenePath);

            GameObject rig = ShowcaseBuildUtility.EnsureRig();
            ShowcaseBuildUtility.ConfigureCamera(rig, Color.black);
            ShowcaseBuildUtility.EnsureEventSystem();
            StylusTip tip = ShowcaseBuildUtility.EnsureTip(rig, 0.005f, false);
            StylusHaptics haptics = tip.GetComponent<StylusHaptics>();

            Material moteMaterial = EnsureAdditiveMaterial();
            ShowcaseScene showcase = EnsureShowcaseScene(tip);
            ConfigureEnvironment();
            EnsureBloom(moteMaterial, tip, haptics, showcase);
            ShowcaseBuildUtility.EnsureDiagnostics(tip);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"Bloom set up and saved to {ScenePath}. Press F9 in play mode for the " +
                "comfort overlay, F10 for an audit. With no pen tracked the tip follows the " +
                "mouse and the scroll wheel moves it in depth.");
        }

        /// <summary>
        /// Black, and no skybox. Bloom is glowing motes against nothing - anything behind them is a
        /// second surface for the eyes to fuse and a competitor for the brightness range.
        /// </summary>
        private static void ConfigureEnvironment() {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.fog = false;
        }

        private static ShowcaseScene EnsureShowcaseScene(StylusTip tip) {
            GameObject host = ShowcaseBuildUtility.FindOrCreateRoot("Showcase");
            ShowcaseScene scene = host.GetComponent<ShowcaseScene>();
            if (scene == null) {
                scene = host.AddComponent<ShowcaseScene>();
            }
            SerializedObject sceneObject = new SerializedObject(scene);
            ShowcaseBuildUtility.SetReference(sceneObject, "tip", tip);
            ShowcaseBuildUtility.SetFloat(sceneObject, "idleDelay", 45f);
            sceneObject.ApplyModifiedPropertiesWithoutUndo();
            return scene;
        }

        private static void EnsureBloom(Material moteMaterial, StylusTip tip, StylusHaptics haptics,
            ShowcaseScene showcase) {
            GameObject root = ShowcaseBuildUtility.FindOrCreateRoot("Bloom");
            root.transform.position = Vector3.zero;

            GameObject audioHost = ShowcaseBuildUtility.FindOrCreateChild(root.transform, "Audio");
            AudioSource source = audioHost.GetComponent<AudioSource>();
            if (source == null) {
                source = audioHost.AddComponent<AudioSource>();
            }
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            ShowcaseAudio cues = audioHost.GetComponent<ShowcaseAudio>();
            if (cues == null) {
                cues = audioHost.AddComponent<ShowcaseAudio>();
            }

            ParticleSystem burst = EnsureBurst(root.transform, moteMaterial);

            GameObject gridHost = ShowcaseBuildUtility.FindOrCreateChild(root.transform, "ScreenGrid");
            BloomScreenGrid grid = gridHost.GetComponent<BloomScreenGrid>();
            if (grid == null) {
                grid = gridHost.AddComponent<BloomScreenGrid>();
            }
            SerializedObject gridObject = new SerializedObject(grid);
            ShowcaseBuildUtility.SetReference(gridObject, "lineMaterial", moteMaterial);
            ShowcaseBuildUtility.SetInt(gridObject, "columns", 4);
            ShowcaseBuildUtility.SetInt(gridObject, "rows", 3);
            ShowcaseBuildUtility.SetFloat(gridObject, "extent", 0.82f);
            gridObject.ApplyModifiedPropertiesWithoutUndo();

            GameObject fieldHost = ShowcaseBuildUtility.FindOrCreateChild(root.transform, "MoteField");
            BloomMoteField field = fieldHost.GetComponent<BloomMoteField>();
            if (field == null) {
                field = fieldHost.AddComponent<BloomMoteField>();
            }
            SerializedObject fieldObject = new SerializedObject(field);
            ShowcaseBuildUtility.SetReference(fieldObject, "tip", tip);
            ShowcaseBuildUtility.SetReference(fieldObject, "haptics", haptics);
            ShowcaseBuildUtility.SetReference(fieldObject, "audioCues", cues);
            ShowcaseBuildUtility.SetReference(fieldObject, "scene", showcase);
            ShowcaseBuildUtility.SetReference(fieldObject, "burst", burst);
            ShowcaseBuildUtility.SetReference(fieldObject, "moteMaterial", moteMaterial);
            ShowcaseBuildUtility.SetInt(fieldObject, "moteCount", 48);
            ShowcaseBuildUtility.SetFloat(fieldObject, "driftSpeed", 0.045f);
            ShowcaseBuildUtility.SetFloat(fieldObject, "moteRadius", 0.0035f);
            fieldObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static ParticleSystem EnsureBurst(Transform parent, Material material) {
            GameObject host = ShowcaseBuildUtility.FindOrCreateChild(parent, "Burst");
            ParticleSystem system = host.GetComponent<ParticleSystem>();
            if (system == null) {
                system = host.AddComponent<ParticleSystem>();
            }

            ParticleSystem.MainModule main = system.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = 0.45f;
            main.startSpeed = 0.06f;
            main.startSize = 0.0025f;
            main.startColor = new Color(1f, 0.85f, 0.55f, 1f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.002f;

            ParticleSystem.ColorOverLifetimeModule fade = system.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            GradientColorKey[] colors = new GradientColorKey[2];
            colors[0] = new GradientColorKey(Color.white, 0f);
            colors[1] = new GradientColorKey(Color.black, 1f);
            GradientAlphaKey[] alphas = new GradientAlphaKey[2];
            alphas[0] = new GradientAlphaKey(1f, 0f);
            alphas[1] = new GradientAlphaKey(1f, 1f);
            gradient.SetKeys(colors, alphas);
            fade.color = new ParticleSystem.MinMaxGradient(gradient);

            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        /// <summary>
        /// The additive material the motes, trails, grid and burst all share.
        ///
        /// Additive rather than alpha blended for two reasons that both matter more here than
        /// usual: a mote fades by going dark, so the scene contains no semi-transparent surface in
        /// front of a solid one - the case that is genuinely hard to fuse in stereo - and glowing
        /// embers against black is what the scene wants to look like anyway.
        /// </summary>
        private static Material EnsureAdditiveMaterial() {
            ShowcaseBuildUtility.EnsureFolder(
                ShowcaseBuildUtility.ModuleRoot + "/Materials/Showcase");
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null) {
                Debug.LogError("Bloom: no suitable additive shader found.");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MoteMaterialPath);
            if (material == null) {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MoteMaterialPath);
            }
            if (material.shader != shader) {
                material.shader = shader;
            }

            // Only the two toggles are written. URP re-derives _SrcBlend and _DstBlend from
            // _Surface and _Blend when the material is validated, so setting the blend factors
            // directly is overwritten without a word - the same trap as setting a clear coat mask
            // without its toggle on the Volvo. Set the thing the pipeline reads from.
            if (material.HasProperty("_Surface")) {
                material.SetFloat("_Surface", 1f);
            }
            if (material.HasProperty("_Blend")) {
                material.SetFloat("_Blend", 2f);
            }
            if (material.HasProperty("_ZWrite")) {
                material.SetFloat("_ZWrite", 0f);
            }
            if (material.HasProperty("_BaseColor")) {
                material.SetColor("_BaseColor", Color.white);
            }
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);

            // Additive is defined by the destination factor, not the source one. URP's additive
            // mode resolves to SrcAlpha/One rather than One/One, which is the better of the two
            // here - alpha drives the trail's fade while the destination factor keeps the blend
            // purely additive, so nothing this material draws can darken what is behind it.
            float src = material.HasProperty("_SrcBlend") ? material.GetFloat("_SrcBlend") : -1f;
            float dst = material.HasProperty("_DstBlend") ? material.GetFloat("_DstBlend") : -1f;
            float zwrite = material.HasProperty("_ZWrite") ? material.GetFloat("_ZWrite") : -1f;
            bool additive = Mathf.Approximately(dst, (float)UnityEngine.Rendering.BlendMode.One)
                && Mathf.Approximately(zwrite, 0f);
            if (!additive) {
                Debug.LogWarning($"Bloom: '{MoteMaterialPath}' did not take the additive blend " +
                    $"(_SrcBlend={src}, _DstBlend={dst}, _ZWrite={zwrite}). The motes will still " +
                    "draw, but they will fade to a visible dark dot instead of vanishing.");
            }
            return material;
        }
    }
}
