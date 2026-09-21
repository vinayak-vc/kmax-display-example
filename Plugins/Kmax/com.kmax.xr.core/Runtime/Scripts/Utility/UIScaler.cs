using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KmaxXR
{
    /// <summary>
    /// 画布缩放器
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class UIScaler : MonoBehaviour
    {
        [Header("Scale Mode: ScaleWithScreenSize")]
        [SerializeField, Tooltip("Reference size.\n参考分辨率。")]
        protected Vector2Int resolution = new Vector2Int(1920, 1080);

        [SerializeField, Range(0, 1)]
        protected float matchWidthOrHeight = 0;

        [SerializeField, Tooltip("Sync transform per frame.\n每帧同步虚拟屏幕位置姿态。")]
        bool syncAlways = true;

        private Canvas canvas;
        /// <summary>
        /// 当前对象上的画布组件
        /// </summary>
        public Canvas ThisCanvas
        {
            get
            {
                if (canvas == null) canvas = GetComponent<Canvas>();
                return canvas;
            }
        }

        /// <summary>
        /// 画布分辨率
        /// </summary>
        public Vector2Int Resolution
        {
            get => resolution;
            set
            {
                resolution = value;
                AutoFix();
            }
        }

        /// <summary>
        /// 是否每帧跟随屏幕位置旋转
        /// </summary>
        public bool SyncAlways { get => syncAlways; set => syncAlways = value; }

        void OnValidate()
        {
            // 分辨率不合法时，使用默认值
            if (resolution.x <= 0 || resolution.y <= 0)
            {
                resolution = new Vector2Int(1920, 1080);
            }
        }

        void Start()
        {
            AutoFix();
        }

        private void OnEnable()
        {
            Canvas.willRenderCanvases += UpdatePoseAndSize;
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= UpdatePoseAndSize;
        }

        void UpdatePoseAndSize()
        {
            if (syncAlways) AutoFix();
        }

        [ContextMenu("AutoFix")]
        public void AutoFix()
        {
            FixSize(XRRig.ViewSize);
            FixPose(XRRig.ScreenTrans);
        }

        public void FixSize(Vector2 size)
        {
            RectTransform rt = transform as RectTransform;
            OnValidate();
            // 单眼画面时，使用缩放比例
            var canvasSize = XRRig.MonoDisplayMode && Application.isPlaying ?// 编辑器中屏幕大小不一定是Game视图大小
                CaculateScaledSize() : resolution;
            var scacleX = size.x / canvasSize.x;
            var scacleY = size.y / canvasSize.y;
            var scaleValue = Mathf.Min(scacleX, scacleY);
            rt.localScale = new Vector3(scaleValue, scaleValue, scaleValue);
            rt.sizeDelta = canvasSize;
        }

        /// <summary>
        /// 计算缩放后的画布大小
        /// </summary>
        /// <returns>画布大小</returns>
        Vector2 CaculateScaledSize()
        {
            //var displaySize = ThisCanvas.renderingDisplaySize;// 在URP中有问题
            var displaySize = new Vector2(Screen.width, Screen.height);

            // 计算宽度和高度比例
            float logWidth = displaySize.x / resolution.x;
            float logHeight = displaySize.y / resolution.y;

            // 根据 matchWidthOrHeight 计算缩放比例
            float scale = logWidth * (1 - matchWidthOrHeight) + logHeight * matchWidthOrHeight;
            //Debug.Log($"Scale: {scale}, DisplaySize: {displaySize}, ScaledSize: {displaySize/scale}");
            return displaySize / scale;
        }

        public void FixPose(Pose p)
        {
            transform.position = p.position;
            transform.rotation = p.rotation;
        }

        public void FixPose(Transform t)
        {
            transform.position = t.position;
            transform.rotation = t.rotation;
        }
    }
}
