using System.IO;
using KmaxXR;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace ViitorCloud.KmaxShowcase.Editor {
    /// <summary>
    /// The parts of a showcase scene that are the same in all four: the Kmax rig, the event
    /// system, the tracked tip, the diagnostics, and the small serialized-field helpers every
    /// build step needs.
    ///
    /// <para>Extracted at the second scene rather than the fourth. The alternative was four copies
    /// of the rig setup drifting apart, which is how a suite ends up with one scene that behaves
    /// differently for reasons nobody can find.</para>
    ///
    /// <para><b>Every helper here converges on the spec.</b> Nothing returns early because the
    /// object already exists - it is found and then re-applied. Two sessions were lost on the
    /// Volvo to build steps that handed back an existing object untouched while logging success.
    /// </para>
    /// </summary>
    public static class ShowcaseBuildUtility {
        public const string ModuleRoot = "Assets/Games/kmax-display-example";
        public const string XrRigPrefabPath =
            ModuleRoot + "/Plugins/Kmax/com.kmax.xr.core/Editor Resources/XRRig.prefab";

        private const string RigName = "XRRig";
        private const string EventSystemName = "EventSystem";
        private const string TipName = "ShowcaseTip";
        private const string DiagnosticsName = "Diagnostics";

        /// <summary>
        /// Opens the scene at <paramref name="scenePath"/>, creating an empty one if it is not
        /// there yet.
        /// </summary>
        public static Scene EnsureScene(string scenePath) {
            EnsureFolder(Path.GetDirectoryName(scenePath).Replace('\\', '/'));
            if (File.Exists(scenePath)) {
                return UnityEditor.SceneManagement.EditorSceneManager.OpenScene(
                    scenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);
            }
            Scene created = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
                UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
                UnityEditor.SceneManagement.NewSceneMode.Single);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(created, scenePath);
            return created;
        }

        /// <summary>
        /// Instantiates the SDK's rig prefab if the scene has none. The prefab carries the whole
        /// rig - the camera with <c>VRRenderer</c> and <c>HeadTracker</c>, its two sub-cameras,
        /// and the pen with <c>PenTracker</c> and <c>KmaxStylus</c> - so nothing else has to be
        /// assembled by hand.
        /// </summary>
        public static GameObject EnsureRig() {
            XRRig existing = Object.FindFirstObjectByType<XRRig>();
            if (existing != null) {
                return existing.gameObject;
            }
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XrRigPrefabPath);
            if (prefab == null) {
                Debug.LogError($"Showcase: the Kmax rig prefab is missing at {XrRigPrefabPath}. " +
                    "No scene can be built without it.");
                return null;
            }
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = RigName;
            // Unpacked so the scene owns it and build steps can re-apply values without fighting
            // prefab overrides.
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot,
                InteractionMode.AutomatedAction);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return instance;
        }

        /// <summary>
        /// Configures the rig's centre camera and returns it. The sub-cameras are created at
        /// runtime from this one.
        /// </summary>
        public static Camera ConfigureCamera(GameObject rig, Color background) {
            if (rig == null) {
                return null;
            }
            Camera camera = rig.GetComponentInChildren<Camera>(true);
            if (camera == null) {
                return null;
            }
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = background;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 10f;
            return camera;
        }

        /// <summary>
        /// An event system running <c>KmaxInputModule</c>. The standalone module is removed if it
        /// is there - two input modules dispatch every press twice, which the eye exhibit found
        /// the hard way.
        /// </summary>
        public static void EnsureEventSystem() {
            EventSystem system = Object.FindFirstObjectByType<EventSystem>();
            GameObject host = system != null ? system.gameObject : FindOrCreateRoot(EventSystemName);
            if (host.GetComponent<EventSystem>() == null) {
                host.AddComponent<EventSystem>();
            }
            StandaloneInputModule standalone = host.GetComponent<StandaloneInputModule>();
            if (standalone != null) {
                Object.DestroyImmediate(standalone);
            }
            if (host.GetComponent<KmaxInputModule>() == null) {
                host.AddComponent<KmaxInputModule>();
            }
        }

        /// <summary>
        /// The tracked tip, its haptics and optionally the grab, parented under the rig.
        /// </summary>
        /// <param name="withGrab">Adds <see cref="StylusGrab"/>. Bloom does not need it.</param>
        public static StylusTip EnsureTip(GameObject rig, float tipRadius, bool withGrab) {
            Transform parent = rig != null ? rig.transform : null;
            GameObject host = FindOrCreateChild(parent, TipName);
            KmaxStylus stylus = rig != null ? rig.GetComponentInChildren<KmaxStylus>(true) : null;

            StylusTip tip = host.GetComponent<StylusTip>();
            if (tip == null) {
                tip = host.AddComponent<StylusTip>();
            }
            StylusHaptics haptics = host.GetComponent<StylusHaptics>();
            if (haptics == null) {
                haptics = host.AddComponent<StylusHaptics>();
            }

            SerializedObject tipObject = new SerializedObject(tip);
            SetReference(tipObject, "stylus", stylus);
            SetFloat(tipObject, "radius", tipRadius);
            SetBool(tipObject, "mouseFallback", true);
            tipObject.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject hapticsObject = new SerializedObject(haptics);
            SetReference(hapticsObject, "stylus", stylus);
            hapticsObject.ApplyModifiedPropertiesWithoutUndo();

            StylusGrab grab = host.GetComponent<StylusGrab>();
            if (withGrab) {
                if (grab == null) {
                    grab = host.AddComponent<StylusGrab>();
                }
                SerializedObject grabObject = new SerializedObject(grab);
                SetReference(grabObject, "tip", tip);
                SetReference(grabObject, "haptics", haptics);
                SetReference(grabObject, "stylus", stylus);
                grabObject.ApplyModifiedPropertiesWithoutUndo();
            } else if (grab != null) {
                Object.DestroyImmediate(grab);
            }
            return tip;
        }

        /// <summary>The comfort overlay and the scene-view gizmo, both development aids.</summary>
        public static void EnsureDiagnostics(StylusTip tip) {
            GameObject host = FindOrCreateRoot(DiagnosticsName);
            ComfortOverlay overlay = host.GetComponent<ComfortOverlay>();
            if (overlay == null) {
                overlay = host.AddComponent<ComfortOverlay>();
            }
            SerializedObject overlayObject = new SerializedObject(overlay);
            SetReference(overlayObject, "tip", tip);
            SetBool(overlayObject, "visibleOnStart", false);
            overlayObject.ApplyModifiedPropertiesWithoutUndo();

            if (host.GetComponent<StereoVolumeGizmo>() == null) {
                host.AddComponent<StereoVolumeGizmo>();
            }
        }

        public static GameObject FindOrCreateRoot(string rootName) {
            Scene scene = SceneManager.GetActiveScene();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) {
                if (roots[i].name == rootName) {
                    return roots[i];
                }
            }
            return new GameObject(rootName);
        }

        public static GameObject FindOrCreateChild(Transform parent, string childName) {
            if (parent == null) {
                return FindOrCreateRoot(childName);
            }
            Transform existing = parent.Find(childName);
            if (existing != null) {
                return existing.gameObject;
            }
            GameObject created = new GameObject(childName);
            created.transform.SetParent(parent, false);
            return created;
        }

        public static void EnsureFolder(string path) {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) {
                return;
            }
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>
        /// Creates or updates a URP Lit material asset. Opaque only - the suite's scope guards rule
        /// out semi-transparent surfaces in front of solid ones, which are the case that genuinely
        /// will not fuse in stereo.
        /// </summary>
        public static Material EnsureLitMaterial(string path, Color color, float smoothness,
            float metallic) {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) {
                Debug.LogError("Showcase: URP Lit shader not found.");
                return null;
            }
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.shader != shader) {
                material.shader = shader;
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Creates or updates a URP Unlit material asset.
        ///
        /// Unlit where the colour itself is the signal rather than the surface - Probe's tunnel
        /// flashes red on contact, and a lit material would make that flash depend on where the
        /// struts happen to face. It also means the scene needs no lights at all.
        /// </summary>
        public static Material EnsureUnlitMaterial(string path, Color color) {
            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) {
                Debug.LogError("Showcase: URP Unlit shader not found.");
                return null;
            }
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.shader != shader) {
                material.shader = shader;
            }
            material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        public static void SetReference(SerializedObject target, string field, Object value) {
            SerializedProperty property = Find(target, field, "object");
            if (property == null) {
                return;
            }
            property.objectReferenceValue = value;
        }

        public static void SetFloat(SerializedObject target, string field, float value) {
            SerializedProperty property = Find(target, field, "float");
            if (property == null) {
                return;
            }
            property.floatValue = value;
        }

        public static void SetInt(SerializedObject target, string field, int value) {
            SerializedProperty property = Find(target, field, "int");
            if (property == null) {
                return;
            }
            property.intValue = value;
        }

        public static void SetBool(SerializedObject target, string field, bool value) {
            SerializedProperty property = Find(target, field, "bool");
            if (property == null) {
                return;
            }
            property.boolValue = value;
        }

        private static SerializedProperty Find(SerializedObject target, string field, string kind) {
            SerializedProperty property = target.FindProperty(field);
            if (property == null) {
                // Loud, because the failure is otherwise invisible: the build logs success and the
                // value it meant to write is simply never applied.
                Debug.LogWarning($"Showcase: no {kind} field '{field}' on " +
                    $"'{target.targetObject.GetType().Name}'. It was renamed or removed, and the " +
                    "build step is out of date.");
            }
            return property;
        }
    }
}
