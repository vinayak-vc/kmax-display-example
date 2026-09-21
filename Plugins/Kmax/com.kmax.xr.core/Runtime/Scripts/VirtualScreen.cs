using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KmaxXR
{
    [System.Serializable]
    public class VirtualScreen
    {
        public enum ScreenType { Screen15_6, Screen27, Screen24 }
        [SerializeField, Tooltip("首选的屏幕尺寸，通常保持同一个尺寸为基准保证各平台视觉一致性。")]
        ScreenType screenType;
        
        public float ScreenSizeInch
        {
            get
            {
                switch (screenType)
                {
                    case ScreenType.Screen15_6: return 15.6f;
                    case ScreenType.Screen24: return 24f;
                    case ScreenType.Screen27: return 27f;
                }
                return 15.6f;
            }
        }
        [SerializeField, Tooltip("屏幕宽高比，仅在双目渲染下使用，单目渲染将使用屏幕的原始分辨率。")]
        private Vector2Int ratio = new Vector2Int(16, 9);
        const float INCH2M = 0.0254f;
        float left, right, top, bottom;
        /// <summary>
        /// 屏幕宽高，由 ScreenType 控制，不可以直接修改。
        /// </summary>
        [SerializeField, HideInInspector]
        float width, height;
        public float Width => width;
        public float Height => height;
        public Vector2 Size => new Vector2(width, height);

        public Vector3 LeftTop => new Vector3(left, top, 0);
        public Vector3 RightTop => new Vector3(right, top, 0);
        public Vector3 LeftBottom => new Vector3(left, bottom, 0);
        public Vector3 RightBottom => new Vector3(right, bottom, 0);

        public VirtualScreen()
        {
            CalculateRect();
        }

        internal void CalculateRect(bool useScreenResolution = false)
        {
            float size = ScreenSizeInch * INCH2M;
            // 如果使用屏幕本身的分辨率则按分辨率计算宽高比
            var widthRatio = useScreenResolution ? Screen.width : ratio.x;
            var heightRatio = useScreenResolution ? Screen.height : ratio.y;
            float sizeRatio = Mathf.Sqrt(widthRatio * widthRatio + heightRatio * heightRatio);
            width = size * widthRatio / sizeRatio;
            height = size * heightRatio / sizeRatio;
            right = width / 2f;
            left = -right;
            top = height / 2f;
            bottom = -top;
        }

        public override string ToString()
        {
            return $"{nameof(VirtualScreen)}({ScreenSizeInch}): ({width}, {height})";
        }
    }

}