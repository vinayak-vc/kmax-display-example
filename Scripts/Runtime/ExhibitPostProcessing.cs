using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Turns post-processing on for the stereo sub-cameras.
    ///
    /// This exists because the SDK's <c>VRRenderer</c> builds the <c>left</c> and <c>right</c>
    /// cameras at runtime and does not give them a <see cref="UniversalAdditionalCameraData"/>.
    /// Without that component URP falls back to defaults, where <c>renderPostProcessing</c> is
    /// false - so the only two cameras that actually draw anything were silently skipping every
    /// volume in the scene. Setting it on the authored root camera in the inspector does nothing,
    /// because the root camera's own <c>Camera</c> component is disabled by the SDK.
    ///
    /// That mattered more than it sounds. Without a tonemapper the model's near-white albedo
    /// clips the moment the lighting is strong enough to read, which is why the rig previously had
    /// to be run so dark that everything looked muddy. With one running, the lights can be driven
    /// properly and the highlights roll off instead of turning into flat white.
    /// </summary>
    public class ExhibitPostProcessing : MonoBehaviour {
        [SerializeField, Tooltip("Rig whose cameras are configured. Falls back to searching the scene.")]
        private Transform cameraRoot;
        [SerializeField, Tooltip("Enable post-processing on every camera found under the root.")]
        private bool enablePostProcessing = true;
        [SerializeField, Tooltip("Allow an HDR colour buffer. Without it values clip before the " +
            "tonemapper ever sees them and tonemapping becomes a no-op.")]
        private bool allowHdr = true;
        [SerializeField, Tooltip("Volume layers the cameras sample. Everything by default, so a " +
            "volume cannot be missed because of the layer it happens to sit on.")]
        private LayerMask volumeLayers = ~0;
        [SerializeField, Tooltip("Seconds between re-checks. The sub-cameras appear a frame or two " +
            "after load, and the SDK may rebuild them when the display mode changes.")]
        private float recheckInterval = 1f;

        private int _configuredCount = -1;
        private float _nextCheck;

        private void Start() {
            if (cameraRoot == null) {
                XRRigFallback();
            }

            Configure();
        }

        private void Update() {
            if (Time.unscaledTime < _nextCheck) {
                return;
            }

            _nextCheck = Time.unscaledTime + Mathf.Max(0.1f, recheckInterval);

            // Cheap guard: only walk the cameras when their number has actually changed.
            if (Camera.allCamerasCount == _configuredCount) {
                return;
            }

            Configure();
        }

        /// <summary>
        /// Applies the settings to every camera under the root. Safe to call repeatedly.
        /// </summary>
        [ContextMenu("Configure")]
        public void Configure() {
            Camera[] cameras = cameraRoot != null
                ? cameraRoot.GetComponentsInChildren<Camera>(true)
                : Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            for (int i = 0; i < cameras.Length; i++) {
                Camera camera = cameras[i];
                if (camera == null) {
                    continue;
                }

                camera.allowHDR = allowHdr;

                UniversalAdditionalCameraData data = camera.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) {
                    data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                }

                data.renderPostProcessing = enablePostProcessing;
                data.volumeLayerMask = volumeLayers;
            }

            _configuredCount = Camera.allCamerasCount;
        }

        private void XRRigFallback() {
            GameObject rig = GameObject.Find("XRRig");
            if (rig != null) {
                cameraRoot = rig.transform;
            }
        }
    }
}
