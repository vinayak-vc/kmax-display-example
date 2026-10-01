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
        internal const string MoteMaterialPath = MaterialsFolder + "/AnatomyMote.mat";

        public const string BeamMaterialPath = MaterialsFolder + "/StylusBeam.mat";
        public const string TipMaterialPath = MaterialsFolder + "/StylusTip.mat";
        public const string TipMeshPath = ModelFolder + "/StylusTip.asset";
        public const string FocusHighlightMaterialPath = MaterialsFolder + "/FocusHighlight.mat";
        public const string ScaleHandleMaterialPath = MaterialsFolder + "/ScaleHandle.mat";
        public const string EnvironmentSkyboxPath = MaterialsFolder + "/AnatomyEnvironment.mat";
        public const string StudioReflectionPath = MaterialsFolder + "/StudioReflection.cubemap";
        public const string FloorGradientPath = MaterialsFolder + "/ShowroomFloorGradient.png";
        public const string KeyCookiePath = MaterialsFolder + "/StudioWindowCookie.png";
        public const string FloorSurfacePath = MaterialsFolder + "/ShowroomFloorSurface.png";
        public const string FloorMaterialPath = MaterialsFolder + "/ShowroomFloor.mat";
        public const string FloorMeshPath = ModelFolder + "/ShowroomFloor.asset";
        public const string SoothingMusicPath = ModuleRoot + "/Music/SoothingAmbient.wav";

        /// <summary>
        /// A ScriptableObject asset at a fixed path, created empty the first time and returned
        /// as it stands on every run after that.
        ///
        /// The catalogues and pose sets are rewritten from the build step's own tables, so what
        /// matters here is only that the asset exists at a stable path: the scene references it by
        /// GUID, and creating a new one each run would break every reference in the scene.
        /// </summary>
        public static T LoadOrCreate<T>(string path) where T : ScriptableObject {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) {
                return existing;
            }

            T created = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(created, path);
            return created;
        }

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
        /// A studio environment for smooth surfaces to reflect: dark, with three long softboxes
        /// overhead and a low horizon glow.
        ///
        /// Set as <c>RenderSettings.customReflectionTexture</c> rather than as the skybox, so the
        /// background the viewer sees stays the near-black the exhibit is lit against while the
        /// bodywork still has something worth mirroring. The two jobs are genuinely different: a
        /// backdrop wants to disappear, and a reflection environment wants structure.
        ///
        /// Car paint is the case that makes this matter. A clear coat at 0.69 smoothness is read
        /// almost entirely from what it reflects, and a smooth gradient reflects nearly the same
        /// value at every normal - which is why the body panels read as flat grey shapes under the
        /// plain sky. Hard-edged bands give the long highlight that runs down a wing and describes
        /// its curvature.
        /// </summary>
        public static Cubemap GetOrCreateStudioReflection() {
            Cubemap existing = AssetDatabase.LoadAssetAtPath<Cubemap>(StudioReflectionPath);
            if (existing != null) {
                return existing;
            }

            const int Size = 64;
            Cubemap cube = new Cubemap(Size, TextureFormat.RGBAHalf, true);

            for (int face = 0; face < 6; face++) {
                Color[] pixels = new Color[Size * Size];

                for (int y = 0; y < Size; y++) {
                    for (int x = 0; x < Size; x++) {
                        float u = (x + 0.5f) / Size * 2f - 1f;
                        float v = (y + 0.5f) / Size * 2f - 1f;
                        pixels[y * Size + x] = SampleStudio(FaceDirection((CubemapFace)face, u, v));
                    }
                }

                cube.SetPixels(pixels, (CubemapFace)face);
            }

            cube.Apply();
            AssetDatabase.CreateAsset(cube, StudioReflectionPath);
            return cube;
        }

        /// <summary>
        /// The direction a cubemap texel looks along. Unity's faces are +X, -X, +Y, -Y, +Z, -Z in
        /// that order, each with its own handedness - hence the sign flips rather than one formula.
        /// </summary>
        private static Vector3 FaceDirection(CubemapFace face, float u, float v) {
            switch (face) {
                case CubemapFace.PositiveX: return new Vector3(1f, -v, -u).normalized;
                case CubemapFace.NegativeX: return new Vector3(-1f, -v, u).normalized;
                case CubemapFace.PositiveY: return new Vector3(u, 1f, v).normalized;
                case CubemapFace.NegativeY: return new Vector3(u, -1f, -v).normalized;
                case CubemapFace.PositiveZ: return new Vector3(u, -v, 1f).normalized;
                default: return new Vector3(-u, -v, -1f).normalized;
            }
        }

        /// <summary>
        /// <summary>
        /// A smoothstep with edges, returning 0 at <paramref name="edge0"/> and 1 at
        /// <paramref name="edge1"/>, smoothly between, and clamped outside.
        ///
        /// Not <see cref="Mathf.SmoothStep"/>, which shares the name and is a different function:
        /// it interpolates *between* its first two arguments rather than treating them as edges, so
        /// <c>Mathf.SmoothStep(0.55f, 0.95f, x)</c> never returns anything outside 0.55 to 0.95 and
        /// is useless as a mask. The first studio cubemap used it that way and consequently lit its
        /// entire upper hemisphere to at least 0.55 of full softbox brightness - which is why the
        /// floor's grazing reflection came out at sRGB 0.47 and everything read washed out.
        ///
        /// <paramref name="edge1"/> below <paramref name="edge0"/> is legitimate and means a
        /// falling edge - which is how the window panes are drawn, since they are defined by
        /// distance *out* from a pane's centre. Clamping the span to a positive minimum, as this
        /// first did, turns every falling edge into a hard rising one: the gobo came out inverted,
        /// bright on its mullions and dark in its panes, with no soft edge anywhere.
        /// </summary>
        private static float Step(float edge0, float edge1, float x) {
            float span = edge1 - edge0;
            if (Mathf.Abs(span) < 1e-6f) {
                return x < edge0 ? 0f : 1f;
            }

            float t = Mathf.Clamp01((x - edge0) / span);
            return t * t * (3f - 2f * t);
        }

        /// <summary>
        /// The studio, as a function of direction: a bright floor, a dark ceiling, and a run of
        /// windows around the walls between them.
        ///
        /// The arrangement is the point, and it is the one every car photograph uses. A car body is
        /// a vertical curve, so the top of a wing reflects what is overhead and its flank reflects
        /// what is level with it. Put the bright thing overhead and the whole car goes pale and
        /// flat; put it around the walls with dark above and bright below, and the boundary between
        /// the two sweeps along the panel as it curves - which is the long streak that makes paint
        /// read as paint rather than as coloured plastic.
        /// </summary>
        private static Color SampleStudio(Vector3 direction) {
            if (direction.y < 0f) {
                // The floor, and it is bright. The lower half of every panel reflects this, and in
                // a real showroom it is a pale floor under strong light.
                float depth = Step(0f, 0.62f, -direction.y);
                return Color.Lerp(new Color(0.42f, 0.435f, 0.470f), new Color(0.20f, 0.21f, 0.235f), depth);
            }

            // The ceiling, falling to near-black straight up.
            Color colour = Color.Lerp(new Color(0.155f, 0.165f, 0.195f), new Color(0.020f, 0.021f, 0.026f),
                Step(0f, 0.68f, direction.y));

            // Windows: bright horizontal bands low on the walls. Kept below the 40 degree mark so
            // they land on the flanks and the shoulder line rather than on the roof.
            float bands = Mathf.Cos(Mathf.Asin(Mathf.Clamp01(direction.y)) * 22f);
            float window = Step(0.72f, 0.97f, bands) * (1f - Step(0.12f, 0.62f, direction.y));
            colour += new Color(3.6f, 3.65f, 3.8f) * window;
            return colour;
        }

        /// <summary>
        /// The showroom floor's base map: a disc that fades from a lit centre to the colour of the
        /// backdrop at its rim.
        ///
        /// The fade is what makes the floor read as an infinity floor rather than as a plate the
        /// car is standing on. It is done in the albedo and the surface is left **opaque** on
        /// purpose - a transparent floor is unreliable about receiving shadows in URP, and the cast
        /// shadow is the whole reason the floor is there. Reaching the backdrop's own value at the
        /// rim hides the edge just as well, and costs nothing.
        ///
        /// It also fades in the middle, under the car: a mirror-smooth floor directly beneath a
        /// model that is not actually reflected in it reads as wrong, so the centre is dulled to
        /// let the cast shadow do that work instead.
        /// </summary>
        public static Texture2D GetOrCreateFloorGradient() {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(FloorGradientPath);
            if (existing != null) {
                return existing;
            }

            const int Size = 256;
            Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
            // Measured off a rendered frame rather than guessed: the backdrop sits at sRGB
            // 0.169, 0.176, 0.208, and the first attempt at this floor rendered to within 0.004 of
            // it - present, correct, and completely invisible. The rim is set to land on that
            // number so the disc has no edge, and the centre to roughly double it so there is
            // visibly a floor for the car to stand on and cast onto.
            // A pale showroom floor, not a grey one. The reference this is built against is a
            // sunlit tiled floor, and it matters twice over: it is what the lower half of the
            // bodywork reflects, and it is what the cast shadow has to read against.
            // Measured against clipping, not picked. At 0.72 with the key at 1.5 the lit panes on
            // this floor blew out and 14.4% of the frame clipped - far past the 0.00% the eye
            // exhibit was calibrated to. The floor is the largest bright surface in shot, so it is
            // the one that decides whether there is any headroom left for the lamps.
            Color centre = new Color(0.540f, 0.550f, 0.575f);
            Color rim = new Color(0.085f, 0.090f, 0.106f);
            Color grout = new Color(0.320f, 0.330f, 0.352f);

            for (int y = 0; y < Size; y++) {
                for (int x = 0; x < Size; x++) {
                    float u = (x + 0.5f) / Size * 2f - 1f;
                    float v = (y + 0.5f) / Size * 2f - 1f;
                    float radius = Mathf.Sqrt(u * u + v * v);

                    // Seven tiles across the disc, with the joint lines only faintly darker. A
                    // strong grid fights the cast shadow, which is what the floor is really for.
                    float lineU = GroutMask(u * 3.5f);
                    float lineV = GroutMask(v * 3.5f);
                    Color tile = Color.Lerp(centre, grout, Mathf.Max(lineU, lineV));

                    float fade = Step(0.30f, 0.88f, radius);
                    texture.SetPixel(x, y, Color.Lerp(tile, rim, fade));
                }
            }

            texture.Apply();
            System.IO.File.WriteAllBytes(FloorGradientPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(FloorGradientPath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(FloorGradientPath) as TextureImporter;
            if (importer != null) {
                importer.textureType = TextureImporterType.Default;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(FloorGradientPath);
        }

        /// <summary>
        /// A window gobo for the key light: panes of light separated by mullions, thrown across the
        /// floor and over the car.
        ///
        /// This is the single thing that turns a lit object into a lit *room*. An even directional
        /// light says nothing about where it comes from; the same light through a window says there
        /// is a window, a wall and a building, none of which have to be modelled. It is also what
        /// gives the floor something to be interesting about - a plain disc under even light is a
        /// grey disc however well it is shaded.
        ///
        /// The grid is rotated inside the texture rather than the light being turned, because the
        /// key's direction is already doing a job: raking along the car's flank. Turning it to get
        /// a diagonal pattern would cost the modelling.
        ///
        /// The mullions are 0.22 rather than 0, so the shaded floor still reads as floor.
        /// </summary>
        public static Texture2D GetOrCreateKeyCookie() {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(KeyCookiePath);
            if (existing != null) {
                return existing;
            }

            const int Size = 512;
            const float Rotation = 22f * Mathf.Deg2Rad;
            Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
            float cos = Mathf.Cos(Rotation);
            float sin = Mathf.Sin(Rotation);

            for (int y = 0; y < Size; y++) {
                for (int x = 0; x < Size; x++) {
                    float u = (x + 0.5f) / Size;
                    float v = (y + 0.5f) / Size;

                    // Rotated about the centre so the panes fall diagonally across the floor.
                    float cu = u - 0.5f;
                    float cv = v - 0.5f;
                    float ru = cu * cos - cv * sin + 0.5f;
                    float rv = cu * sin + cv * cos + 0.5f;

                    // Three panes one way, two the other, with a soft edge on every bar - a hard
                    // cookie edge reads as a rendering artefact rather than as a shadow.
                    float paneU = PaneMask(ru * 3f, 0.09f);
                    float paneV = PaneMask(rv * 2f, 0.07f);
                    // 0.45, not 0.22. The shaded floor between the panes is still a lit showroom
                    // floor, not a hole - at 0.22 the gobo read as one bright rectangle on black
                    // rather than as light through a window.
                    float light = Mathf.Lerp(0.32f, 1f, paneU * paneV);

                    texture.SetPixel(x, y, new Color(light, light, light, 1f));
                }
            }

            texture.Apply();
            System.IO.File.WriteAllBytes(KeyCookiePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(KeyCookiePath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(KeyCookiePath) as TextureImporter;
            if (importer != null) {
                // Default, explicitly. A 2D project imports a new PNG as a Sprite, and a sprite is
                // packed and clipped rather than tiled - not what a cookie wants.
                importer.textureType = TextureImporterType.Default;
                // Repeat, because a directional cookie tiles across the world and a clamped one
                // would light a single square and leave everything outside it black.
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Bilinear;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(KeyCookiePath);
        }

        /// <summary>
        /// One axis of the window grid: 1 inside a pane, 0 on a mullion, soft across the edge.
        /// </summary>
        private static float PaneMask(float coordinate, float barHalfWidth) {
            float withinPane = Mathf.Abs(coordinate - Mathf.Floor(coordinate) - 0.5f);
            return Step(0.5f - barHalfWidth, 0.5f - barHalfWidth - 0.22f, withinPane);
        }

        /// <summary>
        /// One axis of the floor's tile joints: 1 on a line, 0 across the face of a tile.
        /// </summary>
        private static float GroutMask(float coordinate) {
            float fromJoint = Mathf.Abs(coordinate - Mathf.Floor(coordinate) - 0.5f);
            return 1f - Step(0.44f, 0.485f, fromJoint);
        }

        /// <summary>
        /// The floor's metallic-smoothness map: mirror-smooth under the car, matte by the rim.
        ///
        /// Fading the albedo alone does not hide the edge of the disc, and measuring a rendered
        /// frame is what showed why. At its far rim the floor is seen at a grazing angle, which is
        /// exactly where a smooth surface reflects most - it was mirroring the studio's softboxes
        /// and rendering at sRGB 0.47 against a backdrop of 0.17, a hard step of 0.30 across the
        /// seam, while its albedo there was only 0.225. The reflection was the whole problem.
        ///
        /// So the smoothness falls off with the albedo. Near the car the floor is a showroom floor;
        /// by the rim it is rough, dark and reflects nothing, and the disc ends without an edge.
        /// That is also how a real infinity floor behaves.
        ///
        /// RGB is zero - the floor is a dielectric - and smoothness rides in the alpha, which is
        /// where URP Lit reads it from when <c>_SmoothnessTextureChannel</c> is the metallic alpha.
        /// The texture is data rather than colour, so it must import with sRGB off.
        /// </summary>
        public static Texture2D GetOrCreateFloorSurface() {
            Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(FloorSurfacePath);
            if (existing != null) {
                return existing;
            }

            const int Size = 256;
            Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true);

            for (int y = 0; y < Size; y++) {
                for (int x = 0; x < Size; x++) {
                    float u = (x + 0.5f) / Size * 2f - 1f;
                    float v = (y + 0.5f) / Size * 2f - 1f;
                    float radius = Mathf.Sqrt(u * u + v * v);
                    float fade = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((radius - 0.12f) / 0.50f));
                    texture.SetPixel(x, y, new Color(0f, 0f, 0f, Mathf.Lerp(0.95f, 0.02f, fade)));
                }
            }

            texture.Apply();
            System.IO.File.WriteAllBytes(FloorSurfacePath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(FloorSurfacePath, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(FloorSurfacePath) as TextureImporter;
            if (importer != null) {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.alphaIsTransparency = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(FloorSurfacePath);
        }

        /// <summary>
        /// The floor material: dark, and smooth enough to draw the studio's softboxes out into long
        /// streaks across it.
        ///
        /// Smoothness is the whole effect. At 0 this is a grey disc; at 0.74 it mirrors
        /// <see cref="GetOrCreateStudioReflection"/> and the floor gains the bands of light that
        /// say "showroom" before anything else in the frame does.
        /// </summary>
        public static Material GetOrCreateFloorMaterial() {
            Material existing = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
            if (existing != null) {
                return existing;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) {
                Debug.LogError($"{nameof(EyeAnatomyAssetFactory)} could not find the URP Lit shader; " +
                    "the showroom floor will not be built.");
                return null;
            }

            Material material = new Material(shader);
            material.SetTexture("_BaseMap", GetOrCreateFloorGradient());
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_MetallicGlossMap", GetOrCreateFloorSurface());
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            // Smoothness comes from that map's alpha from here on; these are its ceiling and the
            // channel it is read from - 0 is the metallic map's alpha.
            material.SetFloat("_GlossMapScale", 1f);
            material.SetFloat("_Smoothness", 0.74f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_SpecularHighlights", 1f);
            material.SetFloat("_EnvironmentReflections", 1f);
            AssetDatabase.CreateAsset(material, FloorMaterialPath);
            return material;
        }

        /// <summary>
        /// A flat disc in the XZ plane, radius 1, as a triangle fan of rings.
        ///
        /// Rings rather than one fan from the centre, because the floor is the only surface in the
        /// exhibit that receives a shadow across its whole area, and a fan's long thin triangles
        /// make the shadow's edge stair-step along them.
        /// </summary>
        public static Mesh GetOrCreateFloorMesh() {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(FloorMeshPath);
            if (existing != null) {
                return existing;
            }

            const int Segments = 96;
            const int Rings = 12;

            System.Collections.Generic.List<Vector3> vertices = new System.Collections.Generic.List<Vector3>();
            System.Collections.Generic.List<Vector3> normals = new System.Collections.Generic.List<Vector3>();
            System.Collections.Generic.List<Vector2> uvs = new System.Collections.Generic.List<Vector2>();
            System.Collections.Generic.List<int> triangles = new System.Collections.Generic.List<int>();

            for (int ring = 0; ring <= Rings; ring++) {
                float radius = ring / (float)Rings;

                for (int segment = 0; segment < Segments; segment++) {
                    float angle = segment / (float)Segments * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * radius;
                    float z = Mathf.Sin(angle) * radius;
                    vertices.Add(new Vector3(x, 0f, z));
                    normals.Add(Vector3.up);
                    uvs.Add(new Vector2(x * 0.5f + 0.5f, z * 0.5f + 0.5f));
                }
            }

            for (int ring = 0; ring < Rings; ring++) {
                int inner = ring * Segments;
                int outer = (ring + 1) * Segments;

                // Wound so the face points +Y. The obvious order round the ring gives the opposite,
                // and a disc facing the floor is a disc you cannot see from above it - which is
                // exactly how this first went in: present, correctly placed, and invisible.
                for (int segment = 0; segment < Segments; segment++) {
                    int next = (segment + 1) % Segments;
                    triangles.Add(inner + segment);
                    triangles.Add(outer + next);
                    triangles.Add(outer + segment);
                    triangles.Add(inner + segment);
                    triangles.Add(inner + next);
                    triangles.Add(outer + next);
                }
            }

            Mesh mesh = new Mesh();
            mesh.name = "ShowroomFloor";
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            AssetDatabase.CreateAsset(mesh, FloorMeshPath);
            return mesh;
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
        /// Additive material for the scale box's corner handles. Tinted per-frame through a
        /// property block, so the colour set here only matters before the first frame.
        /// </summary>
        public static Material GetOrCreateScaleHandleMaterial() {
            return GetOrCreateMoteVariant(ScaleHandleMaterialPath, new Color(0.62f, 0.88f, 1f, 1f), false);
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

        /// <summary>
        /// Generates and imports the shared soothing ambient soundtrack used across the launcher
        /// and all three exhibit scenes so the background music is calm and consistent everywhere.
        /// </summary>
        public static AudioClip GetOrCreateSoothingMusic() {
            AudioClip existing = AssetDatabase.LoadAssetAtPath<AudioClip>(SoothingMusicPath);
            if (existing != null) {
                return existing;
            }

            if (!AssetDatabase.IsValidFolder(ModuleRoot + "/Music")) {
                AssetDatabase.CreateFolder(ModuleRoot, "Music");
            }

            const int sampleRate = 24000;
            float[] samples = ProceduralAudio.BuildSoothingSamples(sampleRate, 24f);
            WriteStereoWav(SoothingMusicPath, samples, sampleRate);
            AssetDatabase.ImportAsset(SoothingMusicPath, ImportAssetOptions.ForceUpdate);

            AudioImporter importer = AssetImporter.GetAtPath(SoothingMusicPath) as AudioImporter;
            if (importer != null) {
                AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                settings.loadType = AudioClipLoadType.CompressedInMemory;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                settings.preloadAudioData = true;
                importer.defaultSampleSettings = settings;
                importer.loadInBackground = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<AudioClip>(SoothingMusicPath);
        }

        private static void WriteStereoWav(string path, float[] interleavedStereoSamples, int sampleRate) {
            const short channels = 2;
            const short bitsPerSample = 16;
            int sampleCount = interleavedStereoSamples.Length;
            int dataByteCount = sampleCount * 2;
            int byteRate = sampleRate * channels * (bitsPerSample / 8);
            short blockAlign = (short)(channels * (bitsPerSample / 8));

            using (System.IO.FileStream stream = new System.IO.FileStream(path, System.IO.FileMode.Create, System.IO.FileAccess.Write))
            using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream)) {
                writer.Write(new char[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataByteCount);
                writer.Write(new char[] { 'W', 'A', 'V', 'E' });
                writer.Write(new char[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write(channels);
                writer.Write(sampleRate);
                writer.Write(byteRate);
                writer.Write(blockAlign);
                writer.Write(bitsPerSample);
                writer.Write(new char[] { 'd', 'a', 't', 'a' });
                writer.Write(dataByteCount);

                for (int i = 0; i < sampleCount; i++) {
                    float clamped = Mathf.Clamp(interleavedStereoSamples[i], -1f, 1f);
                    short pcm = (short)Mathf.RoundToInt(clamped * 32767f);
                    writer.Write(pcm);
                }
            }
        }
    }
}
