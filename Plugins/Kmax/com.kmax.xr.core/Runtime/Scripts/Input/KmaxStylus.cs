using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KmaxXR
{
    public class KmaxStylus : KmaxPointer, IVibrate
    {
        /// <summary>
        /// 唯一标识，用于区分 KmaxPointer。
        /// <code>
        /// var theStylus = KmaxPointer.PointerById(KmaxStylus.UniqueId);
        /// </code>
        /// </summary>
        public const int UniqueId = 1000;

        [SerializeField]
        protected Transform stylus;
        protected IPointerVisualize pointerVisualize;

        [SerializeField]
        float rayLength = 0.5f;

        /// <summary>
        /// 射线默认长度，即未命中任何物体时的长度。
        /// </summary>
        public float RayLength { get => rayLength; set => rayLength = value; }

        [SerializeField, Tooltip("平滑的末端端点")]
        protected bool smoothEndPoint = false;
        public bool SmoothEndPoint { get => smoothEndPoint; set => smoothEndPoint = value; }
        [Range(0, 0.1f), Tooltip("末端端点渐变间隔")]
        public float EndPointSmoothTime = 0.02f;
        [Tooltip("参与检测的层")]
        public LayerMask layer;
        [Tooltip("主键，直接参与交互。")]
        public PointerEventData.InputButton PrimaryKey = PointerEventData.InputButton.Middle;

        public const int StylusButtnLeft = UniqueId;
        public const int StylusButtnRigth = UniqueId + 1;
        public const int StylusButtnCenter = UniqueId + 2;

        public override int Id => UniqueId;

        protected IStylus pen;
        protected readonly bool[] curState = new bool[] { false, false, false };
        protected readonly bool[] lastState = new bool[] { false, false, false };

        /// <summary>
        /// 有任意按钮按下
        /// </summary>
        public bool AnyButtonPressed => curState[0] || curState[1] || curState[2];

        [System.Serializable]
        public class RaycastEvent : UnityEvent<GameObject> { }
        private GameObject _enteredObject, _exitedObject;

        /// <summary>
        /// 当射线移入某个物体时触发。
        /// Event dispatched when the pointer enters an object.
        /// </summary>
        [Header("Events")]
        [Tooltip("Event dispatched when the pointer enters an object.")]
        public RaycastEvent OnObjectEntered = new RaycastEvent();

        /// <summary>
        /// 当射线移出某个物体时触发。
        /// Event dispatched when the pointer exits an object.
        /// </summary>
        [Tooltip("Event dispatched when the pointer exits an object.")]
        public RaycastEvent OnObjectExited = new RaycastEvent();

        /// <summary>
        /// 当按钮按下时触发，按钮对应 StylusButtnLeft, StylusButtnRigth, StylusButtnCenter 其中一个。
        /// Event dispatched when a pointer button becomes pressed.
        /// </summary>
        [Tooltip("Event dispatched when a pointer button becomes pressed.")]
        public UnityEvent<int> OnButtonPressed = new UnityEvent<int>();

        /// <summary>
        /// 当按钮抬起时触发，按钮对应 StylusButtnLeft, StylusButtnRigth, StylusButtnCenter 其中一个。
        /// Event dispatched when a pointer button becomes released.
        /// </summary>
        [Tooltip("Event dispatched when a pointer button becomes released.")]
        public UnityEvent<int> OnButtonReleased = new UnityEvent<int>();

        /// <summary>
        /// 笔的可见性
        /// </summary>
        public override bool Visible => pen != null && pen.visible;
        public override bool Hit3D { get => pState.hit3D; }

        /// <summary>
        /// 射线笔状态类
        /// </summary>
        public struct PointerState
        {
            public int pressedButton, releasedButton;
            public bool pressing, dragging, drag3D;
            public RaycastResult result; // 当前帧
            public bool hit3D => result.module == raycaster;
            public bool hitSomething => result.gameObject;
            public RaycastResult resultCache; // 上一帧
            public BaseRaycaster raycaster;
            public GameObject CurrentHit => result.gameObject;

            public void Init(Camera eventCamera)
            {
                pressedButton = releasedButton = -1;
                raycaster = eventCamera.GetComponent<BaseRaycaster>();
            }

            /// <summary>
            /// 预先处理拖拽状态，在做射线检测时提前判定是否发生拖拽，用于处理临界状态。
            /// </summary>
            /// <remarks>
            /// PointerEventData 中的 dragging 状态变化发生在射线检测之后，无法及时用于处理射线端点位置，因此需要提前处理。
            /// </remarks>
            /// <param name="key">主键值</param>
            /// <param name="keyPressed">拖拽按钮是否按下</param>
            /// <returns>是否拖拽</returns>
            public bool PreProcessDragState(int key, bool keyPressed)
            {
                if (pressing && keyPressed)
                {
                    dragging = true;
                }
                if (pressedButton == key)
                {
                    pressing = true;
                    dragging = false;
                    drag3D = hit3D;
                }
                if (releasedButton == key)
                {
                    pressing = false;
                    dragging = false;
                }
                return dragging;
            }

            public override string ToString()
            {
                return $"CurrentHit: {CurrentHit}\npressing: {pressing}\ndraging: {dragging}\n{result}";
            }
        }

        /// <summary>
        /// 当前帧的状态
        /// </summary>
        protected PointerState pState;
        public PointerState pointerState => pState;

        /// <summary>
        /// 端点位置世界坐标
        /// </summary>
        public Vector3 PointerPosition { get; private set; }
        private Vector3 smoothPosition, _velocity;
        public GameObject CurrentHitObject => pState.CurrentHit;
        public override Vector2 ScreenPosition => eventCamera.WorldToScreenPoint(PointerPosition);

        /// <summary>
        /// 笔尖位置，上次按下时笔尖位置。
        /// </summary>
        private Vector3 startPointPosition, lastStartPointPosition;

        public override Pose StartpointPose => new Pose(stylus.position, stylus.rotation);
        public override Pose EndpointPose => new Pose(PointerPosition, stylus.rotation);

        protected virtual void Start()
        {
            pen = GetComponent<IStylus>();
            if (pen == null)
            {
                Debug.LogError($"Can not find {nameof(IStylus)} on this GameObject.");
            }
            PointerPosition = stylus.position + rayLength * stylus.forward;
            pointerVisualize = stylus.GetComponent<IPointerVisualize>();
            pointerVisualize?.InitVisualization(this);
            pState.Init(eventCamera);
        }

        protected virtual void Update()
        {
            if (eventCamera == null) return;
            // Send collision events.
            if (_exitedObject != null)
            {
                OnObjectExited.Invoke(_exitedObject);
            }

            if (_enteredObject != null)
            {
                OnObjectEntered.Invoke(_enteredObject);
            }

            for (int i = 0; i < curState.Length; i++)
            {
                if (lastState[i] != curState[i])
                {
                    if (curState[i])
                        OnButtonPressed.Invoke(UniqueId + i);
                    else
                        OnButtonReleased.Invoke(UniqueId + i);
                }
            }
        }

        public string[] CheckPropertyValid()
        {
            List<string> errors = new List<string>();
            if (eventCamera == null)
            {
                errors.Add("Property eventCamera can't be null.");
            }
            if (stylus == null || stylus.GetComponent<IPointerVisualize>() == null)
            {
                errors.Add(stylus ? $"There is no IPointerVisualize on the stylus {stylus}." : "Property stylus can't be null.");
            }
            return errors.ToArray();
        }

        public override PointerEventData.FramePressState StateOf(PointerEventData.InputButton button)
        {
            int i = (int)button;
            bool primaryIsLeft = PrimaryKey == PointerEventData.InputButton.Left;
            // 如果主键不是左键则两个按键的状态互换
            if (!primaryIsLeft)
            {
                if (i == 0) i = (int)PrimaryKey;
                else if (i == (int)PrimaryKey) i = 0;
            }
            return StateOfId(i);
        }

        private PointerEventData.FramePressState StateOfId(int i)
        {
            if (lastState[i] != curState[i])
            {
                return curState[i] ? PointerEventData.FramePressState.Pressed : PointerEventData.FramePressState.Released;
            }
            return PointerEventData.FramePressState.NotChanged;
        }

        public override bool GetButton(int button)
        {
            Debug.Assert(button >= 0 && button <= 2);
            return curState[button];
        }

        public override bool GetButtonDown(int button)
        {
            Debug.Assert(button >= 0 && button <= 2);
            return curState[button] && !lastState[button];
        }

        public override bool GetButtonUp(int button)
        {
            Debug.Assert(button >= 0 && button <= 2);
            return !curState[button] && lastState[button];
        }

        public override Vector2 GetAxis()
        {
            var delta = startPointPosition - lastStartPointPosition;
            //Debug.Log($"Stylus GetAxis: {startPointPosition} - {lastStartPointPosition} = {delta}");

            //var maxValue = Mathf.Min(Screen.width, Screen.height);
            var maxValue = rayLength/10;
            delta = Vector3.ClampMagnitude(delta, maxValue);
            delta /= maxValue/10;
            return delta;
        }

        public override void UpdateState()
        {
            pen.UpdatePose(stylus);
            pState.pressedButton = -1;
            pState.releasedButton = -1;
            for (int i = 0; i < curState.Length; i++)
            {
                lastState[i] = curState[i];
                curState[i] = pen.GetButton(i);
                if (curState[i] && !lastState[i])
                {
                    pState.pressedButton = i;
                }
                else if (lastState[i] && !curState[i])
                {
                    pState.releasedButton = i;
                }
            }
        }

        public override void Raycast(PointerEventData eventData)
        {
            // 缓存上一帧
            pState.resultCache = pState.result;

            raycastResultCache.Clear();
            Ray ray = new Ray(stylus.position, stylus.forward);
            // 3D
            RaycastHit hit;
            if (Physics.Raycast(ray, out hit, rayLength, layer))
            {
                pState.result = new RaycastResult
                {
                    gameObject = hit.collider.gameObject,
                    module = pState.raycaster,
                    distance = hit.distance,
                    worldPosition = hit.point,
                    worldNormal = hit.normal,
                    screenPosition = eventCamera.WorldToScreenPoint(hit.point),
                    index = 0,
                    sortingLayer = 0,
                    sortingOrder = 0
                };
                raycastResultCache.Add(pState.result);
            }
            // UI
            KmaxUIRaycaster.RaycastAll(ray, raycastResultCache, rayLength, layer);
            pState.result = FindFirstRaycast(raycastResultCache);

            // set event data
            eventData.pointerCurrentRaycast = pState.result;

            int key = (int)PrimaryKey;
            bool willDragging = pState.PreProcessDragState(key, curState[key]);
            // 更新屏幕位置及差值
            Vector2 curPosition = eventData.pointerCurrentRaycast.screenPosition;
            if (willDragging || eventData.dragging) // 避免跳变
            {
                var vPosition = stylus.position + eventData.pointerPressRaycast.distance * stylus.forward;
                curPosition = eventCamera.WorldToScreenPoint(vPosition);
            }
            eventData.delta = curPosition - eventData.position;
            eventData.position = curPosition;

            UpdatePointerPosition(eventData);
            ProcessCollisions();
            UpdateVisualization();
        }

        protected virtual void UpdatePointerPosition(PointerEventData eventData)
        {
            // 更新端点位置
            smoothPosition = PointerPosition;
            float d = pState.hitSomething ? eventData.pointerCurrentRaycast.distance : rayLength;
            //if (eventData.dragging && GrabObject != null)
            if ((pState.dragging && pState.drag3D) || (eventData.dragging && GrabObject != null))
                d = eventData.pointerPressRaycast.distance;
            PointerPosition = stylus.position + d * stylus.forward;

            if (smoothEndPoint)
            {
                PointerPosition = Vector3.SmoothDamp(
                    smoothPosition, PointerPosition, ref _velocity, EndPointSmoothTime);
                //PointerPosition = Vector3.Lerp(previousPosition, PointerPosition, 0.1f); // smooth
            }

            // 笔尖位置
            if (AnyButtonPressed)
            {
                lastStartPointPosition = startPointPosition;
            }
            startPointPosition = stylus.localPosition;
        }

        public virtual void UpdateVisualization()
        {
            pointerVisualize?.UpdateVisualization(this);
        }

        private void ProcessCollisions()
        {
            var curObj = CurrentHitObject;
            // Update the cached entered object.
            _enteredObject = null;

            if (curObj != null &&
                curObj != pState.resultCache.gameObject)
            {
                _enteredObject = curObj;
            }

            // Update the cached exited object.
            _exitedObject = null;

            if (pState.resultCache.gameObject != null &&
                pState.resultCache.gameObject != curObj)
            {
                _exitedObject = pState.resultCache.gameObject;
            }
        }

        /// <summary>
        /// 震动一次
        /// </summary>
        /// <param name="t">时长s</param>
        /// <param name="s">强度0-100</param>
        public void VibrationOnce(float t, int s)
        {
            pen?.Vibrate(Mathf.FloorToInt(t * 1000), s);
        }

        /// <summary>
        /// 开始震动
        /// </summary>
        /// <param name="intensity">强度0-100</param>
        public void StartVibration(int intensity)
        {
            pen?.Vibrate(-1, intensity);
        }

        /// <summary>
        /// 停止震动
        /// </summary>
        public void StopVibration()
        {
            pen?.Vibrate(0, 0);
        }
    }
}
