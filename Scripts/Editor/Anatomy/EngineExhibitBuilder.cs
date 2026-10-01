using System.Collections.Generic;
using KmaxXR;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ViitorCloud.KmaxDisplayExample.Editor {
    /// <summary>
    /// Turns <c>VirtualExhibition WR.unity</c> into a Kmax exhibit built on the same runtime stack
    /// as the eye: the model pulls apart, each assembly is catalogued and selectable, and the
    /// stereo rig, stylus, navigator and attract tour all behave as they do next door.
    ///
    /// Everything the eye already solves is reused rather than rewritten - the whole of
    /// <c>Scripts/Runtime</c> is model-agnostic, and the generic build steps on
    /// <see cref="EyeAnatomySceneUpgrader"/> are called directly. What is engine-specific is the
    /// teardown itself, the part catalogue, and the fact that this scene starts with none of the
    /// exhibit scaffolding, so the roots are created here rather than upgraded in place.
    ///
    /// Like the eye's command this finds before it creates, so running it twice changes nothing.
    /// </summary>
    public static class EngineExhibitBuilder {
        private const string MenuPath = "Kmax/Engine Exhibit/Set Up Engine Exhibit";
        private const string ModuleRoot = "Assets/Games/kmax-display-example";
        private const string EngineRoot = ModuleRoot + "/CarEngineAnimated - i4";
        // The scene lives under Scenes/ with the other two; only the model, its materials and its
        // textures stayed in the folder the engine was delivered in.
        private const string ScenePath = ModuleRoot + "/Scenes/VirtualExhibition WR.unity";
        private const string CatalogPath = ModuleRoot + "/Data/EngineCatalog.asset";
        private const string PoseSetPath = ModuleRoot + "/Data/EngineExplodePoses.asset";
        private const string XrRigPrefabPath = ModuleRoot + "/Plugins/Kmax/com.kmax.xr.core/Editor Resources/XRRig.prefab";

        private const string ExhibitName = "EngineExhibit";
        private const string PivotName = "EngineModelPivot";
        private const string FocusAnchorName = "FocusAnchor";
        private const string RigName = "XRRig";
        private const string UiName = "UI";
        private const string AmbienceName = "Ambience";
        private const string EventSystemName = "EventSystem";
        private const string LegacyCanvasName = "Canvas - Engine (1)";
        private const string LegacyLightName = "Point Light";
        private const string ModelWrapperPath = "Engine/Enginei4 (1)";
        private const string ModelRootName = "Enginei4";

        /// <summary>
        /// Longest edge of the assembled engine once it is in the exhibit, in metres.
        ///
        /// The model ships at life size - 1.50 m along the crank - against a virtual screen
        /// 0.345 m wide. This is chosen from the exploded extent rather than the assembled one:
        /// the teardown spreads the parts to roughly 1.5x the assembled length, so 0.16 m closed
        /// lands around 0.24 m open, which still clears the width of the screen with a margin.
        /// Sizing to fill the screen when closed would push half the teardown out of frame.
        /// </summary>
        private const float TargetAssembledSize = 0.16f;

        /// <summary>
        /// One part of the engine, where it travels when the engine comes apart, and what to say
        /// about it if it is one of the labelled assemblies.
        ///
        /// <see cref="Offset"/> is in the model root's own space, in the model's native metres -
        /// the same units the FBX is authored in, not the shrunken exhibit units. The builder
        /// converts it into each part's parent space, which matters because <c>EngineBlock</c> and
        /// <c>CylinderHead</c> both carry a 270 degree X rotation: a naive local-space Y offset on
        /// their children travels along world -Z and the oil pan drops out of the side of the
        /// engine rather than off the bottom of it.
        /// </summary>
        private class TeardownStep {
            public string Path;
            public Vector3 Offset;
            public string DisplayName;
            public string Description;

            public TeardownStep(string path, Vector3 offset) {
                Path = path;
                Offset = offset;
                DisplayName = null;
                Description = null;
            }

            public TeardownStep(string path, Vector3 offset, string displayName, string description) {
                Path = path;
                Offset = offset;
                DisplayName = displayName;
                Description = description;
            }

            public bool IsCatalogued {
                get { return !string.IsNullOrEmpty(DisplayName); }
            }
        }

        /// <summary>
        /// The UI objects the controller has to be handed once the canvas is built.
        /// </summary>
        private class ExhibitUi {
            public Button Expand;
            public Button Back;
            public Button Reset;
            public Button Next;
            public Button Previous;
            public Button NextScene;
            public TextMeshProUGUI ExpandLabel;
            public TextMeshProUGUI Counter;
            public TextMeshProUGUI AttractPrompt;
            public AnatomyInfoPanel InfoPanel;
            public Button Transparency;
            public TextMeshProUGUI TransparencyLabel;
            public Button[] Variations = new Button[0];
        }

        [MenuItem(MenuPath)]
        public static void Run() {
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                Debug.LogError($"{nameof(EngineExhibitBuilder)} must not run in play mode. The explode " +
                    "poses are baked from the parts' current positions, and in play mode those are " +
                    "wherever the transition left them rather than the authored assembled pose.");
                return;
            }

            if (!EnsureSceneOpen()) {
                return;
            }

            Transform modelWrapper = ResolveModelWrapper();
            if (modelWrapper == null) {
                return;
            }

            Transform modelRoot = modelWrapper.Find(ModelRootName);
            if (modelRoot == null) {
                Debug.LogError($"{nameof(EngineExhibitBuilder)} found no '{ModelRootName}' under " +
                    $"'{ModelWrapperPath}'. Nothing was changed.");
                return;
            }

            MonoBehaviour machinery = FindMachinery(modelRoot);

            GameObject exhibit = FindOrCreateRoot(ExhibitName);
            GameObject ui = FindOrCreateRoot(UiName);
            GameObject ambience = FindOrCreateRoot(AmbienceName);
            GameObject eventSystem = EnsureEventSystemRoot();
            GameObject rig = EnsureRig();

            if (rig == null) {
                return;
            }

            Camera camera = EyeAnatomySceneUpgrader.FindRigCamera(rig);
            if (camera == null) {
                Debug.LogError($"{nameof(EngineExhibitBuilder)} found no camera under '{RigName}'. Nothing else was changed.");
                return;
            }

            Transform pivot = NormalizeModel(exhibit, modelWrapper, modelRoot);
            Transform focusAnchor = EyeAnatomySceneUpgrader.FindOrCreateChild(exhibit.transform, FocusAnchorName).transform;
            focusAnchor.localPosition = Vector3.zero;

            EyeExplodePoseSet poseSet = BuildPoseSet(modelRoot);
            EyeAnatomyCatalog catalog = BuildCatalog();

            RetireLegacyPresentation();
            ConfigureMachinery(machinery);

            // Before the lighting, exactly as in the eye's command: the intensities below are only
            // meaningful once a renderer that applies lights is in place and a tonemapper is
            // rolling the highlights off.
            EyeAnatomySceneUpgrader.UpgradeRenderPipeline();
            EyeAnatomySceneUpgrader.UpgradeEnvironment();
            EyeAnatomySceneUpgrader.UpgradePostProcessing(rig, exhibit);
            BuildLighting();

            EyeAnatomySceneUpgrader.UpgradeEventSystem(eventSystem);
            EyeAnatomySceneUpgrader.UpgradeCamera(camera);
            EyeAnatomySceneUpgrader.UpgradeHotspotPrefab();
            EyeAnatomySceneUpgrader.RemoveDuplicatePens(rig);
            EyeAnatomySceneUpgrader.UpgradeMaterialsDoubleSided();

            KmaxStylus stylus = EyeAnatomySceneUpgrader.BuildStylus(rig, camera);
            AnatomyAudioDirector audio = EyeAnatomySceneUpgrader.BuildAudio();
            AnatomyParticleDirector particles = EyeAnatomySceneUpgrader.UpgradeParticles(ambience);
            ExhibitUi exhibitUi = BuildUi(ui, camera, machinery);

            WireExhibit(exhibit, pivot, modelWrapper, modelRoot, focusAnchor, rig, camera,
                poseSet, catalog, machinery, stylus, audio, particles, exhibitUi);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            Debug.Log($"{nameof(EngineExhibitBuilder)} finished. The engine is on the exhibit stack: " +
                "press Play, then Expand to pull it apart.");
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
        /// Finds the model wherever it currently sits, because a second run finds it already
        /// reparented under the exhibit rather than under its original <c>Engine</c> root.
        /// </summary>
        private static Transform ResolveModelWrapper() {
            GameObject direct = GameObject.Find(ModelWrapperPath);
            if (direct != null) {
                return direct.transform;
            }

            GameObject[] roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) {
                Transform[] all = roots[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < all.Length; j++) {
                    if (all[j].name == ModelRootName && all[j].parent != null) {
                        return all[j].parent;
                    }
                }
            }

            Debug.LogError($"{nameof(EngineExhibitBuilder)} could not find the engine model " +
                $"('{ModelRootName}' under a wrapper). Nothing was changed.");
            return null;
        }

        /// <summary>
        /// The model's own animation driver, held as a plain behaviour because <c>Enginei4</c>
        /// lives in <c>Assembly-CSharp</c> and this assembly cannot name that type without closing
        /// a reference cycle.
        /// </summary>
        private static MonoBehaviour FindMachinery(Transform modelRoot) {
            MonoBehaviour[] behaviours = modelRoot.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++) {
                if (behaviours[i] != null && behaviours[i].GetType().Name == "Enginei4") {
                    return behaviours[i];
                }
            }

            Debug.LogWarning($"{nameof(EngineExhibitBuilder)} found no Enginei4 component on " +
                $"'{modelRoot.name}'. The engine will pull apart but nothing will be running to pause.");
            return null;
        }

        /// <summary>
        /// Moves the engine onto the exhibit's own pivot, centres it on the origin and scales it to
        /// exhibit size.
        ///
        /// All three are required, not cosmetic. <see cref="ViewerFlyController"/> orbits
        /// <c>focalCenter</c>, which it resets to <c>Vector3.zero</c>, and the whole eye exhibit
        /// sits at the origin for that reason - the engine ships 270 m out along Z, where the rig
        /// would orbit empty space. The scale matters just as much: the model is life size against
        /// a 0.345 m virtual screen.
        /// </summary>
        private static Transform NormalizeModel(GameObject exhibit, Transform modelWrapper, Transform modelRoot) {
            GameObject pivotObject = EyeAnatomySceneUpgrader.FindOrCreateChild(exhibit.transform, PivotName);
            Transform pivot = pivotObject.transform;
            pivot.localPosition = Vector3.zero;
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;

            if (modelWrapper.parent != pivot) {
                Undo.SetTransformParent(modelWrapper, pivot, "Reparent engine model");
            }

            modelWrapper.localRotation = Quaternion.identity;

            Bounds native;
            if (!TryMeasure(modelRoot, out native)) {
                Debug.LogError($"{nameof(EngineExhibitBuilder)} found no renderers under '{modelRoot.name}'; " +
                    "the model could not be scaled.");
                return pivot;
            }

            // Measured at whatever scale the wrapper currently carries, so a second run rescales
            // from the size it is now rather than compounding the first run's shrink.
            float currentScale = modelWrapper.localScale.x;
            float longestEdge = Mathf.Max(native.size.x, Mathf.Max(native.size.y, native.size.z));
            if (longestEdge <= Mathf.Epsilon || currentScale <= Mathf.Epsilon) {
                Debug.LogError($"{nameof(EngineExhibitBuilder)} measured a degenerate model size {native.size}; " +
                    "the model could not be scaled.");
                return pivot;
            }

            float nativeLongestEdge = longestEdge / currentScale;
            float scale = TargetAssembledSize / nativeLongestEdge;
            modelWrapper.localScale = new Vector3(scale, scale, scale);

            // Centre on the bounds rather than on the wrapper's own origin: the model's pivot sits
            // well outside its geometry, so centring the transform leaves the engine off to one side.
            Bounds scaled;
            if (TryMeasure(modelRoot, out scaled)) {
                modelWrapper.position -= scaled.center;
            }

            EditorUtility.SetDirty(modelWrapper);
            return pivot;
        }

        private static bool TryMeasure(Transform root, out Bounds bounds) {
            bounds = new Bounds(root.position, Vector3.zero);
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool found = false;

            for (int i = 0; i < renderers.Length; i++) {
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
        /// The teardown, laid out the way an engine actually comes apart: the block stays put, the
        /// head stack lifts off the top of it, the sump and crank drop out of the bottom, the two
        /// manifolds part to their own sides, and the gearbox pulls off the back.
        ///
        /// Several steps carry no label. They are here because the teardown would read as broken
        /// without them - the valves have to travel with the camshafts that open them, the plug
        /// leads with the plugs - but they are not assemblies a viewer would ask to be told about,
        /// and eighteen badges on a model this size is a thicket rather than a guide.
        /// </summary>
        private static TeardownStep[] GetTeardown() {
            return new TeardownStep[] {
                new TeardownStep("EngineBlock", Vector3.zero,
                    "Engine Block",
                    "The casting everything else bolts to. It carries the four cylinder bores, the water " +
                    "jacket around them and the main bearing saddles the crankshaft runs in. Nothing here " +
                    "moves, which is why it is the one part left standing when the rest is taken off it."),

                new TeardownStep("EngineBlock/OilPan", new Vector3(0f, -0.34f, 0f),
                    "Oil Pan",
                    "The sump, bolted to the bottom of the block. Oil drains back into it under gravity and " +
                    "the pump draws from it, so it is both the reservoir and the lowest point in the engine."),

                new TeardownStep("Crankshaft", new Vector3(0f, -0.22f, -0.18f),
                    "Crankshaft & Connecting Rods",
                    "Turns the pistons' up-and-down travel into rotation. The four throws are set 180 degrees " +
                    "apart in the 1-3-4-2 firing order, so one piston is always on a power stroke. This is the " +
                    "shaft every other moving part is timed against."),

                new TeardownStep("Crankshaft/Flywheel", new Vector3(0f, 0f, -0.26f),
                    "Clutch & Flywheel",
                    "The flywheel's mass carries the crank between power strokes and smooths the pulses into " +
                    "steady rotation. The clutch on its face is what couples all of that to the gearbox, or " +
                    "lets it spin free."),

                new TeardownStep("Pistons", new Vector3(0f, 0.10f, 0f)),

                new TeardownStep("CylinderHead", new Vector3(0f, 0.22f, 0f),
                    "Cylinder Head",
                    "Closes the top of the bores and forms the combustion chambers. It carries the valve " +
                    "seats, the intake and exhaust ports feeding them, and the passages that let coolant up " +
                    "out of the block."),

                new TeardownStep("CylinderHead/CylinderHeadCovers", new Vector3(0f, 0.18f, 0f),
                    "Cylinder Head Cover",
                    "The cam cover. It seals the top of the head and keeps the oil thrown off the camshafts " +
                    "inside the engine. Usually the first thing off when the valvetrain needs looking at."),

                new TeardownStep("CylinderHead/SparkPlugWires", new Vector3(-0.14f, 0.27f, 0f)),

                new TeardownStep("Camshafts", new Vector3(0f, 0.32f, 0f),
                    "Camshafts & Valvetrain",
                    "Twin overhead camshafts, one for the intake valves and one for the exhaust. They are " +
                    "geared to run at half crankshaft speed, because each valve opens once every two " +
                    "revolutions, and their lobe profile is what sets when and how far each valve lifts."),

                new TeardownStep("IntakeValves", new Vector3(0.05f, 0.16f, 0f)),
                new TeardownStep("IntakeValvesSprings", new Vector3(0.05f, 0.16f, 0f)),
                new TeardownStep("ExhaustValves", new Vector3(-0.05f, 0.16f, 0f)),
                new TeardownStep("ExhaustValvesSprings", new Vector3(-0.05f, 0.16f, 0f)),

                new TeardownStep("SparkPlugs", new Vector3(0f, 0.25f, 0f),
                    "Spark Plugs & Wires",
                    "One plug per cylinder, threaded through the head into the roof of each combustion " +
                    "chamber. The leads carry the high-tension pulse that jumps the gap and lights the " +
                    "charge, timed off the distributor gear on the head."),

                new TeardownStep("IntakeManifolds", new Vector3(0.46f, 0.06f, 0f),
                    "Intake Manifold",
                    "Splits the incoming air between the four cylinders and holds the throttle body and air " +
                    "filter. Runner length is tuned rather than arbitrary - the pressure waves in these pipes " +
                    "help push the charge in as each intake valve closes."),

                new TeardownStep("ExhaustManifolds", new Vector3(-0.50f, 0f, 0f),
                    "Exhaust Manifold",
                    "Collects the burnt gas from all four exhaust ports into one pipe. On the turbocharged " +
                    "build of this engine it is also what feeds the turbine, which is why swapping it swaps " +
                    "the whole turbo assembly with it."),

                new TeardownStep("FuelRail", new Vector3(0.30f, 0.24f, 0f),
                    "Fuel Rail & Injectors",
                    "Holds fuel at regulated pressure across all four injectors so each one delivers the same " +
                    "amount for the same open time. The injectors spray into the intake ports just behind the " +
                    "valves."),

                new TeardownStep("Gearbox", new Vector3(0f, 0f, -0.54f),
                    "Gearbox",
                    "Five forward ratios on two shafts. The primary shaft turns with the clutch and the gears " +
                    "on the secondary shaft step that down - from 1.47 times crank speed in the lowest ratio " +
                    "to about half in the highest."),

                new TeardownStep("TimingBelt", new Vector3(0f, 0f, 0.30f))
            };
        }

        /// <summary>
        /// Bakes the assembled and pulled-apart positions of every moving part.
        ///
        /// The engine has nothing to bake from - unlike the eye's model, whose 23 clips the pose
        /// baker samples, both engine FBXs are static and the one <c>Take 001</c> in
        /// <c>Engine_opt.FBX</c> is a two-key stub on the root. Everything that moves in this model
        /// is procedural code in <c>Enginei4.Update</c>, so the teardown is authored here instead.
        ///
        /// The closed pose is read from the scene rather than stored, which is what makes a re-run
        /// safe: outside play mode the parts are always sitting at their authored assembled
        /// positions, so a second run bakes exactly the same numbers as the first.
        /// </summary>
        private static EyeExplodePoseSet BuildPoseSet(Transform modelRoot) {
            EyeExplodePoseSet poseSet = LoadOrCreateAsset<EyeExplodePoseSet>(PoseSetPath);
            TeardownStep[] steps = GetTeardown();

            // Balanced before baking, or the teardown walks out of frame - see MeasureTeardownDrift.
            Vector3 drift = SolveTeardownDrift(modelRoot, steps);
            List<EyePartPose> poses = new List<EyePartPose>(steps.Length);

            for (int i = 0; i < steps.Length; i++) {
                Transform part = modelRoot.Find(steps[i].Path);
                if (part == null) {
                    Debug.LogError($"{nameof(EngineExhibitBuilder)} could not resolve teardown path " +
                        $"'{steps[i].Path}' under '{modelRoot.name}'; that part will not move.");
                    continue;
                }

                Vector3 closed = part.localPosition;
                Vector3 open = closed + ToParentSpace(modelRoot, part, Correct(modelRoot, part, steps[i].Offset, drift));
                poses.Add(new EyePartPose(steps[i].Path, closed, open));
            }

            poseSet.SetPoses(poses.ToArray());
            EditorUtility.SetDirty(poseSet);

            Debug.Log($"{nameof(EngineExhibitBuilder)} baked {poses.Count} teardown poses and took " +
                $"{drift.ToString("F3")} m of drift out of them.");
            return poseSet;
        }

        /// <summary>
        /// Measures how far the teardown shifts the engine's centre, so it can be taken back out
        /// again.
        ///
        /// An engine does not come apart symmetrically. Far more of it lifts off the top than drops
        /// out of the bottom, and the gearbox alone travels half a model-length backwards, so the
        /// authored offsets carry the whole model up and back as it opens. Measured on the first
        /// build that was 0.046 m up and 0.049 m back at exhibit scale - enough to push the cam
        /// cover 0.04 m above a virtual screen only 0.194 m tall, with the engine still comfortably
        /// small enough to fit. The extent was never the problem; where it sat was.
        ///
        /// Subtracting the drift from every offset keeps the pulled-apart engine centred on the
        /// origin the rig orbits. It costs the block its place as a fixed anchor - it now sinks
        /// slightly as the engine opens - which is a fair trade for a teardown that stays in frame,
        /// and it means the offsets above can be re-authored freely without the framing being
        /// re-derived by hand each time.
        ///
        /// The parts are moved to measure and put straight back, so the scene is left as it was
        /// found and the closed pose is still read from the authored positions.
        ///
        /// Solved rather than measured once, because the correction changes its own answer: the
        /// bounds are defined by whichever parts are furthest out, and shifting everything by the
        /// first measurement can hand that job to a different part. One pass left the teardown
        /// 0.8 mm below the bottom of the screen; the loop closes that out in two or three.
        /// </summary>
        private static Vector3 SolveTeardownDrift(Transform modelRoot, TeardownStep[] steps) {
            Vector3 correction = Vector3.zero;

            for (int pass = 0; pass < 5; pass++) {
                Vector3 residual = MeasureTeardownDrift(modelRoot, steps, correction);

                // A tenth of a millimetre at model scale, which is a hundredth of one on the panel.
                if (residual.magnitude < 0.0001f) {
                    break;
                }

                correction += residual;
            }

            return correction;
        }

        private static Vector3 MeasureTeardownDrift(Transform modelRoot, TeardownStep[] steps, Vector3 correction) {
            Bounds assembled;
            if (!TryMeasure(modelRoot, out assembled)) {
                return Vector3.zero;
            }

            Transform[] parts = new Transform[steps.Length];
            Vector3[] restored = new Vector3[steps.Length];

            for (int i = 0; i < steps.Length; i++) {
                parts[i] = modelRoot.Find(steps[i].Path);
                if (parts[i] == null) {
                    continue;
                }

                restored[i] = parts[i].localPosition;
                parts[i].localPosition = restored[i] +
                    ToParentSpace(modelRoot, parts[i], Correct(modelRoot, parts[i], steps[i].Offset, correction));
            }

            Bounds exploded;
            bool measured = TryMeasure(modelRoot, out exploded);

            for (int i = 0; i < steps.Length; i++) {
                if (parts[i] == null) {
                    continue;
                }

                parts[i].localPosition = restored[i];
            }

            if (!measured) {
                return Vector3.zero;
            }

            // Back into the model root's own space and native metres, which is what the offsets
            // above are written in.
            return modelRoot.InverseTransformVector(exploded.center - assembled.center);
        }

        /// <summary>
        /// Applies the balance correction to one step.
        ///
        /// The correction is a rigid shift of the whole teardown, so only the groups sitting
        /// directly under the model root carry it - everything below them inherits it through the
        /// hierarchy. Applying it to a nested step as well as to its parent shifts that part twice:
        /// the oil pan would drop by the correction once with the block it hangs off and again on
        /// its own account, and the solve would be chasing a target it was itself moving.
        /// </summary>
        private static Vector3 Correct(Transform modelRoot, Transform part, Vector3 authored, Vector3 correction) {
            if (part.parent != modelRoot) {
                return authored;
            }

            return authored - correction;
        }

        /// <summary>
        /// Converts an offset written in the model root's space into the space of one part's
        /// parent.
        ///
        /// Via world space, because a part's parent may be rotated relative to the model root -
        /// <c>EngineBlock</c> and <c>CylinderHead</c> both carry 270 degrees about X, so a local
        /// -Y offset on the oil pan would send it out of the side of the engine rather than off
        /// the bottom. Both conversions run through the same scale chain above the model root, so
        /// the exhibit's own scale cancels and the offsets stay in the model's native metres.
        /// </summary>
        private static Vector3 ToParentSpace(Transform modelRoot, Transform part, Vector3 rootSpaceOffset) {
            if (rootSpaceOffset == Vector3.zero) {
                return Vector3.zero;
            }

            Vector3 worldOffset = modelRoot.TransformVector(rootSpaceOffset);
            return part.parent.InverseTransformVector(worldOffset);
        }

        private static EyeAnatomyCatalog BuildCatalog() {
            EyeAnatomyCatalog catalog = LoadOrCreateAsset<EyeAnatomyCatalog>(CatalogPath);
            TeardownStep[] steps = GetTeardown();
            List<EyePartDefinition> parts = new List<EyePartDefinition>();

            for (int i = 0; i < steps.Length; i++) {
                if (!steps[i].IsCatalogued) {
                    continue;
                }

                parts.Add(new EyePartDefinition(steps[i].DisplayName, steps[i].Description, steps[i].Path));
            }

            catalog.SetParts(parts.ToArray());
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static T LoadOrCreateAsset<T>(string path) where T : ScriptableObject {
            return EyeAnatomyAssetFactory.LoadOrCreate<T>(path);
        }

        /// <summary>
        /// Switches off the screen-space interface and the single point light the model shipped
        /// with, rather than deleting either.
        ///
        /// The old canvas still carries the RPM and zoom sliders, the four tuning variations and
        /// the per-part toggles, none of which the exhibit interface exposes yet. Disabling it
        /// keeps that wiring intact and re-enableable; deleting it would throw the only reference
        /// to those features away. <c>Enginei4</c> now treats both sliders as optional, so nothing
        /// breaks when they stop being active.
        /// </summary>
        private static void RetireLegacyPresentation() {
            GameObject canvas = GameObject.Find("Engine/" + LegacyCanvasName);
            if (canvas != null && canvas.activeSelf) {
                Undo.RecordObject(canvas, "Disable legacy engine canvas");
                canvas.SetActive(false);
            }

            GameObject light = GameObject.Find("Engine/" + LegacyLightName);
            if (light != null && light.activeSelf) {
                Undo.RecordObject(light, "Disable legacy point light");
                light.SetActive(false);
            }
        }

        /// <summary>
        /// Cuts the model's driver loose from the interface it shipped with.
        ///
        /// Both slider references have to be cleared, not just hidden. <c>Enginei4</c> writes its
        /// parent's local scale from <c>ZoomSlider</c> every frame, and a slider on a deactivated
        /// canvas is still a live reference - the object exists, so the null check passes and the
        /// write goes ahead. Left alone it puts the wrapper back to the slider's value of 1 on the
        /// first frame of play, throwing away the scale that fits the engine to the display: the
        /// pulled-apart engine measured 2.24 m across a screen 0.345 m wide.
        ///
        /// With both cleared the driver falls back to its own serialised RPM, which is set here so
        /// the speed is deliberate rather than inherited from wherever the slider was left.
        /// </summary>
        private static void ConfigureMachinery(MonoBehaviour machinery) {
            if (machinery == null) {
                return;
            }

            SerializedObject so = new SerializedObject(machinery);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "RPMSlider", null);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "ZoomSlider", null);
            SetFloat(so, "RPM", 10f);

            // The casing fades to this alpha rather than to the 0 the model shipped with, which
            // made the outer parts vanish outright. A faint shell left behind is what makes it
            // read as seeing *into* an engine rather than as half the engine being deleted.
            SetFloat(so, "tweaks.TransparencyValue", 0.15f);

            // Named from what each variation actually swaps in - hotter cams and a different
            // filter, individual throttle bodies, or the whole turbo assembly.
            SetStringArray(so, "variationNames", new string[] { "Stock", "Sport", "Throttle bodies", "Turbo" });
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// A three-point rig, driven harder than the eye's.
        ///
        /// The eye is near-white tissue and clips the moment the key is raised, which is why its
        /// rig sits close to 1. Cast iron, aluminium and blued steel are far darker and mostly
        /// specular, so they need more light to read at all, and the rim matters more than it does
        /// on tissue: it is what separates one dark metal part from the dark metal part behind it
        /// once the engine is apart and the silhouettes start overlapping.
        /// </summary>
        private static void BuildLighting() {
            Light key = EyeAnatomySceneUpgrader.EnsureLight("Key Light", LightType.Directional);
            key.transform.SetPositionAndRotation(new Vector3(0.3f, 0.4f, -0.4f), Quaternion.Euler(42f, 152f, 0f));
            key.color = new Color(1f, 0.97f, 0.92f);
            key.intensity = 1.5f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.65f;
            key.enabled = true;

            Light fill = EyeAnatomySceneUpgrader.EnsureLight("Fill Light", LightType.Directional);
            fill.transform.SetPositionAndRotation(new Vector3(-0.4f, 0.2f, -0.35f), Quaternion.Euler(20f, 235f, 0f));
            fill.color = new Color(0.82f, 0.88f, 1f);
            fill.intensity = 0.7f;
            fill.shadows = LightShadows.None;
            fill.enabled = true;

            Light rim = EyeAnatomySceneUpgrader.EnsureLight("Rim Light", LightType.Directional);
            rim.transform.SetPositionAndRotation(new Vector3(0f, 0.35f, 0.5f), Quaternion.Euler(28f, 345f, 0f));
            rim.color = new Color(0.74f, 0.83f, 1f);
            rim.intensity = 0.95f;
            rim.shadows = LightShadows.None;
            rim.enabled = true;
        }

        private static GameObject FindOrCreateRoot(string name) {
            GameObject[] roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) {
                if (roots[i].name == name) {
                    return roots[i];
                }
            }

            GameObject created = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(created, "Create " + name);
            return created;
        }

        private static GameObject EnsureEventSystemRoot() {
            EventSystem existing = Object.FindFirstObjectByType<EventSystem>();
            if (existing != null) {
                return existing.gameObject;
            }

            GameObject created = FindOrCreateRoot(EventSystemName);
            if (created.GetComponent<EventSystem>() == null) {
                Undo.AddComponent<EventSystem>(created);
            }

            return created;
        }

        private static GameObject EnsureRig() {
            GameObject[] roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) {
                if (roots[i].GetComponentInChildren<XRRig>(true) != null) {
                    return roots[i];
                }
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XrRigPrefabPath);
            if (prefab == null) {
                Debug.LogError($"{nameof(EngineExhibitBuilder)} could not load the Kmax rig from " +
                    $"'{XrRigPrefabPath}'. The scene has no stereo rig and nothing else was changed.");
                return null;
            }

            GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.name = RigName;
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Undo.RegisterCreatedObjectUndo(rig, "Create " + RigName);
            return rig;
        }

        /// <summary>
        /// Builds the interface.
        ///
        /// World space, not screen space overlay, and pinned to the virtual screen by the SDK's
        /// <see cref="UIScaler"/>, which rewrites the canvas pose and size every frame from the
        /// rig's own screen plane. That is what makes it behave like an overlay - always square to
        /// the viewer, always the same place on the panel - while still being geometry that both
        /// eye cameras render.
        ///
        /// > A true <c>ScreenSpaceOverlay</c> canvas cannot be used here. <c>VRRenderer</c> renders
        /// > side by side, giving the left eye the viewport (0, 0, 0.5, 1) and the right eye
        /// > (0.5, 0, 0.5, 1). An overlay canvas ignores camera viewports entirely and is drawn
        /// > once across the whole framebuffer, so it would span both eye images and never fuse.
        ///
        /// Without the scaler the canvas simply sits at the world origin with no rotation, and
        /// tilts away as soon as the rig orbits - which is what put the buttons at an angle in the
        /// middle of the scene.
        /// </summary>
        private static ExhibitUi BuildUi(GameObject ui, Camera camera, MonoBehaviour machinery) {
            ExhibitUi result = new ExhibitUi();
            RectTransform rect = ExhibitUiFactory.BuildWorldCanvas(ui, camera);

            Vector2 bottomLeft = new Vector2(0f, 0f);
            Vector2 bottomCenter = new Vector2(0.5f, 0f);
            Vector2 bottomRight = new Vector2(1f, 0f);

            BuildTitle(rect);

            // Bottom-Left: Expand & Back
            result.Expand = BuildButton(rect, "ExpandButton", "Pull engine apart",
                bottomLeft, new Vector2(24f, 24f), new Vector2(166f, 36f), 14f);
            result.Back = BuildButton(rect, "BackButton", "Back",
                bottomLeft, new Vector2(24f, 68f), new Vector2(132f, 32f), 13.5f);

            // Bottom-Center: < Back, Counter, Next >, AttractPrompt
            result.Previous = BuildButton(rect, "PreviousButton", "< Back",
                bottomCenter, new Vector2(-116f, 24f), new Vector2(84f, 34f), 14f);
            result.Counter = BuildLabel(rect, "PartCounter", "-", 14f,
                bottomCenter, new Vector2(0f, 24f), new Vector2(120f, 34f), TextAlignmentOptions.Center);
            result.Counter.color = new Color(0.76f, 0.90f, 1.00f, 0.95f);
            result.Next = BuildButton(rect, "NextButton", "Next >",
                bottomCenter, new Vector2(116f, 24f), new Vector2(84f, 34f), 14f);
            result.AttractPrompt = BuildLabel(rect, "AttractPrompt", "Touch the engine to explore it", 14f,
                bottomCenter, new Vector2(0f, 64f), new Vector2(420f, 24f), TextAlignmentOptions.Center);
            result.AttractPrompt.color = new Color(0.75f, 0.90f, 1.00f, 0.92f);

            // Bottom-Right: Reset view & Next Scene side-by-side
            result.Reset = BuildButton(rect, "ResetButton", "Reset view",
                bottomRight, new Vector2(-168f, 24f), new Vector2(134f, 36f), 14f);
            result.NextScene = BuildButton(rect, "NextSceneButton", "Next Scene",
                bottomRight, new Vector2(-24f, 24f), new Vector2(136f, 36f), 14f);

            result.ExpandLabel = result.Expand.GetComponentInChildren<TextMeshProUGUI>(true);

            // Top-Left below Title: compact Transparency & Build Variations
            BuildFeatureControls(rect, machinery, result);

            // Top-Right: Information text panel (InfoPanel)
            result.InfoPanel = BuildInfoPanel(rect);

            EyeAnatomySceneUpgrader.AddMotion(result.Expand.gameObject, true);
            EyeAnatomySceneUpgrader.AddMotion(result.Back.gameObject, false);
            EyeAnatomySceneUpgrader.AddMotion(result.Reset.gameObject, false);
            EyeAnatomySceneUpgrader.AddMotion(result.NextScene.gameObject, false);
            EyeAnatomySceneUpgrader.AddMotion(result.Previous.gameObject, false);
            EyeAnatomySceneUpgrader.AddMotion(result.Next.gameObject, false);

            return result;
        }

        private static void BuildFeatureControls(RectTransform parent, MonoBehaviour machinery, ExhibitUi result) {
            Vector2 topLeft = new Vector2(0f, 1f);

            result.Transparency = BuildButton(parent, "TransparencyButton", "See inside",
                topLeft, new Vector2(24f, -56f), new Vector2(146f, 32f), 13.5f);
            result.TransparencyLabel = result.Transparency.GetComponentInChildren<TextMeshProUGUI>(true);
            EyeAnatomySceneUpgrader.AddMotion(result.Transparency.gameObject, false);

            IExhibitMachinery features = machinery as IExhibitMachinery;
            int count = features == null ? 0 : features.VariationCount;
            if (count <= 0) {
                return;
            }

            TextMeshProUGUI caption = BuildLabel(parent, "BuildCaption", "BUILD", 11.5f,
                topLeft, new Vector2(24f, -98f), new Vector2(146f, 18f), TextAlignmentOptions.Left);
            caption.color = new Color(0.56f, 0.82f, 0.98f, 0.90f);
            caption.fontStyle = FontStyles.Bold;

            result.Variations = new Button[count];
            for (int i = 0; i < count; i++) {
                float y = -120f - i * 36f;
                result.Variations[i] = BuildButton(parent, "VariationButton" + (i + 1), features.GetVariationName(i),
                    topLeft, new Vector2(24f, y), new Vector2(146f, 30f), 13f);
                EyeAnatomySceneUpgrader.AddMotion(result.Variations[i].gameObject, false);
            }
        }

        private static void BuildTitle(RectTransform parent) {
            TextMeshProUGUI title = BuildLabel(parent, "Title", "Inline-Four Engine", 22f,
                new Vector2(0f, 1f), new Vector2(24f, -18f), new Vector2(360f, 28f), TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;
        }

        private static AnatomyInfoPanel BuildInfoPanel(RectTransform parent) {
            return ExhibitUiFactory.BuildInfoPanel(parent, new Vector2(1f, 1f),
                new Vector2(-24f, -24f), new Vector2(410f, 145f));
        }

        private static Button BuildButton(RectTransform parent, string name, string label,
            Vector2 anchor, Vector2 anchoredPosition, Vector2 size, float fontSize = 14f) {
            return ExhibitUiFactory.BuildButton(parent, name, label, anchor, anchoredPosition, size, fontSize);
        }

        private static TextMeshProUGUI BuildLabel(RectTransform parent, string name, string content,
            float fontSize, Vector2 anchor, Vector2 anchoredPosition, Vector2 size, TextAlignmentOptions alignment) {
            return ExhibitUiFactory.BuildLabel(parent, name, content, fontSize, anchor, anchoredPosition, size, alignment);
        }

        /// <summary>
        /// Adds every exhibit component to the exhibit root and hands each one its references.
        ///
        /// The components are the eye's, unchanged. Only the values differ: the model root, the
        /// catalogue and pose set built above, and a slightly wider framing ratio, because an
        /// engine part is a long thin object where the eye's are roughly spherical.
        /// </summary>
        private static void WireExhibit(GameObject exhibit, Transform pivot, Transform modelWrapper,
            Transform modelRoot, Transform focusAnchor, GameObject rig, Camera camera,
            EyeExplodePoseSet poseSet, EyeAnatomyCatalog catalog, MonoBehaviour machinery,
            KmaxStylus stylus, AnatomyAudioDirector audio, AnatomyParticleDirector particles, ExhibitUi ui) {

            EyeExplodeView explode = EyeAnatomySceneUpgrader.GetOrAdd<EyeExplodeView>(exhibit);
            SerializedObject explodeSo = new SerializedObject(explode);
            EyeAnatomySceneUpgrader.SetIfPresent(explodeSo, "modelRoot", modelRoot);
            EyeAnatomySceneUpgrader.SetIfPresent(explodeSo, "poseSet", poseSet);
            SetFloat(explodeSo, "transitionDuration", 1.1f);
            explodeSo.ApplyModifiedPropertiesWithoutUndo();

            EyeManipulator manipulator = EyeAnatomySceneUpgrader.GetOrAdd<EyeManipulator>(exhibit);
            SerializedObject manipulatorSo = new SerializedObject(manipulator);
            EyeAnatomySceneUpgrader.SetIfPresent(manipulatorSo, "pivot", pivot);
            EyeAnatomySceneUpgrader.SetIfPresent(manipulatorSo, "referenceCamera", camera);
            manipulatorSo.ApplyModifiedPropertiesWithoutUndo();

            ViewerFlyController fly = EyeAnatomySceneUpgrader.GetOrAdd<ViewerFlyController>(exhibit);
            SerializedObject flySo = new SerializedObject(fly);
            EyeAnatomySceneUpgrader.SetIfPresent(flySo, "rigRoot", rig.transform);
            flySo.ApplyModifiedPropertiesWithoutUndo();

            EyeFocusView focus = EyeAnatomySceneUpgrader.GetOrAdd<EyeFocusView>(exhibit);
            SerializedObject focusSo = new SerializedObject(focus);
            // The wrapper, not the model root: the focus view scales and moves what it is given to
            // frame a part, and the model root is what the explode view is moving parts underneath.
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "modelRoot", modelWrapper);
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "xrRig", rig.GetComponentInChildren<XRRig>(true));
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "focusAnchor", focusAnchor);
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "eyeManipulator", manipulator);
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "flyController", fly);
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "ghostMaterial",
                AssetDatabase.LoadAssetAtPath<Material>(EyeAnatomySceneUpgrader.GhostMaterialPath));
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "focusHighlightMaterial",
                EyeAnatomyAssetFactory.GetOrCreateFocusHighlightMaterial());
            // Wider than the eye's 0.48. A connecting rod or a camshaft is long and thin, so
            // framing it to the same fraction of the view drives the zoom far higher for the same
            // apparent size and throws the rest of the engine off screen.
            SetFloat(focusSo, "framingRatio", 0.56f);
            focusSo.ApplyModifiedPropertiesWithoutUndo();

            EyeAnatomyController controller = EyeAnatomySceneUpgrader.GetOrAdd<EyeAnatomyController>(exhibit);
            SerializedObject controllerSo = new SerializedObject(controller);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "explodeView", explode);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "focusView", focus);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "catalog", catalog);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "infoPanel", ui.InfoPanel);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "modelRoot", modelRoot);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "hotspotPrefab",
                AssetDatabase.LoadAssetAtPath<EyeHotspot>(EyeAnatomySceneUpgrader.HotspotPrefabPath));
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "manipulator", manipulator);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "flyController", fly);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "expandButton", ui.Expand);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "expandButtonLabel", ui.ExpandLabel);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "backButton", ui.Back);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "resetButton", ui.Reset);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "nextButton", ui.Next);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "previousButton", ui.Previous);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "partCounterLabel", ui.Counter);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "audioDirector", audio);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "particles", particles);
            SetString(controllerSo, "expandLabel", "Pull engine apart");
            SetString(controllerSo, "collapseLabel", "Reassemble engine");
            // The engine is 0.16 m across where the eye is 0.1 m, and it is a far busier silhouette,
            // so the badges are scaled up with it to stay findable against the detail.
            SetFloat(controllerSo, "hotspotWorldRadius", 0.008f);
            SetFloat(controllerSo, "hotspotFrontGap", 0.012f);
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            ExhibitAttractMode attract = EyeAnatomySceneUpgrader.GetOrAdd<ExhibitAttractMode>(exhibit);
            SerializedObject attractSo = new SerializedObject(attract);
            EyeAnatomySceneUpgrader.SetIfPresent(attractSo, "exhibitController", controller);
            EyeAnatomySceneUpgrader.SetIfPresent(attractSo, "flyController", fly);
            EyeAnatomySceneUpgrader.SetIfPresent(attractSo, "explodeView", explode);
            EyeAnatomySceneUpgrader.SetIfPresent(attractSo, "promptLabel", ui.AttractPrompt);
            attractSo.ApplyModifiedPropertiesWithoutUndo();

            AnatomyStylusInput input = EyeAnatomySceneUpgrader.GetOrAdd<AnatomyStylusInput>(exhibit);
            SerializedObject inputSo = new SerializedObject(input);
            EyeAnatomySceneUpgrader.SetIfPresent(inputSo, "stylus", stylus);
            EyeAnatomySceneUpgrader.SetIfPresent(inputSo, "flyController", fly);
            EyeAnatomySceneUpgrader.SetIfPresent(inputSo, "exhibitController", controller);
            EyeAnatomySceneUpgrader.SetIfPresent(inputSo, "rigRoot", rig.transform);
            inputSo.ApplyModifiedPropertiesWithoutUndo();

            ExhibitPostProcessing postFx = EyeAnatomySceneUpgrader.GetOrAdd<ExhibitPostProcessing>(exhibit);
            SerializedObject postFxSo = new SerializedObject(postFx);
            EyeAnatomySceneUpgrader.SetIfPresent(postFxSo, "cameraRoot", rig.transform);
            postFxSo.ApplyModifiedPropertiesWithoutUndo();

            ExhibitFeaturePanel features = EyeAnatomySceneUpgrader.GetOrAdd<ExhibitFeaturePanel>(exhibit);
            SerializedObject featuresSo = new SerializedObject(features);
            EyeAnatomySceneUpgrader.SetIfPresent(featuresSo, "machinerySource", machinery);
            EyeAnatomySceneUpgrader.SetIfPresent(featuresSo, "focusView", focus);
            EyeAnatomySceneUpgrader.SetIfPresent(featuresSo, "transparencyButton", ui.Transparency);
            EyeAnatomySceneUpgrader.SetIfPresent(featuresSo, "transparencyLabel", ui.TransparencyLabel);
            SerializedProperty variations = featuresSo.FindProperty("variationButtons");
            variations.arraySize = ui.Variations.Length;
            for (int i = 0; i < ui.Variations.Length; i++) {
                variations.GetArrayElementAtIndex(i).objectReferenceValue = ui.Variations[i];
            }
            featuresSo.ApplyModifiedPropertiesWithoutUndo();

            ExhibitMachineryGate gate = EyeAnatomySceneUpgrader.GetOrAdd<ExhibitMachineryGate>(exhibit);
            SerializedObject gateSo = new SerializedObject(gate);
            EyeAnatomySceneUpgrader.SetIfPresent(gateSo, "explodeView", explode);
            EyeAnatomySceneUpgrader.SetIfPresent(gateSo, "machinery", machinery);
            gateSo.ApplyModifiedPropertiesWithoutUndo();

            EyeAnatomySceneUpgrader.WireSceneSwitcher(exhibit, ui.NextScene, audio, "VolvoS90");
        }

        private static void SetFloat(SerializedObject so, string fieldName, float value) {
            SerializedProperty property = so.FindProperty(fieldName);
            if (property == null) {
                Debug.LogWarning($"{nameof(EngineExhibitBuilder)} found no field '{fieldName}' on " +
                    $"{so.targetObject.GetType().Name}.");
                return;
            }

            property.floatValue = value;
        }

        private static void SetStringArray(SerializedObject so, string fieldName, string[] values) {
            SerializedProperty property = so.FindProperty(fieldName);
            if (property == null) {
                Debug.LogWarning($"{nameof(EngineExhibitBuilder)} found no field '{fieldName}' on " +
                    $"{so.targetObject.GetType().Name}.");
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) {
                property.GetArrayElementAtIndex(i).stringValue = values[i];
            }
        }

        private static void SetString(SerializedObject so, string fieldName, string value) {
            SerializedProperty property = so.FindProperty(fieldName);
            if (property == null) {
                Debug.LogWarning($"{nameof(EngineExhibitBuilder)} found no field '{fieldName}' on " +
                    $"{so.targetObject.GetType().Name}.");
                return;
            }

            property.stringValue = value;
        }
    }
}
