using System.Collections.Generic;
using KmaxXR;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ViitorCloud.KmaxDisplayExample.Editor {
    /// <summary>
    /// Builds the Volvo S90 exhibit into <c>Model/VOLVO/VolvoS90.unity</c>, which ships with
    /// nothing in it but a camera.
    ///
    /// Same runtime stack as the eye and the engine. What is new here is the car's own geometry
    /// problem: every panel that exists on both sides of the vehicle is one mesh, so the doors have
    /// to be cut down the centreline before any of them can be hinged. That is
    /// <see cref="VehicleMeshSplitter"/>'s job, and this step wires the halves onto pivots placed
    /// at the real hinge lines.
    ///
    /// Finds before it creates, so a re-run changes nothing.
    /// </summary>
    public static class VolvoExhibitBuilder {
        private const string MenuPath = "Kmax/Volvo Exhibit/Set Up Volvo Exhibit";
        private const string ModuleRoot = "Assets/Games/kmax-display-example";
        private const string VolvoRoot = ModuleRoot + "/Model/VOLVO";
        // The scene lives under Scenes/ with the other two; only the model, its split halves, its
        // materials and its textures are under Model/VOLVO.
        private const string ScenePath = ModuleRoot + "/Scenes/VolvoS90.unity";
        private const string ModelPath = VolvoRoot + "/Volvo S90.fbx";
        private const string SplitFolder = VolvoRoot + "/Split";
        private const string XrRigPrefabPath = ModuleRoot + "/Plugins/Kmax/com.kmax.xr.core/Editor Resources/XRRig.prefab";

        private const string MaterialRoot = VolvoRoot + "/mat";
        private const string InteriorTextureRoot = VolvoRoot + "/textures/Interior";
        private const string MusicPath = ModuleRoot + "/Music/Dark-Times.mp3";
        private const string EngineStartPath = VolvoRoot + "/Music/VOLVO-S90-Start.wav";
        private const string EngineLoopPath = VolvoRoot + "/Music/VOLVO-S90-Loop.wav";
        private const string CatalogPath = ModuleRoot + "/Data/VolvoCatalog.asset";
        private const string TourPosePath = ModuleRoot + "/Data/VolvoTourPoses.asset";

        private const string ExhibitName = "VolvoExhibit";
        private const string PivotName = "VolvoModelPivot";
        private const string CarName = "Volvo S90";
        private const string PanelsName = "Panels";
        private const string InteriorName = "Interior";
        private const string PaintFeatureName = "Paint";
        private const string TrimFeatureName = "Interior Trim";
        private const string FocusAnchorName = "FocusAnchor";
        private const string RigName = "XRRig";
        private const string UiName = "UI";
        private const string AmbienceName = "Ambience";
        private const string EventSystemName = "EventSystem";
        private const string ShowroomName = "Showroom";

        /// <summary>
        /// Radius of the showroom floor in metres. See <c>BuildShowroom</c> for why it is this
        /// small rather than the metre a real showroom would suggest.
        /// </summary>
        private const float FloorRadius = 0.45f;

        /// <summary>
        /// Longest edge of the car once it is in the exhibit, in metres.
        ///
        /// The car is 5 m long against a virtual screen 0.345 m wide. At 0.26 m it fills about
        /// three quarters of the screen broadside and still clears it with all four doors open,
        /// which swing the widest silhouette the exhibit ever has to frame.
        /// </summary>
        private const float TargetLength = 0.26f;

        /// <summary>
        /// Yaw put on the pivot so the car rests three-quarter on to the viewer, in degrees.
        ///
        /// <see cref="TargetLength"/> is chosen for a car seen broadside, but the model's own
        /// length runs down +Z and the rig's resting view looks straight along +Z - so without this
        /// the exhibit opened on the car's back end, 0.105 m across a screen 0.345 m wide. At 45
        /// degrees the silhouette measures 0.26 x |sin| + 0.105 x |cos| = 0.258 m, which is the
        /// three quarters of the screen that figure was picked for, and it is the angle a car is
        /// photographed from for the same reason: one flank, one end, and the length reads.
        ///
        /// Applied to the pivot rather than to the car, and last, after every step that measures
        /// the car in world space - the headlight beams and the exhaust audio are placed from
        /// <c>carBounds.max.z</c> and would land on a flank if the car had already turned.
        /// <see cref="EyeManipulator"/> captures the pivot's rotation as its rest pose, so Reset
        /// View comes back here rather than to zero.
        /// </summary>
        private const float RestingYaw = 225f;

        /// <summary>
        /// A panel that exists on both sides of the car as a single mesh, and therefore has to be
        /// cut before it can be hinged.
        /// </summary>
        private class SplitPanel {
            public string SourceName;
            public string Group;

            public SplitPanel(string sourceName, string group) {
                SourceName = sourceName;
                Group = group;
            }
        }

        /// <summary>
        /// One piece of a door: the two-sided mesh it is cut from, and what the half should be
        /// called in the hierarchy.
        ///
        /// A null readable name leaves the half named after its source plus the side letter. That
        /// is not cosmetic - the light rig finds its lamps by mesh name and side, so the mirror
        /// indicators have to keep theirs or they drop off the indicator channel.
        /// </summary>
        private class DoorPart {
            public string SourceName;
            public string ReadableName;
            public bool IsSkin;

            public DoorPart(string sourceName, string readableName, bool isSkin) {
                SourceName = sourceName;
                ReadableName = readableName;
                IsSkin = isSkin;
            }
        }

        /// <summary>
        /// A panel that is already a single centre-hinged piece and needs no cutting.
        /// </summary>
        private class SoloPanel {
            public string SourceName;
            public string PivotName;
            public Vector3 Axis;
            public float Angle;
            public bool HingeAtFront;
            public string DisplayName;
            public string OpenLabel;
            public string CloseLabel;

            public SoloPanel(string sourceName, string pivotName, Vector3 axis, float angle, bool hingeAtFront,
                string displayName, string openLabel, string closeLabel) {
                SourceName = sourceName;
                PivotName = pivotName;
                Axis = axis;
                Angle = angle;
                HingeAtFront = hingeAtFront;
                DisplayName = displayName;
                OpenLabel = openLabel;
                CloseLabel = closeLabel;
            }
        }

        [MenuItem(MenuPath)]
        public static void Run() {
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                Debug.LogError($"{nameof(VolvoExhibitBuilder)} must not run in play mode.");
                return;
            }

            if (!EnsureSceneOpen()) {
                return;
            }

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) {
                Debug.LogError($"{nameof(VolvoExhibitBuilder)} could not load '{ModelPath}'.");
                return;
            }

            if (!EnsureReadable()) {
                return;
            }

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
                Debug.LogError($"{nameof(VolvoExhibitBuilder)} found no camera under '{RigName}'.");
                return;
            }

            Transform pivot = EyeAnatomySceneUpgrader.FindOrCreateChild(exhibit.transform, PivotName).transform;
            pivot.localPosition = Vector3.zero;
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;

            Transform car = EnsureCar(pivot, model);
            if (car == null) {
                return;
            }

            RemoveStrayModelInstances(car);
            RetireStockCamera(rig);

            SplitAllPanels(model);
            List<VehiclePanelGroup> groups = BuildPanels(car, model);
            BuildInterior(car);
            NormaliseCar(car);
            ConfigureGlass();
            ConfigurePaint();
            ConfigureLamps();

            EyeAnatomyCatalog catalog = BuildCatalog();
            EyeExplodePoseSet tourPoses = BuildTourPoses();

            Transform focusAnchor = EyeAnatomySceneUpgrader.FindOrCreateChild(exhibit.transform, FocusAnchorName).transform;
            focusAnchor.localPosition = Vector3.zero;

            EyeAnatomySceneUpgrader.UpgradeRenderPipeline();
            EyeAnatomySceneUpgrader.UpgradeEnvironment();
            EyeAnatomySceneUpgrader.UpgradePostProcessing(rig, exhibit);
            BuildLighting();
            BuildReflections();
            BuildShowroom(car);

            EyeAnatomySceneUpgrader.UpgradeEventSystem(eventSystem);
            EyeAnatomySceneUpgrader.UpgradeCamera(camera);
            EyeAnatomySceneUpgrader.UpgradeHotspotPrefab();
            EyeAnatomySceneUpgrader.RemoveDuplicatePens(rig);
            EyeAnatomySceneUpgrader.UpgradeMaterialsDoubleSided();
            KmaxStylus stylus = EyeAnatomySceneUpgrader.BuildStylus(rig, camera);
            AnatomyAudioDirector audio = EyeAnatomySceneUpgrader.BuildAudio();
            ConfigureMusic(audio);
            AnatomyParticleDirector particles = EyeAnatomySceneUpgrader.UpgradeParticles(ambience);
            RetireAmbientMotes(ambience);

            VehicleLightRig lightRig = BuildLightRig(exhibit, car);
            VehicleIgnition ignition = BuildIgnition(exhibit, car, lightRig);
            ConfigureEngineAudio(ignition);
            VolvoUi volvoUi = BuildUi(ui, camera, groups);

            // Last of the model steps: every measurement above is taken in world space, and the
            // car has to still be pointing down +Z when they are.
            FaceTheViewer(pivot);

            WireExhibit(exhibit, pivot, car, focusAnchor, rig, camera, groups, catalog, tourPoses,
                stylus, audio, particles, volvoUi);
            WireControls(exhibit, groups, lightRig, ignition, volvoUi);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            Debug.Log($"{nameof(VolvoExhibitBuilder)} finished: {groups.Count} panel group(s) hinged, " +
                $"{catalog.Parts.Length} tour stop(s) catalogued.");
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
        /// The panels cannot be cut unless the model's meshes are readable, and colliders cannot be
        /// fitted to the geometry either.
        /// </summary>
        private static bool EnsureReadable() {
            ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (importer == null) {
                Debug.LogError($"{nameof(VolvoExhibitBuilder)} found no ModelImporter on '{ModelPath}'.");
                return false;
            }

            if (importer.isReadable) {
                return true;
            }

            importer.isReadable = true;
            importer.importCameras = false;
            importer.importLights = false;
            importer.SaveAndReimport();
            return true;
        }

        /// <summary>
        /// The panels that exist on both sides of the car as one mesh. The mirrors are here because
        /// they are mounted on the front doors and have to swing with them, and the interior cards
        /// because they are the inside face of the door and have to swing with it too.
        ///
        /// <c>Plane.057</c> and <c>Plane.027</c> are those cards. The modeller left them on
        /// Blender's default object names, so they are identified by the materials they carry -
        /// <c>DoorPanelFront</c> and <c>DoorPanelRear</c> - and by sitting in the z range of the
        /// doors they belong to.
        /// </summary>
        private static SplitPanel[] GetSplitPanels() {
            return new SplitPanel[] {
                new SplitPanel("Door Front", "Front"),
                new SplitPanel("Door Front Handle", "Front"),
                new SplitPanel("FrontDoorGlass ", "Front"),
                new SplitPanel("Plane.057", "Front"),
                new SplitPanel("MIrror", "Front"),
                new SplitPanel("MIrrorTurnSignal Glass", "Front"),
                new SplitPanel("MirrorTurnsignal Reflector", "Front"),
                new SplitPanel("Door Rear", "Rear"),
                new SplitPanel("Door Rear Handle", "Rear"),
                new SplitPanel("RearDoorGlass ", "Rear"),
                new SplitPanel("Plane.027", "Rear")
            };
        }

        /// <summary>
        /// The panels that are already single centre-hinged pieces.
        ///
        /// The hood and the trunk were hinged here too and have been withdrawn: the model has no
        /// engine bay and no boot floor behind them, so opening either put a hole in the car. Any
        /// hinge left over from that build is taken down by <see cref="RemoveRetiredPanels"/>.
        ///
        /// Angles are signed for Unity's rotation sense and were settled by eye in the scene: about
        /// +X, a vector pointing along +Z tilts down, so a panel hinged at its front edge needs a
        /// positive angle to lift its rear.
        /// </summary>
        private static SoloPanel[] GetSoloPanels() {
            return new SoloPanel[] {
                // 32 degrees, not the 14 a real sunroof vents at. It does tilt at 14 - measured,
                // the rear edge lifts 6 mm - but the panel is transparent glass on a car 74 mm
                // tall, and a movement that small through a clear pane reads as nothing happening.
                new SoloPanel("SunRoof", "Sunroof Hinge", Vector3.right, 32f, true,
                    "Sunroof", "Tilt the sunroof", "Close the sunroof")
            };
        }

        private static void SplitAllPanels(GameObject model) {
            if (!AssetDatabase.IsValidFolder(SplitFolder)) {
                AssetDatabase.CreateFolder(VolvoRoot, "Split");
            }

            SplitPanel[] panels = GetSplitPanels();
            for (int i = 0; i < panels.Length; i++) {
                Transform source = model.transform.Find(panels[i].SourceName);
                if (source == null) {
                    Debug.LogError($"{nameof(VolvoExhibitBuilder)} could not find '{panels[i].SourceName}' in the model.");
                    continue;
                }

                Mesh sourceMesh = source.GetComponent<MeshFilter>().sharedMesh;
                Mesh left;
                Mesh right;
                VehicleMeshSplitter.SplitToAssets(sourceMesh, SplitFolder, out left, out right);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Instances the car under the pivot, and strips the two-sided panels out of it - their
        /// halves are rebuilt onto hinges instead.
        /// </summary>
        private static Transform EnsureCar(Transform pivot, GameObject model) {
            Transform existing = pivot.Find(CarName);
            if (existing != null) {
                bool hasCorruptMaterials = false;
                Renderer[] existingRenderers = existing.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < existingRenderers.Length; i++) {
                    Material[] shared = existingRenderers[i].sharedMaterials;
                    for (int j = 0; j < shared.Length; j++) {
                        if (shared[j] != null && shared[j].name.Contains("Ghost")) {
                            hasCorruptMaterials = true;
                            break;
                        }
                    }
                    if (hasCorruptMaterials) {
                        break;
                    }
                }

                if (!hasCorruptMaterials) {
                    return existing;
                }

                Undo.DestroyObjectImmediate(existing.gameObject);
            }

            GameObject car = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (car == null) {
                Debug.LogError($"{nameof(VolvoExhibitBuilder)} could not instantiate the model.");
                return null;
            }

            car.name = CarName;
            Undo.RegisterCreatedObjectUndo(car, "Create " + CarName);

            // Unpacked so the two-sided panels can be removed and the hinges parented in. A prefab
            // instance will not allow either.
            PrefabUtility.UnpackPrefabInstance(car, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            car.transform.SetParent(pivot, false);
            car.transform.localPosition = Vector3.zero;
            car.transform.localRotation = Quaternion.identity;
            car.transform.localScale = Vector3.one;
            return car.transform;
        }

        /// <summary>
        /// Switches off the camera the empty scene shipped with.
        ///
        /// It is not harmless scenery. It renders the whole car a second time from a fixed
        /// viewpoint, and it carries the scene's only <c>AudioListener</c> - which means
        /// <c>UpgradeCamera</c> finds one already present and leaves the rig without it, so every
        /// sound in the exhibit would be heard from wherever this camera was parked rather than
        /// from the viewer.
        ///
        /// Disabled rather than deleted: it is the scene's own object, not the exhibit's.
        /// </summary>
        private static void RetireStockCamera(GameObject rig) {
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < cameras.Length; i++) {
                if (cameras[i].transform.IsChildOf(rig.transform)) {
                    continue;
                }

                AudioListener listener = cameras[i].GetComponent<AudioListener>();
                if (listener != null) {
                    Undo.DestroyObjectImmediate(listener);
                }

                if (cameras[i].gameObject.activeSelf) {
                    Undo.RecordObject(cameras[i].gameObject, "Disable stock camera");
                    cameras[i].gameObject.SetActive(false);
                }
            }
        }

        /// <summary>
        /// Removes any other instance of the model sitting loose in the scene.
        ///
        /// Dragging the FBX into the scene to look at it is the obvious first thing to do, and it
        /// leaves a full copy behind - 3.27 million triangles of it, at life size, standing through
        /// the exhibit's own car. The exhibit owns the model once this has run, so anything else
        /// instancing it is a leftover.
        /// </summary>
        private static void RemoveStrayModelInstances(Transform car) {
            GameObject[] roots = EditorSceneManager.GetActiveScene().GetRootGameObjects();
            int removed = 0;

            for (int i = 0; i < roots.Length; i++) {
                if (roots[i].transform == car || car.IsChildOf(roots[i].transform)) {
                    continue;
                }

                Object source = PrefabUtility.GetCorrespondingObjectFromSource(roots[i]);
                if (source == null || AssetDatabase.GetAssetPath(source) != ModelPath) {
                    continue;
                }

                Undo.DestroyObjectImmediate(roots[i]);
                removed++;
            }

            if (removed > 0) {
                Debug.Log($"{nameof(VolvoExhibitBuilder)} removed {removed} loose instance(s) of the model " +
                    "from the scene root; the exhibit's own car is the one that counts.");
            }
        }

        /// <summary>
        /// Replaces the two-sided panels with hinged halves, and puts the solo panels on hinges of
        /// their own.
        /// </summary>
        private static List<VehiclePanelGroup> BuildPanels(Transform car, GameObject model) {
            GameObject panelsRoot = EyeAnatomySceneUpgrader.FindOrCreateChild(car, PanelsName);
            List<VehiclePanelGroup> groups = new List<VehiclePanelGroup>();

            RemoveRetiredPanels(car, panelsRoot.transform);

            VehiclePanel frontLeft = BuildDoor(car, panelsRoot.transform, model, "Front", "Left", -1f);
            VehiclePanel frontRight = BuildDoor(car, panelsRoot.transform, model, "Front", "Right", 1f);
            VehiclePanel rearLeft = BuildDoor(car, panelsRoot.transform, model, "Rear", "Left", -1f);
            VehiclePanel rearRight = BuildDoor(car, panelsRoot.transform, model, "Rear", "Right", 1f);

            List<VehiclePanel> doors = new List<VehiclePanel>();
            AddIfPresent(doors, frontLeft);
            AddIfPresent(doors, frontRight);
            AddIfPresent(doors, rearLeft);
            AddIfPresent(doors, rearRight);

            if (doors.Count > 0) {
                groups.Add(new VehiclePanelGroup("Doors", doors.ToArray(), "Open the doors", "Close the doors"));
            }

            // The two-sided originals are removed last, so their transforms are still available
            // while the halves are being placed against them.
            RemoveSourcePanels(car);

            SoloPanel[] solo = GetSoloPanels();
            for (int i = 0; i < solo.Length; i++) {
                VehiclePanel panel = BuildSoloPanel(car, panelsRoot.transform, solo[i]);
                if (panel == null) {
                    continue;
                }

                groups.Add(new VehiclePanelGroup(solo[i].DisplayName, new VehiclePanel[] { panel },
                    solo[i].OpenLabel, solo[i].CloseLabel));
            }

            return groups;
        }

        /// <summary>
        /// Adds an item unless it is missing. Used for both the panels of a group and the pieces of
        /// a door, either of which may be absent if the model is not what this step expects.
        /// </summary>
        private static void AddIfPresent<T>(List<T> list, T item) where T : Object {
            if (item != null) {
                list.Add(item);
            }
        }

        /// <summary>
        /// The pieces one door is made of.
        /// </summary>
        private static DoorPart[] GetDoorParts(string row) {
            bool front = row == "Front";
            List<DoorPart> parts = new List<DoorPart>();
            parts.Add(new DoorPart("Door " + row, "Skin", true));
            parts.Add(new DoorPart("Door " + row + " Handle", "Handle", false));
            parts.Add(new DoorPart(front ? "FrontDoorGlass " : "RearDoorGlass ", "Glass", false));
            parts.Add(new DoorPart(front ? "Plane.057" : "Plane.027", "Card", false));

            if (front) {
                parts.Add(new DoorPart("MIrror", "Mirror", false));
                parts.Add(new DoorPart("MIrrorTurnSignal Glass", null, false));
                parts.Add(new DoorPart("MirrorTurnsignal Reflector", null, false));
            }

            return parts.ToArray();
        }

        /// <summary>
        /// The name a door part carries in the hierarchy: "Door Front Left Skin" for most of them,
        /// and the source name plus the side letter for the parts the light rig has to find.
        /// </summary>
        private static string PartName(string pivotName, DoorPart part, string suffix) {
            if (string.IsNullOrEmpty(part.ReadableName)) {
                return part.SourceName.Trim() + suffix;
            }

            return pivotName + " " + part.ReadableName;
        }

        /// <summary>
        /// Builds one door: its skin, handle, glass, interior card and - on the front doors - its
        /// mirror and the mirror's indicator, all parented to a pivot standing on the hinge line.
        ///
        /// The hinge is at the door's leading edge, which for both doors is the end nearest the
        /// front of the car, and it stands vertical. The pivot is created in world space and the
        /// geometry reparented onto it without moving, so the door keeps the pose the model author
        /// gave it and only the axis is new.
        ///
        /// A door that already exists is repaired rather than skipped. The list of pieces a door is
        /// made of has grown since the first build, and skipping would leave those doors short of
        /// a part for good - short of it on the car as well, because the two-sided mesh it is cut
        /// from is removed either way.
        /// </summary>
        private static VehiclePanel BuildDoor(Transform car, Transform panelsRoot, GameObject model,
            string row, string side, float sideSign) {
            string pivotName = "Door " + row + " " + side;
            string suffix = sideSign < 0f ? " L" : " R";
            DoorPart[] wanted = GetDoorParts(row);

            Transform existingPivot = panelsRoot.Find(pivotName);
            if (existingPivot != null) {
                RepairDoor(car, model, existingPivot, pivotName, wanted, suffix);
                return existingPivot.GetComponent<VehiclePanel>();
            }

            List<Transform> parts = new List<Transform>();
            Transform skin = null;

            for (int i = 0; i < wanted.Length; i++) {
                Transform part = BuildHalfPart(car, model, wanted[i].SourceName, suffix,
                    PartName(pivotName, wanted[i], suffix));
                AddIfPresent(parts, part);

                if (wanted[i].IsSkin) {
                    skin = part;
                }
            }

            if (skin == null) {
                Debug.LogError($"{nameof(VolvoExhibitBuilder)} built no skin for '{pivotName}'; that door is missing.");
                return null;
            }

            Bounds skinBounds = skin.GetComponent<MeshRenderer>().bounds;

            // Leading edge, and just inboard of the outer skin so the door swings about the body
            // rather than about thin air beside it.
            GameObject pivotObject = new GameObject(pivotName);
            Undo.RegisterCreatedObjectUndo(pivotObject, "Create " + pivotName);
            pivotObject.transform.SetParent(panelsRoot, false);
            pivotObject.transform.position = new Vector3(
                sideSign * Mathf.Abs(sideSign < 0f ? skinBounds.min.x : skinBounds.max.x) * 0.86f,
                skinBounds.center.y,
                skinBounds.max.z);
            pivotObject.transform.rotation = Quaternion.identity;

            for (int i = 0; i < parts.Count; i++) {
                parts[i].SetParent(pivotObject.transform, true);
            }

            VehiclePanel panel = Undo.AddComponent<VehiclePanel>(pivotObject);
            SerializedObject so = new SerializedObject(panel);
            so.FindProperty("hingeAxis").vector3Value = Vector3.up;
            // Positive about +Y swings a rearward-pointing door toward -X, which is outward on the
            // left of the car; the right door mirrors it.
            so.FindProperty("openAngle").floatValue = sideSign < 0f ? 62f : -62f;
            so.FindProperty("duration").floatValue = 1.2f;
            so.ApplyModifiedPropertiesWithoutUndo();
            panel.CaptureClosedRotation();
            return panel;
        }

        /// <summary>
        /// Adds to an existing door any piece it is missing, and leaves what is already there
        /// alone.
        /// </summary>
        private static void RepairDoor(Transform car, GameObject model, Transform pivot,
            string pivotName, DoorPart[] wanted, string suffix) {
            List<Transform> added = new List<Transform>();

            for (int i = 0; i < wanted.Length; i++) {
                string partName = PartName(pivotName, wanted[i], suffix);
                if (pivot.Find(partName) != null) {
                    continue;
                }

                AddIfPresent(added, BuildHalfPart(car, model, wanted[i].SourceName, suffix, partName));
            }

            if (added.Count == 0) {
                return;
            }

            // The halves are cut in the closed pose and the pivot is built unrotated, so the door
            // is shut before anything is reparented onto it. A piece added to a door left ajar
            // would be fixed at that angle relative to the rest of the door for good.
            pivot.localRotation = Quaternion.identity;

            for (int i = 0; i < added.Count; i++) {
                added[i].SetParent(pivot, true);
            }

            VehiclePanel panel = pivot.GetComponent<VehiclePanel>();
            if (panel != null) {
                panel.CaptureClosedRotation();
            }

            Debug.Log($"{nameof(VolvoExhibitBuilder)} added {added.Count} missing part(s) to '{pivotName}'.");
        }

        /// <summary>
        /// Creates one half of a two-sided panel, copying the source object's full local transform
        /// so the half lands exactly where the whole was. The model bakes its unit conversion into
        /// each object's scale - 100 on most panels, 14.291 on the mirrors - so the transform
        /// cannot be assumed.
        /// </summary>
        private static Transform BuildHalfPart(Transform car, GameObject model, string sourceName, string suffix, string newName) {
            Transform source = model.transform.Find(sourceName);
            if (source == null) {
                Debug.LogError($"{nameof(VolvoExhibitBuilder)} could not find source panel '{sourceName}'.");
                return null;
            }

            string meshPath = SplitFolder + "/" + sourceName.Trim() + suffix + ".asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null) {
                Debug.LogError($"{nameof(VolvoExhibitBuilder)} could not load split mesh '{meshPath}'.");
                return null;
            }

            // Named after the source plus its side, so a half is still recognisable both to a
            // reader of the hierarchy and to the light rig, which finds its lamps by name.
            GameObject part = new GameObject(string.IsNullOrEmpty(newName) ? sourceName.Trim() + suffix : newName);
            Undo.RegisterCreatedObjectUndo(part, "Create " + part.name);
            part.transform.SetParent(car, false);
            part.transform.localPosition = source.localPosition;
            part.transform.localRotation = source.localRotation;
            part.transform.localScale = source.localScale;

            MeshFilter filter = part.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            MeshRenderer sourceRenderer = source.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = sourceRenderer.sharedMaterials;
            renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            return part.transform;
        }

        /// <summary>
        /// Takes down any hinge that is no longer wanted, handing its geometry back to the car
        /// first so the panel stays on the model rather than leaving with the pivot.
        ///
        /// The builder finds before it creates, so a pivot dropped from the spec would otherwise
        /// sit in the scene for good - driven by nothing, and still holding its panel.
        /// </summary>
        private static void RemoveRetiredPanels(Transform car, Transform panelsRoot) {
            List<string> wanted = new List<string>();
            wanted.Add("Door Front Left");
            wanted.Add("Door Front Right");
            wanted.Add("Door Rear Left");
            wanted.Add("Door Rear Right");

            SoloPanel[] solo = GetSoloPanels();
            for (int i = 0; i < solo.Length; i++) {
                wanted.Add(solo[i].PivotName);
            }

            for (int i = panelsRoot.childCount - 1; i >= 0; i--) {
                Transform pivot = panelsRoot.GetChild(i);
                if (wanted.Contains(pivot.name)) {
                    continue;
                }

                // Shut first: the panel is handed back to the car at whatever angle the pivot is
                // holding it at, and a retired hinge should leave the car looking closed.
                pivot.localRotation = Quaternion.identity;

                for (int j = pivot.childCount - 1; j >= 0; j--) {
                    pivot.GetChild(j).SetParent(car, true);
                }

                Debug.Log($"{nameof(VolvoExhibitBuilder)} retired the '{pivot.name}' hinge and " +
                    "returned its geometry to the car.");
                Undo.DestroyObjectImmediate(pivot.gameObject);
            }
        }

        private static void RemoveSourcePanels(Transform car) {
            SplitPanel[] panels = GetSplitPanels();
            for (int i = 0; i < panels.Length; i++) {
                Transform source = car.Find(panels[i].SourceName);
                if (source != null) {
                    Undo.DestroyObjectImmediate(source.gameObject);
                }
            }
        }

        private static VehiclePanel BuildSoloPanel(Transform car, Transform panelsRoot, SoloPanel spec) {
            Transform existingPivot = panelsRoot.Find(spec.PivotName);
            if (existingPivot != null) {
                // Re-applied rather than returned untouched. This step used to hand back whatever
                // was already in the scene, which is the same mistake BuildDoor was fixed for: a
                // build step has to converge on the spec from wherever the scene is, not only from
                // empty. Changing the sunroof's angle in the table did nothing at all until this
                // was corrected, silently, because the hinge already existed.
                VehiclePanel existing = existingPivot.GetComponent<VehiclePanel>();
                if (existing != null) {
                    ApplyPanelSpec(existing, spec);
                    return existing;
                }
            }

            Transform part = car.Find(spec.SourceName);
            if (part == null) {
                Debug.LogError($"{nameof(VolvoExhibitBuilder)} could not find solo panel '{spec.SourceName}'.");
                return null;
            }

            Bounds bounds = part.GetComponent<MeshRenderer>().bounds;

            GameObject pivotObject = new GameObject(spec.PivotName);
            Undo.RegisterCreatedObjectUndo(pivotObject, "Create " + spec.PivotName);
            pivotObject.transform.SetParent(panelsRoot, false);
            pivotObject.transform.position = new Vector3(
                bounds.center.x,
                bounds.center.y,
                spec.HingeAtFront ? bounds.max.z : bounds.min.z);
            pivotObject.transform.rotation = Quaternion.identity;

            part.SetParent(pivotObject.transform, true);

            // The sunroof's frame stays with the roof; only the glass tilts.
            VehiclePanel panel = Undo.AddComponent<VehiclePanel>(pivotObject);
            ApplyPanelSpec(panel, spec);
            panel.CaptureClosedRotation();
            return panel;
        }

        private static void ApplyPanelSpec(VehiclePanel panel, SoloPanel spec) {
            SerializedObject so = new SerializedObject(panel);
            so.FindProperty("hingeAxis").vector3Value = spec.Axis;
            so.FindProperty("openAngle").floatValue = spec.Angle;
            so.FindProperty("duration").floatValue = 1.2f;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(panel);
        }

        /// <summary>
        /// Every mesh that makes up the cabin, in the modeller's own names.
        ///
        /// Derived from the materials rather than guessed: the interior is exactly the geometry
        /// wearing <c>Shell</c>, <c>Dashboard</c>, <c>CenterConsole</c>, <c>Front Seat</c>,
        /// <c>Rear Seats</c> and <c>Steeringwheel</c>, plus the five pieces the modeller gave their
        /// own materials - the two screens, the crystal gear knob, its surround and the pedals.
        ///
        /// The door cards are **not** here. They wear the interior's <c>DoorPanel</c> materials and
        /// the trim swatches repaint them with the rest, but they are hinged to their doors and
        /// have to swing with them, so the cabin focus ghosts them along with the bodywork. That
        /// reads correctly: with the doors shut, a ghosted door is a door you can see through.
        /// </summary>
        private static string[] GetInteriorMeshes() {
            return new string[] {
                "Shell", "Floor", "Driver Carpet", "PassengerCarpet", "Rear Carpet", "PlasticTrim",
                "RearShelf", "CeilingConsole", "RearviewMirror", "RearviewMirrorHolder",
                "SeatBelts Front", "SeatBelts Rear",
                "Dashboard", "Glovebox Handle", "Knobs", "Vents",
                "CenterConsole", "Plane.049", "Shifterknob", "Shifterknob Crystal",
                "Driver Seat", "Passenger Seat", "Rear Seats",
                "SteeringWheel", "SteeringWheel Emblem", "SteeringColumn", "Stalks",
                "SpeedoScreen", "SpeedoGlass", "InfoTainment Screen", "Gas/Brake Pedal"
            };
        }

        /// <summary>
        /// Gathers the cabin under one node, so the exhibit has something to frame.
        ///
        /// This is the whole of the interior view. <see cref="EyeFocusView"/> already scales a part
        /// up to fill the screen and ghosts everything that is not underneath it, so a node holding
        /// the cabin gives a focus that brings the interior forward and turns the shell, the roof
        /// and the glass translucent around it - with no new mechanism, and no camera inside the
        /// car, which the rig cannot do anyway: at exhibit scale the cabin is 0.08 m across and
        /// <see cref="ViewerFlyController"/> will not come closer than 0.14 m.
        ///
        /// Nothing moves. The group is created at the car's own origin and every mesh keeps its
        /// world pose.
        /// </summary>
        private static Transform BuildInterior(Transform car) {
            Transform interior = EyeAnatomySceneUpgrader.FindOrCreateChild(car, InteriorName).transform;
            interior.localPosition = Vector3.zero;
            interior.localRotation = Quaternion.identity;
            interior.localScale = Vector3.one;

            string[] meshes = GetInteriorMeshes();
            int moved = 0;

            for (int i = 0; i < meshes.Length; i++) {
                Transform mesh = FindCarMesh(car, interior, meshes[i]);
                if (mesh == null) {
                    Debug.LogError($"{nameof(VolvoExhibitBuilder)} could not find the cabin mesh " +
                        $"'{meshes[i]}'; the interior view will be short of it.");
                    continue;
                }

                if (mesh.parent == interior) {
                    continue;
                }

                mesh.SetParent(interior, true);
                moved++;
            }

            if (moved > 0) {
                Debug.Log($"{nameof(VolvoExhibitBuilder)} gathered {moved} cabin mesh(es) under " +
                    $"'{InteriorName}'.");
            }

            return interior;
        }

        /// <summary>
        /// A named mesh of the car, wherever the build has already put it.
        ///
        /// Matched on the trimmed name, because the modeller left a trailing space on several of
        /// them - "CeilingConsole ", "Stalks ", "SteeringColumn " - and looked for under the
        /// interior group as well as the car, so that a second run finds what the first one moved.
        /// A plain <c>Transform.Find</c> will not do either job: it is exact, and it reads a slash
        /// as a path separator, which "Gas/Brake Pedal" is not.
        /// </summary>
        private static Transform FindCarMesh(Transform car, Transform interior, string meshName) {
            Transform found = FindChildByName(car, meshName);
            if (found != null) {
                return found;
            }

            return FindChildByName(interior, meshName);
        }

        private static Transform FindChildByName(Transform parent, string meshName) {
            if (parent == null) {
                return null;
            }

            string wanted = meshName.Trim();
            for (int i = 0; i < parent.childCount; i++) {
                if (parent.GetChild(i).name.Trim() == wanted) {
                    return parent.GetChild(i);
                }
            }

            return null;
        }

        /// <summary>
        /// Turns the finished car to face the viewer. See <see cref="RestingYaw"/>.
        /// </summary>
        private static void FaceTheViewer(Transform pivot) {
            pivot.localRotation = Quaternion.Euler(0f, RestingYaw, 0f);
            EditorUtility.SetDirty(pivot);
        }

        /// <summary>
        /// Centres the car on the origin the rig orbits and scales it to exhibit size. Measured
        /// with the doors shut, which is the pose the resting view frames.
        /// </summary>
        private static void NormaliseCar(Transform car) {
            Bounds bounds;
            if (!TryMeasure(car, out bounds)) {
                Debug.LogError($"{nameof(VolvoExhibitBuilder)} found no renderers under the car.");
                return;
            }

            float currentScale = car.localScale.x;
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest <= Mathf.Epsilon || currentScale <= Mathf.Epsilon) {
                return;
            }

            float scale = TargetLength / (longest / currentScale);
            car.localScale = new Vector3(scale, scale, scale);

            Bounds scaled;
            if (TryMeasure(car, out scaled)) {
                car.position -= scaled.center;
            }

            EditorUtility.SetDirty(car);
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
        /// One stop on the guided tour: what to frame, what to call it and what to say about it.
        /// </summary>
        private class TourStop {
            public string Path;
            public string DisplayName;
            public string Description;
            public bool ShowsInterior;

            public TourStop(string path, string displayName, string description)
                : this(path, displayName, description, false) {
            }

            public TourStop(string path, string displayName, string description, bool showsInterior) {
                Path = path;
                DisplayName = displayName;
                Description = description;
                ShowsInterior = showsInterior;
            }
        }

        /// <summary>
        /// The tour, in the order Next steps through it.
        ///
        /// Chosen to go round the car rather than to be a list of the eight best features: the
        /// navigator flies the camera to each stop's own side and the badges are pushed out to the
        /// car's silhouette, so two stops on the same corner would put two badges on top of each
        /// other and fly the viewer nowhere between them. What is here reaches the nose, the tail,
        /// both ends of the left flank, the roof, underneath, and twice inside.
        /// </summary>
        private static TourStop[] GetTour() {
            return new TourStop[] {
                new TourStop(InteriorName, "Cabin",
                    "Leather over a driver-focused dashboard, with a portrait touchscreen in the " +
                    "centre console and a digital instrument display behind the wheel. The gear " +
                    "selector is topped with Orrefors crystal. Try the interior swatches on the right.", true),
                new TourStop("Glass Headlight", "Headlights",
                    "Full-LED headlamps behind a single moulded lens, with the T-shaped daytime " +
                    "running lights - Thor's Hammer - that mark out the front of every modern Volvo. " +
                    "Each reflector, emitter and lens is its own mesh, which is why they can be lit " +
                    "one channel at a time."),
                new TourStop("Frame Taillight", "Tail lights",
                    "The C-shaped rear lamp signature, split between the body and the boot lid so " +
                    "it stays unbroken when the car is shut. Six separate diffusers and reflectors " +
                    "make up each side."),
                new TourStop("Rim FL", "Alloy wheels",
                    "Multi-spoke alloys over vented discs and fixed calipers. The tyre, rim, hub, " +
                    "disc and caliper are modelled as five parts, so the wheel reads as an assembly " +
                    "rather than a decal."),
                new TourStop("Panels/Door Front Left/Door Front Left Mirror", "Door mirrors",
                    "The wing mirrors carry their own indicator repeaters, which flash with the " +
                    "car's. They are hinged to the door, so they swing out with it."),
                new TourStop("Panels/Sunroof Hinge/SunRoof", "Panoramic roof",
                    "A single pane of glass over both rows, which tilts at the rear. Every glazed " +
                    "surface on this model arrived opaque and is made transparent by the exhibit - " +
                    "without that there was no interior to see at all."),
                new TourStop("Exhaust System", "Tailpipes",
                    "Twin trapezoidal tailpipes under the rear valance. The idle is synthesised and " +
                    "played from here, so the exhaust note stays behind the car as the view orbits."),
                new TourStop(InteriorName + "/Driver Seat", "Front seats",
                    "Contoured seats with integrated belts and adjustable bolsters. They take their " +
                    "colour from the interior swatches along with the dashboard, console and door cards.", true)
            };
        }

        private static EyeAnatomyCatalog BuildCatalog() {
            EyeAnatomyCatalog catalog = EyeAnatomyAssetFactory.LoadOrCreate<EyeAnatomyCatalog>(CatalogPath);
            TourStop[] stops = GetTour();
            EyePartDefinition[] parts = new EyePartDefinition[stops.Length];

            for (int i = 0; i < stops.Length; i++) {
                // Everything except the two interior stops keeps the bodywork solid. See the
                // EyePartDefinition field for why it is stored the negative way round.
                parts[i] = new EyePartDefinition(stops[i].DisplayName, stops[i].Description, stops[i].Path,
                    !stops[i].ShowsInterior);
            }

            catalog.SetParts(parts);
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        /// <summary>
        /// An explode pose set with nothing in it, which is what this exhibit's "open" state is.
        ///
        /// <see cref="EyeAnatomyController"/> gates the badges and the navigator on
        /// <see cref="EyeExplodeView.IsExpanded"/>, so on a model that comes apart the two states
        /// are the same thing: the eye is open, therefore its parts are labelled. The car does not
        /// come apart - the hood and boot were withdrawn because there is no engine bay or boot
        /// floor behind them - but it wants the same two states, a car to look at and a car with
        /// its tour running.
        ///
        /// An empty pose set gives exactly that and costs nothing: <c>ResolveParts</c> finds no
        /// parts and reports none missing, and <c>SetExpanded</c> still runs its timer and raises
        /// <c>TransitionCompleted</c>, which is what brings the badges up. Nothing moves. The asset
        /// exists rather than being left null only because the view logs an error without one.
        /// </summary>
        private static EyeExplodePoseSet BuildTourPoses() {
            EyeExplodePoseSet poses = EyeAnatomyAssetFactory.LoadOrCreate<EyeExplodePoseSet>(TourPosePath);
            poses.SetPoses(new EyePartPose[0]);
            EditorUtility.SetDirty(poses);
            return poses;
        }

        /// <summary>
        /// Car paint is a clear-coated specular surface, so it is read almost entirely from what it
        /// reflects. The key is held lower than the engine's and the rim raised, because the shape
        /// of a car body is described by the highlight running along it rather than by diffuse
        /// shading across it.
        /// </summary>
        private static void BuildLighting() {
            Light key = EyeAnatomySceneUpgrader.EnsureLight("Key Light", LightType.Directional);
            // Aimed across the car's resting three-quarter rather than down the world axes, so the
            // key rakes along the flank the viewer is looking at instead of straight into it.
            // 34 degrees rather than 42: a lower sun stretches the window panes across the floor
            // instead of stamping them, which is what the reference shot does with them.
            key.transform.SetPositionAndRotation(new Vector3(0.3f, 0.5f, -0.4f), Quaternion.Euler(34f, 200f, 0f));
            key.color = new Color(1f, 0.98f, 0.94f);
            // Down from 1.6. The floor is a pale surface now, and a full-strength pane on it was
            // clipping to white - the cookie's own contrast is doing the work the intensity was.
            key.intensity = 1.15f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.8f;
            key.enabled = true;

            // The window gobo. A directional cookie tiles across the world at cookieSize2D, so this
            // is the size of one window in metres - a little under half the car's length, which
            // puts three or four panes across the floor rather than one wash or a fine grid.
            Texture2D cookie = EyeAnatomyAssetFactory.GetOrCreateKeyCookie();
            if (cookie != null) {
                key.cookie = cookie;
                // 0.24 m per window, against a floor 0.9 m across: three or four panes to a side
                // rather than one. At 0.42 the whole floor was inside a single pane.
                key.cookieSize2D = new Vector2(0.15f, 0.15f);
            }

            Light fill = EyeAnatomySceneUpgrader.EnsureLight("Fill Light", LightType.Directional);
            fill.transform.SetPositionAndRotation(new Vector3(-0.4f, 0.3f, -0.35f), Quaternion.Euler(16f, 300f, 0f));
            fill.color = new Color(0.78f, 0.86f, 1f);
            fill.intensity = 0.5f;
            fill.shadows = LightShadows.None;
            fill.enabled = true;

            // Behind and above, opposite the key. On a dark car against a dark background this is
            // the only thing separating the roof and boot lid from the backdrop.
            Light rim = EyeAnatomySceneUpgrader.EnsureLight("Rim Light", LightType.Directional);
            rim.transform.SetPositionAndRotation(new Vector3(0f, 0.45f, 0.5f), Quaternion.Euler(18f, 40f, 0f));
            rim.color = new Color(0.80f, 0.88f, 1f);
            rim.intensity = 0.95f;
            rim.shadows = LightShadows.None;
            rim.enabled = true;
        }

        /// <summary>
        /// Switches off the drifting mote layers, keeping the ones that fire on an interaction.
        ///
        /// The motes exist to give a model floating in a void some depth to be read against - near,
        /// far and foreground layers parallaxing as the view orbits. That is the eye's problem, not
        /// this one: the showroom floor gives the car a ground plane, a cast shadow and a horizon,
        /// which is a far better depth cue than dust.
        ///
        /// Over a dark backdrop they were invisible until you looked for them. Over a lit floor they
        /// are white specks two or three pixels across scattered on it, which reads as noise in the
        /// render rather than as atmosphere.
        ///
        /// The burst, ring and spark systems stay. Those are feedback on a press and only exist for
        /// the moment they play.
        /// </summary>
        private static void RetireAmbientMotes(GameObject ambience) {
            string[] fields = new string[] { "MoteField", "MoteField_Near", "MoteField_Far", "MoteField_Foreground" };
            int retired = 0;

            for (int i = 0; i < fields.Length; i++) {
                Transform field = ambience.transform.Find(fields[i]);
                if (field == null || !field.gameObject.activeSelf) {
                    continue;
                }

                field.gameObject.SetActive(false);
                EditorUtility.SetDirty(field.gameObject);
                retired++;
            }

            if (retired > 0) {
                Debug.Log($"{nameof(VolvoExhibitBuilder)} switched off {retired} drifting mote layer(s); " +
                    "the showroom floor is the depth cue now.");
            }
        }

        /// <summary>
        /// Colours the lamp lenses.
        ///
        /// The model's tail lamp glass is not red. Every diffuser and lens on the back of this car
        /// ships white or grey with no texture - `Translucent_Glass` and its .001 sibling are pure
        /// white at 55% alpha - and the red came entirely from the emission the light rig adds.
        /// That worked while the exhibit was a car floating against black. Over a lit showroom floor
        /// the lenses pick up far more light than the emission adds, and the lamp renders pale
        /// yellow-white with a red smear in it.
        ///
        /// So the glass is tinted, which is what makes it a *red lamp* whether it is lit or not - a
        /// tail light is red in daylight too. Only the alpha that <see cref="ConfigureGlass"/> set
        /// is preserved; the colour is this step's.
        ///
        /// Runs after <see cref="ConfigureGlass"/>, which converts these same materials to
        /// transparent and would otherwise be working from an untinted copy.
        /// </summary>
        private static void ConfigureLamps() {
            // The main lenses and both diffusers: deep red, the colour of the glass itself.
            TintLens("Glass Taillight", new Color(0.46f, 0.030f, 0.018f));
            TintLens("Translucent_Glass", new Color(0.50f, 0.035f, 0.020f));
            TintLens("Translucent_Glass.001", new Color(0.50f, 0.035f, 0.020f));
            // The indicator section of the cluster, and the side repeater beside it.
            TintLens("Turn signal(taillight)", new Color(0.62f, 0.22f, 0.020f));
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Sets a lens material's colour while leaving whatever alpha it already carries.
        /// </summary>
        private static void TintLens(string materialName, Color colour) {
            Material material = FindMaterial(materialName);
            if (material == null) {
                Debug.LogWarning($"{nameof(VolvoExhibitBuilder)} could not find the '{materialName}' " +
                    "lens material; that lamp keeps the colour it shipped with.");
                return;
            }

            SetTint(material, "_BaseColor", colour);
            SetTint(material, "_Color", colour);
            EditorUtility.SetDirty(material);
        }

        private static void SetTint(Material material, string property, Color colour) {
            if (!material.HasProperty(property)) {
                return;
            }

            Color existing = material.GetColor(property);
            material.SetColor(property, new Color(colour.r, colour.g, colour.b, existing.a));
        }

        /// <summary>
        /// Puts the supplied recordings of the car under the ignition, in place of the synthesis.
        ///
        /// <see cref="VehicleIgnition"/> already sequences this - it plays the start clip once,
        /// waits <c>catchDelay</c>, then brings the idle loop up under it - so all that is needed is
        /// the two clips and a delay that suits them. The delay was 0.85 s, tuned against a 2.2 s
        /// synthesised starter; the real recording runs 5.5 s, and left alone the idle would have
        /// risen while the engine was still cranking.
        ///
        /// It is derived from the clip rather than typed in, so replacing the recording with a
        /// longer or shorter one needs no second edit. The idle comes up over the last second, which
        /// is where a real start settles into one.
        ///
        /// Import settings differ by role: the starter is short and has to sound the instant the
        /// button is pressed, so it is decompressed on load; the idle loop is longer and only has to
        /// be seamless, so it stays compressed in memory.
        /// </summary>
        private static void ConfigureEngineAudio(VehicleIgnition ignition) {
            if (ignition == null) {
                return;
            }

            AudioClip start = AssetDatabase.LoadAssetAtPath<AudioClip>(EngineStartPath);
            AudioClip loop = AssetDatabase.LoadAssetAtPath<AudioClip>(EngineLoopPath);

            if (start == null || loop == null) {
                Debug.LogWarning($"{nameof(VolvoExhibitBuilder)} could not load the engine recordings " +
                    $"from '{EngineStartPath}' and '{EngineLoopPath}'; the ignition keeps its " +
                    "synthesised start and idle.");
                return;
            }

            SetAudioImport(EngineStartPath, AudioClipLoadType.DecompressOnLoad);
            SetAudioImport(EngineLoopPath, AudioClipLoadType.CompressedInMemory);

            SerializedObject so = new SerializedObject(ignition);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "startOverride", start);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "idleOverride", loop);
            SetFloat(so, "catchDelay", Mathf.Max(0.2f, start.length - 1f));
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetAudioImport(string path, AudioClipLoadType loadType) {
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) {
                return;
            }

            // The starter has to sound the instant the button is pressed, so it is preloaded;
            // the idle loop only has to be seamless and can wait.
            bool preload = loadType == AudioClipLoadType.DecompressOnLoad;

            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            if (settings.loadType == loadType && settings.preloadAudioData == preload) {
                return;
            }

            // Both fields are compared, not just the load type. Checking one and setting two
            // means the second never converges: the load type matched on the second run, the
            // step returned, and the preload flag it had never managed to write stayed wrong.
            settings.loadType = loadType;
            settings.preloadAudioData = preload;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// Puts a real track under the exhibit in place of the synthesised pad.
        ///
        /// <see cref="AnatomyAudioDirector"/> already has the hook for this - <c>musicOverride</c>
        /// bypasses the synthesis entirely - so this is one reference plus the import settings the
        /// clip needs to be sensible about memory.
        ///
        /// Streamed rather than decompressed into memory: this is a three-megabyte MP3 that plays
        /// for the whole session, and the default would unpack the entire thing as PCM at load.
        /// Preloading is off for the same reason, and the ducking the director already does keeps
        /// it under the interaction cues and the engine rather than over them.
        /// </summary>
        private static void ConfigureMusic(AnatomyAudioDirector audio) {
            if (audio == null) {
                return;
            }

            AudioClip track = EyeAnatomyAssetFactory.GetOrCreateSoothingMusic();
            if (track == null) {
                return;
            }

            SerializedObject so = new SerializedObject(audio);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "musicOverride", track);
            SetFloat(so, "musicVolume", 0.28f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Turns the body panels into clear-coated car paint.
        ///
        /// The model ships <c>Car Paint</c> as a plain dielectric at 0.69 smoothness, which is a
        /// satin plastic. Real car paint is two surfaces: a coloured base, and a thin transparent
        /// lacquer over it that is very nearly a mirror. URP Lit models exactly that with its clear
        /// coat, and it is the difference between a body that shades and a body that *reflects* -
        /// the streak that runs the length of a wing is the lacquer mirroring the studio, not the
        /// colour underneath catching a highlight.
        ///
        /// The shader has to change for it. <c>Universal Render Pipeline/Lit</c> has **no clear
        /// coat** - the properties are on the material because they are part of the shared URP
        /// property block, but the shader declares no <c>_CLEARCOAT</c> keyword and ignores them,
        /// which is exactly what setting them alone looked like: mask 1, smoothness 0.96, and no
        /// change whatsoever. Clear coat lives in <c>Complex Lit</c>. Property names are shared, so
        /// the swap carries the colour across and the swatches keep working unchanged.
        ///
        /// Complex Lit is the heavier shader and forward-only. It is put on the seventeen body
        /// renderers and nothing else.
        ///
        /// The chrome and the glossy black trim are lifted too, on the ordinary Lit - they are the
        /// parts that catch the window bands along the shoulder line.
        /// </summary>
        private static void ConfigurePaint() {
            Material paint = FindMaterial("Car Paint");
            if (paint == null) {
                Debug.LogWarning($"{nameof(VolvoExhibitBuilder)} could not find 'Car Paint'; the body " +
                    "will keep the satin finish it shipped with.");
            } else {
                Shader complex = Shader.Find("Universal Render Pipeline/Complex Lit");
                if (complex == null) {
                    Debug.LogWarning($"{nameof(VolvoExhibitBuilder)} could not find the Complex Lit " +
                        "shader; the paint gets a high smoothness instead of a clear coat.");
                    paint.SetFloat("_Smoothness", 0.92f);
                } else {
                    paint.shader = complex;
                    paint.SetFloat("_Smoothness", 0.82f);
                    // _ClearCoat is the feature toggle and _ClearCoatMask is its strength. They are
                    // two different properties and both are needed: URP re-validates the material
                    // on import and rebuilds the keyword from the *toggle*, so setting the mask and
                    // the keyword while the toggle stayed 0 got the keyword switched straight back
                    // off again with nothing logged.
                    paint.SetFloat("_ClearCoat", 1f);
                    paint.SetFloat("_ClearCoatMask", 1f);
                    paint.SetFloat("_ClearCoatSmoothness", 0.96f);
                    EnableLocalKeyword(paint, "_CLEARCOAT");
                }

                EditorUtility.SetDirty(paint);
            }

            SetGloss("Chrome", 0.93f, 1f);
            SetGloss("chrome.001", 0.93f, 1f);
            SetGloss("Black Glossy", 0.88f, 0f);
            SetGloss("Rims.001", 0.78f, 0.9f);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Switches a shader feature on properly.
        ///
        /// <c>Material.EnableKeyword(string)</c> does **not** do this for every keyword, which is
        /// worth knowing because it fails in total silence. Complex Lit declares <c>_CLEARCOAT</c>
        /// as a local keyword that is not overridable, and the string overload will not touch one
        /// of those: it writes the name into the material's keyword list, where it serialises and
        /// reads back convincingly, while <c>IsKeywordEnabled</c> stays false and the shader
        /// renders without the feature. The typed <see cref="LocalKeyword"/> overload is the one
        /// that works, and checking the result afterwards is the only way to know which you got.
        /// </summary>
        private static void EnableLocalKeyword(Material material, string keywordName) {
            LocalKeyword keyword = new LocalKeyword(material.shader, keywordName);
            if (!keyword.isValid) {
                Debug.LogWarning($"{nameof(VolvoExhibitBuilder)} found no '{keywordName}' keyword on " +
                    $"'{material.shader.name}'; '{material.name}' will render without that feature.");
                return;
            }

            material.SetKeyword(keyword, true);

            if (!material.IsKeywordEnabled(keywordName)) {
                Debug.LogWarning($"{nameof(VolvoExhibitBuilder)} could not enable '{keywordName}' on " +
                    $"'{material.name}'.");
            }
        }

        private static void SetGloss(string materialName, float smoothness, float metallic) {
            Material material = FindMaterial(materialName);
            if (material == null) {
                return;
            }

            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);
        }

        /// <summary>
        /// Puts the car in a showroom: a disc of floor under it, dark and smooth enough to draw the
        /// studio's softboxes into streaks, fading to the backdrop's own colour at its rim.
        ///
        /// What it is really there for is the **shadow**. The key light has cast soft shadows all
        /// along, onto nothing - there was no surface below the car to receive one - and a car
        /// floating with no contact under it is the single thing that most says "3D model" rather
        /// than "car". <see cref="EyeAnatomySceneUpgrader.UpgradeRenderQuality"/> is what makes that
        /// shadow legible at this scale.
        ///
        /// Kept to 0.45 m radius, not the metre a real showroom would suggest. This is a stereo
        /// display: geometry in front of the screen plane that runs off the edge of the frame is
        /// the classic window violation, and a floor is the easiest way to create one. At this size
        /// the gradient has reached the backdrop before it reaches the frame.
        ///
        /// Its own root, not a child of the pivot: the floor must not turn with the car, and must
        /// not be scaled by the focus view when a part is framed.
        /// </summary>
        private static void BuildShowroom(Transform car) {
            Mesh mesh = EyeAnatomyAssetFactory.GetOrCreateFloorMesh();
            Material material = EyeAnatomyAssetFactory.GetOrCreateFloorMaterial();
            if (mesh == null || material == null) {
                return;
            }

            GameObject showroom = FindOrCreateRoot(ShowroomName);
            GameObject floor = EyeAnatomySceneUpgrader.FindOrCreateChild(showroom.transform, "Floor");

            Bounds bounds;
            if (!TryMeasure(car, out bounds)) {
                return;
            }

            // A hair below the tyres, so the contact shadow lands tight under them rather than the
            // floor z-fighting with the tread.
            floor.transform.SetPositionAndRotation(
                new Vector3(bounds.center.x, bounds.min.y - 0.0004f, bounds.center.z), Quaternion.identity);
            floor.transform.localScale = new Vector3(FloorRadius, 1f, FloorRadius);

            MeshFilter filter = EyeAnatomySceneUpgrader.GetOrAdd<MeshFilter>(floor);
            filter.sharedMesh = mesh;

            MeshRenderer renderer = EyeAnatomySceneUpgrader.GetOrAdd<MeshRenderer>(floor);
            renderer.sharedMaterial = material;
            // Receives, never casts: the floor is under everything, and a disc casting into itself
            // only costs fill rate.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = true;

            EditorUtility.SetDirty(floor);
        }

        /// <summary>
        /// Gives the bodywork a studio to reflect, without changing the backdrop it is seen against.
        ///
        /// The two are set separately on purpose. <c>UpgradeEnvironment</c> leaves the skybox as the
        /// near-black gradient the whole exhibit is composed against, and this points reflections at
        /// a cubemap with real structure in it instead. See
        /// <see cref="EyeAnatomyAssetFactory.GetOrCreateStudioReflection"/> for why a car needs that
        /// and the eye did not.
        /// </summary>
        private static void BuildReflections() {
            Cubemap studio = EyeAnatomyAssetFactory.GetOrCreateStudioReflection();
            if (studio == null) {
                Debug.LogWarning($"{nameof(VolvoExhibitBuilder)} could not build the studio reflection; " +
                    "the paint will reflect the backdrop instead.");
                return;
            }

            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = studio;
            RenderSettings.reflectionIntensity = 1f;
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
                Debug.LogError($"{nameof(VolvoExhibitBuilder)} could not load the rig from '{XrRigPrefabPath}'.");
                return null;
            }

            GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.name = RigName;
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Undo.RegisterCreatedObjectUndo(rig, "Create " + RigName);
            return rig;
        }

        private static void WireExhibit(GameObject exhibit, Transform pivot, Transform car, Transform focusAnchor,
            GameObject rig, Camera camera, List<VehiclePanelGroup> groups, EyeAnatomyCatalog catalog,
            EyeExplodePoseSet tourPoses, KmaxStylus stylus, AnatomyAudioDirector audio,
            AnatomyParticleDirector particles, VolvoUi ui) {

            EyeManipulator manipulator = EyeAnatomySceneUpgrader.GetOrAdd<EyeManipulator>(exhibit);
            SerializedObject manipulatorSo = new SerializedObject(manipulator);
            EyeAnatomySceneUpgrader.SetIfPresent(manipulatorSo, "pivot", pivot);
            EyeAnatomySceneUpgrader.SetIfPresent(manipulatorSo, "referenceCamera", camera);
            manipulatorSo.ApplyModifiedPropertiesWithoutUndo();

            ViewerFlyController fly = EyeAnatomySceneUpgrader.GetOrAdd<ViewerFlyController>(exhibit);
            SerializedObject flySo = new SerializedObject(fly);
            EyeAnatomySceneUpgrader.SetIfPresent(flySo, "rigRoot", rig.transform);
            // Looking slightly down on the car, which is how a car is looked at and the only way
            // the showroom floor is visible at all - a horizontal disc seen from dead level is a
            // horizontal line. The eye and the engine leave this at zero.
            SetFloat(flySo, "homePitch", 14f);
            flySo.ApplyModifiedPropertiesWithoutUndo();

            EyeFocusView focus = EyeAnatomySceneUpgrader.GetOrAdd<EyeFocusView>(exhibit);
            SerializedObject focusSo = new SerializedObject(focus);
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "modelRoot", car);
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "xrRig", rig.GetComponentInChildren<XRRig>(true));
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "focusAnchor", focusAnchor);
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "eyeManipulator", manipulator);
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "flyController", fly);
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "ghostMaterial",
                AssetDatabase.LoadAssetAtPath<Material>(EyeAnatomySceneUpgrader.GhostMaterialPath));
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "focusHighlightMaterial",
                EyeAnatomyAssetFactory.GetOrCreateFocusHighlightMaterial());
            // Far wider than the eye's 0.48 or the engine's 0.56, because a car's features are a
            // large fraction of the car. The cabin alone is 60% of the wheelbase, and at 0.52 it
            // framed *smaller* than the resting view - the exhibit would have zoomed out to focus
            // it. Small stops are unaffected: a door mirror hits maxZoomMultiplier either way.
            SetFloat(focusSo, "framingRatio", 0.70f);
            focusSo.ApplyModifiedPropertiesWithoutUndo();

            ExhibitPostProcessing postFx = EyeAnatomySceneUpgrader.GetOrAdd<ExhibitPostProcessing>(exhibit);
            SerializedObject postFxSo = new SerializedObject(postFx);
            EyeAnatomySceneUpgrader.SetIfPresent(postFxSo, "cameraRoot", rig.transform);
            postFxSo.ApplyModifiedPropertiesWithoutUndo();

            VehiclePanelController panels = EyeAnatomySceneUpgrader.GetOrAdd<VehiclePanelController>(exhibit);
            panels.SetGroups(groups.ToArray());
            EditorUtility.SetDirty(panels);

            VehicleFinishSwatches paint = BuildPaintSwatches(exhibit, car, focus);
            VehicleFinishSwatches trim = BuildTrimSwatches(exhibit, car, focus);

            EyeExplodeView explode = EyeAnatomySceneUpgrader.GetOrAdd<EyeExplodeView>(exhibit);
            SerializedObject explodeSo = new SerializedObject(explode);
            EyeAnatomySceneUpgrader.SetIfPresent(explodeSo, "modelRoot", car);
            EyeAnatomySceneUpgrader.SetIfPresent(explodeSo, "poseSet", tourPoses);
            // Nothing moves - see BuildTourPoses - so this is only the beat between asking for the
            // tour and the badges arriving. Long enough to read as a response, short enough not to
            // feel like waiting for something that is not happening.
            SetFloat(explodeSo, "transitionDuration", 0.35f);
            explodeSo.ApplyModifiedPropertiesWithoutUndo();

            EyeAnatomyController controller = EyeAnatomySceneUpgrader.GetOrAdd<EyeAnatomyController>(exhibit);
            SerializedObject controllerSo = new SerializedObject(controller);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "explodeView", explode);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "focusView", focus);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "catalog", catalog);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "infoPanel", ui.InfoPanel);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "modelRoot", car);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "hotspotPrefab",
                AssetDatabase.LoadAssetAtPath<EyeHotspot>(EyeAnatomySceneUpgrader.HotspotPrefabPath));
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "manipulator", manipulator);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "flyController", fly);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "expandButton", ui.Tour);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "expandButtonLabel", ui.TourLabel);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "backButton", ui.Back);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "resetButton", ui.Reset);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "nextButton", ui.Next);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "previousButton", ui.Previous);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "partCounterLabel", ui.Counter);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "audioDirector", audio);
            EyeAnatomySceneUpgrader.SetIfPresent(controllerSo, "particles", particles);
            SetString(controllerSo, "expandLabel", "Start the tour");
            SetString(controllerSo, "collapseLabel", "End the tour");
            SetString(controllerSo, "partNoun", "features");
            // The car is 0.26 m long but only 0.075 m tall, and the badges have to read against
            // that shorter dimension, so they are the engine's size rather than scaled to length.
            SetFloat(controllerSo, "hotspotWorldRadius", 0.008f);
            SetFloat(controllerSo, "hotspotFrontGap", 0.012f);
            // The car never comes apart, so a badge in front of its own part is a badge inside the
            // bodywork. See EyeAnatomyController.HotspotPosition.
            SetBool(controllerSo, "hotspotsOutsideModel", true);
            controllerSo.ApplyModifiedPropertiesWithoutUndo();

            ExhibitAttractMode attract = EyeAnatomySceneUpgrader.GetOrAdd<ExhibitAttractMode>(exhibit);
            SerializedObject attractSo = new SerializedObject(attract);
            EyeAnatomySceneUpgrader.SetIfPresent(attractSo, "exhibitController", controller);
            EyeAnatomySceneUpgrader.SetIfPresent(attractSo, "flyController", fly);
            EyeAnatomySceneUpgrader.SetIfPresent(attractSo, "explodeView", explode);
            EyeAnatomySceneUpgrader.SetIfPresent(attractSo, "promptLabel", ui.AttractPrompt);
            attractSo.ApplyModifiedPropertiesWithoutUndo();

            WireFeaturePanel(paint, focus, ui.Paint);
            WireFeaturePanel(trim, focus, ui.Trim);

            AnatomyStylusInput input = EyeAnatomySceneUpgrader.GetOrAdd<AnatomyStylusInput>(exhibit);
            SerializedObject inputSo = new SerializedObject(input);
            EyeAnatomySceneUpgrader.SetIfPresent(inputSo, "stylus", stylus);
            EyeAnatomySceneUpgrader.SetIfPresent(inputSo, "flyController", fly);
            EyeAnatomySceneUpgrader.SetIfPresent(inputSo, "exhibitController", controller);
            EyeAnatomySceneUpgrader.SetIfPresent(inputSo, "rigRoot", rig.transform);
            inputSo.ApplyModifiedPropertiesWithoutUndo();

            EyeAnatomySceneUpgrader.WireSceneSwitcher(exhibit, ui.NextScene, audio, "EyeAnatomy");
        }

        /// <summary>
        /// Puts one swatch set on its own <see cref="ExhibitFeaturePanel"/>, alongside the
        /// <see cref="VehicleFinishSwatches"/> that does the work.
        ///
        /// No transparency button is passed. The panel treats it as optional, and a finish has no
        /// casing to see through - that feature belongs to the engine, which is where the interface
        /// came from. The focus view is still handed over, because focusing a part rewrites every
        /// material and the panel disables what it cannot honour while that is true.
        /// </summary>
        private static void WireFeaturePanel(VehicleFinishSwatches swatches, EyeFocusView focus, SwatchRow row) {
            ExhibitFeaturePanel panel = EyeAnatomySceneUpgrader.GetOrAdd<ExhibitFeaturePanel>(swatches.gameObject);
            SerializedObject so = new SerializedObject(panel);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "machinerySource", swatches);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "focusView", focus);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "variationLabel", row.NameLabel);

            SetObjectArray(so, "variationButtons", row.Buttons);
            SetObjectArray(so, "variationMarkers", row.Markers);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectArray(SerializedObject so, string fieldName, Object[] values) {
            SerializedProperty property = FindField(so, fieldName);
            if (property == null) {
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        private static void SetFloat(SerializedObject so, string fieldName, float value) {
            SerializedProperty property = FindField(so, fieldName);
            if (property != null) {
                property.floatValue = value;
            }
        }

        private static void SetBool(SerializedObject so, string fieldName, bool value) {
            SerializedProperty property = FindField(so, fieldName);
            if (property != null) {
                property.boolValue = value;
            }
        }

        private static void SetString(SerializedObject so, string fieldName, string value) {
            SerializedProperty property = FindField(so, fieldName);
            if (property != null) {
                property.stringValue = value;
            }
        }

        private static SerializedProperty FindField(SerializedObject so, string fieldName) {
            SerializedProperty property = so.FindProperty(fieldName);
            if (property == null) {
                Debug.LogWarning($"{nameof(VolvoExhibitBuilder)} found no field '{fieldName}' on " +
                    $"{so.targetObject.GetType().Name}.");
            }

            return property;
        }
        /// <summary>
        /// The interface objects the controls component has to be handed.
        /// </summary>
        private class VolvoUi {
            public Button[] PanelButtons = new Button[0];
            public Button Ignition;
            public Button Lights;
            public Button Reset;
            public Button NextScene;
            public Button Tour;
            public TextMeshProUGUI TourLabel;
            public Button Back;
            public Button Previous;
            public Button Next;
            public TextMeshProUGUI Counter;
            public TextMeshProUGUI AttractPrompt;
            public AnatomyInfoPanel InfoPanel;
            public SwatchRow Paint = new SwatchRow();
            public SwatchRow Trim = new SwatchRow();
        }

        /// <summary>
        /// One row of colour swatches: the chips, the ring on each that marks the chosen one, and
        /// the label that names it - the chips carry no text of their own.
        /// </summary>
        private class SwatchRow {
            public Button[] Buttons = new Button[0];
            public GameObject[] Markers = new GameObject[0];
            public TextMeshProUGUI NameLabel;
        }

        /// <summary>
        /// One lamp of the car: what it is called, which meshes glow, and in what colour.
        /// </summary>
        private class LampSpec {
            public string Name;
            public string[] Meshes;
            public Color Emission;
            public bool Blinks;

            public LampSpec(string name, Color emission, bool blinks, string[] meshes) {
                Name = name;
                Emission = emission;
                Blinks = blinks;
                Meshes = meshes;
            }
        }

        /// <summary>
        /// Every lamp the model has geometry for.
        ///
        /// The car is unusually well served here - the modeller built each reflector, diffuser and
        /// emitter as its own mesh rather than painting them onto the body - so nothing has to be
        /// approximated. The emission colours are HDR and deliberately above 1: the tonemapper
        /// rolls them back, and a lamp that peaks at exactly white reads as a grey sticker next to
        /// bodywork this dark.
        /// </summary>
        private static LampSpec[] GetLamps() {
            return new LampSpec[] {
                // Subtle warm courtesy illumination on the ceiling console and gear selector
                new LampSpec("Interior", new Color(0.12f, 0.10f, 0.08f), false, new string[] {
                    "CeilingConsole", "Shifterknob Crystal" }),
                // Crisp modern digital display backlight for cockpit screens
                new LampSpec("Dashboard", new Color(0.55f, 0.62f, 0.72f), false, new string[] {
                    "InfoTainment Screen", "SpeedoScreen" }),
                new LampSpec("Daytime", new Color(1.8f, 1.9f, 2.1f), false, new string[] {
                    "Glass Runninglight", "Reflector Runninglight" }),
                new LampSpec("Headlights", new Color(2.0f, 2.0f, 1.9f), false, new string[] {
                    "Emitters Headlight", "Reflector Headlight 1", "Reflector Headlight 2",
                    "Reflector Highbeam", "Logo Headlight" }),
                new LampSpec("Fog", new Color(1.8f, 1.6f, 1.2f), false, new string[] {
                    "Foglight Glass", "Foglight Reflector" }),
                new LampSpec("Tail", new Color(2.4f, 0.08f, 0.03f), false, new string[] {
                    "MainReflectors Taillight", "Diffusers Taillight", "Reflector 2 Taillight",
                    "Reflector 3 Taillight", "Reflector Cubes Taillight", "Diffuser TrunkTaillight",
                    "Reflector TrunkTaillight", "Reflector 2 Trunktaillight", "Lightbulb TrunkTaillight" }),
                new LampSpec("Reverse", new Color(2.0f, 2.0f, 1.9f), false, new string[] {
                    "Diffuser ReverseLight", "Reflector ReverseLight" }),
                new LampSpec("Indicators", new Color(2.4f, 0.8f, 0.03f), true, new string[] {
                    "Glass Turnsignal", "Reflector Turnsignal", "MirrorTurnsignal Reflector",
                    "Diffuser 3 Taillight", "Side Diffuser Taillight" })
            };
        }

        /// <summary>
        /// Builds the lamp channels, and the handful of real lights that have to cast rather than
        /// just glow - the headlights throwing forward, and one in the cabin.
        /// </summary>
        private static VehicleLightRig BuildLightRig(GameObject exhibit, Transform car) {
            LampSpec[] lamps = GetLamps();
            List<VehicleLightChannel> channels = new List<VehicleLightChannel>();
            Bounds carBounds;
            TryMeasure(car, out carBounds);

            for (int i = 0; i < lamps.Length; i++) {
                Renderer[] renderers = CollectRenderers(car, lamps[i].Meshes);
                if (renderers.Length == 0) {
                    Debug.LogWarning($"{nameof(VolvoExhibitBuilder)} found no meshes for the '{lamps[i].Name}' lamp.");
                }

                Light[] lights = BuildLampLights(car, lamps[i].Name, carBounds);
                channels.Add(new VehicleLightChannel(lamps[i].Name, renderers, lights, lamps[i].Emission, lamps[i].Blinks));
            }

            VehicleLightRig rig = EyeAnatomySceneUpgrader.GetOrAdd<VehicleLightRig>(exhibit);
            rig.SetChannels(channels.ToArray());
            EditorUtility.SetDirty(rig);
            return rig;
        }

        /// <summary>
        /// Real lights for the two channels that should affect what is around them.
        ///
        /// Ranges are in world metres and do not scale with the car, so they are derived from the
        /// exhibit-sized bounds rather than written as the real-world figures they represent - a
        /// headlight that throws 40 m would light the whole scene evenly at this size.
        /// </summary>
        private static Light[] BuildLampLights(Transform car, string lampName, Bounds carBounds) {
            if (lampName == "Headlights") {
                float halfWidth = carBounds.extents.x * 0.62f;
                Light left = EnsureChildLight(car, "Headlight Beam L", LightType.Spot,
                    new Vector3(carBounds.center.x - halfWidth, carBounds.center.y, carBounds.max.z * 0.95f));
                Light right = EnsureChildLight(car, "Headlight Beam R", LightType.Spot,
                    new Vector3(carBounds.center.x + halfWidth, carBounds.center.y, carBounds.max.z * 0.95f));
                ConfigureBeam(left, carBounds);
                ConfigureBeam(right, carBounds);
                return new Light[] { left, right };
            }

            if (lampName == "Interior") {
                Light cabin = EnsureChildLight(car, "Cabin Light", LightType.Point,
                    new Vector3(carBounds.center.x, carBounds.center.y + carBounds.extents.y * 0.35f, carBounds.center.z));
                cabin.color = new Color(1f, 0.92f, 0.82f);
                // Stays within the cabin volume so it gently accents the interior without bleaching the cabin or leaking through the roof
                cabin.range = carBounds.size.z * 0.15f;
                cabin.intensity = 0.0008f;
                cabin.shadows = LightShadows.None;
                cabin.enabled = false;
                return new Light[] { cabin };
            }

            return new Light[0];
        }

        private static void ConfigureBeam(Light light, Bounds carBounds) {
            light.color = new Color(1f, 0.97f, 0.92f);
            light.range = carBounds.size.z * 0.85f;
            // 0.018f gives two clear, realistic, elegant headlight pools on the floor without clipping or washing out
            light.intensity = 0.018f;
            light.spotAngle = 44f;
            light.innerSpotAngle = 22f;
            light.shadows = LightShadows.None;
            light.transform.localRotation = Quaternion.Euler(4f, 0f, 0f);
            light.enabled = false;
        }

        private static Light EnsureChildLight(Transform parent, string name, LightType type, Vector3 worldPosition) {
            Transform existing = parent.Find(name);
            GameObject target;

            if (existing != null) {
                target = existing.gameObject;
            } else {
                target = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(target, "Create " + name);
                target.transform.SetParent(parent, true);
            }

            target.transform.position = worldPosition;
            Light light = EyeAnatomySceneUpgrader.GetOrAdd<Light>(target);
            light.type = type;
            return light;
        }

        /// <summary>
        /// Finds the renderers for a lamp by name, matching a split half to the panel it came from
        /// so that, for instance, the mirror indicators are still found after being cut in two and
        /// hung off the doors.
        /// </summary>
        private static Renderer[] CollectRenderers(Transform car, string[] names) {
            List<Renderer> found = new List<Renderer>();
            Renderer[] all = car.GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < all.Length; i++) {
                string rendererName = all[i].name.Trim();

                for (int j = 0; j < names.Length; j++) {
                    string wanted = names[j].Trim();
                    if (rendererName == wanted || rendererName == wanted + " L" || rendererName == wanted + " R") {
                        found.Add(all[i]);
                        break;
                    }
                }
            }

            return found.ToArray();
        }

        /// <summary>
        /// Puts the ignition on the exhibit and gives it two sources: the starter, heard from the
        /// car as a whole, and the idle, placed at the tailpipe so the exhaust note has somewhere
        /// to come from as the viewer orbits.
        /// </summary>
        private static VehicleIgnition BuildIgnition(GameObject exhibit, Transform car, VehicleLightRig lights) {
            Bounds bounds;
            TryMeasure(car, out bounds);

            AudioSource start = EnsureAudioSource(car, "Engine Audio",
                new Vector3(bounds.center.x, bounds.center.y, bounds.max.z * 0.5f), bounds);
            AudioSource idle = EnsureAudioSource(car, "Exhaust Audio",
                new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.1f, bounds.min.z), bounds);
            idle.loop = true;

            VehicleIgnition ignition = EyeAnatomySceneUpgrader.GetOrAdd<VehicleIgnition>(exhibit);
            SerializedObject so = new SerializedObject(ignition);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "lights", lights);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "startSource", start);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "idleSource", idle);
            SetStringArray(so, "accessoryChannels", new string[] { "Interior", "Dashboard" });
            SetStringArray(so, "runningChannels", new string[] { "Daytime", "Headlights", "Tail", "Fog", "Indicators" });
            so.ApplyModifiedPropertiesWithoutUndo();
            return ignition;
        }

        private static AudioSource EnsureAudioSource(Transform car, string name, Vector3 worldPosition, Bounds bounds) {
            Transform existing = car.Find(name);
            GameObject target;

            if (existing != null) {
                target = existing.gameObject;
            } else {
                target = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(target, "Create " + name);
                target.transform.SetParent(car, true);
            }

            target.transform.position = worldPosition;

            AudioSource source = EyeAnatomySceneUpgrader.GetOrAdd<AudioSource>(target);
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            // Sized to the car rather than left at the default 500 m, which at this scale would be
            // the same volume everywhere and give the exhaust no position at all.
            source.minDistance = bounds.size.z * 0.5f;
            source.maxDistance = bounds.size.z * 8f;
            source.dopplerLevel = 0f;
            return source;
        }

        /// <summary>
        /// The interface: one button per panel group down the left, the ignition and the lamps
        /// beneath them, and Reset on the right.
        /// </summary>
        private static VolvoUi BuildUi(GameObject ui, Camera camera, List<VehiclePanelGroup> groups) {
            VolvoUi result = new VolvoUi();
            RectTransform rect = ExhibitUiFactory.BuildWorldCanvas(ui, camera);
            Vector2 topLeft = new Vector2(0f, 1f);
            Vector2 topRight = new Vector2(1f, 1f);
            Vector2 bottomLeft = new Vector2(0f, 0f);
            Vector2 bottomCenter = new Vector2(0.5f, 0f);
            Vector2 bottomRight = new Vector2(1f, 0f);

            TextMeshProUGUI title = ExhibitUiFactory.BuildLabel(rect, "Title", "Volvo S90", 22f,
                topLeft, new Vector2(24f, -18f), new Vector2(360f, 28f), TextAlignmentOptions.Left);
            title.fontStyle = FontStyles.Bold;

            // Bottom-Left: Start the tour & Back to the car
            result.Tour = ExhibitUiFactory.BuildIconButton(rect, "TourButton", "Start the tour",
                ExhibitIconFactory.Tour,
                bottomLeft, new Vector2(24f, 24f), new Vector2(166f, 36f), 14f);
            result.TourLabel = result.Tour.GetComponentInChildren<TextMeshProUGUI>(true);
            result.Back = ExhibitUiFactory.BuildButton(rect, "BackButton", "Back to the car",
                bottomLeft, new Vector2(24f, 68f), new Vector2(150f, 32f), 13.5f);

            // Bottom-Center: < Back, Counter, Next >, AttractPrompt
            result.Previous = ExhibitUiFactory.BuildButton(rect, "PreviousButton", "< Back",
                bottomCenter, new Vector2(-116f, 24f), new Vector2(84f, 34f), 14f);
            result.Counter = ExhibitUiFactory.BuildLabel(rect, "PartCounter", "-", 14f,
                bottomCenter, new Vector2(0f, 24f), new Vector2(120f, 34f), TextAlignmentOptions.Center);
            result.Counter.color = new Color(0.76f, 0.90f, 1.00f, 0.95f);
            result.Next = ExhibitUiFactory.BuildButton(rect, "NextButton", "Next >",
                bottomCenter, new Vector2(116f, 24f), new Vector2(84f, 34f), 14f);
            result.AttractPrompt = ExhibitUiFactory.BuildLabel(rect, "AttractPrompt",
                "Touch the car to explore it", 14f,
                bottomCenter, new Vector2(0f, 64f), new Vector2(420f, 24f), TextAlignmentOptions.Center);
            result.AttractPrompt.color = new Color(0.75f, 0.90f, 1.00f, 0.92f);

            // Bottom-Right: Reset view & Next Scene side-by-side
            result.Reset = ExhibitUiFactory.BuildIconButton(rect, "ResetButton", "Reset view",
                ExhibitIconFactory.Reset,
                bottomRight, new Vector2(-168f, 24f), new Vector2(134f, 36f), 14f);
            result.NextScene = ExhibitUiFactory.BuildButton(rect, "NextSceneButton", "Next Scene",
                bottomRight, new Vector2(-24f, 24f), new Vector2(136f, 36f), 14f);

            // Top-Left below Title: Panel buttons (Doors, Sunroof), Ignition & Lights in a compact left column
            result.PanelButtons = new Button[groups.Count];
            for (int i = 0; i < groups.Count; i++) {
                string glyph = groups[i].DisplayName == "Sunroof"
                    ? ExhibitIconFactory.Sunroof
                    : ExhibitIconFactory.Doors;
                float y = -56f - i * 38f;
                result.PanelButtons[i] = ExhibitUiFactory.BuildIconButton(rect, "PanelButton" + (i + 1),
                    groups[i].OpenLabel, glyph, topLeft, new Vector2(24f, y),
                    new Vector2(158f, 32f), 13f);
                EyeAnatomySceneUpgrader.AddMotion(result.PanelButtons[i].gameObject, false);
            }

            const int MaxPanelButtons = 16;
            for (int i = groups.Count; i < MaxPanelButtons; i++) {
                Transform surplus = rect.Find("PanelButton" + (i + 1));
                if (surplus == null) {
                    continue;
                }

                Undo.DestroyObjectImmediate(surplus.gameObject);
            }

            float ignitionY = -56f - groups.Count * 38f - 4f;
            result.Ignition = ExhibitUiFactory.BuildIconButton(rect, "IgnitionButton", "Start the car",
                ExhibitIconFactory.Ignition,
                topLeft, new Vector2(24f, ignitionY), new Vector2(158f, 34f), 13.5f);
            result.Lights = ExhibitUiFactory.BuildIconButton(rect, "LightsButton", "Lights on",
                ExhibitIconFactory.Lights,
                topLeft, new Vector2(24f, ignitionY - 40f), new Vector2(158f, 34f), 13.5f);

            // Lower-Left below Lights: compact Paint & Interior Trim swatches so Top-Right stays 100% dedicated to InfoPanel
            float paintTopY = ignitionY - 88f;
            result.Paint = BuildSwatchRow(rect, "PaintSwatch", "PaintCaption", "PAINT", "PaintName",
                24f, paintTopY, GetPaintNames(), GetPaintChipColors());
            result.Trim = BuildSwatchRow(rect, "TrimSwatch", "TrimCaption", "INTERIOR", "TrimName",
                24f, paintTopY - 72f, GetTrimNames(), GetTrimChipColors());

            // Top-Right: Information text panel (InfoPanel)
            result.InfoPanel = ExhibitUiFactory.BuildInfoPanel(rect, topRight,
                new Vector2(-24f, -24f), new Vector2(410f, 145f));

            EyeAnatomySceneUpgrader.AddMotion(result.Ignition.gameObject, true);
            EyeAnatomySceneUpgrader.AddMotion(result.Lights.gameObject, false);
            EyeAnatomySceneUpgrader.AddMotion(result.Reset.gameObject, false);
            EyeAnatomySceneUpgrader.AddMotion(result.NextScene.gameObject, false);
            EyeAnatomySceneUpgrader.AddMotion(result.Tour.gameObject, true);
            EyeAnatomySceneUpgrader.AddMotion(result.Back.gameObject, false);
            EyeAnatomySceneUpgrader.AddMotion(result.Previous.gameObject, false);
            EyeAnatomySceneUpgrader.AddMotion(result.Next.gameObject, false);
            return result;
        }

        private static SwatchRow BuildSwatchRow(RectTransform parent, string namePrefix, string captionName,
            string caption, string nameLabelName, float left, float top, string[] labels, Color[] chipColours) {

            const float ChipSize = 26f;
            const float ChipStep = 31f;
            Vector2 topLeft = new Vector2(0f, 1f);

            TextMeshProUGUI captionLabel = ExhibitUiFactory.BuildLabel(parent, captionName, caption, 11.5f,
                topLeft, new Vector2(left, top), new Vector2(160f, 16f), TextAlignmentOptions.Left);
            captionLabel.color = new Color(0.56f, 0.82f, 0.98f, 0.90f);
            captionLabel.fontStyle = FontStyles.Bold;

            SwatchRow row = new SwatchRow();
            row.Buttons = new Button[labels.Length];
            row.Markers = new GameObject[labels.Length];

            for (int i = 0; i < labels.Length; i++) {
                float x = left + i * ChipStep;
                ExhibitUiFactory.SwatchChip chip = ExhibitUiFactory.BuildSwatchChip(parent,
                    namePrefix + (i + 1), i < chipColours.Length ? chipColours[i] : Color.grey,
                    topLeft, new Vector2(x, top - 18f), new Vector2(ChipSize, ChipSize));

                row.Buttons[i] = chip.Button;
                row.Markers[i] = chip.Marker;
                EyeAnatomySceneUpgrader.AddMotion(chip.Button.gameObject, false);
            }

            row.NameLabel = ExhibitUiFactory.BuildLabel(parent, nameLabelName, labels.Length > 0 ? labels[0] : "-",
                13.5f, topLeft, new Vector2(left, top - 48f), new Vector2(160f, 18f),
                TextAlignmentOptions.Left);

            const int MaxSwatches = 12;
            for (int i = labels.Length; i < MaxSwatches; i++) {
                Transform surplus = parent.Find(namePrefix + (i + 1));
                if (surplus == null) {
                    continue;
                }

                Undo.DestroyObjectImmediate(surplus.gameObject);
            }

            return row;
        }

        private static void WireControls(GameObject exhibit, List<VehiclePanelGroup> groups,
            VehicleLightRig lights, VehicleIgnition ignition, VolvoUi ui) {
            VehicleExhibitControls controls = EyeAnatomySceneUpgrader.GetOrAdd<VehicleExhibitControls>(exhibit);
            SerializedObject so = new SerializedObject(controls);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "panels", exhibit.GetComponent<VehiclePanelController>());
            EyeAnatomySceneUpgrader.SetIfPresent(so, "ignition", ignition);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "lights", lights);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "flyController", exhibit.GetComponent<ViewerFlyController>());
            EyeAnatomySceneUpgrader.SetIfPresent(so, "manipulator", exhibit.GetComponent<EyeManipulator>());
            EyeAnatomySceneUpgrader.SetIfPresent(so, "ignitionButton", ui.Ignition);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "lightsButton", ui.Lights);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "resetButton", ui.Reset);

            SerializedProperty buttons = so.FindProperty("panelButtons");
            buttons.arraySize = ui.PanelButtons.Length;
            for (int i = 0; i < ui.PanelButtons.Length; i++) {
                buttons.GetArrayElementAtIndex(i).objectReferenceValue = ui.PanelButtons[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetStringArray(SerializedObject so, string fieldName, string[] values) {
            SerializedProperty property = so.FindProperty(fieldName);
            if (property == null) {
                Debug.LogWarning($"{nameof(VolvoExhibitBuilder)} found no field '{fieldName}' on " +
                    $"{so.targetObject.GetType().Name}.");
                return;
            }

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) {
                property.GetArrayElementAtIndex(i).stringValue = values[i];
            }
        }

        /// <summary>
        /// One interior material, and which of its shipped textures each trim swatch puts on it.
        /// </summary>
        private class TrimSpec {
            public string Material;
            public string Folder;
            public string Prefix;
            public string[] Variants;

            public TrimSpec(string material, string folder, string prefix, string[] variants) {
                Material = material;
                Folder = folder;
                Prefix = prefix;
                Variants = variants;
            }
        }

        private static string[] GetPaintNames() {
            return new string[] { "Onyx Black", "Crystal White", "Denim Blue", "Osmium Grey", "Fusion Red" };
        }

        /// <summary>
        /// The paint swatches, as base colours.
        ///
        /// <c>Car Paint</c> is a single untextured material covering the whole body - shell,
        /// bumpers, wings, both door skins, the hood and the boot lid - so a colour is the entire
        /// change. Its clear-coat smoothness of 0.69 is what makes these read as car paint rather
        /// than as flat plastic, and it is left alone.
        ///
        /// The first swatch is the black the model ships with, exactly, so that the panel applying
        /// its default selection on the first frame changes nothing the viewer can see.
        /// </summary>
        private static Color[] GetPaintColors() {
            return new Color[] {
                new Color(0f, 0f, 0f),
                new Color(0.66f, 0.67f, 0.68f),
                new Color(0.02f, 0.05f, 0.16f),
                new Color(0.10f, 0.11f, 0.12f),
                new Color(0.30f, 0.015f, 0.02f)
            };
        }

        /// <summary>
        /// The same paints as the swatches show them.
        ///
        /// Converted out of linear, because <c>_BaseColor</c> is a shader constant and a UI
        /// <c>Image</c>'s colour is not: handing the same numbers to both would draw Crystal White
        /// as a mid grey and Fusion Red as near-black. Then lifted, because a chip is a flat fill
        /// and a car body is a clear-coated surface read almost entirely from its highlight - the
        /// paint that reads as red on a wing reads as dried blood on a square.
        /// </summary>
        private static Color[] GetPaintChipColors() {
            Color[] paints = GetPaintColors();
            Color[] chips = new Color[paints.Length];

            for (int i = 0; i < paints.Length; i++) {
                Color display = paints[i].gamma;
                chips[i] = new Color(
                    Mathf.Lerp(display.r, 1f, 0.18f),
                    Mathf.Lerp(display.g, 1f, 0.18f),
                    Mathf.Lerp(display.b, 1f, 0.18f),
                    1f);
            }

            return chips;
        }

        private static string[] GetTrimNames() {
            return new string[] { "Black", "Blue", "Brown", "Tan" };
        }

        /// <summary>
        /// The interior swatches as chips.
        ///
        /// Authored rather than sampled from the textures they stand for: the interior maps import
        /// without read/write access, so averaging one at build time would mean flipping its
        /// importer, and an average of a leather map is a muddy grey anyway - the colour a viewer
        /// would call "Brown" is the colour of the hide, not the mean of its creases and shadows.
        /// </summary>
        private static Color[] GetTrimChipColors() {
            return new Color[] {
                new Color(0.13f, 0.13f, 0.14f),
                new Color(0.20f, 0.28f, 0.47f),
                new Color(0.40f, 0.25f, 0.16f),
                new Color(0.80f, 0.72f, 0.58f)
            };
        }

        /// <summary>
        /// The interior swatches, as base maps.
        ///
        /// The model ships a full set of interior textures and they are not symmetric: the console,
        /// the seats and the door cards have four apiece, but the dashboard's black and blue are
        /// one texture, the shell's blue and brown are one, and the steering wheel has only black
        /// and tan. The table repeats a texture wherever the source does, which is what keeps the
        /// four swatches whole - a missing entry would leave one surface behind on the last choice.
        ///
        /// Every first column is the texture the material already carries, so the default selection
        /// is a no-op like the paint's.
        /// </summary>
        private static TrimSpec[] GetTrimSpecs() {
            return new TrimSpec[] {
                new TrimSpec("Shell", "Shell", "Shell_Base_Color",
                    new string[] { "Black", "BlueBrown", "BlueBrown", "Tan" }),
                new TrimSpec("Dashboard", "Dashboard", "Dashboard_Base_Color",
                    new string[] { "BlackBlue", "BlackBlue", "Brown", "Tan" }),
                new TrimSpec("CenterConsole", "CenterConsole", "CenterConsole_Base_Color",
                    new string[] { "Black", "Blue", "Brown", "Tan" }),
                new TrimSpec("Front Seat", "Seats/Front", "Front_Seat_Base_Color",
                    new string[] { "black", "Blue", "Brown", "Tan" }),
                new TrimSpec("Rear Seats", "Seats/Rear", "Rear Seats_Base_Color",
                    new string[] { "Black", "Blue", "Brown", "Tan" }),
                new TrimSpec("Steeringwheel", "SteeringWheel", "Steeringwheel_Base_Color",
                    new string[] { "Black", "Black", "Black", "Tan" }),
                new TrimSpec("DoorPanelFront", "DoorPanels/Front", "DoorPanelFront_Base_Color",
                    new string[] { "Black", "Blue", "Brown", "Tan" }),
                new TrimSpec("DoorPanelRear", "DoorPanels/Rear", "DoorPanelRear_Base_Color",
                    new string[] { "Black", "Blue", "Brown", "Tan" })
            };
        }

        /// <summary>
        /// Puts the paint swatches on their own node under the exhibit.
        ///
        /// Each swatch set gets a node of its own because each needs its own
        /// <see cref="ExhibitFeaturePanel"/>, and that component holds one run of variants - the
        /// shape it was written for. Two of them on the exhibit root would be indistinguishable to
        /// every find-before-create step in this file.
        /// </summary>
        private static VehicleFinishSwatches BuildPaintSwatches(GameObject exhibit, Transform car, EyeFocusView focus) {
            GameObject host = EyeAnatomySceneUpgrader.FindOrCreateChild(exhibit.transform, PaintFeatureName);
            VehicleFinishSwatches swatches = EyeAnatomySceneUpgrader.GetOrAdd<VehicleFinishSwatches>(host);
            WireSwatches(swatches, car, focus);

            Material paint = FindMaterial("Car Paint");
            if (paint == null) {
                Debug.LogError($"{nameof(VolvoExhibitBuilder)} could not find the 'Car Paint' material; " +
                    "the paint swatches are empty.");
                swatches.SetSwatches(new string[0], new VehicleFinishTarget[0]);
                EditorUtility.SetDirty(swatches);
                return swatches;
            }

            VehicleFinishTarget target = new VehicleFinishTarget(paint, new Texture2D[0], GetPaintColors());
            swatches.SetSwatches(GetPaintNames(), new VehicleFinishTarget[] { target });
            EditorUtility.SetDirty(swatches);
            return swatches;
        }

        private static VehicleFinishSwatches BuildTrimSwatches(GameObject exhibit, Transform car, EyeFocusView focus) {
            GameObject host = EyeAnatomySceneUpgrader.FindOrCreateChild(exhibit.transform, TrimFeatureName);
            VehicleFinishSwatches swatches = EyeAnatomySceneUpgrader.GetOrAdd<VehicleFinishSwatches>(host);
            WireSwatches(swatches, car, focus);

            string[] names = GetTrimNames();
            TrimSpec[] specs = GetTrimSpecs();
            List<VehicleFinishTarget> targets = new List<VehicleFinishTarget>();

            for (int i = 0; i < specs.Length; i++) {
                Material material = FindMaterial(specs[i].Material);
                if (material == null) {
                    Debug.LogError($"{nameof(VolvoExhibitBuilder)} could not find the " +
                        $"'{specs[i].Material}' material; that surface will not change with the trim.");
                    continue;
                }

                Texture2D[] maps = new Texture2D[names.Length];
                bool complete = true;

                for (int j = 0; j < names.Length; j++) {
                    string path = $"{InteriorTextureRoot}/{specs[i].Folder}/{specs[i].Prefix} {specs[i].Variants[j]}.png";
                    maps[j] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                    if (maps[j] == null) {
                        Debug.LogError($"{nameof(VolvoExhibitBuilder)} could not load '{path}'; " +
                            $"'{specs[i].Material}' will not change with the trim.");
                        complete = false;
                    }
                }

                if (!complete) {
                    continue;
                }

                targets.Add(new VehicleFinishTarget(material, maps, new Color[0]));
            }

            swatches.SetSwatches(names, targets.ToArray());
            EditorUtility.SetDirty(swatches);
            return swatches;
        }

        private static void WireSwatches(VehicleFinishSwatches swatches, Transform car, EyeFocusView focus) {
            SerializedObject so = new SerializedObject(swatches);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "modelRoot", car);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "focusView", focus);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// One glazed material and how see-through it should be.
        /// </summary>
        private class GlassSpec {
            public string Material;
            public float Alpha;
            public float Smoothness;

            public GlassSpec(string material, float alpha, float smoothness) {
                Material = material;
                Alpha = alpha;
                Smoothness = smoothness;
            }
        }

        /// <summary>
        /// Turns the car's glass into actual glass.
        ///
        /// Every glazed material on this model imported as an opaque URP Lit surface -
        /// <c>_Surface</c> 0, alpha 1, render queue 2000 - so the windows, the sunroof and the lamp
        /// lenses were all solid panels. With the windows opaque there is no interior to see from
        /// outside at all, and the windscreen reads as a sheet of white where it catches the key.
        ///
        /// URP decides transparency from a set of floats and a keyword that all have to agree;
        /// setting the colour's alpha alone leaves the material opaque, which is why each of these
        /// is written explicitly.
        ///
        /// This edits the shared material assets rather than instancing them. That is deliberate -
        /// opaque glass is simply wrong, and a per-renderer copy would leave the asset broken for
        /// anything else that used it.
        /// </summary>
        private static void ConfigureGlass() {
            GlassSpec[] specs = new GlassSpec[] {
                // The windows: low alpha, very smooth. The interior has to read through them.
                new GlassSpec("Glass", 0.22f, 0.96f),
                new GlassSpec("Glass_wavey", 0.35f, 0.95f),
                // Lamp lenses sit over their own reflectors, so they stay more solid or the
                // emitters behind them look like they are floating.
                new GlassSpec("Glass headlights", 0.42f, 0.94f),
                new GlassSpec("Glass Taillight", 0.45f, 0.92f),
                new GlassSpec("GlassRunninglight", 0.45f, 0.92f),
                new GlassSpec("Translucent_Glass", 0.55f, 0.85f),
                new GlassSpec("Translucent_Glass.001", 0.55f, 0.85f),
                new GlassSpec("heaxagon glass", 0.5f, 0.9f),
                new GlassSpec("crystal", 0.4f, 0.98f),
                new GlassSpec("Black Glass", 0.5f, 0.9f)
            };

            int changed = 0;
            for (int i = 0; i < specs.Length; i++) {
                Material material = FindMaterial(specs[i].Material);
                if (material == null) {
                    continue;
                }

                MakeTransparent(material, specs[i].Alpha, specs[i].Smoothness);
                changed++;
            }

            AssetDatabase.SaveAssets();

            if (changed > 0) {
                Debug.Log($"{nameof(VolvoExhibitBuilder)} made {changed} glazed material(s) transparent; " +
                    "they imported as opaque, which left the windows solid.");
            }
        }

        private static Material FindMaterial(string name) {
            string[] guids = AssetDatabase.FindAssets("t:Material", new string[] { MaterialRoot });

            for (int i = 0; i < guids.Length; i++) {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null && material.name == name) {
                    return material;
                }
            }

            return null;
        }

        private static void MakeTransparent(Material material, float alpha, float smoothness) {
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            if (material.HasProperty("_BaseColor")) {
                Color colour = material.GetColor("_BaseColor");
                colour.a = alpha;
                material.SetColor("_BaseColor", colour);
            }

            if (material.HasProperty("_Color")) {
                Color colour = material.GetColor("_Color");
                colour.a = alpha;
                material.SetColor("_Color", colour);
            }

            if (material.HasProperty("_Smoothness")) {
                material.SetFloat("_Smoothness", smoothness);
            }

            EditorUtility.SetDirty(material);
        }

    }
}
