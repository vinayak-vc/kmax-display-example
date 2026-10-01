using System.Collections.Generic;
using System.IO;
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
    /// Builds <c>Scenes/Launcher.unity</c>: a stereoscopic Kmax launcher scene with three scene
    /// tiles (thumbnail + scene title), a non-interactive 3D background stage that loads and
    /// rotates the selected scene's 3D model at stereo pop-out distance, and a Load button that
    /// appears below the tiles once a tile is selected.
    ///
    /// Finds before it creates, so running the command repeatedly is idempotent.
    /// </summary>
    public static class LauncherSceneBuilder {
        private const string MenuPath = "Kmax/Launcher/Set Up Launcher";
        private const string ModuleRoot = "Assets/Games/kmax-display-example";
        private const string ScenePath = ModuleRoot + "/Scenes/Launcher.unity";
        private const string ThumbnailsFolder = ModuleRoot + "/Materials/Thumbnails";
        private const string XrRigPrefabPath = ModuleRoot + "/Plugins/Kmax/com.kmax.xr.core/Editor Resources/XRRig.prefab";

        private const string EyeModelPath = ModuleRoot + "/Model/EyeAnatomy.glb";
        private const string EyePosesPath = ModuleRoot + "/Data/EyeExplodePoses.asset";
        private const string EnginePrefabPath = ModuleRoot + "/CarEngineAnimated - i4/Prefabs/Enginei4.prefab";
        private const string VolvoModelPath = ModuleRoot + "/Model/VOLVO/Volvo S90.fbx";
        private const string MusicPath = ModuleRoot + "/Music/Dark-Times.mp3";

        private const string LauncherRootName = "ExhibitLauncher";
        private const string StageRootName = "PreviewStage";
        private const string RigName = "XRRig";
        private const string UiName = "UI";
        private const string AmbienceName = "Ambience";
        private const string EventSystemName = "EventSystem";

        private const float StageElevation = 0.022f;
        private const float PopOutDistance = 0.085f;

        private class SceneDefinition {
            public string Key;
            public string SceneName;
            public string SceneAssetPath;
            public string DisplayName;
            public string ModelAssetPath;
            public float TargetSize;
            public Vector3 InitialEulerAngles;
            public Vector3 ModelLocalEulerAngles;

            public SceneDefinition(
                string key,
                string sceneName,
                string sceneAssetPath,
                string displayName,
                string modelAssetPath,
                float targetSize,
                Vector3 initialEulerAngles,
                Vector3 modelLocalEulerAngles) {
                Key = key;
                SceneName = sceneName;
                SceneAssetPath = sceneAssetPath;
                DisplayName = displayName;
                ModelAssetPath = modelAssetPath;
                TargetSize = targetSize;
                InitialEulerAngles = initialEulerAngles;
                ModelLocalEulerAngles = modelLocalEulerAngles;
            }
        }

        private class BuiltTileUi {
            public Button Button;
            public Image SelectionHighlight;
        }

        private class BuiltLauncherUi {
            public BuiltTileUi[] Tiles;
            public Button LoadButton;
            public TextMeshProUGUI LoadButtonLabel;
            public TextMeshProUGUI StatusPrompt;
        }

        private static SceneDefinition[] GetScenes() {
            return new SceneDefinition[] {
                new SceneDefinition(
                    "EyeAnatomy",
                    "EyeAnatomy",
                    ModuleRoot + "/Scenes/EyeAnatomy.unity",
                    "Eye Anatomy",
                    EyeModelPath,
                    0.125f,
                    new Vector3(0f, 25f, 0f),
                    new Vector3(0f, 180f, 0f)),
                new SceneDefinition(
                    "Engine",
                    "VirtualExhibition WR",
                    ModuleRoot + "/Scenes/VirtualExhibition WR.unity",
                    "Inline-Four Engine",
                    EnginePrefabPath,
                    0.135f,
                    new Vector3(0f, 140f, 0f),
                    Vector3.zero),
                new SceneDefinition(
                    "VolvoS90",
                    "VolvoS90",
                    ModuleRoot + "/Scenes/VolvoS90.unity",
                    "Volvo S90",
                    VolvoModelPath,
                    0.185f,
                    new Vector3(0f, 220f, 0f),
                    Vector3.zero)
            };
        }

        [MenuItem(MenuPath)]
        public static void Run() {
            if (EditorApplication.isPlayingOrWillChangePlaymode) {
                Debug.LogError($"{nameof(LauncherSceneBuilder)} must not run in play mode.");
                return;
            }

            if (!EnsureSceneOpen()) {
                return;
            }

            SceneDefinition[] definitions = GetScenes();

            GameObject launcherRoot = FindOrCreateRoot(LauncherRootName);
            GameObject ui = FindOrCreateRoot(UiName);
            GameObject ambience = FindOrCreateRoot(AmbienceName);
            GameObject eventSystem = EnsureEventSystemRoot();
            GameObject rig = EnsureRig();
            if (rig == null) {
                return;
            }

            RetireStockCameras(rig);

            Camera camera = EyeAnatomySceneUpgrader.FindRigCamera(rig);
            if (camera == null) {
                Debug.LogError($"{nameof(LauncherSceneBuilder)} found no camera under '{RigName}'.");
                return;
            }

            EyeAnatomySceneUpgrader.UpgradeRenderPipeline();
            EyeAnatomySceneUpgrader.UpgradeEnvironment();
            EyeAnatomySceneUpgrader.UpgradePostProcessing(rig, launcherRoot);
            BuildLighting();
            BuildReflections();

            EyeAnatomySceneUpgrader.UpgradeEventSystem(eventSystem);
            EyeAnatomySceneUpgrader.UpgradeCamera(camera);
            EyeAnatomySceneUpgrader.RemoveDuplicatePens(rig);
            EyeAnatomySceneUpgrader.BuildStylus(rig, camera);

            AnatomyAudioDirector audio = EyeAnatomySceneUpgrader.BuildAudio();
            ConfigureMusic(audio);
            EyeAnatomySceneUpgrader.UpgradeParticles(ambience);

            Transform stageRoot = EyeAnatomySceneUpgrader.FindOrCreateChild(launcherRoot.transform, StageRootName).transform;
            stageRoot.localPosition = Vector3.zero;
            stageRoot.localRotation = Quaternion.identity;
            stageRoot.localScale = Vector3.one;

            Transform[] previewPivots = new Transform[definitions.Length];
            Sprite[] thumbnails = new Sprite[definitions.Length];

            for (int i = 0; i < definitions.Length; i++) {
                previewPivots[i] = BuildPreviewModel(stageRoot, definitions[i]);
            }

            EyeAnatomySceneUpgrader.UpgradeMaterialsDoubleSided();

            bool rigWasActive = rig.activeSelf;
            bool uiWasActive = ui.activeSelf;
            bool ambienceWasActive = ambience.activeSelf;
            rig.SetActive(false);
            ui.SetActive(false);
            ambience.SetActive(false);

            try {
                for (int i = 0; i < definitions.Length; i++) {
                    thumbnails[i] = RenderOrLoadThumbnail(definitions[i], previewPivots, i);
                }
            } finally {
                rig.SetActive(rigWasActive);
                ui.SetActive(uiWasActive);
                ambience.SetActive(ambienceWasActive);
            }

            for (int i = 0; i < previewPivots.Length; i++) {
                if (previewPivots[i] != null) {
                    previewPivots[i].localPosition = new Vector3(0f, StageElevation, -PopOutDistance);
                    previewPivots[i].localRotation = Quaternion.Euler(definitions[i].InitialEulerAngles);
                    previewPivots[i].gameObject.SetActive(i == 0);
                    EditorUtility.SetDirty(previewPivots[i].gameObject);
                }
            }

            BuiltLauncherUi builtUi = BuildUi(ui, camera, definitions, thumbnails);
            WireController(launcherRoot, definitions, previewPivots, builtUi, audio);
            EnsureBuildSettings(definitions);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"{nameof(LauncherSceneBuilder)} finished: 3 scene tiles, thumbnails, non-interactive " +
                "pop-out 3D preview models, and Load button are wired in Scenes/Launcher.unity.");
        }

        private static bool EnsureSceneOpen() {
            if (EditorSceneManager.GetActiveScene().path == ScenePath) {
                return true;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) {
                return false;
            }

            if (File.Exists(ScenePath)) {
                EditorSceneManager.OpenScene(ScenePath);
                return EditorSceneManager.GetActiveScene().path == ScenePath;
            }

            UnityEngine.SceneManagement.Scene created =
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            return EditorSceneManager.SaveScene(created, ScenePath);
        }

        private static Transform BuildPreviewModel(Transform stageRoot, SceneDefinition definition) {
            string pivotName = "Preview_" + definition.Key;
            Transform pivot = EyeAnatomySceneUpgrader.FindOrCreateChild(stageRoot, pivotName).transform;
            pivot.gameObject.SetActive(true);
            pivot.localPosition = Vector3.zero;
            pivot.localRotation = Quaternion.identity;
            pivot.localScale = Vector3.one;

            string modelChildName = definition.Key + "_Model";
            Transform modelInstance = pivot.Find(modelChildName);
            if (modelInstance == null) {
                GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(definition.ModelAssetPath);
                if (asset == null) {
                    Debug.LogError($"{nameof(LauncherSceneBuilder)} could not load model asset '{definition.ModelAssetPath}'.");
                    return pivot;
                }

                GameObject instantiated = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                if (instantiated == null) {
                    instantiated = Object.Instantiate(asset);
                }

                instantiated.name = modelChildName;
                Undo.RegisterCreatedObjectUndo(instantiated, "Create " + modelChildName);
                if (PrefabUtility.IsPartOfPrefabInstance(instantiated)) {
                    PrefabUtility.UnpackPrefabInstance(instantiated, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }

                modelInstance = instantiated.transform;
                modelInstance.SetParent(pivot, false);
            }

            modelInstance.gameObject.SetActive(true);
            modelInstance.localPosition = Vector3.zero;
            modelInstance.localRotation = Quaternion.Euler(definition.ModelLocalEulerAngles);
            modelInstance.localScale = Vector3.one;

            SanitizePreviewModel(modelInstance, definition.Key);
            NormalizeAndCenter(modelInstance, definition.TargetSize);

            pivot.localPosition = new Vector3(0f, StageElevation, -PopOutDistance);
            pivot.localRotation = Quaternion.Euler(definition.InitialEulerAngles);
            EditorUtility.SetDirty(pivot);
            return pivot;
        }

        /// <summary>
        /// Ensures the background model cannot be interacted with and does not play stray legacy
        /// clips or overwrite its own scale from legacy UI sliders.
        /// </summary>
        private static void SanitizePreviewModel(Transform modelRoot, string key) {
            Animation[] animations = modelRoot.GetComponentsInChildren<Animation>(true);
            for (int i = 0; i < animations.Length; i++) {
                if (animations[i] != null) {
                    animations[i].playAutomatically = false;
                    animations[i].Stop();
                    animations[i].enabled = false;
                }
            }

            if (key == "EyeAnatomy") {
                EyeExplodePoseSet poseSet = AssetDatabase.LoadAssetAtPath<EyeExplodePoseSet>(EyePosesPath);
                if (poseSet != null && poseSet.Poses != null) {
                    EyePartPose[] poses = poseSet.Poses;
                    for (int i = 0; i < poses.Length; i++) {
                        if (poses[i] == null || string.IsNullOrEmpty(poses[i].PartPath)) {
                            continue;
                        }

                        Transform part = modelRoot.Find(poses[i].PartPath);
                        if (part != null) {
                            part.localPosition = poses[i].ClosedPosition;
                        }
                    }
                }
            }

            Collider[] colliders = modelRoot.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++) {
                if (colliders[i] != null) {
                    Object.DestroyImmediate(colliders[i]);
                }
            }

            MonoBehaviour[] behaviours = modelRoot.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++) {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null) {
                    continue;
                }

                if (behaviour.GetType().Name == "Enginei4") {
                    SerializedObject so = new SerializedObject(behaviour);
                    EyeAnatomySceneUpgrader.SetIfPresent(so, "RPMSlider", null);
                    EyeAnatomySceneUpgrader.SetIfPresent(so, "ZoomSlider", null);
                    SerializedProperty rpmProp = so.FindProperty("RPM");
                    if (rpmProp != null) {
                        rpmProp.floatValue = 10f;
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();

                    IExhibitMachinery machinery = behaviour as IExhibitMachinery;
                    if (machinery != null && machinery.VariationCount > 0) {
                        machinery.ApplyVariation(0);
                    }
                }
            }
        }

        private static void NormalizeAndCenter(Transform modelInstance, float targetLongestEdge) {
            Bounds bounds;
            if (!TryMeasure(modelInstance, out bounds)) {
                return;
            }

            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (longest <= Mathf.Epsilon) {
                return;
            }

            float scale = targetLongestEdge / longest;
            modelInstance.localScale = new Vector3(scale, scale, scale);

            Bounds scaled;
            if (TryMeasure(modelInstance, out scaled)) {
                Vector3 worldDelta = scaled.center - modelInstance.parent.position;
                modelInstance.position -= worldDelta;
            }

            EditorUtility.SetDirty(modelInstance);
        }

        private static bool TryMeasure(Transform root, out Bounds bounds) {
            bounds = new Bounds(root.position, Vector3.zero);
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool found = false;

            for (int i = 0; i < renderers.Length; i++) {
                if (renderers[i] == null || !renderers[i].gameObject.activeInHierarchy) {
                    continue;
                }

                if (!found) {
                    bounds = renderers[i].bounds;
                    found = true;
                    continue;
                }

                bounds.Encapsulate(renderers[i].bounds);
            }

            return found;
        }

        private static Sprite RenderOrLoadThumbnail(
            SceneDefinition definition,
            Transform[] allPivots,
            int activeIndex) {
            if (!AssetDatabase.IsValidFolder(ThumbnailsFolder)) {
                AssetDatabase.CreateFolder(ModuleRoot + "/Materials", "Thumbnails");
            }

            string assetPath = ThumbnailsFolder + "/" + definition.Key + "Thumbnail.png";

            for (int i = 0; i < allPivots.Length; i++) {
                if (allPivots[i] != null) {
                    allPivots[i].gameObject.SetActive(i == activeIndex);
                }
            }

            Transform target = allPivots[activeIndex];
            if (target != null) {
                target.localPosition = Vector3.zero;
                target.localRotation = Quaternion.Euler(definition.InitialEulerAngles);

                Bounds bounds;
                if (TryMeasure(target, out bounds)) {
                    RenderThumbnailToDisk(bounds, assetPath);
                }
            }

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer != null) {
                bool changed = false;
                if (importer.textureType != TextureImporterType.Sprite) {
                    importer.textureType = TextureImporterType.Sprite;
                    changed = true;
                }
                if (importer.spriteImportMode != SpriteImportMode.Single) {
                    importer.spriteImportMode = SpriteImportMode.Single;
                    changed = true;
                }
                if (importer.mipmapEnabled) {
                    importer.mipmapEnabled = false;
                    changed = true;
                }
                if (changed) {
                    importer.SaveAndReimport();
                }
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static void RenderThumbnailToDisk(Bounds bounds, string assetPath) {
            const int width = 512;
            const int height = 288;

            GameObject camObject = new GameObject("ThumbnailCaptureCamera");
            camObject.hideFlags = HideFlags.HideAndDontSave;

            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            Camera cam = null;

            try {
                cam = camObject.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.08f, 0.10f, 0.14f, 1f);
                cam.fieldOfView = 24f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 5f;
                cam.targetTexture = rt;

                float radius = bounds.extents.magnitude;
                float halfFovRad = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;
                float distance = Mathf.Max(0.14f, (radius / Mathf.Sin(halfFovRad)) * 0.68f);

                cam.transform.position = bounds.center + new Vector3(0f, radius * 0.26f, -distance);
                cam.transform.LookAt(bounds.center);

                cam.Render();

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = rt;

                Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                RenderTexture.active = previous;

                byte[] png = tex.EncodeToPNG();
                Object.DestroyImmediate(tex);

                File.WriteAllBytes(assetPath, png);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            } finally {
                if (cam != null) {
                    cam.targetTexture = null;
                }
                Object.DestroyImmediate(camObject);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }

        private static BuiltLauncherUi BuildUi(
            GameObject ui,
            Camera camera,
            SceneDefinition[] definitions,
            Sprite[] thumbnails) {
            RectTransform canvasRect = ExhibitUiFactory.BuildWorldCanvas(ui, camera);
            Vector2 topCenter = new Vector2(0.5f, 1f);
            Vector2 bottomCenter = new Vector2(0.5f, 0f);

            TextMeshProUGUI header = EnsureLabel(
                canvasRect,
                "HeaderTitle",
                topCenter,
                topCenter,
                topCenter,
                new Vector2(0f, -20f),
                new Vector2(640f, 28f),
                "SELECT AN EXHIBIT",
                19f,
                FontStyles.Bold,
                new Color(0.94f, 0.97f, 1f, 0.98f),
                TextAlignmentOptions.Center);
            header.characterSpacing = 5f;

            TextMeshProUGUI statusPrompt = EnsureLabel(
                canvasRect,
                "StatusPrompt",
                topCenter,
                topCenter,
                topCenter,
                new Vector2(0f, -48f),
                new Vector2(640f, 20f),
                definitions[0].DisplayName + "  \u2022  3D Stereo Pop-Out Preview",
                13.5f,
                FontStyles.Normal,
                new Color(0.60f, 0.86f, 1.00f, 0.92f),
                TextAlignmentOptions.Center);

            const float tileWidth = 176f;
            const float tileHeight = 114f;
            const float tileSpacing = 18f;
            float totalWidth = definitions.Length * tileWidth + (definitions.Length - 1) * tileSpacing;

            Transform tilesRoot = EyeAnatomySceneUpgrader.FindOrCreateChild(canvasRect, "SceneTiles").transform;
            RectTransform tilesRect = GetOrAdd<RectTransform>(tilesRoot.gameObject);
            tilesRect.anchorMin = bottomCenter;
            tilesRect.anchorMax = bottomCenter;
            tilesRect.pivot = bottomCenter;
            tilesRect.anchoredPosition = new Vector2(0f, 66f);
            tilesRect.sizeDelta = new Vector2(totalWidth, tileHeight);
            tilesRect.localScale = Vector3.one;

            BuiltTileUi[] tiles = new BuiltTileUi[definitions.Length];
            for (int i = 0; i < definitions.Length; i++) {
                float x = (i - (definitions.Length - 1) * 0.5f) * (tileWidth + tileSpacing);
                tiles[i] = BuildTile(
                    tilesRect,
                    definitions[i],
                    thumbnails[i],
                    new Vector2(x, 0f),
                    new Vector2(tileWidth, tileHeight),
                    i == 0);
            }

            Button loadButton = ExhibitUiFactory.BuildButton(
                canvasRect,
                "LoadButton",
                "Load " + definitions[0].DisplayName,
                bottomCenter,
                new Vector2(0f, 20f),
                new Vector2(198f, 36f),
                14f);

            ExhibitUiFactory.ApplyModernButtonStyle(loadButton, true);
            EyeAnatomySceneUpgrader.AddMotion(loadButton.gameObject, true);

            TextMeshProUGUI loadLabel = loadButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (loadLabel != null) {
                loadLabel.fontSize = 14f;
                loadLabel.fontStyle = FontStyles.Bold;
                loadLabel.text = "Load " + definitions[0].DisplayName;
            }

            loadButton.gameObject.SetActive(true);

            BuiltLauncherUi result = new BuiltLauncherUi();
            result.Tiles = tiles;
            result.LoadButton = loadButton;
            result.LoadButtonLabel = loadLabel;
            result.StatusPrompt = statusPrompt;
            return result;
        }

        private static BuiltTileUi BuildTile(
            Transform parent,
            SceneDefinition definition,
            Sprite thumbnailSprite,
            Vector2 anchoredPosition,
            Vector2 size,
            bool selectedByDefault) {
            string tileName = "Tile_" + definition.Key;
            GameObject tileObj = EyeAnatomySceneUpgrader.FindOrCreateChild(parent, tileName);
            Vector2 bottomCenter = new Vector2(0.5f, 0f);

            RectTransform rect = GetOrAdd<RectTransform>(tileObj);
            rect.anchorMin = bottomCenter;
            rect.anchorMax = bottomCenter;
            rect.pivot = bottomCenter;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            rect.localScale = Vector3.one;

            Image background = GetOrAdd<Image>(tileObj);
            Sprite cardSprite = ExhibitUiFactory.GetOrCreateCardSprite();
            if (cardSprite != null) {
                background.sprite = cardSprite;
                background.type = Image.Type.Sliced;
                background.pixelsPerUnitMultiplier = 1.15f;
            }
            background.color = Color.white;
            background.raycastTarget = true;

            Button button = GetOrAdd<Button>(tileObj);
            button.targetGraphic = background;

            ColorBlock colors = button.colors;
            colors.colorMultiplier = 1f;
            colors.normalColor = selectedByDefault
                ? new Color(0.18f, 0.36f, 0.56f, 0.96f)
                : new Color(0.12f, 0.17f, 0.26f, 0.90f);
            colors.highlightedColor = new Color(0.24f, 0.48f, 0.72f, 0.98f);
            colors.pressedColor = new Color(0.12f, 0.62f, 0.52f, 1f);
            colors.selectedColor = colors.normalColor;
            button.colors = colors;
            EyeAnatomySceneUpgrader.ClearPersistentCalls(button);
            EyeAnatomySceneUpgrader.AddMotion(tileObj, false);

            GameObject highlightObj = EyeAnatomySceneUpgrader.FindOrCreateChild(tileObj.transform, "SelectionHighlight");
            RectTransform highlightRect = GetOrAdd<RectTransform>(highlightObj);
            highlightRect.anchorMin = new Vector2(0.08f, 1f);
            highlightRect.anchorMax = new Vector2(0.92f, 1f);
            highlightRect.pivot = new Vector2(0.5f, 1f);
            highlightRect.anchoredPosition = new Vector2(0f, -2f);
            highlightRect.sizeDelta = new Vector2(0f, 3f);
            highlightRect.localScale = Vector3.one;

            Image highlightImage = GetOrAdd<Image>(highlightObj);
            highlightImage.color = new Color(0.22f, 0.92f, 1.00f, 1f);
            highlightImage.raycastTarget = false;
            highlightImage.enabled = selectedByDefault;

            GameObject thumbObj = EyeAnatomySceneUpgrader.FindOrCreateChild(tileObj.transform, "Thumbnail");
            RectTransform thumbRect = GetOrAdd<RectTransform>(thumbObj);
            thumbRect.anchorMin = new Vector2(0.5f, 1f);
            thumbRect.anchorMax = new Vector2(0.5f, 1f);
            thumbRect.pivot = new Vector2(0.5f, 1f);
            thumbRect.anchoredPosition = new Vector2(0f, -7f);
            thumbRect.sizeDelta = new Vector2(size.x - 16f, 76f);
            thumbRect.localScale = Vector3.one;

            Image thumbImage = GetOrAdd<Image>(thumbObj);
            thumbImage.sprite = thumbnailSprite;
            thumbImage.color = Color.white;
            thumbImage.preserveAspect = false;
            thumbImage.raycastTarget = false;

            EnsureLabel(
                tileObj.transform,
                "SceneName",
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, 6f),
                new Vector2(-12f, 22f),
                definition.DisplayName,
                12.5f,
                FontStyles.Bold,
                new Color(0.94f, 0.97f, 1f, 0.98f),
                TextAlignmentOptions.Center);

            BuiltTileUi built = new BuiltTileUi();
            built.Button = button;
            built.SelectionHighlight = highlightImage;
            return built;
        }

        private static TextMeshProUGUI EnsureLabel(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta,
            string text,
            float fontSize,
            FontStyles fontStyle,
            Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center) {
            GameObject go = EyeAnatomySceneUpgrader.FindOrCreateChild(parent, name);
            RectTransform rect = GetOrAdd<RectTransform>(go);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            rect.localScale = Vector3.one;

            TextMeshProUGUI tmp = GetOrAdd<TextMeshProUGUI>(go);
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.fontStyle = fontStyle;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.raycastTarget = false;
            if (TMP_Settings.defaultFontAsset != null) {
                tmp.font = TMP_Settings.defaultFontAsset;
            }

            return tmp;
        }

        private static void WireController(
            GameObject launcherRoot,
            SceneDefinition[] definitions,
            Transform[] previewPivots,
            BuiltLauncherUi ui,
            AnatomyAudioDirector audio) {
            ExhibitLauncherController controller = GetOrAdd<ExhibitLauncherController>(launcherRoot);
            SerializedObject so = new SerializedObject(controller);

            SerializedProperty optionsProp = so.FindProperty("options");
            optionsProp.arraySize = definitions.Length;

            for (int i = 0; i < definitions.Length; i++) {
                SerializedProperty element = optionsProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("sceneName").stringValue = definitions[i].SceneName;
                element.FindPropertyRelative("displayName").stringValue = definitions[i].DisplayName;
                element.FindPropertyRelative("tileButton").objectReferenceValue = ui.Tiles[i].Button;
                element.FindPropertyRelative("selectionHighlight").objectReferenceValue = ui.Tiles[i].SelectionHighlight;
                element.FindPropertyRelative("previewModelRoot").objectReferenceValue = previewPivots[i];
                element.FindPropertyRelative("initialEulerAngles").vector3Value = definitions[i].InitialEulerAngles;
            }

            EyeAnatomySceneUpgrader.SetIfPresent(so, "loadButton", ui.LoadButton);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "loadButtonLabel", ui.LoadButtonLabel);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "statusPrompt", ui.StatusPrompt);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "audioDirector", audio);

            SerializedProperty defIdxProp = so.FindProperty("defaultSelectedIndex");
            if (defIdxProp != null) {
                defIdxProp.intValue = 0;
            }

            SerializedProperty rotProp = so.FindProperty("rotationSpeed");
            if (rotProp != null) {
                rotProp.floatValue = 24f;
            }

            SerializedProperty popProp = so.FindProperty("popOutDistance");
            if (popProp != null) {
                popProp.floatValue = PopOutDistance;
            }

            SerializedProperty elevProp = so.FindProperty("stageElevation");
            if (elevProp != null) {
                elevProp.floatValue = StageElevation;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(controller);
        }

        private static void EnsureBuildSettings(SceneDefinition[] definitions) {
            List<EditorBuildSettingsScene> existing = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            List<EditorBuildSettingsScene> updated = new List<EditorBuildSettingsScene>();

            for (int i = 0; i < existing.Count; i++) {
                if (!existing[i].enabled && existing[i].path != ScenePath) {
                    updated.Add(existing[i]);
                }
            }

            updated.Add(new EditorBuildSettingsScene(ScenePath, true));

            for (int i = 0; i < definitions.Length; i++) {
                updated.Add(new EditorBuildSettingsScene(definitions[i].SceneAssetPath, true));
            }

            for (int i = 0; i < existing.Count; i++) {
                if (!existing[i].enabled) {
                    continue;
                }

                string path = existing[i].path;
                bool alreadyAdded = false;
                for (int j = 0; j < updated.Count; j++) {
                    if (updated[j].path == path) {
                        alreadyAdded = true;
                        break;
                    }
                }

                if (!alreadyAdded) {
                    updated.Add(existing[i]);
                }
            }

            EditorBuildSettings.scenes = updated.ToArray();
        }

        private static void BuildLighting() {
            Light key = EyeAnatomySceneUpgrader.EnsureLight("Key Light", LightType.Directional);
            key.transform.SetPositionAndRotation(new Vector3(0.3f, 0.45f, -0.4f), Quaternion.Euler(38f, 165f, 0f));
            key.color = new Color(1f, 0.97f, 0.93f);
            key.intensity = 1.25f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.7f;
            key.enabled = true;

            Light fill = EyeAnatomySceneUpgrader.EnsureLight("Fill Light", LightType.Directional);
            fill.transform.SetPositionAndRotation(new Vector3(-0.4f, 0.25f, -0.35f), Quaternion.Euler(18f, 250f, 0f));
            fill.color = new Color(0.80f, 0.87f, 1f);
            fill.intensity = 0.58f;
            fill.shadows = LightShadows.None;
            fill.enabled = true;

            Light rim = EyeAnatomySceneUpgrader.EnsureLight("Rim Light", LightType.Directional);
            rim.transform.SetPositionAndRotation(new Vector3(0f, 0.42f, 0.5f), Quaternion.Euler(22f, 25f, 0f));
            rim.color = new Color(0.78f, 0.86f, 1f);
            rim.intensity = 0.90f;
            rim.shadows = LightShadows.None;
            rim.enabled = true;
        }

        private static void BuildReflections() {
            Cubemap studio = EyeAnatomyAssetFactory.GetOrCreateStudioReflection();
            if (studio != null) {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = studio;
                RenderSettings.reflectionIntensity = 1.0f;
            }
        }

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
            SerializedProperty vol = so.FindProperty("musicVolume");
            if (vol != null) {
                vol.floatValue = 0.28f;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject EnsureRig() {
            GameObject existing = FindRoot(RigName);
            if (existing != null) {
                existing.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                existing.transform.localScale = Vector3.one;
                return existing;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(XrRigPrefabPath);
            if (prefab == null) {
                Debug.LogError($"{nameof(LauncherSceneBuilder)} could not load XRRig prefab at '{XrRigPrefabPath}'.");
                return null;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = RigName;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            Undo.RegisterCreatedObjectUndo(instance, "Create " + RigName);
            return instance;
        }

        private static GameObject EnsureEventSystemRoot() {
            GameObject existing = FindRoot(EventSystemName);
            if (existing != null) {
                return existing;
            }

            EventSystem found = Object.FindFirstObjectByType<EventSystem>();
            if (found != null) {
                found.gameObject.name = EventSystemName;
                return found.gameObject;
            }

            GameObject created = new GameObject(EventSystemName);
            Undo.RegisterCreatedObjectUndo(created, "Create " + EventSystemName);
            created.AddComponent<EventSystem>();
            return created;
        }

        private static void RetireStockCameras(GameObject rig) {
            Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++) {
                if (cameras[i] == null || cameras[i].transform.IsChildOf(rig.transform)) {
                    continue;
                }

                Object.DestroyImmediate(cameras[i].gameObject);
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

        private static GameObject FindOrCreateRoot(string name) {
            GameObject existing = FindRoot(name);
            if (existing != null) {
                return existing;
            }

            GameObject created = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(created, "Create " + name);
            return created;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component {
            T existing = go.GetComponent<T>();
            if (existing != null) {
                return existing;
            }

            return Undo.AddComponent<T>(go);
        }
    }
}
