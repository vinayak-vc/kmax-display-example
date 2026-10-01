using System.Collections.Generic;
using UnityEngine;

namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// The tunnel in Probe: a generated centreline running from in front of the glass back into the
    /// volume, the geometry that marks it, and the distance query the game scores against.
    ///
    /// <para><b>The wall is mathematics, not a collider.</b> Contact is the tip's distance from the
    /// centreline exceeding the lumen radius. A collider shell for a thin curved tube is fiddly to
    /// build, unreliable to test from the inside, and would answer a slightly different question
    /// than the one being scored. A distance to a polyline is exact, cheap and says precisely what
    /// the rule says.</para>
    ///
    /// <para><b>Rails and hoops rather than a solid tube.</b> A closed tube hides the tip inside it
    /// and has to be drawn double-sided to be seen into at all. An open cage shows the tip the
    /// whole way along, and the hoops are the best depth cue in the scene - as the tunnel comes
    /// forward they grow and pass around the viewer, which is motion through the screen plane doing
    /// exactly what pop-out needs.</para>
    ///
    /// <para><b>No <see cref="LineRenderer"/> anywhere.</b> It billboards to the camera, and this
    /// display has two - facing one eye points it away from the other and the result will not
    /// fuse. Every part of this is swept geometry with one correct orientation for both eyes.</para>
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class ProbePath : MonoBehaviour {
        /// <summary>
        /// Seed for the tunnel drawn in the editor. Fixed, so the scene view shows the same shape
        /// every time it is opened rather than a different one on each domain reload - a scene that
        /// looks different every time you look at it is very hard to author against.
        /// </summary>
        private const int PreviewSeed = 20260930;

        [Header("Shape")]
        [SerializeField, Range(0.2f, 0.95f), Tooltip("Where the tunnel mouth sits, as a fraction " +
            "of the pop-out budget in front of the glass. The mouth is the invitation, so it comes " +
            "out to meet the viewer.")]
        private float mouthOfPopOut = 0.72f;
        [SerializeField, Range(0.2f, 0.95f), Tooltip("Where the far end sits, as a fraction of the " +
            "depth budget behind the glass.")]
        private float endOfDepth = 0.8f;
        [SerializeField, Range(2, 6), Tooltip("Bends in the path. More is harder.")]
        private int waypoints = 3;
        [SerializeField, Range(16, 240), Tooltip("Samples along the centreline. Enough that the " +
            "polyline the distance test uses is indistinguishable from the curve.")]
        private int samples = 140;

        [Header("Size")]
        [SerializeField, Range(0.02f, 0.3f), Tooltip("Lumen radius at easiest, as a fraction of " +
            "the window height. This is the wall the tip must not touch.")]
        private float lumenOfHeight = 0.085f;
        [SerializeField, Range(0.2f, 1f), Tooltip("Lumen radius at hardest, as a fraction of the " +
            "easiest. Difficulty tightens the tunnel rather than lengthening it.")]
        private float lumenHardScale = 0.45f;
        [SerializeField, Range(0.05f, 0.5f), Tooltip("Lateral wander of the waypoints, as a " +
            "fraction of the usable half-window. Difficulty widens this.")]
        private float wanderOfHalfWindow = 0.22f;

        [Header("Geometry")]
        [SerializeField, Range(3, 8), Tooltip("Longitudinal rails marking the tunnel wall.")]
        private int rails = 5;
        [SerializeField, Range(2, 20), Tooltip("Hoops along the tunnel. The strongest depth cue " +
            "the scene has - they grow and pass around the viewer as the tunnel comes forward.")]
        private int hoops = 8;
        [SerializeField, Range(0.02f, 0.25f), Tooltip("Thickness of a rail or hoop, as a fraction " +
            "of the lumen radius.")]
        private float strutOfLumen = 0.1f;
        [SerializeField, Range(3, 12), Tooltip("Sides on a strut's cross-section.")]
        private int strutSides = 5;

        [Header("Safety")]
        [SerializeField, Range(0f, 0.2f), Tooltip("Metres of projected margin kept between the " +
            "tunnel and the frame edge. The generator clamps every point to respect it.")]
        private float edgeMargin = 0.03f;

        private readonly List<Vector3> _centre = new List<Vector3>();
        private readonly List<Vector3> _normal = new List<Vector3>();
        private readonly List<Vector3> _binormal = new List<Vector3>();
        private readonly List<float> _cumulative = new List<float>();
        private Mesh _mesh;
        private float _lumenRadius;
        private float _length;

        /// <summary>Radius of the lumen in world units. Beyond this is the wall.</summary>
        public float LumenRadius {
            get { return _lumenRadius; }
        }

        /// <summary>Total centreline length in world units.</summary>
        public float Length {
            get { return _length; }
        }

        /// <summary>True once a path has been generated.</summary>
        public bool IsBuilt {
            get { return _centre.Count > 1; }
        }

        /// <summary>The tunnel mouth, where a run starts.</summary>
        public Vector3 Mouth {
            get { return _centre.Count > 0 ? _centre[0] : transform.position; }
        }

        /// <summary>The far end, where a run finishes.</summary>
        public Vector3 End {
            get { return _centre.Count > 0 ? _centre[_centre.Count - 1] : transform.position; }
        }

        /// <summary>
        /// Generates a new tunnel and rebuilds its geometry.
        /// </summary>
        /// <param name="seed">Chosen by the game, so a run is reproducible.</param>
        /// <param name="difficulty">0 easiest, 1 hardest. Tightens the lumen and widens the bends.</param>
        public void Generate(int seed, float difficulty) {
            if (!StereoVolume.IsReady) {
                Debug.LogError($"{nameof(ProbePath)}: no comfort volume, so the tunnel cannot be " +
                    "sized. Is there an XRRig in the scene?", this);
                return;
            }

            float clamped = Mathf.Clamp01(difficulty);
            Vector2 window = StereoVolume.Window;
            _lumenRadius = window.y * lumenOfHeight * Mathf.Lerp(1f, lumenHardScale, clamped);

            Random.State previous = Random.state;
            Random.InitState(seed);
            BuildCentreline(window, clamped);
            Random.state = previous;

            BuildFrames();
            BuildMesh();
        }

        private void BuildCentreline(Vector2 window, float difficulty) {
            float near = -StereoVolume.PopOutLimit * mouthOfPopOut;
            float far = StereoVolume.DepthLimit * endOfDepth;
            float wander = window.x * 0.5f * wanderOfHalfWindow * Mathf.Lerp(0.6f, 1.4f, difficulty);

            // Control points, evenly spaced in depth with lateral wander between them.
            int controlCount = waypoints + 2;
            Vector3[] controls = new Vector3[controlCount];
            for (int i = 0; i < controlCount; i++) {
                float t = i / (float)(controlCount - 1);
                float z = Mathf.Lerp(near, far, t);
                float x = 0f;
                float y = 0f;
                if (i > 0 && i < controlCount - 1) {
                    x = Random.Range(-wander, wander);
                    y = Random.Range(-wander * 0.6f, wander * 0.6f);
                }
                controls[i] = ClampInsideWindow(new Vector3(x, y, z));
            }

            _centre.Clear();
            for (int i = 0; i < samples; i++) {
                float t = i / (float)(samples - 1);
                Vector3 local = CatmullRom(controls, t);
                _centre.Add(StereoVolume.ToWorld(ClampInsideWindow(local)));
            }

            _cumulative.Clear();
            _length = 0f;
            _cumulative.Add(0f);
            for (int i = 1; i < _centre.Count; i++) {
                _length += Vector3.Distance(_centre[i - 1], _centre[i]);
                _cumulative.Add(_length);
            }
        }

        /// <summary>
        /// Pulls a point in until the tunnel around it stays inside the stereo window.
        ///
        /// The magnification is the whole reason this is needed. A point in front of the glass is
        /// projected outward from the eye, so a tunnel that looks comfortably inside the frame when
        /// measured flat can be well outside it by the time it reaches the viewer - and a tunnel
        /// mouth clipped by the bezel while at negative parallax is the one thing that reliably
        /// breaks the stereo.
        /// </summary>
        private Vector3 ClampInsideWindow(Vector3 local) {
            float eye = StereoVolume.EyeDistance;
            float toEye = local.z + eye;
            if (toEye <= Mathf.Epsilon) {
                return local;
            }
            float scale = eye / toEye;
            Vector2 half = StereoVolume.Window * 0.5f;
            // The tunnel has width, and that width is magnified too.
            float outerRadius = _lumenRadius * (1f + strutOfLumen * 2f);
            float limitX = Mathf.Max(0f, (half.x - edgeMargin) / scale - outerRadius);
            float limitY = Mathf.Max(0f, (half.y - edgeMargin) / scale - outerRadius);
            local.x = Mathf.Clamp(local.x, -limitX, limitX);
            local.y = Mathf.Clamp(local.y, -limitY, limitY);
            return local;
        }

        private static Vector3 CatmullRom(Vector3[] controls, float t) {
            int segments = controls.Length - 1;
            float scaled = Mathf.Clamp01(t) * segments;
            int index = Mathf.Min((int)scaled, segments - 1);
            float local = scaled - index;

            Vector3 p0 = controls[Mathf.Max(index - 1, 0)];
            Vector3 p1 = controls[index];
            Vector3 p2 = controls[Mathf.Min(index + 1, controls.Length - 1)];
            Vector3 p3 = controls[Mathf.Min(index + 2, controls.Length - 1)];

            float t2 = local * local;
            float t3 = t2 * local;
            return 0.5f * ((2f * p1)
                + (-p0 + p2) * local
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        /// <summary>
        /// Builds a frame at every sample by parallel transport. Recomputing an arbitrary
        /// perpendicular at each sample instead makes the rails spiral around the tunnel wherever
        /// the curve turns, which reads as the tunnel twisting.
        /// </summary>
        private void BuildFrames() {
            _normal.Clear();
            _binormal.Clear();
            if (_centre.Count < 2) {
                return;
            }

            Vector3 tangent = (_centre[1] - _centre[0]).normalized;
            Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.9f
                ? Vector3.right
                : Vector3.up;
            Vector3 normal = Vector3.Normalize(Vector3.Cross(reference, tangent));

            for (int i = 0; i < _centre.Count; i++) {
                Vector3 nextTangent = i == _centre.Count - 1
                    ? (_centre[i] - _centre[i - 1]).normalized
                    : (_centre[i + 1] - _centre[i]).normalized;
                Quaternion rotation = Quaternion.FromToRotation(tangent, nextTangent);
                normal = Vector3.Normalize(rotation * normal);
                tangent = nextTangent;
                _normal.Add(normal);
                _binormal.Add(Vector3.Normalize(Vector3.Cross(tangent, normal)));
            }
        }

        private void BuildMesh() {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            float strut = _lumenRadius * strutOfLumen;

            for (int r = 0; r < rails; r++) {
                float angle = r / (float)rails * Mathf.PI * 2f;
                Vector3[] rail = new Vector3[_centre.Count];
                for (int i = 0; i < _centre.Count; i++) {
                    rail[i] = _centre[i]
                        + (_normal[i] * Mathf.Cos(angle) + _binormal[i] * Mathf.Sin(angle))
                        * _lumenRadius;
                }
                Sweep(rail, false, strut, vertices, triangles);
            }

            for (int h = 0; h < hoops; h++) {
                int index = Mathf.RoundToInt(h / (float)Mathf.Max(1, hoops - 1) * (_centre.Count - 1));
                index = Mathf.Clamp(index, 0, _centre.Count - 1);
                const int HoopSegments = 20;
                Vector3[] hoop = new Vector3[HoopSegments];
                for (int i = 0; i < HoopSegments; i++) {
                    float angle = i / (float)HoopSegments * Mathf.PI * 2f;
                    hoop[i] = _centre[index]
                        + (_normal[index] * Mathf.Cos(angle) + _binormal[index] * Mathf.Sin(angle))
                        * _lumenRadius;
                }
                Sweep(hoop, true, strut * 1.15f, vertices, triangles);
            }

            if (_mesh == null) {
                _mesh = new Mesh();
                _mesh.name = "ProbeTunnel";
                _mesh.MarkDynamic();
            }
            _mesh.Clear();
            _mesh.indexFormat = vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.SetVertices(vertices);
            _mesh.SetTriangles(triangles, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
        }

        /// <summary>
        /// Sweeps a small polygonal cross-section along a centreline. Used for both the rails and
        /// the hoops - a hoop is just a closed sweep around a circle.
        /// </summary>
        private void Sweep(Vector3[] line, bool closed, float radius, List<Vector3> vertices,
            List<int> triangles) {
            if (line.Length < 2) {
                return;
            }
            int baseIndex = vertices.Count;
            int count = line.Length;

            Vector3 tangent = (line[1] - line[0]).normalized;
            Vector3 reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.9f
                ? Vector3.right
                : Vector3.up;
            Vector3 normal = Vector3.Normalize(Vector3.Cross(reference, tangent));

            for (int i = 0; i < count; i++) {
                Vector3 next;
                if (i == count - 1) {
                    next = closed ? (line[0] - line[i]).normalized : (line[i] - line[i - 1]).normalized;
                } else {
                    next = (line[i + 1] - line[i]).normalized;
                }
                normal = Vector3.Normalize(Quaternion.FromToRotation(tangent, next) * normal);
                tangent = next;
                Vector3 binormal = Vector3.Normalize(Vector3.Cross(tangent, normal));
                for (int s = 0; s < strutSides; s++) {
                    float angle = s / (float)strutSides * Mathf.PI * 2f;
                    vertices.Add(line[i]
                        + (normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle)) * radius);
                }
            }

            int rings = closed ? count : count - 1;
            for (int i = 0; i < rings; i++) {
                int a = baseIndex + i * strutSides;
                int b = baseIndex + ((i + 1) % count) * strutSides;
                for (int s = 0; s < strutSides; s++) {
                    int s2 = (s + 1) % strutSides;
                    triangles.Add(a + s);
                    triangles.Add(b + s);
                    triangles.Add(a + s2);
                    triangles.Add(a + s2);
                    triangles.Add(b + s);
                    triangles.Add(b + s2);
                }
            }
        }

        /// <summary>
        /// Distance from a world point to the centreline, and how far along the tunnel the nearest
        /// point is. Returns false before a path has been generated.
        /// </summary>
        public bool TryGetNearest(Vector3 worldPoint, out float distance, out float progress) {
            distance = 0f;
            progress = 0f;
            if (_centre.Count < 2) {
                return false;
            }
            float best = float.MaxValue;
            int bestSegment = 0;
            float bestT = 0f;
            for (int i = 0; i < _centre.Count - 1; i++) {
                Vector3 a = _centre[i];
                Vector3 segment = _centre[i + 1] - a;
                float lengthSq = segment.sqrMagnitude;
                float t = lengthSq <= Mathf.Epsilon
                    ? 0f
                    : Mathf.Clamp01(Vector3.Dot(worldPoint - a, segment) / lengthSq);
                float d = Vector3.SqrMagnitude(worldPoint - (a + segment * t));
                if (d < best) {
                    best = d;
                    bestSegment = i;
                    bestT = t;
                }
            }
            distance = Mathf.Sqrt(best);
            if (_length > 0f) {
                float along = Mathf.Lerp(_cumulative[bestSegment], _cumulative[bestSegment + 1], bestT);
                progress = Mathf.Clamp01(along / _length);
            }
            return true;
        }

        /// <summary>A point on the centreline, by fraction of length.</summary>
        public Vector3 SampleAt(float progress) {
            if (_centre.Count == 0) {
                return transform.position;
            }
            if (_centre.Count == 1) {
                return _centre[0];
            }
            float target = Mathf.Clamp01(progress) * _length;
            for (int i = 1; i < _cumulative.Count; i++) {
                if (_cumulative[i] < target) {
                    continue;
                }
                float span = _cumulative[i] - _cumulative[i - 1];
                float t = span <= Mathf.Epsilon ? 0f : (target - _cumulative[i - 1]) / span;
                return Vector3.Lerp(_centre[i - 1], _centre[i], t);
            }
            return _centre[_centre.Count - 1];
        }

        /// <summary>
        /// Worst window overshoot anywhere on the tunnel, in world units. Zero means the whole path
        /// is safely inside the frame. Used by the build step to prove the clamp did its job rather
        /// than assume it.
        /// </summary>
        public float WorstWindowOvershoot() {
            float worst = 0f;
            float outer = _lumenRadius * (1f + strutOfLumen * 2f);
            for (int i = 0; i < _centre.Count; i++) {
                Bounds bounds = new Bounds(_centre[i], Vector3.one * outer * 2f);
                float overshoot;
                if (StereoVolume.ViolatesWindow(bounds, out overshoot) && overshoot > worst) {
                    worst = overshoot;
                }
            }
            return worst;
        }

        private void OnEnable() {
            // Draws a tunnel in the editor too, so the scene is not empty until someone presses
            // play. The game regenerates it with a real seed at the start of every run.
            if (!IsBuilt && StereoVolume.IsReady) {
                Generate(PreviewSeed, 0f);
            }
        }

        private void OnDestroy() {
            if (_mesh == null) {
                return;
            }
            if (Application.isPlaying) {
                Destroy(_mesh);
            } else {
                DestroyImmediate(_mesh);
            }
        }
    }
}
