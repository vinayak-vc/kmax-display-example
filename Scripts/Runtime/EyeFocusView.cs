using KmaxXR;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Frames one part of the eye by moving and scaling the model, never the camera.
    /// On a head-tracked stereo rig the camera belongs to the viewer, so zooming is done by
    /// bringing the content to the screen plane instead.
    ///
    /// Everything here is written in the model's local space, because the model sits under a
    /// pivot that <see cref="EyeManipulator"/> rotates. That pivot is never scaled.
    /// </summary>
    public class EyeFocusView : MonoBehaviour {
        [SerializeField] private Transform modelRoot;
        [SerializeField, Tooltip("Only used to read the live view size; the camera is never moved.")]
        private XRRig xrRig;
        [SerializeField, Tooltip("Where a focused part is brought to. Defaults to the world origin.")]
        private Transform focusAnchor;
        [SerializeField, Tooltip("Optional reference to EyeManipulator to update rotation pivot on focus.")]
        private EyeManipulator eyeManipulator;
        [SerializeField, Tooltip("Optional reference to ViewerFlyController to update orbit focal center on focus.")]
        private ViewerFlyController flyController;
        [SerializeField, Range(0.1f, 1f)] private float framingRatio = 0.48f;
        [SerializeField, Tooltip("Hard ceiling on the zoom, as a multiple of the model's resting scale. " +
            "The smallest parts are a sixth of the eye, so framing one to fill the view would blow the " +
            "rest of the eye far off screen - which defeats keeping it visible behind the focus.")]
        [Range(1f, 10f)] private float maxZoomMultiplier = 2.5f;
        [SerializeField, Range(0.05f, 3f)] private float transitionDuration = 0.6f;

        [Header("Isolation")]
        [SerializeField, Tooltip("Fade the rest of the eye while one part is focused, so the part in " +
            "focus reads clearly without losing the context around it.")]
        private bool fadeOtherParts = true;
        [SerializeField, Tooltip("Applied to every part except the focused one while focused.")]
        private Material ghostMaterial;

        [Header("Transparent Part Rescue")]
        [SerializeField, Tooltip("Swap a focused part onto an opaque material when its own material is " +
            "too transparent to read. Lens and Tear film share the model's Mat.1 - a 91% grey at 58% " +
            "alpha - and against any background they are nearly invisible however the scene is lit.")]
        private bool highlightTransparentFocus = true;
        [SerializeField, Range(0f, 1f), Tooltip("A part whose every material is below this alpha gets " +
            "the highlight. 1 would rescue every part, 0 none.")]
        private float transparentAlphaThreshold = 0.75f;
        [SerializeField, Tooltip("Opaque stand-in used for the rescue. Left empty, nothing is swapped.")]
        private Material focusHighlightMaterial;

        [SerializeField, Tooltip("View size used outside play mode, when the XRRig has not initialised.")]
        private Vector2 fallbackViewSize = new Vector2(0.3454f, 0.1943f);

        private Renderer[] modelRenderers;
        private Material[][] originalMaterials;
        private Vector3 restLocalPosition;
        private Vector3 restLocalScale;
        private Vector3 fromPosition;
        private Vector3 toPosition;
        private Vector3 fromScale;
        private Vector3 toScale;
        private float progress = 1f;
        private bool isTransitioning;
        private bool isFocused;

        public bool IsFocused {
            get { return isFocused; }
        }

        private void Awake() {
            if (modelRoot == null) {
                Debug.LogError($"{nameof(EyeFocusView)} on '{name}' has no {nameof(modelRoot)} assigned; focusing is disabled.", this);
                return;
            }

            if (xrRig == null) {
                Debug.LogWarning($"{nameof(EyeFocusView)} on '{name}' has no {nameof(xrRig)} assigned; falling back to a fixed view size of {fallbackViewSize}.", this);
            }

            if (fadeOtherParts && ghostMaterial == null) {
                Debug.LogWarning($"{nameof(EyeFocusView)} on '{name}' has no {nameof(ghostMaterial)} assigned; the rest of the eye will stay fully opaque when a part is focused.", this);
            }

            if (eyeManipulator == null) {
                eyeManipulator = GetComponent<EyeManipulator>();
            }

            if (flyController == null) {
                flyController = GetComponent<ViewerFlyController>();
            }

            restLocalPosition = modelRoot.localPosition;
            restLocalScale = modelRoot.localScale;
            CacheRenderers();
        }

        private void Update() {
            if (!isTransitioning) {
                return;
            }

            progress = Mathf.MoveTowards(progress, 1f, Time.deltaTime / transitionDuration);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            modelRoot.localPosition = Vector3.Lerp(fromPosition, toPosition, eased);
            modelRoot.localScale = Vector3.Lerp(fromScale, toScale, eased);

            if (progress >= 1f) {
                isTransitioning = false;
            }
        }

        public void Focus(Transform part) {
            if (modelRoot == null) {
                return;
            }

            if (part == null) {
                Debug.LogError($"{nameof(EyeFocusView)} was asked to focus a null part.", this);
                return;
            }

            Bounds worldBounds;
            if (!EyePartBounds.TryGet(part, out worldBounds)) {
                Debug.LogError($"{nameof(EyeFocusView)} found no renderer under '{part.name}', so it cannot be framed.", this);
                return;
            }

            float currentScale = modelRoot.localScale.x;
            if (currentScale <= Mathf.Epsilon) {
                Debug.LogError($"{nameof(EyeFocusView)} cannot frame '{part.name}': the model root has a zero scale.", this);
                return;
            }

            // Express the part in model-root local units so the result does not depend on
            // whatever scale the root is sitting at right now.
            Vector3 localCentre = modelRoot.InverseTransformPoint(worldBounds.center);
            Vector3 localSize = worldBounds.size / currentScale;

            Vector2 viewSize = GetViewSize();
            float scaleForWidth = (viewSize.x * framingRatio) / Mathf.Max(localSize.x, Mathf.Epsilon);
            float scaleForHeight = (viewSize.y * framingRatio) / Mathf.Max(localSize.y, Mathf.Epsilon);
            float targetScale = Mathf.Min(scaleForWidth, scaleForHeight);
            targetScale = Mathf.Min(targetScale, restLocalScale.x * maxZoomMultiplier);

            Vector3 anchor = focusAnchor != null ? focusAnchor.position : Vector3.zero;
            Vector3 worldTarget = anchor - modelRoot.rotation * (localCentre * targetScale);

            if (fadeOtherParts) {
                SetFadedExcept(part);
            }

            if (eyeManipulator != null) {
                eyeManipulator.SetFocalPoint(anchor);
            }

            if (flyController != null) {
                flyController.SetFocalCenter(anchor);
            }

            BeginTransition(ToLocal(worldTarget), Vector3.one * targetScale);
            isFocused = true;
        }

        public void ClearFocus() {
            if (modelRoot == null) {
                return;
            }

            if (eyeManipulator != null) {
                eyeManipulator.ClearFocalPoint();
            }

            if (flyController != null) {
                flyController.ResetFocalCenter();
            }

            SetFadedExcept(null);
            BeginTransition(restLocalPosition, restLocalScale);
            isFocused = false;
        }

        /// <summary>
        /// Fades every part except the given one, or restores all of them when passed null.
        /// </summary>
        private void SetFadedExcept(Transform part) {
            if (modelRenderers == null) {
                return;
            }

            for (int i = 0; i < modelRenderers.Length; i++) {
                Renderer renderer = modelRenderers[i];
                if (renderer == null) {
                    continue;
                }

                bool keepOpaque = part == null || renderer.transform.IsChildOf(part);
                if (keepOpaque) {
                    // Always restore first, so a part rescued last time goes back to its own look
                    // when it stops being the focus.
                    renderer.sharedMaterials = originalMaterials[i];

                    if (part != null && NeedsHighlight(originalMaterials[i])) {
                        renderer.sharedMaterials = Fill(focusHighlightMaterial, originalMaterials[i].Length);
                    }

                    continue;
                }

                if (ghostMaterial != null) {
                    renderer.sharedMaterials = Fill(ghostMaterial, originalMaterials[i].Length);
                }
            }
        }

        private static Material[] Fill(Material material, int count) {
            Material[] result = new Material[count];
            for (int i = 0; i < count; i++) {
                result[i] = material;
            }

            return result;
        }

        /// <summary>
        /// True when every material on a part is too see-through to read on its own.
        ///
        /// Deliberately "every" rather than "any": a part built from one solid mesh and one glassy
        /// one already reads through the solid mesh, and replacing both would throw away more of the
        /// source art than the problem justifies.
        /// </summary>
        private bool NeedsHighlight(Material[] materials) {
            if (!highlightTransparentFocus || focusHighlightMaterial == null || materials == null || materials.Length == 0) {
                return false;
            }

            for (int i = 0; i < materials.Length; i++) {
                if (materials[i] == null) {
                    return false;
                }

                if (GetAlpha(materials[i]) >= transparentAlphaThreshold) {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Reads a material's base alpha. The model's materials come from glTFast, which names the
        /// property <c>baseColorFactor</c> rather than either of URP's two spellings.
        /// </summary>
        private static float GetAlpha(Material material) {
            if (material.HasProperty(BaseColorFactorId)) {
                return material.GetColor(BaseColorFactorId).a;
            }

            if (material.HasProperty(BaseColorId)) {
                return material.GetColor(BaseColorId).a;
            }

            if (material.HasProperty(ColorId)) {
                return material.GetColor(ColorId).a;
            }

            return 1f;
        }

        private static readonly int BaseColorFactorId = Shader.PropertyToID("baseColorFactor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void CacheRenderers() {
            // Runs before the controller spawns hotspots, so this is purely the model's own meshes.
            Renderer[] found = modelRoot.GetComponentsInChildren<Renderer>(true);
            modelRenderers = new Renderer[found.Length];
            originalMaterials = new Material[found.Length][];

            for (int i = 0; i < found.Length; i++) {
                modelRenderers[i] = found[i];
                originalMaterials[i] = found[i].sharedMaterials;
            }
        }

        private Vector3 ToLocal(Vector3 worldPosition) {
            if (modelRoot.parent == null) {
                return worldPosition;
            }

            return modelRoot.parent.InverseTransformPoint(worldPosition);
        }

        private void BeginTransition(Vector3 localPosition, Vector3 localScale) {
            fromPosition = modelRoot.localPosition;
            fromScale = modelRoot.localScale;
            toPosition = localPosition;
            toScale = localScale;
            progress = 0f;
            isTransitioning = true;
        }

        private Vector2 GetViewSize() {
            if (Application.isPlaying && xrRig != null) {
                return XRRig.ViewSize;
            }

            return fallbackViewSize;
        }
    }
}