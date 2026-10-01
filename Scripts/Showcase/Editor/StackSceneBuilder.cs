using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ViitorCloud.KmaxShowcase.Editor {
    /// <summary>
    /// Authors the Stack scene, start to finish, from an empty project state.
    ///
    /// <para><b>The layout is the design.</b> The bench runs from the screen plane back into the
    /// depth budget, the blocks start at the far end of it, and the stacking platform sits at the
    /// front - at <b>zero parallax</b>. So the natural motion of the task is to draw a block
    /// forward through the volume and set it down on the glass, which puts the pop-out on the
    /// object in the viewer's hand and leaves the target on the most stable, most precisely
    /// resolved plane the display has.</para>
    ///
    /// <para>Nothing is placed in front of the screen plane. A bench extending to negative
    /// parallax would be clipped by the frame at its corners, which is the one thing that reliably
    /// breaks the stereo window. The only object that ever goes forward of the glass is the block
    /// being held, which is centre-frame by construction because that is where the viewer's hand
    /// is.</para>
    /// </summary>
    public static class StackSceneBuilder {
        private const string ScenePath =
            ShowcaseBuildUtility.ModuleRoot + "/Scenes/Showcase/Stack.unity";
        private const string MaterialsFolder =
            ShowcaseBuildUtility.ModuleRoot + "/Materials/Showcase";
        // ".physicMaterial", not ".physicsMaterial". Unity 6 renamed the class from PhysicMaterial
        // to PhysicsMaterial but left the asset extension on the old spelling. Getting it wrong
        // writes a file that looks fine on disk, keeps working through the session because the
        // live object is still referenced, and comes back as a DefaultAsset the next time anything
        // loads it by path - so the friction silently reverts on the next open.
        private const string PhysicsMaterialPath = MaterialsFolder + "/StackBlockPhysics.physicMaterial";

        // The layout is expressed as fractions of the live comfort volume, never in metres.
        //
        // The rig's panel size is not a constant of this project: the SDK prefab defaults to
        // 15.6 inches (a 345 x 194 mm window) and the eye exhibit overrides it to 27 (598 x 336).
        // A bench authored in metres against one of those overhangs the frame on the other, which
        // is a window violation - and it looks perfectly correct while authoring, because flat
        // there is nothing to see. Deriving every dimension from StereoVolume makes the scene
        // right on any panel and at any view scale.
        private const float BenchWidthOfWindow = 0.66f;
        private const float BenchTopOfHalfHeight = -0.72f;
        private const float BenchFarOfDepth = 0.85f;
        private const float BenchThicknessOfHeight = 0.06f;
        private const float PlatformZOfDepth = 0.12f;
        private const float PlatformOfBench = 0.36f;
        private const float BlockSizeOfHeight = 0.12f;
        private const int BlockCount = 10;
        private const int ScatterSeed = 20260930;

        /// <summary>
        /// The scene's dimensions, resolved from the comfort volume at build time.
        /// </summary>
        private struct Layout {
            public float BenchTopY;
            public float BenchFarZ;
            public float BenchWidth;
            public float BenchThickness;
            public float PlatformZ;
            public float PlatformSize;
            public float PlatformThickness;
            public float BlockSize;
        }

        private static bool TryResolveLayout(out Layout layout) {
            layout = new Layout();
            if (!StereoVolume.IsReady || StereoVolume.Window.x <= 0f) {
                Debug.LogError("Stack: the comfort volume is not readable, so the layout cannot be " +
                    "sized. Is there an XRRig in the scene?");
                return false;
            }
            Vector2 window = StereoVolume.Window;
            float depth = StereoVolume.DepthLimit;
            layout.BenchWidth = window.x * BenchWidthOfWindow;
            layout.BenchTopY = window.y * 0.5f * BenchTopOfHalfHeight;
            layout.BenchFarZ = depth * BenchFarOfDepth;
            layout.BenchThickness = window.y * BenchThicknessOfHeight;
            layout.PlatformZ = depth * PlatformZOfDepth;
            layout.PlatformSize = layout.BenchWidth * PlatformOfBench;
            layout.PlatformThickness = layout.BenchThickness * 0.35f;
            layout.BlockSize = window.y * BlockSizeOfHeight;
            return true;
        }

        [MenuItem("Kmax/Showcase/Set Up Stack")]
        public static void SetUp() {
            Scene scene = ShowcaseBuildUtility.EnsureScene(ScenePath);

            GameObject rig = ShowcaseBuildUtility.EnsureRig();
            ShowcaseBuildUtility.ConfigureCamera(rig, new Color(0.03f, 0.035f, 0.045f, 1f));
            ShowcaseBuildUtility.EnsureEventSystem();
            StylusTip tip = ShowcaseBuildUtility.EnsureTip(rig, 0.006f, true);
            StylusHaptics haptics = tip.GetComponent<StylusHaptics>();
            StylusGrab grab = tip.GetComponent<StylusGrab>();
            TuneGrab(grab);

            ConfigureEnvironment();
            EnsureLights();

            Material benchMaterial = ShowcaseBuildUtility.EnsureLitMaterial(
                MaterialsFolder + "/StackBench.mat", new Color(0.16f, 0.17f, 0.20f), 0.25f, 0f);
            Material platformMaterial = ShowcaseBuildUtility.EnsureLitMaterial(
                MaterialsFolder + "/StackPlatform.mat", new Color(0.42f, 0.44f, 0.48f), 0.35f, 0f);
            Material[] blockMaterials = new Material[] {
                ShowcaseBuildUtility.EnsureLitMaterial(MaterialsFolder + "/StackBlockA.mat",
                    new Color(0.78f, 0.52f, 0.28f), 0.22f, 0f),
                ShowcaseBuildUtility.EnsureLitMaterial(MaterialsFolder + "/StackBlockB.mat",
                    new Color(0.60f, 0.62f, 0.68f), 0.30f, 0f),
                ShowcaseBuildUtility.EnsureLitMaterial(MaterialsFolder + "/StackBlockC.mat",
                    new Color(0.40f, 0.55f, 0.58f), 0.26f, 0f)
            };
            PhysicsMaterial blockPhysics = EnsurePhysicsMaterial();

            Layout layout;
            if (!TryResolveLayout(out layout)) {
                return;
            }

            GameObject root = ShowcaseBuildUtility.FindOrCreateRoot("Stack");
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            EnsureBench(root.transform, benchMaterial, blockPhysics, layout);
            Transform platform = EnsurePlatform(root.transform, platformMaterial, blockPhysics, layout);
            Transform blocks = EnsureBlocks(root.transform, blockMaterials, blockPhysics, layout);

            ShowcaseScene showcase = EnsureShowcaseScene(tip);
            EnsureGame(root.transform, platform, blocks, grab, haptics, showcase);
            ShowcaseBuildUtility.EnsureDiagnostics(tip);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            Debug.Log($"Stack set up and saved to {ScenePath}. Without a pen the tip follows the " +
                "mouse, the scroll wheel moves it in depth and the left button grabs. F9 draws " +
                "the comfort volume, F10 audits it.");
        }

        /// <summary>
        /// The grab's feel. <see cref="StylusGrab.followTime"/> is the number this whole scene
        /// lives or dies by and it cannot be set from here with any confidence - it needs a hand
        /// on real hardware. The value is a starting point, not a result.
        /// </summary>
        private static void TuneGrab(StylusGrab grab) {
            if (grab == null) {
                return;
            }
            SerializedObject grabObject = new SerializedObject(grab);
            ShowcaseBuildUtility.SetFloat(grabObject, "followTime", 0.04f);
            ShowcaseBuildUtility.SetFloat(grabObject, "maxSpeed", 2.5f);
            ShowcaseBuildUtility.SetBool(grabObject, "matchRotation", true);
            grabObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureEnvironment() {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            // Not black. Unlike Bloom this scene is lit geometry, and a shadow that falls to pure
            // black loses the contact cue that tells the viewer where a block is above the bench -
            // which is one of the strongest depth cues the scene has.
            RenderSettings.ambientLight = new Color(0.10f, 0.11f, 0.14f);
            RenderSettings.fog = false;
        }

        private static void EnsureLights() {
            GameObject root = ShowcaseBuildUtility.FindOrCreateRoot("Lighting");

            GameObject keyHost = ShowcaseBuildUtility.FindOrCreateChild(root.transform, "Key");
            Light key = keyHost.GetComponent<Light>();
            if (key == null) {
                key = keyHost.AddComponent<Light>();
            }
            key.type = LightType.Directional;
            key.color = new Color(1f, 0.96f, 0.90f);
            key.intensity = 1.15f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.75f;
            // Steep enough that a held block casts onto the bench beneath it. The cast shadow is
            // what tells the viewer how high above the tower they are when disparity alone is
            // ambiguous, so it is load-bearing rather than decorative.
            keyHost.transform.rotation = Quaternion.Euler(58f, -35f, 0f);

            GameObject fillHost = ShowcaseBuildUtility.FindOrCreateChild(root.transform, "Fill");
            Light fill = fillHost.GetComponent<Light>();
            if (fill == null) {
                fill = fillHost.AddComponent<Light>();
            }
            fill.type = LightType.Directional;
            fill.color = new Color(0.72f, 0.80f, 1f);
            fill.intensity = 0.35f;
            fill.shadows = LightShadows.None;
            fillHost.transform.rotation = Quaternion.Euler(18f, 145f, 0f);
        }

        private static Transform EnsureBench(Transform parent, Material material,
            PhysicsMaterial physics, Layout layout) {
            GameObject host = ShowcaseBuildUtility.FindOrCreateChild(parent, "Bench");
            ShapeBox(host, material, physics,
                new Vector3(0f, layout.BenchTopY - layout.BenchThickness * 0.5f,
                    layout.BenchFarZ * 0.5f),
                new Vector3(layout.BenchWidth, layout.BenchThickness, layout.BenchFarZ));
            return host.transform;
        }

        private static Transform EnsurePlatform(Transform parent, Material material,
            PhysicsMaterial physics, Layout layout) {
            GameObject host = ShowcaseBuildUtility.FindOrCreateChild(parent, "Platform");
            ShapeBox(host, material, physics,
                new Vector3(0f, layout.BenchTopY + layout.PlatformThickness * 0.5f, layout.PlatformZ),
                new Vector3(layout.PlatformSize, layout.PlatformThickness, layout.PlatformSize));
            return host.transform;
        }

        private static Transform EnsureBlocks(Transform parent, Material[] materials,
            PhysicsMaterial physics, Layout layout) {
            GameObject root = ShowcaseBuildUtility.FindOrCreateChild(parent, "Blocks");

            float unit = layout.BlockSize;
            Vector3[] sizes = new Vector3[] {
                new Vector3(unit, unit, unit),
                new Vector3(unit * 1.5f, unit * 0.75f, unit * 0.75f),
                new Vector3(unit * 1.25f, unit * 0.5f, unit * 1.25f)
            };

            // Seeded, so the scatter is identical every time the build step runs. An arrangement
            // that moved on each re-run would make the scene impossible to compare against itself.
            Random.State previous = Random.state;
            Random.InitState(ScatterSeed);
            for (int i = 0; i < BlockCount; i++) {
                GameObject host = ShowcaseBuildUtility.FindOrCreateChild(
                    root.transform, "Block" + i.ToString("D2"));
                Vector3 size = sizes[i % sizes.Length];
                Material material = materials[i % materials.Length];

                // Dropped from just above the bench and left to settle. StackGame records where
                // they come to rest as the arrangement a reset returns to, so the opening layout
                // is physically plausible rather than authored block by block.
                // Behind the platform, so the natural motion of the task is to draw a block
                // forward through the volume towards the glass.
                Vector3 position = new Vector3(
                    Random.Range(-layout.BenchWidth * 0.40f, layout.BenchWidth * 0.40f),
                    layout.BenchTopY + unit * 0.6f + i * unit * 0.02f,
                    Random.Range(layout.PlatformZ + layout.PlatformSize * 0.8f,
                        layout.BenchFarZ - unit));
                Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                ShapeBox(host, material, physics, position, size);
                host.transform.rotation = rotation;

                Rigidbody body = host.GetComponent<Rigidbody>();
                if (body == null) {
                    body = host.AddComponent<Rigidbody>();
                }
                body.mass = size.x * size.y * size.z * 500f;
                body.linearDamping = 0.05f;
                body.angularDamping = 0.15f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                // A held block is driven by velocity and can move fast. Discrete detection lets it
                // pass through the tower it is meant to be landing on.
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.solverIterations = 12;
                body.solverVelocityIterations = 4;

                if (host.GetComponent<Grabbable>() == null) {
                    host.AddComponent<Grabbable>();
                }
                if (host.GetComponent<StackBlock>() == null) {
                    host.AddComponent<StackBlock>();
                }
            }
            Random.state = previous;
            return root.transform;
        }

        /// <summary>
        /// Gives an object a box mesh, a collider and a material, and places it. Re-applies all of
        /// it on a re-run rather than assuming an existing object is already correct.
        /// </summary>
        private static void ShapeBox(GameObject host, Material material, PhysicsMaterial physics,
            Vector3 position, Vector3 size) {
            MeshFilter filter = host.GetComponent<MeshFilter>();
            if (filter == null) {
                filter = host.AddComponent<MeshFilter>();
            }
            filter.sharedMesh = UnitCubeMesh();

            MeshRenderer renderer = host.GetComponent<MeshRenderer>();
            if (renderer == null) {
                renderer = host.AddComponent<MeshRenderer>();
            }
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

            BoxCollider collider = host.GetComponent<BoxCollider>();
            if (collider == null) {
                collider = host.AddComponent<BoxCollider>();
            }
            collider.size = Vector3.one;
            collider.sharedMaterial = physics;

            host.transform.SetPositionAndRotation(position, Quaternion.identity);
            host.transform.localScale = size;
        }

        private static Mesh _unitCube;

        private static Mesh UnitCubeMesh() {
            if (_unitCube != null) {
                return _unitCube;
            }
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _unitCube = primitive.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(primitive);
            return _unitCube;
        }

        private static PhysicsMaterial EnsurePhysicsMaterial() {
            ShowcaseBuildUtility.EnsureFolder(MaterialsFolder);
            PhysicsMaterial material =
                AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PhysicsMaterialPath);
            if (material == null) {
                material = new PhysicsMaterial("StackBlockPhysics");
                AssetDatabase.CreateAsset(material, PhysicsMaterialPath);
            }
            // High friction and no bounce. Blocks that slide or bounce make a tower impossible to
            // build, and the scene's whole point is that the difficulty should come from judging
            // depth rather than from fighting the physics.
            material.dynamicFriction = 0.6f;
            material.staticFriction = 0.7f;
            material.bounciness = 0f;
            material.frictionCombine = PhysicsMaterialCombine.Average;
            material.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static ShowcaseScene EnsureShowcaseScene(StylusTip tip) {
            GameObject host = ShowcaseBuildUtility.FindOrCreateRoot("Showcase");
            ShowcaseScene scene = host.GetComponent<ShowcaseScene>();
            if (scene == null) {
                scene = host.AddComponent<ShowcaseScene>();
            }
            SerializedObject sceneObject = new SerializedObject(scene);
            ShowcaseBuildUtility.SetReference(sceneObject, "tip", tip);
            // Longer than Bloom's. Someone deciding where to put a block is thinking, not gone.
            ShowcaseBuildUtility.SetFloat(sceneObject, "idleDelay", 60f);
            ShowcaseBuildUtility.SetFloat(sceneObject, "resolvedHoldSeconds", 6f);
            sceneObject.ApplyModifiedPropertiesWithoutUndo();
            return scene;
        }

        private static void EnsureGame(Transform parent, Transform platform, Transform blocks,
            StylusGrab grab, StylusHaptics haptics, ShowcaseScene showcase) {
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

            StackGame game = host.GetComponent<StackGame>();
            if (game == null) {
                game = host.AddComponent<StackGame>();
            }
            SerializedObject gameObject = new SerializedObject(game);
            ShowcaseBuildUtility.SetReference(gameObject, "platform", platform);
            ShowcaseBuildUtility.SetReference(gameObject, "blockRoot", blocks);
            ShowcaseBuildUtility.SetReference(gameObject, "grab", grab);
            ShowcaseBuildUtility.SetReference(gameObject, "haptics", haptics);
            ShowcaseBuildUtility.SetReference(gameObject, "audioCues", cues);
            ShowcaseBuildUtility.SetReference(gameObject, "scene", showcase);
            // Earth gravity on a 40 mm block drops it its own height in 90 ms. These blocks stand
            // in for objects about five times their size, so dividing g by five restores the
            // timing of the thing being portrayed and makes the task placeable.
            ShowcaseBuildUtility.SetFloat(gameObject, "gravity", 2f);
            gameObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
