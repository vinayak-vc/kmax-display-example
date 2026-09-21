using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace KmaxXR
{
    /// <summary>
    /// XR套件，控制显示交互方式。
    /// </summary>
    [DefaultExecutionOrder(ScriptPriority)]
    public class XRRig : MonoBehaviour
    {
        /// <summary>
        /// 脚本优先级，确保在其他脚本之前实例化全局单例。
        /// </summary>
        public const int ScriptPriority = -500;
        [SerializeField, Header("View")]
        protected VirtualScreen screen;
        [SerializeField, Range(0.001f, 1000f)]
        protected float viewScale = 1f;
        [SerializeField, Header("Camera")]
        protected StereoCamera stereoCamera;
        [SerializeField, Header("Editor")]
        bool alwaysShowGizmos = true;
        public StereoCamera StereoRender => stereoCamera;
        protected event System.Action<float> onViewScaleChanged;

        #region 全局变量或属性
        private static XRRig rig;
        private static bool isSRP;
        /// <summary>
        /// 是否使用了SRP
        /// </summary>
        public static bool IsSRP { get => isSRP; }

        static bool mono = true;
        /// <summary>
        /// 单目显示模式
        /// </summary>
        public static bool MonoDisplayMode
        {
            get => mono;
            set
            {
                mono = value;
                manuallyControled = true;
                KmaxNative.Log("Manually set the display mode to " + (value ? "mono" : "sbs"));
                rig?.SetWorkMode(!value);
            }
        }
        /// <summary>
        /// 是否手动控制显示模式
        /// </summary>
        static bool manuallyControled = false;

        /// <summary>
        /// 虚拟屏幕
        /// </summary>
        public static VirtualScreen Screen => rig.screen;
        /// <summary>
        /// 屏幕四角位置(世界坐标)
        /// </summary>
        public static Vector3[] ScreenCorners => rig.GetScreenCorners();
        /// <summary>
        /// 虚拟屏幕位置及姿态，等同于对象本身的位置及姿态。
        /// </summary>
        public static Transform ScreenTrans => rig.transform;
        /// <summary>
        /// 视窗比例改变事件。
        /// </summary>
        public static event System.Action<float> OnViewScaleChanged
        {
            add
            {
                rig.onViewScaleChanged += value;
            }
            remove
            {
                rig.onViewScaleChanged -= value;
            }
        }

        /// <summary>
        /// 视窗缩放比例，默认为1。
        /// </summary>
        public static float ViewScale
        {
            get => rig.viewScale;
            set
            {
                rig.viewScale = value;
                rig.onViewScaleChanged?.Invoke(value);
            }
        }

        /// <summary>
        /// 视窗尺寸，表示虚拟屏幕的大小。
        /// </summary>
        public static Vector2 ViewSize => rig.screen.Size * ViewScale;
        #endregion

        #region Unity 消息
        void Awake()
        {
            if (rig == null)
            {
                KmaxNative.Log($"SDK Version: {KmaxNative.SDKVersion}");
                KmaxNative.Log($"View: {screen} x {viewScale}");
                KmaxNative.EnableHighFPS = true; // for Android
                KmaxNative.Initialize();
            }
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
            isSRP = pipeline != null;
            rig = this;
        }

        void OnValidate()
        {
            rig = this;
            screen.CalculateRect();
            AdjustCameraPosition();
            stereoCamera?.Validate();
        }

        protected virtual void Start()
        {
            if (manuallyControled)
            {
                // 不修改
            }
            else
            {
                // 初始化显示模式
                mono = KmaxNative.PrimaryDisplayMode == KmaxNative.DisplayMode.Mono;
            }
            SetWorkMode(!mono);
        }
        #endregion

        /// <summary>
        /// 获取屏幕四个角点的位置。
        /// </summary>
        /// <returns>屏幕四角位置(世界坐标)</returns>
        public Vector3[] GetScreenCorners()
        {
            Vector3[] cs = new Vector3[] {
                screen.LeftTop * viewScale,
                screen.LeftBottom * viewScale,
                screen.RightBottom * viewScale,
                screen.RightTop * viewScale,
            };
            for (int i = 0; i < cs.Length; i++)
            {
                cs[i] = transform.localToWorldMatrix.MultiplyPoint(cs[i]);
            }
            return cs;
        }

        /// <summary>
        /// 调整相机位置以适应视口。
        /// </summary>
        void AdjustCameraPosition()
        {
            if (stereoCamera == null) return;
            stereoCamera.transform.localPosition =
                -StereoCamera.DefaultDistance * Vector3.forward * viewScale;
        }

        /// <summary>
        /// 设置工作模式
        /// </summary>
        /// <param name="sbs">是否左右显示</param>
        internal void SetWorkMode(bool sbs)
        {
            if (!Application.isPlaying) return;

            // 调整输入
#if UNITY_2022_3_OR_NEWER
            var inputModule = FindFirstObjectByType<KmaxInputModule>();
#else
            var inputModule = FindObjectOfType<KmaxInputModule>();
#endif
            if (inputModule != null) // 单目则不需要改写鼠标位置
                inputModule.MouseOverride = sbs && !KmaxNative.UsingStereoscopic;

            // 改渲染方式
            if (stereoCamera != null && stereoCamera is VRRenderer renderer)
            {
                // 使用图形接口时使用固定的相机渲染模式
                if (KmaxNative.UsingStereoscopic)
                {
                    renderer.Stereoscopic();
                    return;
                }

                if (sbs) renderer.SideBySide();
                else renderer.Mono();
            }
        }

        /// <summary>
        /// 手动设置显示模式
        /// </summary>
        /// <param name="stereo">是否立体显示</param>
        public static bool ManuallySetDisplayMode(bool stereo)
        {
            if (rig == null)
            {
                Debug.LogError("XRRig not initialized");
                return false;
            }
            var tracker = rig.stereoCamera ? rig.stereoCamera.GetComponent<HeadTracker>() : null;
            if (tracker != null)
            {
                if (stereo) tracker.StartTracking();
                else tracker.StopTracking();
            }
            MonoDisplayMode = !stereo;

            return true;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (alwaysShowGizmos)
            {
                DrawVisualScreen();
                DrawFrustum();
            }
        }

        void OnDrawGizmosSelected()
        {
            if (!alwaysShowGizmos)
            {
                DrawVisualScreen();
                DrawFrustum();
            }
        }

        private readonly Color color_screen = new Color(1f, 1f, 1f, 0f);
        private readonly Color color_screen_frame = new Color(0f, 1f, 0f, 0.5f);
        void DrawVisualScreen()
        {
            var cs = GetScreenCorners();
            if (cs == null) return;
            Handles.DrawSolidRectangleWithOutline(cs, color_screen, color_screen_frame);
            Handles.Label(cs[0], nameof(VirtualScreen));
        }

        private readonly Rect view_port = new Rect(0, 0, 1, 1);
        private readonly Vector3[] near = new Vector3[4];
        private readonly Vector3[] far = new Vector3[4];
        void DrawFrustum()
        {
            if (stereoCamera == null) return;

            // calculate by camera api
            var cam = stereoCamera.CenterCamera;
            if (cam == null) cam = stereoCamera.GetComponent<Camera>();
            if (!Application.isPlaying) stereoCamera.Converge();
            cam.CalculateFrustumCorners(view_port, 0.37f * viewScale, Camera.MonoOrStereoscopicEye.Mono, near);
            cam.CalculateFrustumCorners(view_port, 0.8f * viewScale, Camera.MonoOrStereoscopicEye.Mono, far);

            Handles.matrix = stereoCamera.transform.localToWorldMatrix;
            void DrawComfortZone(Vector3[] startCorners, Vector3[] endCorners)
            {
                var lineColor = new Color(1f, 1f, 1f, 0.5f);
                Handles.DrawSolidRectangleWithOutline(startCorners, Color.clear, lineColor);
                Handles.DrawSolidRectangleWithOutline(endCorners, Color.clear, lineColor);
                Handles.color = lineColor;
                for (int i = 0; i < startCorners.Length; ++i)
                {
                    Handles.DrawLine(startCorners[i], endCorners[i]);
                }
            }
            DrawComfortZone(near, far);
        }

#endif

    }
}
