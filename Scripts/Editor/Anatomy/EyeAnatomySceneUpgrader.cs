using KmaxXR;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace ViitorCloud.KmaxDisplayExample.Editor {
    /// <summary>
    /// Authors everything the interaction upgrades need into <c>Scenes/EyeAnatomy.unity</c>: the
    /// stylus and its beam, the Next and Back navigator, the audio rig and the extra particle
    /// layers, with every reference wired.
    ///
    /// This is a menu command rather than a hand-edited scene because the scene is 12,000 lines of
    /// YAML full of file IDs. Every step finds before it creates, so running it twice changes
    /// nothing and a half-finished run can simply be repeated.
    /// </summary>
    public static class EyeAnatomySceneUpgrader {
        private const string MenuPath = "Kmax/Eye Anatomy/Set Up Interaction Upgrades";
        private const string ScenePath = "Assets/Games/kmax-display-example/Scenes/EyeAnatomy.unity";
        private const string HotspotPrefabPath = "Assets/Games/kmax-display-example/Prefabs/EyeHotspot.prefab";
        private const string HaloName = "SelectionHalo";
        private const string GhostMaterialPath = "Assets/Games/kmax-display-example/Materials/EyeGhost.mat";
        private const string ForwardRendererPath = "Assets/Games/kmax-display-example/URPAssets/URPAsset_ForwardRenderer.asset";
        private const string VolumeProfilePath = "Assets/Games/kmax-display-example/URPAssets/AnatomyPostFX.asset";
        private const string PostFxName = "PostFX";

        private const string PenName = "AnatomyPen";
        private const string StylusName = "Stylus";
        private const string BeamName = "Beam";
        private const string TipName = "Tip";
        private const string AudioRootName = "Audio";
        private const string NextButtonName = "NextButton";
        private const string PreviousButtonName = "PreviousButton";
        private const string PartCounterName = "PartCounter";

        [MenuItem(MenuPath)]
        public static void Run() {
            if (!EnsureSceneOpen()) {
                return;
            }

            GameObject rig = FindRoot("XRRig");
            GameObject ui = FindRoot("UI");
            GameObject ambience = FindRoot("Ambience");
            GameObject exhibit = FindRoot("EyeAnatomyExhibit");
            GameObject eventSystem = FindRoot("EventSystem");

            if (rig == null || ui == null || ambience == null || exhibit == null || eventSystem == null) {
                Debug.LogError($"{nameof(EyeAnatomySceneUpgrader)} could not find every expected scene root " +
                    "(XRRig, UI, Ambience, EyeAnatomyExhibit, EventSystem). Nothing was changed.");
                return;
            }

            Camera camera = FindRigCamera(rig);
            if (camera == null) {
                Debug.LogError($"{nameof(EyeAnatomySceneUpgrader)} found no camera under XRRig. Nothing was changed.");
                return;
            }

            UpgradeEventSystem(eventSystem);
            UpgradeCamera(camera);
            UpgradeHotspotPrefab();
            RemoveDuplicatePens(rig);
            // Before the lighting: the intensities below are only meaningful once a renderer that
            // actually applies lights is in place.
            UpgradeRenderPipeline();
            UpgradeEnvironment();
            // Post-processing before the lighting: the intensities below assume a tonemapper is
            // rolling the highlights off, and clip to flat white without one.
            UpgradePostProcessing(rig, exhibit);
            UpgradeLighting();
            UpgradeFocusView(exhibit);
            UpgradeCanvasDepth(ui);

            KmaxStylus stylus = BuildStylus(rig, camera);
            AnatomyAudioDirector audio = BuildAudio();
            AnatomyParticleDirector particles = UpgradeParticles(ambience);
            NavigationUi navigation = BuildNavigationUi(ui);

            WireExhibit(exhibit, stylus, audio, particles, navigation);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            Debug.Log($"{nameof(EyeAnatomySceneUpgrader)} finished. Stylus, navigator, audio and particle " +
                "layers are in place and wired. Press Play to check it.");
        }

        private static bool EnsureSceneOpen() {
            if (EditorSceneManager.GetActiveScene().path == ScenePath) {
                return true;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
                return false;
            }

            EditorSceneManager.OpenScene(ScenePath);
            return EditorSceneManager.GetActiveScene().path == ScenePath;
        }

        /// <summary>
        /// Swaps the stock input module for the Kmax one.
        ///
        /// <see cref="StandaloneInputModule"/> only knows about the mouse. It works today because
        /// the Kmax driver drives the OS cursor with the pen, which is enough to press a button but
        /// never produces a 3D pointer. <see cref="KmaxInputModule"/> derives from it, so the mouse
        /// keeps working exactly as before, and adds the pass over every registered
        /// <see cref="KmaxPointer"/> that turns the pen into a real pointer.
        /// </summary>
        private static void UpgradeEventSystem(GameObject eventSystem) {
            if (eventSystem.GetComponent<KmaxInputModule>() != null) {
                return;
            }

            StandaloneInputModule stock = eventSystem.GetComponent<StandaloneInputModule>();
            if (stock != null) {
                Undo.DestroyObjectImmediate(stock);
            }

            Undo.AddComponent<KmaxInputModule>(eventSystem);
            Debug.Log($"{nameof(EyeAnatomySceneUpgrader)} replaced StandaloneInputModule with KmaxInputModule.");
        }

        /// <summary>
        /// The event camera needs a physics raycaster for 3D pointer events, and the scene needs
        /// exactly one audio listener - on the camera, so it travels with the viewer.
        /// </summary>
        private static void UpgradeCamera(Camera camera) {
            if (camera.GetComponent<BaseRaycaster>() == null) {
                Undo.AddComponent<KmaxPhysicRaycaster>(camera.gameObject);
            }

            if (Object.FindFirstObjectByType<AudioListener>() == null) {
                Undo.AddComponent<AudioListener>(camera.gameObject);
            }
        }

        /// <summary>
        /// Builds the pen: the tracker and pointer on one object, and a child carrying the beam
        /// and the pointed tip.
        ///
        /// <see cref="KmaxStylus"/> looks for its <see cref="IPointerVisualize"/> with
        /// <c>GetComponent</c> on the transform assigned to its <c>stylus</c> field, so the beam
        /// script has to live on that child and not on the pen root.
        /// </summary>
        private static KmaxStylus BuildStylus(GameObject rig, Camera camera) {
            GameObject pen = FindOrCreateChild(rig.transform, PenName);
            GameObject stylusObject = FindOrCreateChild(pen.transform, StylusName);
            GameObject beamObject = FindOrCreateChild(stylusObject.transform, BeamName);
            GameObject tipObject = FindOrCreateChild(stylusObject.transform, TipName);

            LineRenderer line = GetOrAdd<LineRenderer>(beamObject);
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, Vector3.zero);
            line.SetPosition(1, Vector3.forward * 0.5f);
            line.startWidth = 0.0016f;
            line.endWidth = 0.0005f;
            line.numCapVertices = 4;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = EyeAnatomyAssetFactory.GetOrCreateBeamMaterial();

            MeshFilter tipFilter = GetOrAdd<MeshFilter>(tipObject);
            tipFilter.sharedMesh = EyeAnatomyAssetFactory.GetOrCreateTipMesh();

            MeshRenderer tipRenderer = GetOrAdd<MeshRenderer>(tipObject);
            tipRenderer.sharedMaterial = EyeAnatomyAssetFactory.GetOrCreateTipMaterial();
            tipRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tipRenderer.receiveShadows = false;

            AnatomyStylusBeam beam = GetOrAdd<AnatomyStylusBeam>(stylusObject);
            SerializedObject beamSo = new SerializedObject(beam);
            beamSo.FindProperty("beam").objectReferenceValue = line;
            beamSo.FindProperty("tip").objectReferenceValue = tipObject.transform;
            beamSo.FindProperty("tipRenderer").objectReferenceValue = tipRenderer;
            beamSo.ApplyModifiedPropertiesWithoutUndo();

            PenTracker tracker = GetOrAdd<PenTracker>(pen);
            SerializedObject trackerSo = new SerializedObject(tracker);
            trackerSo.FindProperty("pen").objectReferenceValue = stylusObject.transform;
            trackerSo.ApplyModifiedPropertiesWithoutUndo();

            KmaxStylus stylus = GetOrAdd<KmaxStylus>(pen);
            SerializedObject stylusSo = new SerializedObject(stylus);
            stylusSo.FindProperty("stylus").objectReferenceValue = stylusObject.transform;
            stylusSo.FindProperty("eventCamera").objectReferenceValue = camera;
            stylusSo.FindProperty("rayLength").floatValue = 0.5f;
            stylusSo.FindProperty("smoothEndPoint").boolValue = true;

            // Everything is on the default layer, and a mask that excludes a layer silently stops
            // the beam landing on it, so this stays open rather than guessing.
            stylusSo.FindProperty("layer").intValue = ~0;

            // Index 0, the front button, becomes the click. StateOf maps index to button without
            // swapping anything only when the primary is Left, which keeps AnatomyStylusInput's
            // button numbers and the input module's buttons describing the same physical keys.
            stylusSo.FindProperty("PrimaryKey").enumValueIndex = (int)PointerEventData.InputButton.Left;
            stylusSo.ApplyModifiedPropertiesWithoutUndo();

            return stylus;
        }

        /// <summary>
        /// Gives the badge prefab a halo disc, which <see cref="EyeHotspot"/> swells and fades on a
        /// loop while that badge is the selected one.
        ///
        /// It is cloned from the badge quad and sits just behind it, so the badge itself occludes
        /// the middle and what reads is a ring pinging outward. The field is optional on the
        /// component, so a prefab without it still works - this only adds the flourish.
        /// </summary>
        private static void UpgradeHotspotPrefab() {
            GameObject contents = PrefabUtility.LoadPrefabContents(HotspotPrefabPath);
            if (contents == null) {
                Debug.LogWarning($"{nameof(EyeAnatomySceneUpgrader)} could not open '{HotspotPrefabPath}'; " +
                    "badges will have no selection halo.");
                return;
            }

            try {
                EyeHotspot hotspot = contents.GetComponent<EyeHotspot>();
                Transform visuals = contents.transform.Find("Visuals");
                Transform badgeQuad = visuals != null ? visuals.Find("BadgeQuad") : null;

                if (hotspot == null || visuals == null || badgeQuad == null) {
                    Debug.LogWarning($"{nameof(EyeAnatomySceneUpgrader)} did not recognise the badge prefab's " +
                        "layout (expected Visuals/BadgeQuad); no selection halo was added.");
                    return;
                }

                Transform halo = visuals.Find(HaloName);
                if (halo == null) {
                    GameObject created = Object.Instantiate(badgeQuad.gameObject, visuals, false);
                    created.name = HaloName;
                    halo = created.transform;

                    // Behind the badge: the billboard's forward points away from the viewer, so a
                    // small positive local Z puts the halo behind without z-fighting.
                    halo.localPosition = badgeQuad.localPosition + new Vector3(0f, 0f, 0.0006f);
                    halo.localRotation = badgeQuad.localRotation;
                    halo.localScale = badgeQuad.localScale;
                }

                // Started by the component only while something is selected.
                halo.gameObject.SetActive(false);

                SerializedObject so = new SerializedObject(hotspot);
                so.FindProperty("selectionHalo").objectReferenceValue = halo;
                so.FindProperty("haloRenderer").objectReferenceValue = halo.GetComponent<Renderer>();
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(contents, HotspotPrefabPath);
            } finally {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Removes any stylus under the rig that is not the one this command builds.
        ///
        /// Every <see cref="KmaxStylus"/> registers as a pointer under the same
        /// <see cref="KmaxStylus.UniqueId"/>, and <see cref="KmaxInputModule"/> iterates every
        /// registered pointer - so a second pen means every press is raycast and dispatched twice,
        /// from two different poses. The SDK's own <c>pen.prefab</c> being dropped into the scene
        /// alongside this one is the easy way to end up there.
        /// </summary>
        private static void RemoveDuplicatePens(GameObject rig) {
            KmaxStylus[] pens = rig.GetComponentsInChildren<KmaxStylus>(true);
            int removed = 0;

            for (int i = 0; i < pens.Length; i++) {
                if (pens[i] == null || pens[i].gameObject.name == PenName) {
                    continue;
                }

                Debug.LogWarning($"{nameof(EyeAnatomySceneUpgrader)} removed a duplicate stylus '{pens[i].gameObject.name}' " +
                    $"under XRRig. Two pointers sharing id {KmaxStylus.UniqueId} would dispatch every press twice.", rig);
                Undo.DestroyObjectImmediate(pens[i].gameObject);
                removed++;
            }

            if (removed == 0) {
                return;
            }

            Debug.Log($"{nameof(EyeAnatomySceneUpgrader)} removed {removed} duplicate pen(s); '{PenName}' is now the only stylus.");
        }

        /// <summary>
        /// Puts a grey behind the anatomy and gives the scene an environment to reflect.
        ///
        /// The background is a slate grey rather than a light one on purpose. The eye is rendered
        /// through about twenty ghost shells while a part is focused, and every one of them is a
        /// pale blue at 4% alpha - lift the background much past this and they accumulate into a
        /// milky haze that flattens the whole model. This is the point where the grey reads as grey
        /// and the anatomy still has contrast to sit against.
        /// </summary>
        private static void UpgradeEnvironment() {
            Material skybox = EyeAnatomyAssetFactory.GetOrCreateEnvironmentSkybox();
            if (skybox != null) {
                RenderSettings.skybox = skybox;
                RenderSettings.ambientMode = AmbientMode.Skybox;
                // Held below the key so the directional lights still model the form. Ambient from
                // a grey sky arrives evenly from every direction, so leaning on it too hard
                // flattens exactly the curvature the lights are there to bring out.
                RenderSettings.ambientIntensity = 0.55f;
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
                RenderSettings.reflectionIntensity = 0.55f;
                DynamicGI.UpdateEnvironment();
            }

            Color background = new Color(0.165f, 0.175f, 0.205f, 1f);
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++) {
                // Solid colour, not the skybox: the sky exists to light the model, not to be seen.
                cameras[i].clearFlags = CameraClearFlags.SolidColor;
                cameras[i].backgroundColor = background;
                EditorUtility.SetDirty(cameras[i]);
            }

            // The ghosts were tuned against a black background. Against grey the same alpha reads
            // as fog, so they come down and darken to sit under the background rather than over it.
            Material ghost = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
            if (ghost != null) {
                Color faded = new Color(0.30f, 0.38f, 0.50f, 0.028f);
                ghost.SetColor("_BaseColor", faded);
                ghost.SetColor("_Color", faded);
                EditorUtility.SetDirty(ghost);
            }
        }

        /// <summary>
        /// Adds the two lights the wet surfaces need: a front fill so what faces the viewer is lit
        /// at all, and a close point light whose highlight slides across the cornea as the view
        /// orbits. A moving specular is most of what makes tissue look wet rather than matte.
        /// </summary>
        /// <summary>
        /// Balances the light rig for a tonemapped image.
        ///
        /// These values only make sense alongside <see cref="UpgradeLighting"/>'s two companions:
        /// a renderer that applies lights at all, and a tonemapper that rolls highlights off. With
        /// no tonemapper the model's near-white albedo clips past about 0.6 of total energy, which
        /// forces the rig so dark that everything reads muddy. With one, the key can be driven
        /// properly and the whites keep their form. Measured: 0.00% of the subject clipped, against
        /// 26.4% before.
        ///
        /// Key to fill is held near 2:1. A wider ratio crushes the shadow side of a sphere, which
        /// is most of what the eye is.
        /// </summary>
        private static void UpgradeLighting() {
            SetLightIntensity("Key Light", 1.05f);
            SetLightIntensity("Fill Light", 0.50f);
            SetLightIntensity("Rim Light", 0.60f);

            // Both of these are created disabled rather than deleted, so the rig is self-documenting
            // about what was tried. Neither should be switched back on without re-measuring.
            //
            // The point light sat 0.23 m from a model about 0.1 m across; inverse-square falloff
            // turned it into a blowtorch on the near side, and it alone accounted for 26% of the
            // frame clipping to white. A directional already gives a specular that slides as the
            // camera orbits, because specular is view-dependent.
            Light spark = EnsureLight("Specular Point", LightType.Point);
            spark.transform.position = new Vector3(-0.13f, 0.11f, -0.16f);
            spark.color = new Color(1f, 0.97f, 0.92f);
            spark.intensity = 0f;
            spark.range = 1.1f;
            spark.shadows = LightShadows.None;
            spark.enabled = false;

            // A fill aimed down the viewing axis lights everything facing the viewer evenly, which
            // is the fastest way to flatten a curved surface.
            Light front = EnsureLight("Front Fill", LightType.Directional);
            front.transform.SetPositionAndRotation(new Vector3(0f, 0.2f, -0.6f), Quaternion.Euler(28f, 215f, 0f));
            front.color = new Color(0.86f, 0.91f, 1f);
            front.intensity = 0f;
            front.shadows = LightShadows.None;
            front.enabled = false;
        }

        /// <summary>
        /// Builds the post-processing volume and makes sure the stereo cameras actually use it.
        ///
        /// The volume alone is not enough: <c>VRRenderer</c> creates the <c>left</c> and
        /// <c>right</c> cameras at runtime without a <c>UniversalAdditionalCameraData</c>, so URP
        /// defaults them to <c>renderPostProcessing = false</c> and every volume in the scene is
        /// skipped. <see cref="ExhibitPostProcessing"/> configures them once they exist.
        /// </summary>
        private static void UpgradePostProcessing(GameObject rig, GameObject exhibit) {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null) {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);
            }

            Tonemapping tonemapping;
            if (!profile.TryGet(out tonemapping)) {
                tonemapping = profile.Add<Tonemapping>(true);
            }

            tonemapping.active = true;
            tonemapping.mode.overrideState = true;
            // Neutral rather than ACES: ACES pushes saturation and contrast in a way that suits
            // film, not an anatomical reference where the tissue colours are the content.
            tonemapping.mode.value = TonemappingMode.Neutral;

            Bloom bloom;
            if (!profile.TryGet(out bloom)) {
                bloom = profile.Add<Bloom>(true);
            }

            bloom.active = true;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 0.95f;
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0.55f;
            bloom.scatter.overrideState = true;
            bloom.scatter.value = 0.62f;
            bloom.tint.overrideState = true;
            bloom.tint.value = new Color(0.85f, 0.92f, 1f);

            EditorUtility.SetDirty(profile);

            GameObject host = FindRoot(PostFxName);
            if (host == null) {
                host = new GameObject(PostFxName);
                Undo.RegisterCreatedObjectUndo(host, "Create " + PostFxName);
            }

            Volume volume = GetOrAdd<Volume>(host);
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;

            ExhibitPostProcessing driver = GetOrAdd<ExhibitPostProcessing>(exhibit);
            SerializedObject so = new SerializedObject(driver);
            SetIfPresent(so, "cameraRoot", rig.transform);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Makes sure the pipeline renders with a 3D renderer.
        ///
        /// `URPAsset` shipped pointing at a **`Renderer2DData`** - the 2D renderer, which only
        /// handles `Light2D` and silently discards every directional and point light in the scene.
        /// The whole exhibit was rendering as albedo times ambient, which is why the model looked
        /// flat, why a focused lens had no terminator however it was lit, and - almost certainly -
        /// why the custom hotspot shader recorded in decisions.md compiled, reported
        /// `isSupported`, and drew nothing.
        ///
        /// Measured before and after: mean luminance with every light on versus every light off
        /// differed by 0.0000 under the 2D renderer and by 0.0443 under this one.
        /// </summary>
        private static void UpgradeRenderPipeline() {
            UniversalRenderPipelineAsset urp =
                UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) {
                Debug.LogWarning($"{nameof(EyeAnatomySceneUpgrader)} found no URP asset; lighting was not checked.");
                return;
            }

            System.Reflection.FieldInfo listField = typeof(UniversalRenderPipelineAsset)
                .GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (listField == null) {
                return;
            }

            ScriptableRendererData[] list = (ScriptableRendererData[])listField.GetValue(urp);
            if (list == null || list.Length == 0 || list[0] is UniversalRendererData) {
                return;
            }

            UniversalRendererData data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(ForwardRendererPath);
            if (data == null) {
                data = ScriptableObject.CreateInstance<UniversalRendererData>();
                data.name = "URPAsset_ForwardRenderer";
                AssetDatabase.CreateAsset(data, ForwardRendererPath);
            }

            string previous = list[0] != null ? list[0].GetType().Name : "null";
            list[0] = data;
            listField.SetValue(urp, list);

            // Drop the cached renderer instances so the pipeline rebuilds against the new data.
            System.Reflection.FieldInfo renderersField = typeof(UniversalRenderPipelineAsset)
                .GetField("m_Renderers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (renderersField != null) {
                renderersField.SetValue(urp, new ScriptableRenderer[list.Length]);
            }

            EditorUtility.SetDirty(urp);
            AssetDatabase.SaveAssets();

            Debug.LogWarning($"{nameof(EyeAnatomySceneUpgrader)} replaced the pipeline's {previous} with a " +
                "UniversalRendererData. The 2D renderer discards every 3D light, so nothing in the scene " +
                "was actually being lit. Light intensities are calibrated for the corrected renderer.", urp);
        }

        private static void SetLightIntensity(string name, float intensity) {
            GameObject root = FindRoot(name);
            if (root == null) {
                return;
            }

            Light light = root.GetComponent<Light>();
            if (light == null) {
                return;
            }

            light.intensity = intensity;
            EditorUtility.SetDirty(light);
        }

        private static Light EnsureLight(string name, LightType type) {
            GameObject existing = FindRoot(name);
            if (existing == null) {
                existing = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(existing, "Create " + name);
            }

            Light light = GetOrAdd<Light>(existing);
            light.type = type;
            return light;
        }

        /// <summary>
        /// Hands the focus view an opaque stand-in for parts that are too transparent to read.
        /// </summary>
        private static void UpgradeFocusView(GameObject exhibit) {
            EyeFocusView focus = exhibit.GetComponent<EyeFocusView>();
            if (focus == null) {
                Debug.LogWarning($"{nameof(EyeAnatomySceneUpgrader)} found no {nameof(EyeFocusView)} on EyeAnatomyExhibit; " +
                    "Lens and Tear film will stay hard to see when focused.");
                return;
            }

            SerializedObject so = new SerializedObject(focus);
            SetIfPresent(so, "focusHighlightMaterial", EyeAnatomyAssetFactory.GetOrCreateFocusHighlightMaterial());
            SerializedProperty enable = so.FindProperty("highlightTransparentFocus");
            if (enable != null) {
                enable.boolValue = true;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Takes the interface out of the depth test so the anatomy can never cover it.
        /// </summary>
        private static void UpgradeCanvasDepth(GameObject ui) {
            GetOrAdd<UiAlwaysOnTop>(ui);
        }

        private static AnatomyAudioDirector BuildAudio() {
            GameObject root = FindRoot(AudioRootName);
            if (root == null) {
                root = new GameObject(AudioRootName);
                Undo.RegisterCreatedObjectUndo(root, "Create Audio root");
            }

            return GetOrAdd<AnatomyAudioDirector>(root);
        }

        /// <summary>
        /// Adds the near and far mote layers, the selection ring and the rising sparks.
        ///
        /// The near and far layers are the point of the exercise: a single sheet of motes reads as
        /// a flat backdrop on a stereo display however good the stereo is, and it is the parallax
        /// between layers at different depths that makes the box look like it has a volume in it.
        /// </summary>
        private static AnatomyParticleDirector UpgradeParticles(GameObject ambience) {
            AnatomyParticleDirector director = GetOrAdd<AnatomyParticleDirector>(ambience);
            ParticleSystem source = FindChildComponent<ParticleSystem>(ambience.transform, "MoteField");

            // Negative Z is in front of the display and positive is behind it, so the near layer
            // pops out towards the viewer and the far one sinks away. Sizes and alphas are graded
            // with depth, which is the cue that keeps the layers reading as separate planes.
            //
            // Rates are deliberately modest: everything here is drawn twice on a stereo panel, and
            // the existing field already contributes around 220 motes.
            ParticleSystem near = GetOrCreateLayer(ambience.transform, "MoteField_Near", source,
                depth: -0.10f, radius: 0.22f, size: 0.0026f, rate: 12f, speed: 0.012f, alpha: 0.5f);
            ParticleSystem far = GetOrCreateLayer(ambience.transform, "MoteField_Far", source,
                depth: 0.16f, radius: 0.40f, size: 0.0013f, rate: 16f, speed: 0.006f, alpha: 0.28f);

            // Well in front of the display, large and sparse. These are the strongest depth cue in
            // the scene: a handful of motes drifting close to the viewer's face produce far more
            // parallax per particle than any number of them out at the model's distance, and being
            // sparse they never obscure the anatomy.
            ParticleSystem foreground = GetOrCreateLayer(ambience.transform, "MoteField_Foreground", source,
                depth: -0.24f, radius: 0.30f, size: 0.0052f, rate: 4f, speed: 0.018f, alpha: 0.34f);

            ParticleSystem ring = GetOrCreateEffect(ambience.transform, "PopupRing", source, ShapeType.Ring);
            ParticleSystem sparks = GetOrCreateEffect(ambience.transform, "RiseSparks", source, ShapeType.Rise);

            SerializedObject so = new SerializedObject(director);
            SerializedProperty layers = so.FindProperty("depthLayers");
            layers.arraySize = 3;
            layers.GetArrayElementAtIndex(0).objectReferenceValue = near;
            layers.GetArrayElementAtIndex(1).objectReferenceValue = far;
            layers.GetArrayElementAtIndex(2).objectReferenceValue = foreground;
            so.FindProperty("popupRing").objectReferenceValue = ring;
            so.FindProperty("riseSparks").objectReferenceValue = sparks;
            so.ApplyModifiedPropertiesWithoutUndo();

            return director;
        }

        private enum ShapeType { Ring, Rise }

        private static ParticleSystem GetOrCreateLayer(Transform parent, string name, ParticleSystem template,
            float depth, float radius, float size, float rate, float speed, float alpha) {
            ParticleSystem system = FindChildComponent<ParticleSystem>(parent, name);
            if (system != null) {
                return system;
            }

            system = CloneOrCreate(parent, name, template);
            system.transform.localPosition = new Vector3(0f, 0f, depth);

            ParticleSystem.MainModule main = system.main;
            main.startSize = size;
            main.startSpeed = speed;
            main.startLifetime = 18f;
            main.startColor = new Color(0.62f, 0.86f, 1f, alpha);
            // rate * lifetime is the steady-state population; the cap has to clear it or particles
            // are silently culled and the field visibly thins out.
            main.maxParticles = Mathf.CeilToInt(rate * 18f * 1.2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;
            main.prewarm = true;
            main.loop = true;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = rate;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;

            return system;
        }

        private static ParticleSystem GetOrCreateEffect(Transform parent, string name, ParticleSystem template, ShapeType kind) {
            ParticleSystem system = FindChildComponent<ParticleSystem>(parent, name);
            if (system != null) {
                return system;
            }

            system = CloneOrCreate(parent, name, template);
            system.transform.localPosition = Vector3.zero;

            ParticleSystem.MainModule main = system.main;
            main.playOnAwake = false;
            main.loop = false;
            // Cloned from the ambient field, which prewarms. Prewarm on a non-looping system is
            // an invalid combination and Unity warns about it every time the scene loads.
            main.prewarm = false;
            main.maxParticles = 200;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = system.emission;
            // Emitted explicitly through ParticleSystem.Emit, so nothing streams on its own.
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;

            if (kind == ShapeType.Ring) {
                main.startLifetime = 0.55f;
                main.startSpeed = 0.20f;
                main.startSize = 0.0045f;
                main.startColor = new Color(1f, 0.86f, 0.45f, 0.9f);
                // A flat circle that fires outward in its own plane, which the director turns to
                // face the viewer before each emission.
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.004f;
                shape.radiusThickness = 0f;
                shape.arcMode = ParticleSystemShapeMultiModeValue.Random;
            } else {
                main.startLifetime = 1.1f;
                main.startSpeed = 0.07f;
                main.startSize = 0.0030f;
                main.startColor = new Color(0.75f, 0.93f, 1f, 0.85f);
                main.gravityModifier = -0.01f;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.012f;
            }

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = system.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
                AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

            return system;
        }

        /// <summary>
        /// Copies the existing mote system where there is one, so every new layer inherits its
        /// renderer settings and material rather than arriving as an untuned default.
        /// </summary>
        private static ParticleSystem CloneOrCreate(Transform parent, string name, ParticleSystem template) {
            GameObject created;

            if (template != null) {
                created = Object.Instantiate(template.gameObject, parent, false);
            } else {
                created = new GameObject(name);
                created.transform.SetParent(parent, false);
                created.AddComponent<ParticleSystem>();
            }

            created.name = name;
            Undo.RegisterCreatedObjectUndo(created, "Create " + name);
            return created.GetComponent<ParticleSystem>();
        }

        private struct NavigationUi {
            public Button Next;
            public Button Previous;
            public TextMeshProUGUI Counter;
        }

        /// <summary>
        /// Adds the Next and Back navigator across the bottom centre of the canvas, between the
        /// existing bottom-left and bottom-right button stacks.
        ///
        /// The buttons are cloned from the existing Expand button so they inherit its sprite,
        /// colours and font exactly - building them from scratch would mean reproducing a style
        /// that already exists and drifting from it the first time someone retouches it.
        /// </summary>
        private static NavigationUi BuildNavigationUi(GameObject ui) {
            NavigationUi result = new NavigationUi();

            Button template = FindChildComponent<Button>(ui.transform, "ExpandButton");
            if (template == null) {
                Debug.LogError($"{nameof(EyeAnatomySceneUpgrader)} could not find ExpandButton to clone; " +
                    "the Next and Back buttons were not created.");
                return result;
            }

            result.Previous = CloneButton(ui.transform, template, PreviousButtonName, "< Back", new Vector2(-250f, 32f));
            result.Next = CloneButton(ui.transform, template, NextButtonName, "Next >", new Vector2(250f, 32f));
            result.Counter = BuildCounter(ui.transform, template);

            // Every button gets the press and hover motion, including the ones that were already
            // there - on a stereo panel scale is the only hover cue that survives being looked at
            // from an angle with two eyes.
            AddMotion(template.gameObject, idlePulse: true);
            AddMotion(result.Previous, false);
            AddMotion(result.Next, false);
            AddMotion(FindChildComponent<Button>(ui.transform, "BackButton"), false);
            AddMotion(FindChildComponent<Button>(ui.transform, "ResetButton"), false);

            return result;
        }

        private static Button CloneButton(Transform parent, Button template, string name, string label, Vector2 anchoredPosition) {
            Button existing = FindChildComponent<Button>(parent, name);
            if (existing != null) {
                return existing;
            }

            GameObject created = Object.Instantiate(template.gameObject, parent, false);
            created.name = name;
            Undo.RegisterCreatedObjectUndo(created, "Create " + name);

            RectTransform rect = created.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(200f, 88f);
            rect.anchoredPosition = anchoredPosition;

            TextMeshProUGUI text = created.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null) {
                text.text = label;
            }

            Button button = created.GetComponent<Button>();
            ClearPersistentCalls(button);
            return button;
        }

        private static TextMeshProUGUI BuildCounter(Transform parent, Button template) {
            TextMeshProUGUI existing = FindChildComponent<TextMeshProUGUI>(parent, PartCounterName);
            if (existing != null) {
                return existing;
            }

            TextMeshProUGUI templateText = template.GetComponentInChildren<TextMeshProUGUI>(true);
            GameObject created = new GameObject(PartCounterName, typeof(RectTransform));
            created.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(created, "Create " + PartCounterName);

            RectTransform rect = created.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(280f, 88f);
            rect.anchoredPosition = new Vector2(0f, 32f);

            TextMeshProUGUI text = created.AddComponent<TextMeshProUGUI>();
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;

            if (templateText != null) {
                text.font = templateText.font;
                text.fontSize = templateText.fontSize;
                text.color = templateText.color;
            }

            text.text = "-";
            return text;
        }

        private static void AddMotion(Button button, bool idlePulse) {
            if (button != null) {
                AddMotion(button.gameObject, idlePulse);
            }
        }

        private static void AddMotion(GameObject target, bool idlePulse) {
            UiButtonMotion motion = GetOrAdd<UiButtonMotion>(target);
            SerializedObject so = new SerializedObject(motion);
            so.FindProperty("idlePulse").boolValue = idlePulse;

            Graphic graphic = target.GetComponent<Graphic>();
            if (graphic != null) {
                so.FindProperty("tintTarget").objectReferenceValue = graphic;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireExhibit(GameObject exhibit, KmaxStylus stylus, AnatomyAudioDirector audio,
            AnatomyParticleDirector particles, NavigationUi navigation) {
            ViewerFlyController fly = exhibit.GetComponent<ViewerFlyController>();
            EyeAnatomyController controller = exhibit.GetComponent<EyeAnatomyController>();

            if (controller == null) {
                Debug.LogError($"{nameof(EyeAnatomySceneUpgrader)} found no {nameof(EyeAnatomyController)} on " +
                    "EyeAnatomyExhibit; the new buttons and audio were not wired.");
                return;
            }

            SerializedObject controllerSo = new SerializedObject(controller);
            SetIfPresent(controllerSo, "nextButton", navigation.Next);
            SetIfPresent(controllerSo, "previousButton", navigation.Previous);
            SetIfPresent(controllerSo, "partCounterLabel", navigation.Counter);
            SetIfPresent(controllerSo, "audioDirector", audio);
            SetIfPresent(controllerSo, "particles", particles);
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            AnatomyStylusInput input = GetOrAdd<AnatomyStylusInput>(exhibit);
            SerializedObject inputSo = new SerializedObject(input);
            SetIfPresent(inputSo, "stylus", stylus);
            SetIfPresent(inputSo, "flyController", fly);
            SetIfPresent(inputSo, "exhibitController", controller);

            if (fly != null) {
                SerializedObject flySo = new SerializedObject(fly);
                SerializedProperty rigRoot = flySo.FindProperty("rigRoot");
                if (rigRoot != null) {
                    SetIfPresent(inputSo, "rigRoot", rigRoot.objectReferenceValue);
                }
            }

            inputSo.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetIfPresent(SerializedObject so, string fieldName, Object value) {
            SerializedProperty property = so.FindProperty(fieldName);
            if (property == null) {
                Debug.LogWarning($"{nameof(EyeAnatomySceneUpgrader)} found no field '{fieldName}' on {so.targetObject.GetType().Name}.");
                return;
            }

            property.objectReferenceValue = value;
        }

        private static void ClearPersistentCalls(Button button) {
            SerializedObject so = new SerializedObject(button);
            SerializedProperty calls = so.FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            if (calls != null) {
                calls.ClearArray();
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static GameObject FindRoot(string name) {
            GameObject[] roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) {
                if (roots[i].name == name) {
                    return roots[i];
                }
            }

            return null;
        }

        private static Camera FindRigCamera(GameObject rig) {
            Camera[] cameras = rig.GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cameras.Length; i++) {
                // The SDK spawns 'left' and 'right' sub-cameras under the root camera at runtime and
                // disables the root's own Camera component; the root is still the one to bind to.
                if (cameras[i].name != "left" && cameras[i].name != "right") {
                    return cameras[i];
                }
            }

            return cameras.Length > 0 ? cameras[0] : null;
        }

        private static GameObject FindOrCreateChild(Transform parent, string name) {
            Transform existing = parent.Find(name);
            if (existing != null) {
                return existing.gameObject;
            }

            GameObject created = new GameObject(name);
            created.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(created, "Create " + name);
            return created;
        }

        private static T FindChildComponent<T>(Transform parent, string name) where T : Component {
            T[] found = parent.GetComponentsInChildren<T>(true);
            for (int i = 0; i < found.Length; i++) {
                if (found[i].name == name) {
                    return found[i];
                }
            }

            return null;
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component {
            T existing = target.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(target);
        }
    }
}
