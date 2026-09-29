using KmaxXR;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ViitorCloud.KmaxDisplayExample.Editor {
    /// <summary>
    /// The interface pieces every exhibit builds the same way: the canvas itself, buttons and
    /// labels.
    ///
    /// Extracted once a second exhibit needed them. Each builder still decides what its interface
    /// says and where things sit; only the mechanics of making one live here.
    /// </summary>
    public static class ExhibitUiFactory {
        /// <summary>
        /// Metres per canvas unit that makes a 1920 x 1080 canvas exactly the virtual screen,
        /// 0.3454 x 0.1943 m - the view size the SDK reports.
        /// </summary>
        public const float CanvasScale = 0.00017989584f;

        /// <summary>
        /// Prepares a root as a world-space canvas pinned to the display.
        ///
        /// World space rather than screen space overlay, and pinned by the SDK's
        /// <see cref="UIScaler"/>, which rewrites the canvas pose and size every frame from the
        /// rig's own screen plane. That is what makes it behave like an overlay while still being
        /// geometry both eye cameras render.
        ///
        /// > A true <c>ScreenSpaceOverlay</c> canvas cannot be used on this hardware.
        /// > <c>VRRenderer</c> renders side by side, giving each eye half the viewport, and an
        /// > overlay ignores camera viewports and is drawn once across the whole framebuffer - so
        /// > it would span both eye images and never fuse.
        ///
        /// The event camera has to be set explicitly. Left empty, a world-space
        /// <see cref="GraphicRaycaster"/> falls back to <c>Camera.main</c>, and a Kmax rig has none:
        /// the SDK disables the rig camera's own <c>Camera</c> component and renders through the
        /// <c>left</c> and <c>right</c> sub-cameras it creates at runtime. Without it not one
        /// button is clickable, and nothing is logged.
        /// </summary>
        public static RectTransform BuildWorldCanvas(GameObject root, Camera eventCamera) {
            Canvas canvas = EyeAnatomySceneUpgrader.GetOrAdd<Canvas>(root);
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = eventCamera;

            EyeAnatomySceneUpgrader.GetOrAdd<CanvasScaler>(root);
            EyeAnatomySceneUpgrader.GetOrAdd<GraphicRaycaster>(root);
            EyeAnatomySceneUpgrader.GetOrAdd<UIScaler>(root);

            // Takes the interface out of the depth test. The canvas is pinned half a metre from the
            // viewer while the camera flies closer than that to the model, so without this the
            // model cuts through the panel it is being described on.
            EyeAnatomySceneUpgrader.GetOrAdd<UiAlwaysOnTop>(root);

            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1920f, 1080f);
            rect.localPosition = Vector3.zero;
            rect.localRotation = Quaternion.identity;
            rect.localScale = new Vector3(CanvasScale, CanvasScale, CanvasScale);
            return rect;
        }

        /// <summary>
        /// A button with its label, found before it is created so a rebuild keeps the one that is
        /// already there.
        /// </summary>
        public static Button BuildButton(RectTransform parent, string name, string label,
            Vector2 anchor, Vector2 anchoredPosition, Vector2 size) {
            Transform existing = parent.Find(name);
            GameObject buttonObject;

            if (existing != null) {
                buttonObject = existing.gameObject;
            } else {
                buttonObject = new GameObject(name, typeof(RectTransform));
                buttonObject.transform.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(buttonObject, "Create " + name);
            }

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(anchor.x, anchor.y);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            // The graphic stays white and every colour comes from the button's own ColorBlock.
            // A Selectable with a colour transition drives the canvas renderer directly on each
            // state change, so anything written to Image.color is thrown away the first time the
            // pointer touches the button.
            Image background = EyeAnatomySceneUpgrader.GetOrAdd<Image>(buttonObject);
            background.color = Color.white;

            Button button = EyeAnatomySceneUpgrader.GetOrAdd<Button>(buttonObject);
            button.targetGraphic = background;

            ColorBlock colors = button.colors;
            colors.colorMultiplier = 1f;
            colors.normalColor = new Color(0.16f, 0.20f, 0.27f, 0.92f);
            colors.highlightedColor = new Color(0.26f, 0.33f, 0.43f, 0.95f);
            colors.pressedColor = new Color(0.11f, 0.14f, 0.19f, 0.95f);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(0.13f, 0.15f, 0.18f, 0.45f);
            button.colors = colors;

            // Cleared because the controllers subscribe in code. A persistent call left over from a
            // previous build would fire a second time alongside it.
            EyeAnatomySceneUpgrader.ClearPersistentCalls(button);

            TextMeshProUGUI text = EyeAnatomySceneUpgrader.FindChildComponent<TextMeshProUGUI>(rect, "Label");
            if (text == null) {
                text = BuildLabel(rect, "Label", label, 34f, new Vector2(0.5f, 0.5f), Vector2.zero,
                    Vector2.zero, TextAlignmentOptions.Center);
                text.rectTransform.anchorMin = Vector2.zero;
                text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = Vector2.zero;
                text.rectTransform.offsetMax = Vector2.zero;
            }

            text.text = label;
            return button;
        }

        public static TextMeshProUGUI BuildLabel(RectTransform parent, string name, string content,
            float fontSize, Vector2 anchor, Vector2 anchoredPosition, Vector2 size, TextAlignmentOptions alignment) {
            Transform existing = parent.Find(name);
            GameObject labelObject;

            if (existing != null) {
                labelObject = existing.gameObject;
            } else {
                labelObject = new GameObject(name, typeof(RectTransform));
                labelObject.transform.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(labelObject, "Create " + name);
            }

            RectTransform rect = labelObject.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(anchor.x, anchor.y);
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            TextMeshProUGUI text = EyeAnatomySceneUpgrader.GetOrAdd<TextMeshProUGUI>(labelObject);
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = new Color(0.94f, 0.96f, 1f);
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;

            if (text.font == null && TMP_Settings.defaultFontAsset != null) {
                text.font = TMP_Settings.defaultFontAsset;
            }

            return text;
        }
    }
}
