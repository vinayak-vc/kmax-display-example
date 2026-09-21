using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KmaxXR
{
    /// <summary>
    /// 抽象的指针基类
    /// </summary>
    public abstract class KmaxPointer : MonoBehaviour
    {
        #region 静态成员
        private readonly static List<KmaxPointer> pointers = new List<KmaxPointer>();

        /// <summary>
        /// 指针迭代器
        /// </summary>
        public static IEnumerable<KmaxPointer> Pointers => pointers;
        /// <summary>
        /// 通过Id获取指针
        /// </summary>
        /// <param name="id">指针Id</param>
        /// <returns>指针</returns>
        public static KmaxPointer PointerById(int id) => pointers.Find(p => p.Contains(id));
        /// <summary>
        /// 场景中是否存在指针
        /// </summary>
        public static bool Enable => pointers.Count > 0;

        /// <summary>
        /// 按钮是否处于按下状态
        /// </summary>
        /// <param name="id">指针id</param>
        /// <param name="button">按钮id</param>
        /// <returns>状态</returns>
        public static bool GetPointerButton(int id, int button)
        {
            var pointer = PointerById(id);
            return pointer && pointer.GetButton(button);
        }

        /// <summary>
        /// 当前帧按钮是否按下
        /// </summary>
        /// <param name="id">指针id</param>
        /// <param name="button">按钮id</param>
        /// <returns>状态</returns>
        public static bool GetPointerButtonDown(int id, int button)
        {
            var pointer = PointerById(id);
            return pointer && pointer.GetButtonDown(button);
        }

        /// <summary>
        /// 当前帧按钮是否抬起
        /// </summary>
        /// <param name="id">指针id</param>
        /// <param name="button">按钮id</param>
        /// <returns>状态</returns>
        public static bool GetPointerButtonUp(int id, int button)
        {
            var pointer = PointerById(id);
            return pointer && pointer.GetButtonUp(button);
        }

        /// <summary>
        /// 当前帧按钮是否抬起
        /// </summary>
        /// <param name="id">指针id</param>
        /// <param name="button">按钮id</param>
        /// <returns>状态</returns>
        public static Vector2 GetPointerAxis(int id)
        {
            var pointer = PointerById(id);
            return pointer ? pointer.GetAxis() : default;
        }
        #endregion

        [SerializeField]
        protected Camera eventCamera;
        /// <summary>
        /// 事件相机
        /// </summary>
        public Camera EventCamera => eventCamera;
        /// <summary>
        /// 基础射线检测器
        /// </summary>
        protected BaseRaycaster raycaster;
        /// <summary>
        /// 指针唯一Id
        /// </summary>
        public abstract int Id { get; }
        /// <summary>
        /// 指针所在屏幕位置
        /// </summary>
        public abstract Vector2 ScreenPosition { get; }
        /// <summary>
        /// 射线发射端位置及旋转
        /// </summary>
        public abstract Pose StartpointPose { get; }
        /// <summary>
        /// 射线命中端位置及旋转
        /// </summary>
        public abstract Pose EndpointPose { get; }
        /// <summary>
        /// 是否可见
        /// </summary>
        public abstract bool Visible { get; }
        /// <summary>
        /// 是否命中3d物体
        /// </summary>
        public virtual bool Hit3D { get; }
        /// <summary>
        /// 当前抓取的物体
        /// </summary>
        public virtual GameObject GrabObject { get; set; }

        /// <summary>
        /// 根据按钮类别获取按钮状态，子类通过改写此方法实现按钮映射。
        /// </summary>
        /// <param name="button">按钮类别</param>
        /// <returns>按钮状态</returns>
        public virtual PointerEventData.FramePressState StateOf(PointerEventData.InputButton button)
        {
            switch (button)
            {
                case PointerEventData.InputButton.Left:
                case PointerEventData.InputButton.Right:
                case PointerEventData.InputButton.Middle:
                default:
                    return PointerEventData.FramePressState.NotChanged;
            }
        }

        public abstract bool GetButton(int button);
        public abstract bool GetButtonDown(int button);
        public abstract bool GetButtonUp(int button);
        public virtual Vector2 GetAxis() { return Vector2.zero; }

        /// <summary>
        /// 是否包含指针
        /// </summary>
        /// <param name="pointerId">指针标识</param>
        /// <returns>是否包含该指针</returns>
        public virtual bool Contains(int pointerId)
        {
            return pointerId >= Id && pointerId <= Id + 2;
        }

        virtual protected void OnEnable()
        {
            pointers.Add(this);
            if (eventCamera == null) eventCamera = Camera.main;
            // 获取射线检测器，不是必须的，因为没有继承 BaseRaycaster，所以使用相机上的检测器
            if (eventCamera) raycaster = eventCamera.GetComponent<BaseRaycaster>();
        }
        virtual protected void OnDisable()
        {
            pointers.Remove(this);
        }

        public abstract void UpdateState();
        [System.NonSerialized]
        protected List<RaycastResult> raycastResultCache = new List<RaycastResult>();
        public abstract void Raycast(PointerEventData eventData);

        /// <summary>
        /// 射线检测
        /// </summary>
        /// <param name="ray">射线</param>
        /// <param name="rayLength">射线长度</param>
        /// <param name="layer">层级</param>
        public virtual void Raycast(Ray ray, float rayLength, int layer)
        {
            // 清空缓存
            raycastResultCache.Clear();
            // 3D
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, rayLength, layer))
            {
                var result = new RaycastResult
                {
                    gameObject = hit.collider.gameObject,
                    module = raycaster, // 便于调试
                    distance = hit.distance,
                    worldPosition = hit.point,
                    worldNormal = hit.normal,
                    screenPosition = eventCamera.WorldToScreenPoint(hit.point),
                    index = 0,
                    sortingLayer = 0,
                    sortingOrder = 0
                };
                raycastResultCache.Add(result);
            }
            // UI
            KmaxUIRaycaster.RaycastAll(ray, raycastResultCache, rayLength, layer);
        }

        private static Comparison<RaycastResult> s_RaycastComparer;

        /// <summary>
        /// 查找第一个射线命中结果，将结果按深度，优先级等排序。
        /// </summary>
        /// <param name="candidates">命中结果列表</param>
        /// <returns>第一个命中结果</returns>
        internal static RaycastResult FindFirstRaycast(List<RaycastResult> candidates)
        {
            // 排序
            if (s_RaycastComparer == null)
            {
                var m = typeof(EventSystem).GetMethod("RaycastComparer",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (m != null)
                {
                    s_RaycastComparer = Delegate.CreateDelegate(typeof(Comparison<RaycastResult>), m) as Comparison<RaycastResult>;
                }
                else
                {
                    Debug.LogWarning("RaycastComparer is null.");

                    // Sort the results by depth.
                    candidates.Sort((x, y) => y.depth.CompareTo(x.depth));

                    // Sort the results by sortingOrder.
                    candidates.Sort((x, y) => y.sortingOrder.CompareTo(x.sortingOrder));
                }
            }
            else
            {
                candidates.Sort(s_RaycastComparer);
            }
            for (var i = 0; i < candidates.Count; ++i)
            {
                if (candidates[i].gameObject == null)
                    continue;

                return candidates[i];
            }
            return new RaycastResult();
        }
    }
}
