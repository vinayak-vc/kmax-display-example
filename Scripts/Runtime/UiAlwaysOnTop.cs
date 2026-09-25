using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Keeps the world-space interface drawn in front of the anatomy, whatever the anatomy is doing.
    ///
    /// The canvas is pinned to the virtual screen by the SDK's <c>UIScaler</c>, which parks it at
    /// the rig's screen plane - always exactly 0.5 m from the viewer. The model sits at the orbit
    /// centre, which is <c>distance</c> from the viewer. So any time the camera is dollied closer
    /// than 0.5 m, the model is physically in front of the interface and occludes it: the info
    /// panel loses its text behind a muscle, which is what was happening.
    ///
    /// No depth offset fixes that across the whole dolly range - the distance varies from 0.12 m to
    /// 1.2 m, and an offset large enough to clear the model when zoomed in would put the panel
    /// uncomfortably close to the viewer's face. So the interface is taken out of the depth test
    /// instead, and drawn last.
    ///
    /// <c>unity_GUIZTestMode</c> is declared outside the shader's Properties block, so
    /// <c>Material.HasProperty</c> reports false for it - that is expected and not a reason to skip
    /// the write. Both <c>UI/Default</c> and the TextMeshPro distance-field shaders read it.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class UiAlwaysOnTop : MonoBehaviour {
        private static readonly int GuiZTestMode = Shader.PropertyToID("unity_GUIZTestMode");
        private static readonly int ZTestMode = Shader.PropertyToID("_ZTestMode");

        [SerializeField, Tooltip("Render queue the interface is pushed to. Above 3000 puts it after " +
            "the model's transparent passes, which matters for the order the blend happens in.")]
        private int renderQueue = 4000;
        [SerializeField, Tooltip("Re-apply whenever a child is enabled. Needed because the Back and " +
            "navigator buttons are switched off and on as the flow changes.")]
        private bool reapplyOnEnable = true;

        private bool _applied;

        private void Start() {
            Apply();
        }

        private void OnEnable() {
            if (reapplyOnEnable && _applied) {
                Apply();
            }
        }

        /// <summary>
        /// Gives every graphic under this canvas its own material with the depth test disabled.
        /// Safe to call repeatedly.
        /// </summary>
        [ContextMenu("Apply")]
        public void Apply() {
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
