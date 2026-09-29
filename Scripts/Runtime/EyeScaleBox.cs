using KmaxXR;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// A bounding frame around the eye with four corner handles. Dragging any corner scales the
    /// model uniformly, the way a selection box behaves in any editor.
    ///
    /// The frame is billboarded and rectangular rather than a true eight-cornered box. A wireframe
    /// cube in stereo is a thicket of lines that reads as clutter and hides the anatomy behind it;
    /// four corners on a plane facing the viewer are unambiguous from any angle and stay out of
    /// the way. It also matches what the gesture means - dragging a corner outward is a screen-space
    /// idea, not a volumetric one.
    ///
    /// Scale is applied through <see cref="EyeManipulator.SetZoom"/> rather than to a transform.
    /// The manipulator rewrites the pivot's scale from its own zoom value every frame, so anything
    /// written to the transform directly would be gone by the next one. Going through it also means
    /// Reset View restores the scale for free.
    /// </summary>
    public class EyeScaleBox : MonoBehaviour {
        /// <summary>
        /// True while a handle is hovered or being dragged anywhere in the scene.
        ///
        /// A static is the honest shape here: this is input arbitration between systems that have
        /// no reason to hold references to each other - the fly controller and the stylus both need
        /// to stand down, and neither should have to know what a scale box is. The SDK's own
        /// <see cref="KmaxPointer"/> registry works the same way.
        /// </summary>
        public static bool SuppressViewDrag { get; private set; }

        [Header("References")]
        [SerializeField, Tooltip("Drives the model's scale. Scaling the pivot directly does not work.")]
        private EyeManipulator manipulator;
        [SerializeField, Tooltip("Measured to size the frame. The model pivot, normally.")]
        private Transform boundsSource;
        [SerializeField, Tooltip("Hidden while a part is focused, where the model is already scaled " +
            "and moved for framing and a second scale on top of it only confuses.")]
        private EyeFocusView focusView;

        [Header("Materials")]
        [SerializeField, Tooltip("Frame outline. Reads vertex colours, so a particle unlit shader.")]
        private Material frameMaterial;
        [SerializeField, Tooltip("Corner handles.")]
        private Material handleMaterial;

        [Header("Frame")]
        [SerializeField, Tooltip("Metres of clearance between the model and the frame.")]
        private float padding = 0.012f;
        [SerializeField, Tooltip("Frame line width in metres.")]
        private float lineWidth = 0.0009f;
        [SerializeField] private Color frameColor = new Color(0.55f, 0.80f, 1f, 0.35f);

        [Header("Handles")]
        [SerializeField, Tooltip("Handle size in metres. Also the size of its pick volume.")]
        private float handleSize = 0.011f;
        [SerializeField] private Color handleColor = new Color(0.62f, 0.88f, 1f, 0.9f);
        [SerializeField] private Color handleHoverColor = new Color(1f, 0.86f, 0.45f, 1f);
        [SerializeField, Range(1f, 2f), Tooltip("Handle scale while hovered.")]
        private float handleHoverScale = 1.4f;

        [Header("Behaviour")]
        [SerializeField, Tooltip("Hide the frame while a part is focused.")]
        private bool hideWhileFocused = true;
        [SerializeField, Tooltip("Smallest drag radius in pixels that still starts a scale. Grabbing " +
            "a handle while the pointer sits almost on the centre would otherwise divide by nearly " +
            "nothing and fling the scale to its limit.")]
        private float minimumDragRadius = 24f;

        private static readonly int[] ColorPropertyIds = {
            Shader.PropertyToID("_BaseColor"),
            Shader.PropertyToID("_Color")
        };

        private LineRenderer _frame;
        private readonly EyeScaleHandle[] _handles = new EyeScaleHandle[4];
        private readonly Transform[] _handleTransforms = new Transform[4];
        private readonly Renderer[] _handleRenderers = new Renderer[4];
        private readonly Vector3[] _corners = new Vector3[4];
        private MaterialPropertyBlock _propertyBlock;

        private Camera _camera;
        private bool _isDragging;
        private float _startZoom = 1f;
        private float _startRadius;
        private Vector2 _dragCentre;
        private bool _built;

        private void Awake() {
            if (manipulator == null || boundsSource == null) {
                Debug.LogError($"{nameof(EyeScaleBox)} on '{name}' needs both {nameof(manipulator)} and " +
                    $"{nameof(boundsSource)}; the scale handles are disabled.", this);
                enabled = false;
                return;
            }

            _propertyBlock = new MaterialPropertyBlock();
            Build();
        }

        private void OnDisable() {
            // Never leave the view's orbit suppressed because this was switched off mid-grab.
            _isDragging = false;
            SuppressViewDrag = false;
        }

        private void Update() {
            if (!_built) {
                return;
            }

            if (hideWhileFocused && focusView != null && focusView.IsFocused && !_isDragging) {
                SetVisible(false);
                SuppressViewDrag = false;
                return;
            }

            Bounds bounds;
            if (!EyePartBounds.TryGet(boundsSource, out bounds)) {
                SetVisible(false);
                return;
            }

            _camera = ResolveCamera();
            if (_camera == null) {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            Fit(bounds);
            UpdateHandleVisuals();

            SuppressViewDrag = _isDragging || AnyHandleHovered();
        }

        /// <summary>
        /// Records where the grab started. Everything afterwards is measured against this, so the
        /// scale tracks the pointer rather than accumulating per-frame error.
        /// </summary>
        public void BeginScale(PointerEventData eventData) {
            if (_camera == null || manipulator == null) {
                return;
            }

            // The centre is frozen for the duration. Read live it would move as the model grows,
            // which feeds the scale back into its own input.
            _dragCentre = _camera.WorldToScreenPoint(transform.position);
            _startRadius = (eventData.position - _dragCentre).magnitude;
            _startZoom = manipulator.Zoom;

            if (_startRadius < minimumDragRadius) {
                _startRadius = 0f;
                return;
            }

            _isDragging = true;
            SuppressViewDrag = true;
        }

        public void UpdateScale(PointerEventData eventData) {
            if (!_isDragging || _startRadius <= 0f || manipulator == null) {
                return;
            }

            float radius = (eventData.position - _dragCentre).magnitude;
            manipulator.SetZoom(_startZoom * (radius / _startRadius));
        }

        public void EndScale() {
            _isDragging = false;
            _startRadius = 0f;
            SuppressViewDrag = AnyHandleHovered();
        }

        /// <summary>
        /// Builds the frame and its four corners. Done in code rather than authored into the scene
        /// because the whole thing is four quads and a line - there is nothing to art-direct, and a
        /// prefab would be four more references to keep wired.
        /// </summary>
        private void Build() {
            GameObject frameObject = new GameObject("Frame");
            frameObject.transform.SetParent(transform, false);

            _frame = frameObject.AddComponent<LineRenderer>();
            _frame.useWorldSpace = false;
            _frame.loop = true;
            _frame.positionCount = 4;
            _frame.startWidth = lineWidth;
            _frame.endWidth = lineWidth;
            _frame.numCornerVertices = 2;
            _frame.alignment = LineAlignment.View;
            _frame.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _frame.receiveShadows = false;
            _frame.startColor = frameColor;
            _frame.endColor = frameColor;

            if (frameMaterial != null) {
                _frame.sharedMaterial = frameMaterial;
            }

            for (int i = 0; i < 4; i++) {
                _handles[i] = BuildHandle(i);
                _handleTransforms[i] = _handles[i].transform;
                _handleRenderers[i] = _handles[i].GetComponent<Renderer>();
            }

            _built = true;
        }

        private EyeScaleHandle BuildHandle(int index) {
            GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Quad);
            handle.name = "ScaleHandle_" + index;
            handle.transform.SetParent(transform, false);
            handle.transform.localScale = Vector3.one * handleSize;

            Renderer renderer = handle.GetComponent<Renderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (handleMaterial != null) {
                renderer.sharedMaterial = handleMaterial;
            }

            // A quad's own collider is paper-thin and sits edge-on to a beam arriving off-axis,
            // which makes it very hard to hit. A box with real depth is far more forgiving.
            Destroy(handle.GetComponent<MeshCollider>());
            BoxCollider box = handle.AddComponent<BoxCollider>();
            box.size = new Vector3(1.6f, 1.6f, 1.6f);

            EyeScaleHandle component = handle.AddComponent<EyeScaleHandle>();
            component.Initialize(this);
            return component;
        }

        /// <summary>
        /// Wraps the frame around the model as seen from where the viewer is now.
        ///
        /// The bounds' eight corners are projected onto the camera's right and up axes and the
        /// extremes taken, which gives a rectangle that hugs the silhouette from this angle rather
        /// than a loose box sized to the model's longest diagonal.
        /// </summary>
        private void Fit(Bounds bounds) {
            transform.SetPositionAndRotation(bounds.center, _camera.transform.rotation);

            Vector3 right = _camera.transform.right;
            Vector3 up = _camera.transform.up;
            Vector3 extents = bounds.extents;

            float halfWidth = 0f;
            float halfHeight = 0f;

            for (int x = -1; x <= 1; x += 2) {
                for (int y = -1; y <= 1; y += 2) {
                    for (int z = -1; z <= 1; z += 2) {
                        Vector3 offset = new Vector3(extents.x * x, extents.y * y, extents.z * z);
                        halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(offset, right)));
                        halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(offset, up)));
                    }
                }
            }

            halfWidth += padding;
            halfHeight += padding;

            _corners[0] = new Vector3(-halfWidth, -halfHeight, 0f);
            _corners[1] = new Vector3(-halfWidth, halfHeight, 0f);
            _corners[2] = new Vector3(halfWidth, halfHeight, 0f);
            _corners[3] = new Vector3(halfWidth, -halfHeight, 0f);

            _frame.SetPositions(_corners);

            for (int i = 0; i < 4; i++) {
                _handleTransforms[i].localPosition = _corners[i];
                _handleTransforms[i].localRotation = Quaternion.identity;
            }
        }

        private void UpdateHandleVisuals() {
            for (int i = 0; i < 4; i++) {
                EyeScaleHandle handle = _handles[i];
                bool active = handle.IsHovered || handle.IsDragging;

                _handleTransforms[i].localScale =
                    Vector3.one * (handleSize * (active ? handleHoverScale : 1f));

                if (_handleRenderers[i] == null || _propertyBlock == null) {
                    continue;
                }

                Color color = active ? handleHoverColor : handleColor;
                _handleRenderers[i].GetPropertyBlock(_propertyBlock);
                for (int c = 0; c < ColorPropertyIds.Length; c++) {
                    _propertyBlock.SetColor(ColorPropertyIds[c], color);
                }
                _handleRenderers[i].SetPropertyBlock(_propertyBlock);
            }
        }

        private bool AnyHandleHovered() {
            for (int i = 0; i < 4; i++) {
                if (_handles[i] != null && (_handles[i].IsHovered || _handles[i].IsDragging)) {
                    return true;
                }
            }

            return false;
        }

        private void SetVisible(bool visible) {
            if (_frame != null && _frame.enabled != visible) {
                _frame.enabled = visible;
            }

            for (int i = 0; i < 4; i++) {
                if (_handles[i] != null && _handles[i].gameObject.activeSelf != visible) {
                    _handles[i].gameObject.SetActive(visible);
                }
            }
        }

        private static Camera ResolveCamera() {
            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled) {
                return main;
            }

            Camera[] all = Camera.allCameras;
            for (int i = 0; i < all.Length; i++) {
                if (all[i] != null && all[i].isActiveAndEnabled) {
                    return all[i];
                }
            }

            return null;
        }
    }
}
