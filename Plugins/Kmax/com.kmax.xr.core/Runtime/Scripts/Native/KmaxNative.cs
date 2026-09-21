using System;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace KmaxXR
{
    public static partial class KmaxNative
    {
        /// <summary>
        /// 设备端显示模式
        /// </summary>
        public enum DisplayMode
        {
            Mono = 0,
            Stereoscopic = 1 << 0,
            SideBySide = 1 << 1,
            Autostereoscopy = 1 << 2,
        }

        private static DisplayMode displayMode = DisplayMode.Mono;

        /// <summary>
        /// 设备主要的/建议的显示模式，由设备支持的模式和显示配置共同决定。
        /// </summary>
        public static DisplayMode PrimaryDisplayMode => displayMode;

        /// <summary>
        /// 是否采用图形接口立体显示
        /// </summary>
        public static bool UsingStereoscopic => displayMode == DisplayMode.Stereoscopic;

        /// <summary>
        /// 追踪是否开启
        /// </summary>
        private static bool trackingState = false;

        /// <summary>
        /// 是否立体显示，屏幕硬件的显示状态
        /// </summary>
        private static bool display3d = false;

        /// <summary>
        /// 初始化，检查设备确定支持的显示模式。
        /// </summary>
        /// <returns></returns>
        internal static bool Initialize()
        {
#if UNITY_STANDALONE
            InitializeAndDeterminDisplayMode();
#elif UNITY_ANDROID && !UNITY_EDITOR
            // 如果无法获取到设备ID则使用单目渲染
            displayMode = string.IsNullOrEmpty(DeviceId) ? DisplayMode.Mono : DisplayMode.SideBySide;
#endif
            Log($"Use DisplayMode {displayMode}.");
            return true;
        }

        /// <summary>
        /// 设置追踪状态及设备显示模式
        /// </summary>
        /// <param name="enable">开启/关闭追踪</param>
        /// <param name="sbs">是否切换成立体显示模式</param>
        internal static void SetTracking(bool enable, bool sbs)
        {
            if (enable != trackingState)
            {
                Log(string.Format("{0} tracking, display {1}", enable ? "start" : "stop", sbs ? "3d" : "2d"));
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            if (trackingState == enable) { return; }
            trackingState = enable;
            using (AndroidJavaClass jc = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject jo = jc.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaClass jutil = new AndroidJavaClass("com.kmax.track_conn.AidlConn"))
            {
                //jutil.CallStatic("validate", jo);
                if (enable) jutil.CallStatic("Open", jo, sbs);
                else jutil.CallStatic("Close", jo);
            }
#elif UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
            if (trackingState != enable)
            {
                trackingState = enable;
                kxrSetTracking(enable ? 1 : 0);
            }
            // 未使用软件接口的立体显示则手动切换显示模式
            if (!UsingStereoscopic && display3d != sbs)
            {
                display3d = sbs;
#if !UNITY_EDITOR
                kxrSetDisplayMode(sbs ? 1 : 0);
#endif
            }
#endif
        }

        /// <summary>
        /// 获取追踪及显示状态
        /// </summary>
        /// <returns>追踪是否开启，是否立体显示</returns>
        public static (bool, bool) GetTracking()
        {
            return (trackingState, display3d);
        }

        /// <summary>
        /// 设备ID，仅安卓平台有效
        /// </summary>
        public static string DeviceId
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                using (AndroidJavaClass jc = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject jo = jc.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaClass jutil = new AndroidJavaClass("com.kmax.track_conn.AidlConn"))
                {
                    return jutil.CallStatic<string>("getDeviceId", jo);
                }
#else
                Debug.LogWarning("DeviceId is not support on this platform.");
                return string.Empty;
#endif
            }
        }

        public static Version SDKVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

        internal static void Log(string message)
        {
            Debug.Log("<color=green>KmaxXR </color>" + message);
        }

        public const int HighFPS = 120;
        public const int LowFPS = 60;
        public static bool EnableHighFPS
        {
            set
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                Application.targetFrameRate = value ? HighFPS : LowFPS;
#endif
            }
        }

        /// <summary>
        /// 获取数字中的第一个标志位，转换成枚举类型
        /// </summary>
        /// <typeparam name="T">枚举类型</typeparam>
        /// <param name="numericValue">数字枚举</param>
        /// <returns>第一个标志位</returns>
        public static T GetFirstFlag<T>(int numericValue) where T : Enum
        {
            if (numericValue == 0) return default;
            // 找到最低位的标志
            int firstFlag = numericValue & ~(numericValue - 1);

            // 转换回枚举类型
            return (T)Enum.ToObject(typeof(T), firstFlag);
        }
    }
}
