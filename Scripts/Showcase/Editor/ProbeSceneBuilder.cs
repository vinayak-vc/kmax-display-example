using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ViitorCloud.KmaxShowcase.Editor {
    /// <summary>
    /// Authors the Probe scene, start to finish, from an empty project state.
    ///
    /// <para>The scene is deliberately almost empty: a tunnel and nothing else. Probe is asking one
    /// question - is the tracked tip in the same place as the rendered geometry - and every other
    /// object would be something for the eye to use instead of the tunnel when judging that.</para>
    ///
    /// <para>Unlit, so no lights and no shadows. The tunnel's colour is the feedback channel: it
    /// flashes red the instant the tip leaves the lumen. A lit material would make that flash
    /// depend on which way a strut happened to face.</para>
    /// </summary>
    public static class ProbeSceneBuilder {
        private const string ScenePath =
            ShowcaseBuildUtility.ModuleRoot + "/Scenes/Showcase/Probe.unity";
        private const string MaterialsFolder =
            ShowcaseBuildUtility.ModuleRoot + "/Materials/Showcase";

        [MenuItem("Kmax/Showcase/Set Up Probe")]
        public static void SetUp() {
            Scene scene = ShowcaseBuildUtility.EnsureScene(ScenePath);

            GameObject rig = ShowcaseBuildUtility.EnsureRig();
            ShowcaseBuildUtility.ConfigureCamera(rig, new Color(0.02f, 0.025f, 0.035f, 1f));
            ShowcaseBuildUtility.EnsureEventSystem();
            StylusTip tip = ShowcaseBuildUtility.EnsureTip(rig, 0.005f, false);
            StylusHaptics haptics = tip.GetComponent<StylusHaptics>();

            ConfigureEnvironment();

            Material tunnelMaterial = ShowcaseBuildUtility.EnsureUnlitMaterial(
                MaterialsFolder + "/ProbeTunnel.mat", new Color(0.35f, 0.62f, 0.78f));
            Material markerMaterial = ShowcaseBuildUtility.EnsureUnlitMaterial(
                MaterialsFolder + "/ProbeMarker.mat", new Color(1f, 0.82f, 0.4f));

            GameObject root = ShowcaseBuildUtility.FindOrCreateRoot("Probe");
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            ProbePath path = EnsurePath(root.transform, tunnelMaterial);
            Transform marker = EnsureMarker(root.transform, markerMaterial, path);
            ShowcaseScene showcase = EnsureShowcaseScene(tip);
            EnsureGame(root.transform, path, tip, haptics, showcase, marker);
            ShowcaseBuildUtility.EnsureDiagnostics(tip);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"Probe set up and saved to {ScenePath}. Without a pen the tip follows the " +
                "mouse and the scroll wheel moves it in depth - put it in the tunnel mouth to " +
                "start. F9 draws the comfort volume, F10 audits it.");
        }

        private static void ConfigureEnvironment() {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.fog = false;
        }

        private static ProbePath EnsurePath(Transform parent, Material material) {
            GameObject host = ShowcaseBuildUtility.FindOrCreateChild(parent, "Tunnel");
            host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            if (host.GetComponent<MeshFilter>() == null) {
                host.AddComponent<MeshFilter>();
            }
            MeshRenderer renderer = host.GetComponent<MeshRenderer>();
            if (renderer == null) {
                renderer = host.AddComponent<MeshRenderer>();
            }
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ProbePath path = host.GetComponent<ProbePath>();
            if (path == null) {
                path = host.AddComponent<ProbePath>();
            }
            SerializedObject pathObject = new SerializedObject(path);
            ShowcaseBuildUtility.SetInt(pathObject, "waypoints", 3);
            ShowcaseBuildUtility.SetInt(pathObject, "samples", 140);
            ShowcaseBuildUtility.SetInt(pathObject, "rails", 5);
            ShowcaseBuildUtility.SetInt(pathObject, "hoops", 8);
            ShowcaseBuildUtility.SetFloat(pathObject, "lumenOfHeight", 0.085f);
            ShowcaseBuildUtility.SetFloat(pathObject, "edgeMargin", 0.03f);
            pathObject.ApplyModifiedPropertiesWithoutUndo();

            // Build one now, so the scene has a tunnel in it the moment the command finishes
            // rather than only once someone presses play.
            path.Generate(20260930, 0f);
            return path;
        }

        private static Transform EnsureMarker(Transform parent, Material material, ProbePath path) {
            GameObject host = ShowcaseBuildUtility.FindOrCreateChild(parent, "AttractMarker");
            // Placed and sized here as well as at runtime. ProbeGame only touches it while the
            // scene is playing, so left alone it saves into the scene as a one-metre sphere at the
            // origin - far outside the depth budget and breaking the stereo window. The comfort
            // audit caught it; a person opening the scene would have seen it immediately.
            if (path != null && path.IsBuilt) {
                float size = path.LumenRadius * 0.45f;
                host.transform.position = path.SampleAt(0.5f);
                host.transform.localScale = new Vector3(size, size, size);
            }
            MeshFilter filter = host.GetComponent<MeshFilter>();
            if (filter == null) {
                filter = host.AddComponent<MeshFilter>();
            }
            filter.sharedMesh = UnitSphereMesh();
            MeshRenderer renderer = host.GetComponent<MeshRenderer>();
            if (renderer == null) {
                renderer = host.AddComponent<MeshRenderer>();
            }
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return host.transform;
        }

        private static Mesh _unitSphere;

        private static Mesh UnitSphereMesh() {
            if (_unitSphere != null) {
                return _unitSphere;
            }
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _unitSphere = primitive.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(primitive);
            return _unitSphere;
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
            // Probe manages its own replay. The scene's automatic reset would return to Attract a
            // few seconds after a run finished, and Attract is the signal that somebody new has
            // arrived - it clears the difficulty and the best time. Someone who just finished a run
            // has not gone anywhere, so only the idle timeout should reset them.
            ShowcaseBuildUtility.SetBool(sceneObject, "autoResetWhenResolved", false);
            sceneObject.ApplyModifiedPropertiesWithoutUndo();
            return scene;
        }

        private static void EnsureGame(Transform parent, ProbePath path, StylusTip tip,
            StylusHaptics haptics, ShowcaseScene showcase, Transform marker) {
            GameObject host = ShowcaseBuildUtility.FindOrCreateChild(parent, "Game");

            GameObject audioHost = ShowcaseBuildUtility.FindOrCreateChild(host.transform, "Audio");
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

            ProbeGame game = host.GetComponent<ProbeGame>();
            if (game == null) {
                game = host.AddComponent<ProbeGame>();
            }
            SerializedObject gameObject = new SerializedObject(game);
            ShowcaseBuildUtility.SetReference(gameObject, "path", path);
            ShowcaseBuildUtility.SetReference(gameObject, "tip", tip);
            ShowcaseBuildUtility.SetReference(gameObject, "haptics", haptics);
            ShowcaseBuildUtility.SetReference(gameObject, "audioCues", cues);
            ShowcaseBuildUtility.SetReference(gameObject, "scene", showcase);
            ShowcaseBuildUtility.SetReference(gameObject, "attractMarker", marker);
            ShowcaseBuildUtility.SetFloat(gameObject, "penaltySeconds", 2f);
            ShowcaseBuildUtility.SetFloat(gameObject, "startDifficulty", 0f);
            ShowcaseBuildUtility.SetFloat(gameObject, "difficultyStep", 0.2f);
            gameObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
