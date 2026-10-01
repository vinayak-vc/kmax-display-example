using UnityEngine;

namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// Bloom: motes drift out of the volume, through the screen plane, into the viewer's space,
    /// and burst on the pen tip.
    ///
    /// The scene exists to prove one thing - that there is room in front of the glass - and the
    /// thing that proves it is <b>motion through the screen plane</b>, not the motes' final
    /// position. A mote parked at -0.08 m is a static disparity the eye accepts and stops
    /// noticing; a mote crossing from behind the glass to in front of it is unmistakable. So the
    /// field is a slow continuous flow rather than a cloud, and every mote makes the whole trip.
    ///
    /// <para><b>The known weakness is handled rather than hidden.</b> Loose particles carry pop-out
    /// brilliantly and depth badly, because a point has no size to read a distance from. Three
    /// cheap corrections: each mote trails a ribbon so it has extent; motes grow slightly as they
    /// approach, exaggerating the perspective gradient rather than fighting it; and
    /// <see cref="BloomScreenGrid"/> marks the glass so the crossing is a visible event.</para>
    ///
    /// <para><b>Window safety is a fade, not a check.</b> A mote drifting straight at the viewer
    /// loses margin on its own, because projection from the eye magnifies anything in front of the
    /// glass - it does not have to move sideways to reach the frame edge. Rather than let one be
    /// clipped and break the stereo, every mote dims as
    /// <see cref="StereoVolume.ProjectedMargin"/> runs out and is retired before it can.</para>
    /// </summary>
    public class BloomMoteField : MonoBehaviour {
        [Header("References")]
        [SerializeField, Tooltip("The pen tip motes burst on. Found in the scene on first use.")]
        private StylusTip tip;
        [SerializeField, Tooltip("Pulsed when a mote bursts. Optional.")]
        private StylusHaptics haptics;
        [SerializeField, Tooltip("Chimes when a mote bursts. Optional.")]
        private ShowcaseAudio audioCues;
        [SerializeField, Tooltip("Told when the first mote is burst. Optional.")]
        private ShowcaseScene scene;
        [SerializeField, Tooltip("Emitted at the burst point. Authored by the build step.")]
        private ParticleSystem burst;

        [Header("Look")]
        [SerializeField, Tooltip("Material for the motes and their trails. Additive, so a mote " +
            "fades by going dark rather than by going transparent - which keeps the scene free of " +
            "the semi-transparent surfaces that are hard to fuse in stereo.")]
        private Material moteMaterial;
        [SerializeField, Tooltip("Colour of a mote at full brightness.")]
        private Color moteColor = new Color(1f, 0.72f, 0.35f, 1f);
        [SerializeField, Tooltip("Radius of a mote in metres, before the near-size boost.")]
        private float moteRadius = 0.0035f;
        [SerializeField, Tooltip("How much larger a mote is at the front of its travel than at " +
            "the back. Above 1 exaggerates the perspective gradient, which is the cue doing the " +
            "work here.")]
        private float nearSizeBoost = 1.5f;
        [SerializeField, Tooltip("Seconds of ribbon behind each mote. The trail is what gives a " +
            "point some extent to judge distance by.")]
        private float trailTime = 0.9f;

        [Header("Field")]
        [SerializeField, Tooltip("How many motes are in flight at once.")]
        private int moteCount = 48;
        [SerializeField, Range(0.05f, 1f), Tooltip("Where motes spawn, as a fraction of the depth " +
            "budget behind the glass. 1 is the far limit.")]
        private float spawnDepthFraction = 0.7f;
        [SerializeField, Range(0.1f, 1f), Tooltip("Where motes are retired, as a fraction of the " +
            "pop-out budget in front of the glass. Kept below 1 so nothing sits at the very limit.")]
        private float retireDepthFraction = 0.75f;
        [SerializeField, Range(0.1f, 1f), Tooltip("How much of the window motes spawn across. " +
            "Well under 1, because projection magnifies them as they come forward - a mote that " +
            "starts near the edge is off it by the time it arrives.")]
        private float spawnSpread = 0.45f;

        [Header("Motion")]
        [SerializeField, Tooltip("Metres per second a mote travels towards the viewer. Slow: " +
            "vergence cannot track fast motion in depth, and it reads as a glitch rather than as " +
            "an approach.")]
        private float driftSpeed = 0.045f;
        [SerializeField, Range(0f, 0.9f), Tooltip("How much mote speeds vary, so the field does " +
            "not advance as a sheet.")]
        private float speedVariance = 0.45f;
        [SerializeField, Tooltip("Metres of sideways wander on the way in.")]
        private float curlAmount = 0.018f;
        [SerializeField, Tooltip("Speed of the wander.")]
        private float curlSpeed = 0.4f;
        [SerializeField, Range(0.2f, 1f), Tooltip("Drift speed multiplier while nobody is here, " +
            "so the idle scene is calm rather than busy.")]
        private float attractSpeedScale = 0.7f;

        [Header("Safety")]
        [SerializeField, Tooltip("Metres of projected margin over which a mote fades out as it " +
            "approaches the frame edge. It is retired before it can be clipped, which would break " +
            "the stereo window.")]
        private float edgeFadeMargin = 0.035f;
        [SerializeField, Range(0.01f, 0.5f), Tooltip("Fraction of a mote's travel spent fading in " +
            "at the back and out at the front, so none of them pop.")]
        private float travelFade = 0.15f;

        [Header("Burst")]
        [SerializeField, Tooltip("Particles emitted when a mote is struck.")]
        private int burstParticles = 14;
        [SerializeField, Tooltip("Lowest chime pitch, at the back of the volume.")]
        private float chimePitchLow = 0.8f;
        [SerializeField, Tooltip("Highest chime pitch, at the front.")]
        private float chimePitchHigh = 1.6f;

        private Mote[] _motes;
        private MaterialPropertyBlock _properties;
        private Mesh _sphere;
        private int _burstCount;
        private bool _ready;

        /// <summary>How many motes have been burst since the last reset.</summary>
        public int BurstCount {
            get { return _burstCount; }
        }

        /// <summary>
        /// Per-mote state. A plain class in a fixed-size pool rather than a component: nothing here
        /// wants Unity's message loop, and forty-eight MonoBehaviours ticking independently would
        /// be both slower and harder to reason about than one field updating its own array.
        /// </summary>
        private class Mote {
            public Transform Transform;
            public MeshRenderer Renderer;
            public TrailRenderer Trail;
            public Vector2 Origin;
            public float Depth;
            public float Speed;
            public float Phase;
            public float CurlScale;
            public float Brightness;
        }

        private void Awake() {
            if (tip == null) {
                tip = FindFirstObjectByType<StylusTip>();
            }
            if (scene == null) {
                scene = FindFirstObjectByType<ShowcaseScene>();
            }
            _properties = new MaterialPropertyBlock();
            _sphere = BuildSphereMesh();
            BuildPool();
            _ready = _motes != null && _motes.Length > 0;
        }

        private void OnEnable() {
            if (scene != null) {
                scene.StateChanged += OnSceneStateChanged;
            }
        }

        private void OnDisable() {
            if (scene != null) {
                scene.StateChanged -= OnSceneStateChanged;
            }
        }

        private void OnSceneStateChanged(ShowcaseState state) {
            if (state == ShowcaseState.Attract) {
                _burstCount = 0;
            }
        }

        private void Update() {
            if (!_ready || !StereoVolume.IsReady) {
                return;
            }

            float spawnDepth = StereoVolume.DepthLimit * spawnDepthFraction;
            float retireDepth = -StereoVolume.PopOutLimit * retireDepthFraction;
            float travel = spawnDepth - retireDepth;
            if (travel <= 0f) {
                return;
            }

            bool attracting = scene == null || scene.State == ShowcaseState.Attract;
            float speedScale = attracting ? attractSpeedScale : 1f;
            float delta = Time.deltaTime;
            float time = Time.time;
            float viewScale = StereoVolume.ViewScale;

            for (int i = 0; i < _motes.Length; i++) {
                Mote mote = _motes[i];
                mote.Depth -= mote.Speed * speedScale * delta * viewScale;

                float progress = Mathf.Clamp01((spawnDepth - mote.Depth) / travel);
                float curl = curlAmount * mote.CurlScale * viewScale;
                Vector3 local = new Vector3(
                    mote.Origin.x + Mathf.Sin(mote.Phase + time * curlSpeed) * curl,
                    mote.Origin.y + Mathf.Cos(mote.Phase * 1.37f + time * curlSpeed * 0.8f) * curl,
                    mote.Depth);
                Vector3 world = StereoVolume.ToWorld(local);

                float brightness = TravelEnvelope(progress) * EdgeEnvelope(world);
                mote.Brightness = brightness;

                float size = moteRadius * viewScale * Mathf.Lerp(1f, nearSizeBoost, progress) * 2f;
                mote.Transform.position = world;
                mote.Transform.localScale = new Vector3(size, size, size);
                ApplyBrightness(mote, brightness);

                if (progress >= 1f) {
                    Respawn(mote, spawnDepth, true);
                    continue;
                }
                if (brightness > 0.25f && TryBurst(mote, world)) {
                    Respawn(mote, spawnDepth, true);
                }
            }
        }

        /// <summary>
        /// Fades a mote in at the back of its travel and out at the front, so none of them appear
        /// or vanish abruptly.
        /// </summary>
        private float TravelEnvelope(float progress) {
            if (travelFade <= 0f) {
                return 1f;
            }
            float rising = Mathf.Clamp01(progress / travelFade);
            float falling = Mathf.Clamp01((1f - progress) / travelFade);
            return Mathf.Min(rising, falling);
        }

        /// <summary>
        /// Dims a mote as it runs out of room inside the stereo window, so it is gone before it
        /// can be clipped by the frame while in front of the glass.
        /// </summary>
        private float EdgeEnvelope(Vector3 world) {
            if (edgeFadeMargin <= 0f) {
                return 1f;
            }
            float margin = StereoVolume.ProjectedMargin(world);
            return Mathf.Clamp01(margin / (edgeFadeMargin * StereoVolume.ViewScale));
        }

        private bool TryBurst(Mote mote, Vector3 world) {
            if (tip == null) {
                return false;
            }
            float reach = tip.Radius + mote.Transform.localScale.x * 0.5f;
            if ((world - tip.Position).sqrMagnitude > reach * reach) {
                return false;
            }

            if (burst != null) {
                ParticleSystem.EmitParams parameters = new ParticleSystem.EmitParams();
                parameters.position = world;
                parameters.applyShapeToPosition = false;
                burst.Emit(parameters, burstParticles);
            }
            if (haptics != null) {
                haptics.Tick();
            }
            if (audioCues != null) {
                audioCues.PlayChimeAtDepth(world, chimePitchLow, chimePitchHigh);
            }
            _burstCount++;
            if (scene != null) {
                scene.BeginPlay();
            }
            return true;
        }

        private void BuildPool() {
            _motes = new Mote[Mathf.Max(1, moteCount)];
            float spawnDepth = StereoVolume.IsReady
                ? StereoVolume.DepthLimit * spawnDepthFraction
                : 0.2f;
            for (int i = 0; i < _motes.Length; i++) {
                GameObject host = new GameObject("Mote" + i.ToString("D2"));
                host.transform.SetParent(transform, false);

                MeshFilter filter = host.AddComponent<MeshFilter>();
                filter.sharedMesh = _sphere;
                MeshRenderer renderer = host.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = moteMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                TrailRenderer trail = host.AddComponent<TrailRenderer>();
                trail.time = trailTime;
                trail.sharedMaterial = moteMaterial;
                trail.numCapVertices = 0;
                trail.numCornerVertices = 0;
                trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.autodestruct = false;
                trail.emitting = true;

                Mote mote = new Mote();
                mote.Transform = host.transform;
                mote.Renderer = renderer;
                mote.Trail = trail;
                _motes[i] = mote;

                Respawn(mote, spawnDepth, false);
                // Stagger the field so it does not arrive as one wave on the first frame.
                mote.Depth = Mathf.Lerp(spawnDepth, -StereoVolume.PopOutLimit * retireDepthFraction,
                    i / (float)_motes.Length);
            }
        }

        private void Respawn(Mote mote, float spawnDepth, bool clearTrail) {
            Vector2 half = StereoVolume.Window * 0.5f * spawnSpread;
            mote.Origin = new Vector2(
                Random.Range(-half.x, half.x),
                Random.Range(-half.y, half.y));
            mote.Depth = spawnDepth;
            mote.Speed = driftSpeed * Random.Range(1f - speedVariance, 1f + speedVariance);
            mote.Phase = Random.Range(0f, Mathf.PI * 2f);
            mote.CurlScale = Random.Range(0.4f, 1.4f);
            mote.Brightness = 0f;
            ApplyBrightness(mote, 0f);
            if (clearTrail && mote.Trail != null) {
                // Without this the ribbon stretches across the whole volume from where the mote
                // died to where it reappeared.
                mote.Transform.position = StereoVolume.ToWorld(
                    new Vector3(mote.Origin.x, mote.Origin.y, mote.Depth));
                mote.Trail.Clear();
            }
        }

        private void ApplyBrightness(Mote mote, float brightness) {
            Color lit = moteColor * brightness;
            lit.a = 1f;
            _properties.Clear();
            _properties.SetColor("_BaseColor", lit);
            _properties.SetColor("_Color", lit);
            mote.Renderer.SetPropertyBlock(_properties);
            if (mote.Trail != null) {
                mote.Trail.startColor = lit;
                mote.Trail.endColor = new Color(lit.r, lit.g, lit.b, 0f);
            }
        }

        /// <summary>
        /// A sphere rather than a billboard. <b>A billboard has no single correct orientation in
        /// stereo</b> - facing it at one eye points it away from the other, and the mismatch shows
        /// up as a quad that will not fuse. Real geometry costs a few hundred triangles a mote and
        /// is simply correct.
        /// </summary>
        private Mesh BuildSphereMesh() {
            GameObject primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh mesh = primitive.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) {
                Destroy(primitive);
            } else {
                DestroyImmediate(primitive);
            }
            return mesh;
        }
    }
}
