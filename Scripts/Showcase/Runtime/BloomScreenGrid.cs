using System.Collections.Generic;
using UnityEngine;

namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// A sparse grid lying exactly on the screen plane, so a mote crossing it is a visible event
    /// rather than something that merely happens.
    ///
    /// Bloom's weakness is that loose points carry pop-out well and depth badly. This is the
    /// cheapest correction: a viewer cannot judge that a mote is nearer than the glass without
    /// something marking where the glass is. With the grid there, the crossing reads as a mote
    /// passing through a plane - which is the entire claim the scene is making.
    ///
    /// <para><b>On the wallpaper effect.</b> A regular repeating pattern is normally a hazard on a
    /// stereoscopic display: the eyes lock onto the wrong repeat and the surface jumps in depth.
    /// It does not apply here, and the reason is worth keeping. The hazard needs disparity to be
    /// ambiguous, and <b>at exactly zero parallax there is only one possible match</b> - both eyes
    /// see the grid at the same place on the glass. Content at the screen plane is the one place a
    /// repeating pattern is safe. It is kept sparse and dim regardless, because it is a reference
    /// and not the subject.</para>
    /// </summary>
    [ExecuteAlways]
    public class BloomScreenGrid : MonoBehaviour {
        [Header("Look")]
        [SerializeField, Tooltip("Material for the grid lines. Additive, matching the motes.")]
        private Material lineMaterial;
        [SerializeField, Tooltip("Colour of the grid. Dim: this is a reference, not the subject.")]
        private Color lineColor = new Color(0.22f, 0.35f, 0.5f, 1f);
        [SerializeField, Tooltip("Line width in metres, before view scale.")]
        private float lineWidth = 0.0006f;

        [Header("Layout")]
        [SerializeField, Range(0.2f, 1f), Tooltip("How much of the window the grid covers. Inset " +
            "so it reads as a plane in the scene rather than as the edge of the display.")]
        private float extent = 0.82f;
        [SerializeField, Range(1, 8), Tooltip("Vertical lines inside the frame. Few, on purpose - " +
            "a dense grid competes with the motes for attention.")]
        private int columns = 4;
        [SerializeField, Range(1, 6), Tooltip("Horizontal lines inside the frame.")]
        private int rows = 3;
        [SerializeField, Tooltip("Draw the outer frame as well as the inner lines.")]
        private bool drawFrame = true;

        private readonly List<LineRenderer> _lines = new List<LineRenderer>();
        private Transform _root;
        private float _builtViewScale = -1f;
        private int _builtColumns = -1;
        private int _builtRows = -1;

        private void OnEnable() {
            Rebuild();
        }

        private void Update() {
            // The window and the budget both scale with the rig's view scale, which is settable at
            // runtime, so the grid cannot be built once and left alone.
            if (!StereoVolume.IsReady) {
                return;
            }
            if (!Mathf.Approximately(_builtViewScale, StereoVolume.ViewScale)
                || _builtColumns != columns || _builtRows != rows) {
                Rebuild();
            }
        }

        // Deliberately no OnValidate. Rebuilding from there would call DestroyImmediate inside one
        // of the callbacks Unity forbids it in, which logs an error every time the inspector is
        // touched. The change detection in Update covers the same cases a frame later, and this
        // component runs in edit mode, so the delay is invisible.

        /// <summary>
        /// Rebuilds the grid from the current window size. Safe to call repeatedly.
        /// </summary>
        public void Rebuild() {
            if (!StereoVolume.IsReady) {
                return;
            }
            Clear();

            Transform plane = StereoVolume.ScreenTransform;
            if (plane == null) {
                return;
            }
            GameObject host = new GameObject("GridLines");
            host.hideFlags = HideFlags.DontSave;
            _root = host.transform;
            _root.SetParent(plane, false);

            Vector2 half = StereoVolume.Window * 0.5f * extent;
            float width = lineWidth * StereoVolume.ViewScale;

            if (drawFrame) {
                LineRenderer frame = CreateLine("Frame", 5, width);
                frame.SetPosition(0, new Vector3(-half.x, -half.y, 0f));
                frame.SetPosition(1, new Vector3(half.x, -half.y, 0f));
                frame.SetPosition(2, new Vector3(half.x, half.y, 0f));
                frame.SetPosition(3, new Vector3(-half.x, half.y, 0f));
                frame.SetPosition(4, new Vector3(-half.x, -half.y, 0f));
            }

            for (int i = 1; i < columns; i++) {
                float x = Mathf.Lerp(-half.x, half.x, i / (float)columns);
                LineRenderer line = CreateLine("Column" + i, 2, width);
                line.SetPosition(0, new Vector3(x, -half.y, 0f));
                line.SetPosition(1, new Vector3(x, half.y, 0f));
            }
            for (int i = 1; i < rows; i++) {
                float y = Mathf.Lerp(-half.y, half.y, i / (float)rows);
                LineRenderer line = CreateLine("Row" + i, 2, width);
                line.SetPosition(0, new Vector3(-half.x, y, 0f));
                line.SetPosition(1, new Vector3(half.x, y, 0f));
            }

            _builtViewScale = StereoVolume.ViewScale;
            _builtColumns = columns;
            _builtRows = rows;
        }

        private LineRenderer CreateLine(string lineName, int points, float width) {
            GameObject host = new GameObject(lineName);
            host.hideFlags = HideFlags.DontSave;
            host.transform.SetParent(_root, false);
            LineRenderer line = host.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = points;
            line.widthMultiplier = width;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = lineMaterial;
            line.startColor = lineColor;
            line.endColor = lineColor;
            _lines.Add(line);
            return line;
        }

        private void Clear() {
            _lines.Clear();
            if (_root == null) {
                return;
            }
            if (Application.isPlaying) {
                Destroy(_root.gameObject);
            } else {
                DestroyImmediate(_root.gameObject);
            }
            _root = null;
        }

        private void OnDisable() {
            Clear();
        }
    }
}
