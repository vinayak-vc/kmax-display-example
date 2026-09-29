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
        private const string ScenePath = VolvoRoot + "/VolvoS90.unity";
        private const string ModelPath = VolvoRoot + "/Volvo S90.fbx";
        private const string SplitFolder = VolvoRoot + "/Split";
        private const string XrRigPrefabPath = ModuleRoot + "/Plugins/Kmax/com.kmax.xr.core/Editor Resources/XRRig.prefab";

        private const string ExhibitName = "VolvoExhibit";
        private const string PivotName = "VolvoModelPivot";
        private const string CarName = "Volvo S90";
        private const string PanelsName = "Panels";
        private const string FocusAnchorName = "FocusAnchor";
        private const string RigName = "XRRig";
        private const string UiName = "UI";
        private const string AmbienceName = "Ambience";
        private const string EventSystemName = "EventSystem";

        /// <summary>
        /// Longest edge of the car once it is in the exhibit, in metres.
        ///
        /// The car is 5 m long against a virtual screen 0.345 m wide. At 0.26 m it fills about
        /// three quarters of the screen broadside and still clears it with all four doors open,
        /// which swing the widest silhouette the exhibit ever has to frame.
        /// </summary>
        private const float TargetLength = 0.26f;

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
            NormaliseCar(car);
            ConfigureGlass();

            Transform focusAnchor = EyeAnatomySceneUpgrader.FindOrCreateChild(exhibit.transform, FocusAnchorName).transform;
            focusAnchor.localPosition = Vector3.zero;

            EyeAnatomySceneUpgrader.UpgradeRenderPipeline();
            EyeAnatomySceneUpgrader.UpgradeEnvironment();
            EyeAnatomySceneUpgrader.UpgradePostProcessing(rig, exhibit);
            BuildLighting();

            EyeAnatomySceneUpgrader.UpgradeEventSystem(eventSystem);
            EyeAnatomySceneUpgrader.UpgradeCamera(camera);
            EyeAnatomySceneUpgrader.RemoveDuplicatePens(rig);
            KmaxStylus stylus = EyeAnatomySceneUpgrader.BuildStylus(rig, camera);
            AnatomyParticleDirector particles = EyeAnatomySceneUpgrader.UpgradeParticles(ambience);

            VehicleLightRig lightRig = BuildLightRig(exhibit, car);
            VehicleIgnition ignition = BuildIgnition(exhibit, car, lightRig);
            VolvoUi volvoUi = BuildUi(ui, camera, groups);

            WireExhibit(exhibit, pivot, car, focusAnchor, rig, camera, groups, stylus, particles, ui);
            WireControls(exhibit, groups, lightRig, ignition, volvoUi);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();

            Debug.Log($"{nameof(VolvoExhibitBuilder)} finished: {groups.Count} panel group(s) hinged.");
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
                new SoloPanel("SunRoof", "Sunroof Hinge", Vector3.right, 14f, true,
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
                return existing;
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
                return existingPivot.GetComponent<VehiclePanel>();
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
            SerializedObject so = new SerializedObject(panel);
            so.FindProperty("hingeAxis").vector3Value = spec.Axis;
            so.FindProperty("openAngle").floatValue = spec.Angle;
            so.FindProperty("duration").floatValue = 1.2f;
            so.ApplyModifiedPropertiesWithoutUndo();
            panel.CaptureClosedRotation();
            return panel;
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
        /// Car paint is a clear-coated specular surface, so it is read almost entirely from what it
        /// reflects. The key is held lower than the engine's and the rim raised, because the shape
        /// of a car body is described by the highlight running along it rather than by diffuse
        /// shading across it.
        /// </summary>
        private static void BuildLighting() {
            Light key = EyeAnatomySceneUpgrader.EnsureLight("Key Light", LightType.Directional);
            key.transform.SetPositionAndRotation(new Vector3(0.3f, 0.5f, -0.4f), Quaternion.Euler(48f, 145f, 0f));
            key.color = new Color(1f, 0.98f, 0.94f);
            key.intensity = 1.1f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.7f;
            key.enabled = true;

            Light fill = EyeAnatomySceneUpgrader.EnsureLight("Fill Light", LightType.Directional);
            fill.transform.SetPositionAndRotation(new Vector3(-0.4f, 0.3f, -0.35f), Quaternion.Euler(18f, 232f, 0f));
            fill.color = new Color(0.80f, 0.87f, 1f);
            fill.intensity = 0.62f;
            fill.shadows = LightShadows.None;
            fill.enabled = true;

            Light rim = EyeAnatomySceneUpgrader.EnsureLight("Rim Light", LightType.Directional);
            rim.transform.SetPositionAndRotation(new Vector3(0f, 0.45f, 0.5f), Quaternion.Euler(22f, 350f, 0f));
            rim.color = new Color(0.78f, 0.86f, 1f);
            rim.intensity = 0.72f;
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
            GameObject rig, Camera camera, List<VehiclePanelGroup> groups, KmaxStylus stylus,
            AnatomyParticleDirector particles, GameObject ui) {

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
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "modelRoot", car);
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "xrRig", rig.GetComponentInChildren<XRRig>(true));
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "focusAnchor", focusAnchor);
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "eyeManipulator", manipulator);
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "flyController", fly);
            EyeAnatomySceneUpgrader.SetIfPresent(focusSo, "ghostMaterial",
                AssetDatabase.LoadAssetAtPath<Material>(EyeAnatomySceneUpgrader.GhostMaterialPath));
            focusSo.ApplyModifiedPropertiesWithoutUndo();

            ExhibitPostProcessing postFx = EyeAnatomySceneUpgrader.GetOrAdd<ExhibitPostProcessing>(exhibit);
            SerializedObject postFxSo = new SerializedObject(postFx);
            EyeAnatomySceneUpgrader.SetIfPresent(postFxSo, "cameraRoot", rig.transform);
            postFxSo.ApplyModifiedPropertiesWithoutUndo();

            VehiclePanelController panels = EyeAnatomySceneUpgrader.GetOrAdd<VehiclePanelController>(exhibit);
            panels.SetGroups(groups.ToArray());
            EditorUtility.SetDirty(panels);

            AnatomyStylusInput input = EyeAnatomySceneUpgrader.GetOrAdd<AnatomyStylusInput>(exhibit);
            SerializedObject inputSo = new SerializedObject(input);
            EyeAnatomySceneUpgrader.SetIfPresent(inputSo, "stylus", stylus);
            EyeAnatomySceneUpgrader.SetIfPresent(inputSo, "flyController", fly);
            EyeAnatomySceneUpgrader.SetIfPresent(inputSo, "rigRoot", rig.transform);
            inputSo.ApplyModifiedPropertiesWithoutUndo();
        }
        /// <summary>
        /// The interface objects the controls component has to be handed.
        /// </summary>
        private class VolvoUi {
            public Button[] PanelButtons = new Button[0];
            public Button Ignition;
            public Button Lights;
            public Button Reset;
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
                new LampSpec("Interior", new Color(2.6f, 2.0f, 1.3f), false, new string[] {
                    "CeilingConsole", "InfoTainment Screen", "SpeedoScreen", "Shifterknob Crystal" }),
                new LampSpec("Daytime", new Color(4.5f, 4.8f, 5.4f), false, new string[] {
                    "Glass Runninglight", "Reflector Runninglight" }),
                new LampSpec("Headlights", new Color(5.2f, 5.2f, 5.0f), false, new string[] {
                    "Emitters Headlight", "Reflector Headlight 1", "Reflector Headlight 2",
                    "Reflector Highbeam", "Logo Headlight" }),
                new LampSpec("Fog", new Color(4.0f, 3.6f, 2.8f), false, new string[] {
                    "Foglight Glass", "Foglight Reflector" }),
                new LampSpec("Tail", new Color(5.0f, 0.35f, 0.18f), false, new string[] {
                    "MainReflectors Taillight", "Diffusers Taillight", "Reflector 2 Taillight",
                    "Reflector 3 Taillight", "Reflector Cubes Taillight", "Diffuser TrunkTaillight",
                    "Reflector TrunkTaillight", "Reflector 2 Trunktaillight", "Lightbulb TrunkTaillight" }),
                new LampSpec("Reverse", new Color(4.2f, 4.2f, 4.0f), false, new string[] {
                    "Diffuser ReverseLight", "Reflector ReverseLight" }),
                new LampSpec("Indicators", new Color(5.0f, 1.8f, 0.12f), true, new string[] {
                    "Glass Turnsignal", "Reflector Turnsignal", "MIrrorTurnSignal Glass",
                    "MirrorTurnsignal Reflector", "Diffuser 3 Taillight", "Side Diffuser Taillight" })
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
                    new Vector3(carBounds.center.x, carBounds.center.y + carBounds.extents.y * 0.4f, carBounds.center.z));
                cabin.color = new Color(1f, 0.88f, 0.72f);
                cabin.range = carBounds.size.z * 0.45f;
                // Tiny, because it sits centimetres from every surface it lights at this scale.
                // The emissive meshes carry the look of a lit cabin; this only has to lift the
                // seats and the dash off black.
                cabin.intensity = 0.09f;
                cabin.shadows = LightShadows.None;
                cabin.enabled = false;
                return new Light[] { cabin };
            }

            return new Light[0];
        }

        private static void ConfigureBeam(Light light, Bounds carBounds) {
            light.color = new Color(1f, 0.97f, 0.9f);
            light.range = carBounds.size.z * 2.2f;
            // Aimed forward and kept modest. There is no fog or floor for a beam to land on, so
            // this is about putting a glow in front of the car, not about lighting a road.
            light.intensity = 1.4f;
            light.spotAngle = 52f;
            light.innerSpotAngle = 22f;
            light.shadows = LightShadows.None;
            light.transform.rotation = Quaternion.identity;
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
            SetStringArray(so, "accessoryChannels", new string[] { "Interior" });
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

            TextMeshProUGUI title = ExhibitUiFactory.BuildLabel(rect, "Title", "Volvo S90", 52f,
                new Vector2(0.5f, 1f), new Vector2(0f, -48f), new Vector2(1200f, 72f), TextAlignmentOptions.Center);
            title.fontStyle = FontStyles.Bold;

            result.PanelButtons = new Button[groups.Count];
            for (int i = 0; i < groups.Count; i++) {
                result.PanelButtons[i] = ExhibitUiFactory.BuildButton(rect, "PanelButton" + (i + 1),
                    groups[i].OpenLabel, new Vector2(0f, 1f), new Vector2(24f, -24f - i * 72f), new Vector2(380f, 64f));
                EyeAnatomySceneUpgrader.AddMotion(result.PanelButtons[i].gameObject, false);
            }

            // A group withdrawn from the spec leaves its button behind, because the factory finds
            // a button by name before it makes one.
            const int MaxPanelButtons = 16;
            for (int i = groups.Count; i < MaxPanelButtons; i++) {
                Transform surplus = rect.Find("PanelButton" + (i + 1));
                if (surplus == null) {
                    continue;
                }

                Undo.DestroyObjectImmediate(surplus.gameObject);
            }

            result.Ignition = ExhibitUiFactory.BuildButton(rect, "IgnitionButton", "Start the car",
                new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(380f, 88f));
            result.Lights = ExhibitUiFactory.BuildButton(rect, "LightsButton", "Lights on",
                new Vector2(0f, 0f), new Vector2(24f, 124f), new Vector2(380f, 64f));
            result.Reset = ExhibitUiFactory.BuildButton(rect, "ResetButton", "Reset view",
                new Vector2(1f, 0f), new Vector2(-24f, 24f), new Vector2(380f, 88f));

            EyeAnatomySceneUpgrader.AddMotion(result.Ignition.gameObject, true);
            EyeAnatomySceneUpgrader.AddMotion(result.Lights.gameObject, false);
            EyeAnatomySceneUpgrader.AddMotion(result.Reset.gameObject, false);
            return result;
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
            string[] guids = AssetDatabase.FindAssets("t:Material", new string[] { VolvoRoot + "/mat" });

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
