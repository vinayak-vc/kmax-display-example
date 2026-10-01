using System.Collections.Generic;
using UnityEngine;

namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// Draws the comfort volume at runtime, on the device, so the budget can be judged in stereo
    /// rather than reasoned about flat.
    ///
    /// The scene-view gizmo is the authoring tool; this is the one that matters, because almost
    /// every question this suite has to answer - is that too far forward, does that corner break
    /// the window, is the held block at a comfortable depth - can only really be settled by
    /// someone looking at the display with their own two eyes while the numbers are drawn next to
    /// the content.
    ///
    /// <para>Built from <see cref="LineRenderer"/>s parented to the screen plane. Not a canvas and
    /// not a post effect: this project has no screen-space overlay canvas, and <c>VRRenderer</c>'s
    /// sub-cameras have no <c>UniversalAdditionalCameraData</c>, so anything routed through a
    /// Volume would simply not appear.</para>
    ///
    /// <para>A development aid. Leave <see cref="visibleOnStart"/> off in anything a visitor will
    /// see.</para>
    /// </summary>
    public class ComfortOverlay : MonoBehaviour {
        [Header("Visibility")]
        [SerializeField, Tooltip("Key that shows and hides the overlay.")]
        private KeyCode toggleKey = KeyCode.F9;
        [SerializeField, Tooltip("Start visible. Leave off for anything a visitor will see.")]
        private bool visibleOnStart;

        [Header("References")]
        [SerializeField, Tooltip("Marked with a depth indicator while visible. Found in the scene " +
            "on first use when left empty.")]
        private StylusTip tip;
        [SerializeField, Tooltip("Reported on when the audit key is pressed. Leave empty to audit " +
            "every renderer in the scene.")]
        private Transform inspect;
        [SerializeField, Tooltip("Key that writes a comfort audit to the console.")]
        private KeyCode auditKey = KeyCode.F10;

        [Header("Appearance")]
        [SerializeField, Tooltip("Line width in metres, before view scale.")]
        private float lineWidth = 0.0008f;
        [SerializeField, Tooltip("The glass itself, at zero parallax.")]
        private Color screenPlaneColor = new Color(1f, 1f, 1f, 0.9f);
        [SerializeField, Tooltip("The pop-out limit, in front of the glass.")]
        private Color popOutColor = new Color(1f, 0.55f, 0.2f, 1f);
        [SerializeField, Tooltip("The depth limit, behind the glass.")]
        private Color depthColor = new Color(0.3f, 0.75f, 1f, 1f);
        [SerializeField, Tooltip("The struts joining the two limits.")]
        private Color strutColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
        [SerializeField, Tooltip("The tip's depth marker while it is inside the budget.")]
        private Color tipInsideColor = new Color(0.3f, 1f, 0.5f, 1f);
        [SerializeField, Tooltip("The tip's depth marker while it is outside the budget.")]
        private Color tipOutsideColor = new Color(1f, 0.2f, 0.2f, 1f);

        private readonly List<LineRenderer> _lines = new List<LineRenderer>();
        private Transform _root;
        private LineRenderer _screenRect;
        private LineRenderer _popOutRect;
        private LineRenderer _depthRect;
        private LineRenderer _tipMarker;
        private Material _tipMaterial;
        private bool _visible;

        /// <summary>Whether the overlay is currently drawn.</summary>
        public bool Visible {
            get { return _visible; }
        }

        private void Start() {
            if (tip == null) {
                tip = FindFirstObjectByType<StylusTip>();
            }
            SetVisible(visibleOnStart);
        }

        private void Update() {
            if (Input.GetKeyDown(toggleKey)) {
                SetVisible(!_visible);
            }
            if (Input.GetKeyDown(auditKey)) {
                Audit();
            }
            if (_visible) {
                Refresh();
            }
        }

        /// <summary>Shows or hides the overlay, building it on first use.</summary>
        public void SetVisible(bool visible) {
            _visible = visible;
            if (visible && _root == null) {
                Build();
            }
            if (_root != null) {
                _root.gameObject.SetActive(visible);
            }
            if (visible) {
                Refresh();
            }
        }

        private void Build() {
            if (!StereoVolume.IsReady) {
                Debug.LogWarning($"{nameof(ComfortOverlay)} found no XRRig, so there is no comfort " +
                    "volume to draw.", this);
                return;
            }

            GameObject host = new GameObject("ComfortOverlay");
            _root = host.transform;
            _root.SetParent(StereoVolume.ScreenTransform, false);

            _screenRect = CreateLine("ScreenPlane", screenPlaneColor, 5);
            _popOutRect = CreateLine("PopOutLimit", popOutColor, 5);
            _depthRect = CreateLine("DepthLimit", depthColor, 5);
            for (int i = 0; i < 4; i++) {
                CreateLine("Strut" + i, strutColor, 2);
            }
            _tipMarker = CreateLine("TipDepth", tipInsideColor, 2);
            _tipMaterial = _tipMarker.sharedMaterial;
        }

        private LineRenderer CreateLine(string lineName, Color color, int points) {
            GameObject host = new GameObject(lineName);
            host.transform.SetParent(_root, false);
            LineRenderer line = host.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = points;
            line.numCornerVertices = 0;
            line.numCapVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = CreateMaterial(color);
            _lines.Add(line);
            return line;
        }

        private Material CreateMaterial(Color color) {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) {
                shader = Shader.Find("Sprites/Default");
            }
            Material material = new Material(shader);
            material.hideFlags = HideFlags.HideAndDontSave;
            if (material.HasProperty("_BaseColor")) {
                material.SetColor("_BaseColor", color);
            }
            if (material.HasProperty("_Color")) {
                material.SetColor("_Color", color);
            }
            return material;
        }

        private void Refresh() {
            if (_root == null || !StereoVolume.IsReady) {
                return;
            }

            Vector2 window = StereoVolume.Window;
            float halfWidth = window.x * 0.5f;
            float halfHeight = window.y * 0.5f;
            float near = -StereoVolume.PopOutLimit;
            float far = StereoVolume.DepthLimit;
            float width = lineWidth * StereoVolume.ViewScale;

            for (int i = 0; i < _lines.Count; i++) {
                _lines[i].widthMultiplier = width;
            }

            SetRect(_screenRect, halfWidth, halfHeight, 0f);
            SetRect(_popOutRect, halfWidth, halfHeight, near);
            SetRect(_depthRect, halfWidth, halfHeight, far);

            // The four struts follow the three rectangles in _lines, in creation order.
            int strutStart = 3;
            for (int i = 0; i < 4; i++) {
                LineRenderer strut = _lines[strutStart + i];
                float x = (i == 0 || i == 3) ? -halfWidth : halfWidth;
                float y = (i < 2) ? -halfHeight : halfHeight;
                strut.SetPosition(0, new Vector3(x, y, near));
                strut.SetPosition(1, new Vector3(x, y, far));
            }

            RefreshTipMarker(halfHeight);
        }

        private void RefreshTipMarker(float halfHeight) {
            if (_tipMarker == null) {
                return;
            }
            if (tip == null) {
                _tipMarker.enabled = false;
                return;
            }
            _tipMarker.enabled = true;

            Vector3 local = StereoVolume.ToVolumeSpace(tip.Position);
            bool inside = StereoVolume.Contains(tip.Position);
            if (_tipMaterial != null) {
                Color color = inside ? tipInsideColor : tipOutsideColor;
                if (_tipMaterial.HasProperty("_BaseColor")) {
                    _tipMaterial.SetColor("_BaseColor", color);
                }
                if (_tipMaterial.HasProperty("_Color")) {
                    _tipMaterial.SetColor("_Color", color);
                }
            }

            // A plumb line from the tip down to the floor of the volume, so its depth can be read
            // against the two limit rectangles rather than guessed at.
            _tipMarker.SetPosition(0, local);
            _tipMarker.SetPosition(1, new Vector3(local.x, -halfHeight, local.z));
        }

        private void SetRect(LineRenderer line, float halfWidth, float halfHeight, float z) {
            if (line == null) {
                return;
            }
            line.SetPosition(0, new Vector3(-halfWidth, -halfHeight, z));
            line.SetPosition(1, new Vector3(halfWidth, -halfHeight, z));
            line.SetPosition(2, new Vector3(halfWidth, halfHeight, z));
            line.SetPosition(3, new Vector3(-halfWidth, halfHeight, z));
            line.SetPosition(4, new Vector3(-halfWidth, -halfHeight, z));
        }

        /// <summary>
        /// Writes a comfort audit to the console: everything that breaks the stereo window or
        /// leaves the depth budget, with the numbers. Bound to <see cref="auditKey"/> so it can be
        /// run on the device, where the answer actually matters.
        /// </summary>
        public void Audit() {
            if (!StereoVolume.IsReady) {
                Debug.LogWarning($"{nameof(ComfortOverlay)}: no XRRig, nothing to audit.", this);
                return;
            }

            Renderer[] renderers = inspect != null
                ? inspect.GetComponentsInChildren<Renderer>()
                : FindObjectsByType<Renderer>(FindObjectsSortMode.None);

            int windowBreaks = 0;
            int depthBreaks = 0;
            for (int i = 0; i < renderers.Length; i++) {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled) {
                    continue;
                }
                Bounds bounds = renderer.bounds;

                float overshoot;
                if (StereoVolume.ViolatesWindow(bounds, out overshoot)) {
                    windowBreaks++;
                    Debug.LogWarning($"Window violation: '{renderer.name}' projects {overshoot * 1000f:F1} mm " +
                        "past the frame edge while in front of the glass.", renderer);
                }

                float nearest;
                float furthest;
                if (StereoVolume.ExceedsComfortDepth(bounds, out nearest, out furthest)) {
                    depthBreaks++;
                    Debug.LogWarning($"Outside the depth budget: '{renderer.name}' spans " +
                        $"{nearest * 1000f:F0} to {furthest * 1000f:F0} mm, budget is " +
                        $"{-StereoVolume.PopOutLimit * 1000f:F0} to {StereoVolume.DepthLimit * 1000f:F0} mm.",
                        renderer);
                }
            }

            Debug.Log($"Comfort audit: {renderers.Length} renderers, {windowBreaks} window violations, " +
                $"{depthBreaks} outside the depth budget. Budget is {StereoVolume.PopOutLimit * 1000f:F0} mm " +
                $"of pop-out and {StereoVolume.DepthLimit * 1000f:F0} mm of depth at view scale " +
                $"{StereoVolume.ViewScale:F2}.", this);
        }

        private void OnDestroy() {
            for (int i = 0; i < _lines.Count; i++) {
                if (_lines[i] != null && _lines[i].sharedMaterial != null) {
                    Destroy(_lines[i].sharedMaterial);
                }
            }
            _lines.Clear();
        }
    }
}
