using UnityEditor;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample.Editor {
    /// <summary>
    /// Draws the interface's icons and saves them as sprite assets.
    ///
    /// Generated rather than brought in, for the same reason the studio cubemap and the showroom
    /// floor are: the build step is the single author of everything this exhibit is made of, and a
    /// downloaded icon set is a third-party licence to track in a repository that currently has
    /// none. Six glyphs of circles, boxes and capsules do not justify one.
    ///
    /// Each glyph is a signed distance field - every primitive returns how far a point is from its
    /// edge, negative inside - and they are combined with min for union and max for intersection.
    /// That is what makes them crisp at any size and antialiased for nothing: the coverage of a
    /// pixel is just the distance rendered through a smoothstep one pixel wide, which is far better
    /// than supersampling and considerably less code.
    ///
    /// Drawn white on transparent. The buttons tint them, so one set serves every state.
    /// </summary>
    public static class ExhibitIconFactory {
        private const string IconFolder = "Assets/Games/kmax-display-example/Materials/Icons";
        private const int Resolution = 128;

        public const string Doors = "Doors";
        public const string Sunroof = "Sunroof";
        public const string Tour = "Tour";
        public const string Lights = "Lights";
        public const string Ignition = "Ignition";
        public const string Reset = "Reset";

        /// <summary>
        /// The sprite for one glyph, drawn on the first run and returned as it stands after that.
        /// </summary>
        public static Sprite GetOrCreate(string glyph) {
            string path = IconFolder + "/Icon" + glyph + ".png";
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) {
                return existing;
            }

            if (!AssetDatabase.IsValidFolder(IconFolder)) {
                AssetDatabase.CreateFolder("Assets/Games/kmax-display-example/Materials", "Icons");
            }

            Texture2D texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);

            // One pixel in glyph space, which is what the edge is softened across.
            float pixel = 2f / Resolution;

            for (int y = 0; y < Resolution; y++) {
                for (int x = 0; x < Resolution; x++) {
                    Vector2 p = new Vector2(
                        (x + 0.5f) / Resolution * 2f - 1f,
                        (y + 0.5f) / Resolution * 2f - 1f);

                    float distance = Evaluate(glyph, p);
                    float coverage = Mathf.Clamp01(0.5f - distance / (pixel * 1.5f));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, coverage));
                }
            }

            texture.Apply();
            System.IO.File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null) {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            Sprite created = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (created == null) {
                Debug.LogWarning($"{nameof(ExhibitIconFactory)} wrote '{path}' but could not load a sprite " +
                    "from it; that button will show its label alone.");
            }

            return created;
        }

        /// <summary>
        /// The signed distance to one glyph's outline: negative inside the ink, positive outside.
        /// </summary>
        private static float Evaluate(string glyph, Vector2 p) {
            switch (glyph) {
                case Doors: return DoorsGlyph(p);
                case Sunroof: return SunroofGlyph(p);
                case Tour: return TourGlyph(p);
                case Lights: return LightsGlyph(p);
                case Ignition: return IgnitionGlyph(p);
                case Reset: return ResetGlyph(p);
                default:
                    Debug.LogWarning($"{nameof(ExhibitIconFactory)} has no glyph called '{glyph}'.");
                    return 1f;
            }
        }

        /// <summary>A door panel: outline, window, handle.</summary>
        private static float DoorsGlyph(Vector2 p) {
            float panel = Outline(RoundedBox(p, new Vector2(0.46f, 0.66f), 0.12f), 0.075f);
            float window = RoundedBox(p - new Vector2(0f, 0.26f), new Vector2(0.25f, 0.20f), 0.06f);
            float handle = Segment(p, new Vector2(-0.20f, -0.16f), new Vector2(0.06f, -0.16f), 0.055f);
            return Union(panel, Union(window, handle));
        }

        /// <summary>A roof seen from above, with the glass panel slid back off the opening.</summary>
        private static float SunroofGlyph(Vector2 p) {
            float roof = Outline(RoundedBox(p, new Vector2(0.72f, 0.48f), 0.14f), 0.075f);
            float opening = RoundedBox(p - new Vector2(-0.20f, 0f), new Vector2(0.24f, 0.22f), 0.06f);
            float panel = Outline(RoundedBox(p - new Vector2(0.30f, 0f), new Vector2(0.20f, 0.22f), 0.06f), 0.05f);
            return Union(roof, Union(opening, panel));
        }

        /// <summary>A play triangle in a ring: start the tour.</summary>
        private static float TourGlyph(Vector2 p) {
            float ring = Ring(p, 0.66f, 0.075f);
            float play = Triangle(p,
                new Vector2(-0.16f, 0.30f), new Vector2(-0.16f, -0.30f), new Vector2(0.32f, 0f));
            return Union(ring, play);
        }

        /// <summary>A headlamp throwing three beams to the right.</summary>
        private static float LightsGlyph(Vector2 p) {
            // A circle cut flat on its right-hand side, which is a headlamp in two strokes.
            float lamp = Intersect(Circle(p - new Vector2(-0.24f, 0f), 0.52f), p.x - 0.20f);
            float rays = Segment(p, new Vector2(0.38f, 0.30f), new Vector2(0.84f, 0.30f), 0.055f);
            rays = Union(rays, Segment(p, new Vector2(0.38f, 0f), new Vector2(0.90f, 0f), 0.055f));
            rays = Union(rays, Segment(p, new Vector2(0.38f, -0.30f), new Vector2(0.84f, -0.30f), 0.055f));
            return Union(lamp, rays);
        }

        /// <summary>The power symbol: a broken ring with a bar through the break.</summary>
        private static float IgnitionGlyph(Vector2 p) {
            float ring = Ring(p, 0.54f, 0.08f);
            float gap = RoundedBox(p - new Vector2(0f, 0.62f), new Vector2(0.20f, 0.34f), 0.02f);
            float bar = Segment(p, new Vector2(0f, 0.08f), new Vector2(0f, 0.76f), 0.08f);
            return Union(Subtract(ring, gap), bar);
        }

        /// <summary>A circular arrow: return the view to where it started.</summary>
        private static float ResetGlyph(Vector2 p) {
            float ring = Ring(p, 0.54f, 0.08f);
            float gap = RoundedBox(p - new Vector2(0.34f, 0.46f), new Vector2(0.34f, 0.30f), 0.02f);
            float head = Triangle(p,
                new Vector2(0.10f, 0.78f), new Vector2(0.10f, 0.22f), new Vector2(0.66f, 0.50f));
            return Union(Subtract(ring, gap), head);
        }

        /// <summary>
        /// Turns a filled shape into a stroke of the given half-thickness around its edge.
        /// </summary>
        private static float Outline(float distance, float halfThickness) {
            return Mathf.Abs(distance) - halfThickness;
        }

        private static float Circle(Vector2 p, float radius) {
            return p.magnitude - radius;
        }

        private static float Ring(Vector2 p, float radius, float halfThickness) {
            return Mathf.Abs(p.magnitude - radius) - halfThickness;
        }

        private static float RoundedBox(Vector2 p, Vector2 half, float corner) {
            Vector2 d = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half + new Vector2(corner, corner);
            Vector2 outside = new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f));
            return outside.magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f) - corner;
        }

        /// <summary>A capsule: the set of points within <paramref name="thickness"/> of the segment.</summary>
        private static float Segment(Vector2 p, Vector2 a, Vector2 b, float thickness) {
            Vector2 pa = p - a;
            Vector2 ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(Vector2.Dot(ba, ba), 1e-6f));
            return (pa - ba * h).magnitude - thickness;
        }

        /// <summary>
        /// A convex triangle, as the intersection of its three edge half-planes.
        ///
        /// Not a true distance field outside the shape - the value grows too slowly near a corner -
        /// but the edge itself is exact, and the edge is all the antialiasing reads.
        /// </summary>
        private static float Triangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c) {
            float ab = HalfPlane(p, a, b);
            float bc = HalfPlane(p, b, c);
            float ca = HalfPlane(p, c, a);
            return Mathf.Max(ab, Mathf.Max(bc, ca));
        }

        private static float HalfPlane(Vector2 p, Vector2 a, Vector2 b) {
            Vector2 edge = b - a;
            Vector2 normal = new Vector2(edge.y, -edge.x).normalized;
            return Vector2.Dot(p - a, normal);
        }

        private static float Union(float a, float b) {
            return Mathf.Min(a, b);
        }

        private static float Intersect(float a, float b) {
            return Mathf.Max(a, b);
        }

        private static float Subtract(float shape, float cutter) {
            return Mathf.Max(shape, -cutter);
        }
    }
}
