using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using System.Text;

namespace KmaxXR
{
    /// <summary>
    /// 头部/眼部追踪器
    /// </summary>
    [DefaultExecutionOrder(ScriptPriority)]
    public class HeadTracker : BaseTracker, IEyeTracker
    {
        /// <summary>
        /// 脚本执行次序
        /// </summary>
        const int ScriptPriority = XRRig.ScriptPriority + 10;
        /// <summary>
        /// 超过3秒没有检测到眼部将触发眼部位置回归及眼部丢失事件。
        /// </summary>
        [SerializeField, Range(0, 60)]
        private float WaitForEyeScends = 3f;
        /// <summary>
        /// 眼部位置回归动作的间隔，如果值小于零则不会触发回归动作。
        /// </summary>
        [SerializeField, Tooltip("If you set this value to a number less than zero, you can freely control the position of the object in the editor.")]
        private float TransitionDuration = 1f;
        /// <summary>
        /// 是否要在对象销毁时停止追踪(不建议，在某些平台可能无效)。
        /// 注意，不建议在立体场景切换到立体场景时开启这个选项。
        /// </summary>
        [Tooltip("Enabling this option will cause tracking and stereoscopic display to turn off when switching scenes.")]
        public bool StopTrackingOnDestroy = false;

        private EyePose _eyePose;
        /// <summary>
        /// 眼部位置及姿态
        /// </summary>
        public EyePose eyePose => _eyePose;
        public bool EyeVisible => _eyePose.visible;

        private Transform eyeCenter, eyeLeft, eyeRight;
        private StereoCamera _camera;
        /// <summary>
        /// 眼镜追踪状态变化事件。
        /// </summary>
        public TrackingStatusChangeEvent OnEyeStatusChanged;

        readonly Vector3 defaultPos = -StereoCamera.DefaultDistance * Vector3.forward;

        protected virtual void Start()
        {
            _eyePose.head = defaultPos;
            //_eyePose.left = leftVec;
            //_eyePose.right = rightVec;

            PNClient.Start();
            StartTracking();
        }

        private float loseEyeTimer = 0f;
        void Update()
        {
            UpdateTargetPose();
            UpdateScreenResolution();
            _camera?.Converge();
        }

        void OnDestroy()
        {
            // 如果你需要在普通场景和立体场景之间来回切换
            // 你可以把这个属性设置成true
            if (StopTrackingOnDestroy) StopTracking();
        }

        /// <summary>
        /// 更新目标的姿态
        /// </summary>
        protected virtual void UpdateTargetPose()
        {
            if (eyeCenter == null) { return; }
            var vscale = XRRig.ViewScale;
            if (_eyePose.visible)
            {
                eyeCenter.localPosition = _eyePose.head * vscale;
                // 单眼模式使用默认位置，忽略视差
                if (XRRig.MonoDisplayMode)
                {
                    eyeLeft.localPosition = eyeRight.localPosition = Vector3.zero;
                }
                else
                {
                    eyeLeft.localPosition = _eyePose.left * vscale;
                    eyeRight.localPosition = _eyePose.right * vscale;
                }
                loseEyeTimer = WaitForEyeScends;
                UpdateTrackingStatus(TrackingStatus.Detected);
            }
            else
            {
                loseEyeTimer -= Time.deltaTime;
                if (loseEyeTimer <= 0)
                {
                    if (TransitionDuration >= 0)
                    {
                        float f = TransitionDuration == 0 || loseEyeTimer < -TransitionDuration ?
                            1f : AnimationCurveValue(-loseEyeTimer / TransitionDuration);
                        eyeCenter.localPosition = Vector3.Lerp(_eyePose.head * vscale, defaultPos * vscale, f);
                        eyeLeft.localPosition = Vector3.Lerp(_eyePose.left * vscale, Vector3.zero, f);
                        eyeRight.localPosition = Vector3.Lerp(_eyePose.right * vscale, Vector3.zero, f);
                    }
                    UpdateTrackingStatus(TrackingStatus.Missing);
                }
            }
        }

        protected virtual void UpdateScreenResolution()
        {
            if (eyeCenter == null) { return; }
            // 调整屏幕
            if (XRRig.MonoDisplayMode)// 单目则使用屏幕原始分辨率
            {
                XRRig.Screen.CalculateRect(true);
            }
        }

        protected virtual float AnimationCurveValue(float t)
        {
            t = Mathf.Clamp01(t);
            return (Mathf.Sin(Mathf.PI*(t-0.5f)) + 1)/2;
        }

        /// <summary>
        /// 开始追踪
        /// </summary>
        public virtual void StartTracking()
        {
            KmaxNative.SetTracking(true, true);
        }

        /// <summary>
        /// 开始追踪并指定显示模式
        /// </summary>
        /// <param name="sbs">是否左右</param>
        public virtual void StartTracking(bool sbs)
        {
            KmaxNative.SetTracking(true, sbs);
        }

        /// <summary>
        /// 停止追踪
        /// </summary>
        public virtual void StopTracking()
        {
            KmaxNative.SetTracking(false, false);
        }

        protected bool isPaused = false;

        void OnApplicationFocus(bool hasFocus)
        {
            isPaused = !hasFocus;
            if (!isPaused)
            {
                // Debug.Log($"On focus: mono {XRRig.MonoDisplayMode}");
                // 使用当前的状态恢复
                var isSBS = !XRRig.MonoDisplayMode;
                StartTracking(isSBS);
            }
            else
            {
                // Debug.Log($"On lost focus: mono {XRRig.MonoDisplayMode}");
                StopTracking();
            }
        }

        void OnApplicationPause(bool pauseStatus)
        {
            isPaused = pauseStatus;
            if (!isPaused)
            {
                StartTracking();
            }
            else
            {
                StopTracking();
            }
        }

        void OnApplicationQuit()
        {
            StopTracking();
            PNClient.Stop();
        }

        /// <summary>
        /// 更新追踪状态
        /// </summary>
        /// <param name="s">状态</param>
        private void UpdateTrackingStatus(TrackingStatus s)
        {
            if (status != s)
            {
                status = s;
                OnEyeStatusChanged?.Invoke(status);
                //Debug.Log($"OnGlassStatusChanged:{status}");
            }
        }

        internal override void ParseTrackerData(TrackerData data)
        {
            base.ParseTrackerData(data);
            // 眼部追踪
            _eyePose.visible = data.eyeVisible > 0;
            if (_eyePose.visible)
            {
                _eyePose.head = data.eye.pos;
                var headRot = data.eye.rot;
                var mr = Matrix4x4.Rotate(headRot.normalized);
                _eyePose.look = headRot.normalized;
                _eyePose.left = mr.MultiplyPoint(leftVec * PNClient.DataFactor);
                _eyePose.right = mr.MultiplyPoint(rightVec * PNClient.DataFactor);
            }
        }

        internal void SetTrackTarget(Transform c, Transform l, Transform r)
        {
            eyeCenter = c;
            eyeLeft = l;
            eyeRight = r;
            _camera = c.GetComponent<StereoCamera>();
        }

        StringBuilder sb = new StringBuilder();
        public override string ToString()
        {
            sb.Clear();
            sb.AppendLine($"<b>{GetType().Name}</b>");
            sb.AppendLine($"<color=yellow>[眼]</color>");
            sb.AppendLine($"[visible]: {eyePose.visible}");
            sb.AppendLine($"[pos]: {eyePose.head}");
            sb.AppendLine($"[rot]: {eyePose.look.eulerAngles}");
            return sb.ToString();
        }
    }
}
