using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KmaxXR
{
    /// <summary>
    /// 改写默认的物理射线检测，添加3D光标显示。
    /// </summary>
    public class KmaxPhysicRaycaster : PhysicsRaycaster
    {
        [SerializeField]
        private bool autoHideCursor = true;
        [SerializeField]
        private Transform visualCursor;
        [SerializeField]
        private float defaultScale = 0.02f;
        [SerializeField]
        private float defaultDistance = 0.5f;

        private RaycastHit hit;
        RaycastHit[] m_Hits;
        /// <summary>
        /// 是否命中任何对象
        /// </summary>
        public bool IsHit => m_Hits != null && m_Hits.Length > 0;
        /// <summary>
        /// 命中结果
        /// </summary>
        public RaycastHit Hit => hit;
        public float MaxDistance => eventCamera.farClipPlane;

        Vector2 mousePosition;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (autoHideCursor)
            {
                Cursor.visible = false;
                if (visualCursor)
                {
                    visualCursor.gameObject.SetActive(true);
                }
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (autoHideCursor)
            {
                Cursor.visible = true;
                if (visualCursor)
                {
                    visualCursor.gameObject.SetActive(false);
                }
            }
        }

        protected virtual void Update()
        {
            if (visualCursor)
            {
                Vector3 m = mousePosition;
                m.z = -eventCamera.transform.localPosition.z;
                var defaultPos = eventCamera.ScreenToWorldPoint(m);
                visualCursor.position = IsHit ? hit.point : defaultPos;
                visualCursor.rotation = IsHit ?
                    Quaternion.FromToRotation(Vector3.down, hit.normal) :
                    Quaternion.LookRotation(eventCamera.transform.forward, eventCamera.transform.up);
                // 保持光标大小不变
                visualCursor.localScale = Vector3.one * defaultScale / defaultDistance *
                    Vector3.Distance(visualCursor.position, eventCamera.transform.position);
            }
        }

        int Compare(RaycastHit x, RaycastHit y)
        {
            return x.distance.CompareTo(y.distance);
        }

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            Ray ray = new Ray();
            int displayIndex = 0;
            float distanceToClipPlane = 0;
            mousePosition = eventData.position;// 记录鼠标位置
            if (!ComputeRayAndDistance(eventData, ref ray, ref displayIndex, ref distanceToClipPlane))
                return;

            int hitCount = 0;

            if (m_MaxRayIntersections == 0)
            {
                m_Hits = Physics.RaycastAll(ray, distanceToClipPlane, m_EventMask);
                hitCount = m_Hits.Length;
            }

            if (hitCount != 0)
            {
                if (hitCount > 1)
                {
                    System.Array.Sort(m_Hits, Compare);
                }
                hit = m_Hits[0]; // 记录第一个命中

                for (int b = 0, bmax = hitCount; b < bmax; ++b)
                {
                    var result = new RaycastResult
                    {
                        gameObject = m_Hits[b].collider.gameObject,
                        module = this,
                        distance = m_Hits[b].distance,
                        worldPosition = m_Hits[b].point,
                        worldNormal = m_Hits[b].normal,
                        screenPosition = eventData.position,
                        displayIndex = displayIndex,
                        index = resultAppendList.Count,
                        sortingLayer = 0,
                        sortingOrder = 0
                    };
                    resultAppendList.Add(result);
                }
            }
        }

        private void OnDrawGizmos()
        {
            Ray ray = eventCamera.ScreenPointToRay(Input.mousePosition);
            if (IsHit)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(ray.origin, hit.point);
            }
            else
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(ray.origin, ray.origin + ray.direction * MaxDistance);
            }
        }
    }
}
