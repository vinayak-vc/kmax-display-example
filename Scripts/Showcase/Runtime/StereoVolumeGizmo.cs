using UnityEngine;

namespace ViitorCloud.KmaxShowcase {
    /// <summary>
    /// Draws <see cref="StereoVolume"/> in the scene view so content can be authored against it.
    ///
    /// The SDK already gizmos its comfort frustum, but only as four lines between two rectangles
    /// hanging off the camera, which is hard to place a building or a bin of blocks against. This
    /// draws the same budget as a box in the rig's own space, with the screen plane marked, so the
    /// two halves of it are legible: everything on the viewer's side of the bright rectangle is
    /// pop-out and has to stay clear of the frame edges, and everything past it is depth.
    ///
    /// Purely an authoring aid. It has no runtime behaviour and costs nothing in a build.
    /// </summary>
    [ExecuteAlways]
    public class StereoVolumeGizmo : MonoBehaviour {
        [SerializeField, Tooltip("Draw the volume even when this object is not selected.")]
        private bool alwaysShow = true;
        [SerializeField, Tooltip("Draw the pop-out half, in front of the glass.")]
        private bool showPopOut = true;
        [SerializeField, Tooltip("Draw the depth half, behind the glass.")]
        private bool showDepth = true;
        [SerializeField, Tooltip("Outline renderers under this object that break the stereo window " +
            "or leave the depth budget. Leave empty to check nothing.")]
        private Transform inspect;

        private static readonly Color PopOutColor = new Color(1f, 0.55f, 0.2f, 1f);
        private static readonly Color DepthColor = new Color(0.3f, 0.75f, 1f, 1f);
        private static readonly Color ScreenPlaneColor = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color ViolationColor = new Color(1f, 0.15f, 0.15f, 1f);

#if UNITY_EDITOR
        private void OnDrawGizmos() {
            if (alwaysShow) {
                Draw();
            }
        }

        private void OnDrawGizmosSelected() {
            if (!alwaysShow) {
                Draw();
            }
        }

        private void Draw() {
            if (!StereoVolume.IsReady) {
                return;
            }
            Transform plane = StereoVolume.ScreenTransform;
            if (plane == null) {
                return;
            }

            Matrix4x4 restore = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(plane.position, plane.rotation, Vector3.one);

            Vector2 window = StereoVolume.Window;
            float popOut = StereoVolume.PopOutLimit;
            float depth = StereoVolume.DepthLimit;

            Gizmos.color = ScreenPlaneColor;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(window.x, window.y, 0f));

            if (showPopOut && popOut > 0f) {
                Gizmos.color = PopOutColor;
                Gizmos.DrawWireCube(new Vector3(0f, 0f, -popOut * 0.5f),
                    new Vector3(window.x, window.y, popOut));
            }
            if (showDepth && depth > 0f) {
                Gizmos.color = DepthColor;
                Gizmos.DrawWireCube(new Vector3(0f, 0f, depth * 0.5f),
                    new Vector3(window.x, window.y, depth));
            }

            Gizmos.matrix = restore;
            DrawViolations();
        }

        private void DrawViolations() {
            if (inspect == null) {
                return;
            }
            Renderer[] renderers = inspect.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++) {
                Bounds bounds = renderers[i].bounds;
                float nearest;
                float furthest;
                bool outOfDepth = StereoVolume.ExceedsComfortDepth(bounds, out nearest, out furthest);
                if (!StereoVolume.ViolatesWindow(bounds) && !outOfDepth) {
                    continue;
                }
                Gizmos.color = ViolationColor;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }
        }
#endif
    }
}
