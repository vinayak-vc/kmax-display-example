using System.Collections.Generic;
using KmaxXR;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Keeps the world-space interface drawn and raycast in front of the 3D model, whatever the
    /// model is doing.
    ///
    /// Visually, every graphic under the canvas receives a material instance with depth testing
    /// set to <see cref="CompareFunction.Always"/> and a high render queue.
    /// For input raycasting (both mouse and Kmax 6-DOF stylus), the canvas is given an elevated
    /// <see cref="Canvas.sortingOrder"/> and a <see cref="KmaxUIRaycaster"/> so UI hits always sort
    /// ahead of 3D physics hits even when the 3D model pops out in front of the screen plane.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class UiAlwaysOnTop : MonoBehaviour {
        private static readonly int GuiZTestMode = Shader.PropertyToID("unity_GUIZTestMode");
        private static readonly int ZTestMode = Shader.PropertyToID("_ZTestMode");
        private static readonly List<RaycastResult> RaycastScratch = new List<RaycastResult>();
        private static PointerEventData _sharedPointerData;

        [SerializeField, Tooltip("Render queue the interface is pushed to. Above 3000 puts it after " +
            "the model's transparent passes, which matters for the order the blend happens in.")]
        private int renderQueue = 4000;
        [SerializeField, Tooltip("Canvas sorting order used so EventSystem and KmaxStylus raycasts " +
            "always prioritise UI elements over 3D colliders (which sit at sortingOrder 0).")]
        private int canvasSortingOrder = 100;
        [SerializeField, Tooltip("Re-apply whenever a child is enabled. Needed because the Back and " +
            "navigator buttons are switched off and on as the flow changes.")]
        private bool reapplyOnEnable = true;

        private bool _applied;

        private void Awake() {
            ConfigureCanvasRaycasting();
        }

        private void Start() {
            Apply();
        }

        private void OnEnable() {
            ConfigureCanvasRaycasting();
            if (reapplyOnEnable && _applied) {
                Apply();
            }
        }

        private void ConfigureCanvasRaycasting() {
            Canvas canvas = GetComponent<Canvas>();
            if (canvas != null) {
                canvas.overrideSorting = true;
                canvas.sortingOrder = canvasSortingOrder;
            }

            if (GetComponent<GraphicRaycaster>() == null) {
                gameObject.AddComponent<GraphicRaycaster>();
            }

            if (GetComponent<KmaxUIRaycaster>() == null) {
                gameObject.AddComponent<KmaxUIRaycaster>();
            }
        }

        /// <summary>
        /// True when the active pointer (mouse or Kmax stylus) is currently over a UI element on a
        /// Canvas. Used by 3D interactables and camera orbit controllers so clicking or dragging on
        /// UI never selects or drags the 3D model behind it.
        /// </summary>
        public static bool IsPointerOverUi(PointerEventData eventData = null) {
            if (eventData != null && eventData.pointerCurrentRaycast.gameObject != null) {
                if (eventData.pointerCurrentRaycast.gameObject.GetComponentInParent<Canvas>() != null) {
                    return true;
                }
            }

            KmaxStylus stylus = KmaxPointer.PointerById(KmaxStylus.UniqueId) as KmaxStylus;
            if (stylus != null && stylus.Visible) {
                GameObject stylusHit = stylus.CurrentHitObject;
                if (stylusHit != null && stylusHit.GetComponentInParent<Canvas>() != null) {
                    return true;
                }

                KmaxStylus.PointerState state = stylus.pointerState;
                if (state.hitSomething && !state.hit3D) {
                    return true;
                }
            }

            EventSystem events = EventSystem.current;
            if (events == null) {
                return false;
            }

            if (_sharedPointerData == null) {
                _sharedPointerData = new PointerEventData(events);
            }

            _sharedPointerData.Reset();
            _sharedPointerData.position = eventData != null ? eventData.position : (Vector2)Input.mousePosition;

            RaycastScratch.Clear();
            events.RaycastAll(_sharedPointerData, RaycastScratch);

            for (int i = 0; i < RaycastScratch.Count; i++) {
                GameObject hit = RaycastScratch[i].gameObject;
                if (hit != null && hit.GetComponentInParent<Canvas>() != null) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gives every graphic under this canvas its own material with the depth test disabled.
        /// Safe to call repeatedly.
        /// </summary>
        [ContextMenu("Apply")]
        public void Apply() {
            ConfigureCanvasRaycasting();

            Graphic[] graphics = GetComponentsInChildren<Graphic>(true);

            for (int i = 0; i < graphics.Length; i++) {
                Graphic graphic = graphics[i];
                if (graphic == null) {
                    continue;
                }

                Material source = graphic.materialForRendering;
                if (source == null) {
                    continue;
                }

                if (source.name.EndsWith(SuffixMarker)) {
                    source.SetInt(GuiZTestMode, (int)CompareFunction.Always);
                    source.SetInt(ZTestMode, (int)CompareFunction.Always);
                    source.renderQueue = renderQueue;
                    continue;
                }

                Material instance = new Material(source);
                instance.name = source.name + SuffixMarker;
                instance.SetInt(GuiZTestMode, (int)CompareFunction.Always);
                instance.SetInt(ZTestMode, (int)CompareFunction.Always);
                instance.renderQueue = renderQueue;

                // TextMeshPro keeps its own material reference and would overwrite Graphic.material.
                TMP_Text text = graphic as TMP_Text;
                if (text != null) {
                    text.fontMaterial = instance;
                } else {
                    graphic.material = instance;
                }
            }

            _applied = true;
        }

        private const string SuffixMarker = " (AlwaysOnTop)";
    }
}
