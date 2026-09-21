using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Unity-scene-view style flying, applied to the XRRig root rather than to the camera.
    ///
    /// The camera under the rig belongs to the head tracker - driving it directly fights the
    /// tracker and breaks the stereo geometry. Moving the rig instead moves the whole viewing
    /// frustum and virtual screen through the world, which is the correct way to navigate a
    /// fish-tank stereo display, and head tracking keeps working on top of it.
    ///
    /// Right mouse to look, WASD to move, Q/E down and up, shift to go faster, wheel to dolly,
    /// middle mouse to pan. R returns both the view and the eye to their authored state.
    /// </summary>
    public class ViewerFlyController : MonoBehaviour {
        [SerializeField, Tooltip("The XRRig root. Never the camera underneath it.")]
        private Transform rigRoot;
        [SerializeField, Tooltip("Also reset by the reset key, so one key restores the whole view.")]
        private EyeManipulator eyeManipulator;
        [SerializeField, Tooltip("Main exhibit controller to notify on reset.")]
        private EyeAnatomyController exhibitController;
        [SerializeField, Tooltip("Enable scene-view flying for the XRRig.")]
        private bool enableFly = true;

        [Header("Speeds")]
        [SerializeField, Tooltip("Metres per second.")] private float moveSpeed = 0.25f;
        [SerializeField] private float boostMultiplier = 3f;
        [SerializeField, Tooltip("Degrees per pixel of mouse movement.")]
        private float lookSensitivity = 0.15f;
        [SerializeField, Tooltip("Metres per wheel notch.")] private float dollySpeed = 0.06f;
        [SerializeField, Tooltip("Metres per pixel while panning with the middle button.")]
        private float panSpeed = 0.0006f;

        [Header("Keys")]
        [SerializeField] private KeyCode resetKey = KeyCode.R;
        [SerializeField] private KeyCode boostKey = KeyCode.LeftShift;

        private Vector3 restPosition;
        private Quaternion restRotation;
        private float yaw;
        private float pitch;
        private Vector3 lastMousePosition;

        private void Awake() {
            if (rigRoot == null) {
                Debug.LogError($"{nameof(ViewerFlyController)} on '{name}' has no {nameof(rigRoot)} assigned; flying is disabled.", this);
                enabled = false;
                return;
            }

            if (eyeManipulator == null) {
                eyeManipulator = GetComponent<EyeManipulator>();
            }

            if (exhibitController == null) {
                exhibitController = GetComponent<EyeAnatomyController>();
            }

            restPosition = rigRoot.position;
            restRotation = rigRoot.rotation;
            CaptureAngles();
        }

        private void Update() {
            if (Input.GetKeyDown(resetKey)) {
                ResetView();

                if (exhibitController != null) {
                    exhibitController.ResetToHome();
                } else if (eyeManipulator != null) {
                    eyeManipulator.ResetTransform(true);
                }

                return;
            }

            if (!enableFly) {
                return;
            }

            Vector3 mousePosition = Input.mousePosition;
            Vector3 mouseDelta = mousePosition - lastMousePosition;
            lastMousePosition = mousePosition;

            if (Input.GetMouseButtonDown(1)) {
                CaptureAngles();
            }

            if (Input.GetMouseButtonDown(2)) {
                lastMousePosition = Input.mousePosition;
            }

            if (Input.GetMouseButton(1)) {
                Look(mouseDelta);
                Dolly();
            } else if (Input.GetMouseButton(2)) {
                Pan(mouseDelta);
            }

            Move();
        }

        /// <summary>
        /// Returns the rig to the pose it was authored at.
        /// </summary>
        public void ResetView() {
            rigRoot.SetPositionAndRotation(restPosition, restRotation);
            CaptureAngles();
        }

        private void CaptureAngles() {
            Vector3 euler = rigRoot.rotation.eulerAngles;
            pitch = NormaliseAngle(euler.x);
            yaw = euler.y;
            lastMousePosition = Input.mousePosition;
        }

        private void Look(Vector3 mouseDelta) {
            yaw += mouseDelta.x * lookSensitivity;
            pitch -= mouseDelta.y * lookSensitivity;
            pitch = Mathf.Clamp(pitch, -85f, 85f);
            rigRoot.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        private void Move() {
            float forward = 0f;
            float strafe = 0f;
            float rise = 0f;

            if (Input.GetKey(KeyCode.W)) {
                forward += 1f;
            }

            if (Input.GetKey(KeyCode.S)) {
                forward -= 1f;
            }

            if (Input.GetKey(KeyCode.D)) {
                strafe += 1f;
            }

            if (Input.GetKey(KeyCode.A)) {
                strafe -= 1f;
            }

            if (Input.GetKey(KeyCode.E)) {
                rise += 1f;
            }

            if (Input.GetKey(KeyCode.Q)) {
                rise -= 1f;
            }

            if (Mathf.Approximately(forward, 0f) && Mathf.Approximately(strafe, 0f) && Mathf.Approximately(rise, 0f)) {
                return;
            }

            float speed = moveSpeed;
            if (Input.GetKey(boostKey)) {
                speed *= boostMultiplier;
            }

            Vector3 motion = rigRoot.forward * forward + rigRoot.right * strafe + rigRoot.up * rise;
            rigRoot.position += motion.normalized * speed * Time.deltaTime;
        }

        private void Pan(Vector3 mouseDelta) {
            rigRoot.position -= rigRoot.right * mouseDelta.x * panSpeed + rigRoot.up * mouseDelta.y * panSpeed;
        }

        private void Dolly() {
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(wheel, 0f)) {
                return;
            }

            rigRoot.position += rigRoot.forward * wheel * dollySpeed;
        }

        private static float NormaliseAngle(float degrees) {
            if (degrees > 180f) {
                return degrees - 360f;
            }

            return degrees;
        }
    }
}