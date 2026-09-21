using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace KmaxXR
{
    /// <summary>
    /// 将画面渲染成左右格式
    /// render to side by side
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class VRRenderer : StereoCamera
    {
        [SerializeField]
        Camera leftEye, rightEye;
        Camera centerEye;

        /// <summary>
        /// 单目显示时是否使用主相机
        /// </summary>
        protected bool mainCameraForMono = false;

        public override Camera CenterCamera => centerEye;
        protected override Camera LeftCamera => leftEye;
        protected override Camera RightCamera => rightEye;

        protected RenderTexture leftTex, rightTex;

        void Awake()
        {
            centerEye = GetComponent<Camera>();
            if (leftEye == null || rightEye == null)
            {
                leftEye = AddRenderCamera("l-eye");
                rightEye = AddRenderCamera("r-eye");
            }
            //ViewSplit();

            // set tracking target
            var tracker = GetComponent<HeadTracker>();
            tracker?.SetTrackTarget(centerEye.transform, leftEye.transform, rightEye.transform);
        }

        /// <summary>
        /// 左右显示
        /// </summary>
        public virtual void SideBySide()
        {
            leftEye.rect = new Rect(0, 0, 0.5f, 1);
            rightEye.rect = new Rect(1-0.5f, 0, 0.5f, 1);
            if (!XRRig.IsSRP)
            {
                leftEye.stereoTargetEye = StereoTargetEyeMask.Left;
                rightEye.stereoTargetEye = StereoTargetEyeMask.Right;
            }
            centerEye.enabled = false;
            leftEye.enabled = rightEye.enabled = true;
        }

        /// <summary>
        /// 单目显示
        /// </summary>
        public virtual void Mono()
        {
            leftEye.rect = rightEye.rect = new Rect(0, 0, 1, 1);
            if (mainCameraForMono)
            {
                centerEye.enabled = true;
                leftEye.enabled = rightEye.enabled = false;
            }
            else
            {
                centerEye.enabled = false;
                leftEye.enabled = true;
                rightEye.enabled = false;
            }
        }

        /// <summary>
        /// 立体显示
        /// </summary>
        public virtual void Stereoscopic()
        {
            leftEye.rect = new Rect(0, 0, 1, 1);
            rightEye.rect = new Rect(0, 0, 1, 1);
            if (!XRRig.IsSRP)
            {
                leftEye.stereoTargetEye = StereoTargetEyeMask.Left;
                rightEye.stereoTargetEye = StereoTargetEyeMask.Right;
            }
            centerEye.enabled = false;
            leftEye.enabled = true;
            rightEye.enabled = true;
        }

        internal override void Validate()
        {
            centerEye = GetComponent<Camera>();
            Converge();
        }

        /// <summary>
        /// 渲染到纹理
        /// </summary>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        /// <param name="format">格式</param>
        /// <returns>纹理数组</returns>
        public virtual Texture[] RenderToTexture(int width, int height, out RenderTextureFormat format)
        {
            if (leftTex != null || rightTex != null)
            {
                RenderTexture.ReleaseTemporary(leftTex);
                RenderTexture.ReleaseTemporary(rightTex);
            }
            bool hdr = leftEye.allowHDR || rightEye.allowHDR;
            format = hdr ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.ARGB32;
            leftEye.targetTexture = leftTex = RenderTexture.GetTemporary(width, height, 24, format);
            rightEye.targetTexture = rightTex = RenderTexture.GetTemporary(width, height, 24, format);
            leftEye.rect = rightEye.rect = new Rect(0, 0, 1, 1);
            return new Texture[] { leftTex, rightTex };
        }

        protected Camera AddRenderCamera(string name)
        {
            var rc = new GameObject(name);
            rc.transform.SetParent(transform);
            var c = rc.AddComponent<Camera>();
            c.CopyFrom(centerEye);
            return c;
        }

    }
}
