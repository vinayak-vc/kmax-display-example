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
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;

            EyeAnatomySceneUpgrader.GetOrAdd<CanvasScaler>(root);
            EyeAnatomySceneUpgrader.GetOrAdd<GraphicRaycaster>(root);
            EyeAnatomySceneUpgrader.GetOrAdd<KmaxUIRaycaster>(root);
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
        /// A colour swatch and the ring that marks it when it is the chosen one.
        /// </summary>
        public class SwatchChip {
            public Button Button;
            public GameObject Marker;
        }

        /// <summary>
        /// A swatch: a square of the colour being offered, with no text on it at all.
        ///
        /// This is what lets a run of choices shrink. Five paint names down the edge are five
        /// full-width buttons; five paint colours are a row 368 px wide, and they say more - the
        /// swatch *is* the answer to what the button does.
        ///
        /// Built as three stacked graphics rather than one, because a <see cref="Selectable"/> with
        /// a colour transition owns its target graphic's colour and rewrites it on every state
        /// change. Putting the swatch colour there would mean the hover state erasing the very
        /// thing being chosen. So the fill is left alone, and the button is given a transparent
        /// wash on top as its target instead - the wash carries the hover and press states, the
        /// ring underneath carries the selection, and the colour is never touched by either.
        /// </summary>
        public static SwatchChip BuildSwatchChip(RectTransform parent, string name, Color colour,
            Vector2 anchor, Vector2 anchoredPosition, Vector2 size) {

            GameObject chip = FindOrCreateRect(parent, name);
            RectTransform rect = chip.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            float ringOutset = Mathf.Max(2.5f, size.x * 0.08f);
            GameObject ring = FindOrCreateRect(chip.transform, "Ring");
            Stretch(ring.GetComponent<RectTransform>(), ringOutset);
            Image ringImage = EyeAnatomySceneUpgrader.GetOrAdd<Image>(ring);
            ringImage.color = new Color(0.96f, 0.98f, 1f, 0.95f);
            ringImage.raycastTarget = false;

            GameObject fill = FindOrCreateRect(chip.transform, "Fill");
            Stretch(fill.GetComponent<RectTransform>(), 0f);
            Image fillImage = EyeAnatomySceneUpgrader.GetOrAdd<Image>(fill);
            fillImage.color = colour;
            fillImage.raycastTarget = false;

            GameObject wash = FindOrCreateRect(chip.transform, "Wash");
            Stretch(wash.GetComponent<RectTransform>(), 0f);
            Image washImage = EyeAnatomySceneUpgrader.GetOrAdd<Image>(wash);
            washImage.color = Color.white;
            washImage.raycastTarget = true;

            // Sibling order is draw order, and the ring only reads as a frame while the fill covers
            // its middle.
            ring.transform.SetSiblingIndex(0);
            fill.transform.SetSiblingIndex(1);
            wash.transform.SetSiblingIndex(2);

            Button button = EyeAnatomySceneUpgrader.GetOrAdd<Button>(chip);
            button.targetGraphic = washImage;

            // A chip at this name may be a text button from an earlier build - the swatches used to
            // be a column of named buttons. Find-before-create keeps the object, so the parts of it
            // that no longer belong have to be taken off explicitly: its label would sit over the
            // colour, and its own background Image would draw behind the ring and still take
            // raycasts. Removed after the target graphic has been repointed, never before.
            Transform staleLabel = chip.transform.Find("Label");
            if (staleLabel != null) {
                Undo.DestroyObjectImmediate(staleLabel.gameObject);
            }

            Image staleBackground = chip.GetComponent<Image>();
            if (staleBackground != null) {
                Undo.DestroyObjectImmediate(staleBackground);
            }

            ColorBlock colors = button.colors;
            colors.colorMultiplier = 1f;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.24f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.42f);
            colors.selectedColor = new Color(1f, 1f, 1f, 0f);
            colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.5f);
            button.colors = colors;
            EyeAnatomySceneUpgrader.ClearPersistentCalls(button);

            SwatchChip result = new SwatchChip();
            result.Button = button;
            result.Marker = ring;
            return result;
        }

        private static GameObject FindOrCreateRect(Transform parent, string name) {
            Transform existing = parent.Find(name);
            if (existing != null) {
                return existing.gameObject;
            }

            GameObject created = new GameObject(name, typeof(RectTransform));
            created.transform.SetParent(parent, false);
            Undo.RegisterCreatedObjectUndo(created, "Create " + name);
            return created;
        }

        /// <summary>
        /// Fills the parent rect, growing past it by <paramref name="outset"/> pixels on every side.
        /// </summary>
        private static void Stretch(RectTransform rect, float outset) {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(-outset, -outset);
            rect.offsetMax = new Vector2(outset, outset);
        }

        private const string UiSpritesFolder = "Assets/Games/kmax-display-example/Materials/UI";
        private const string PillSpritePath = UiSpritesFolder + "/ModernPillButton.png";
        private const string CardSpritePath = UiSpritesFolder + "/ModernGlassCard.png";

        /// <summary>
        /// Generates (or loads) a 9-sliced rounded pill sprite with a soft vertical glass gradient
        /// and a crisp top-lit glowing rim so buttons look modern and tactile rather than flat rectangles.
        /// </summary>
        public static Sprite GetOrCreatePillSprite() {
            return GetOrCreateRoundedBoxSprite(PillSpritePath, 64, 18f, 1.8f, 20f,
                new Color(0.76f, 0.84f, 0.94f, 0.94f),
                new Color(0.50f, 0.60f, 0.74f, 0.92f),
                new Color(0.92f, 0.98f, 1.00f, 1.00f));
        }

        /// <summary>
        /// Generates (or loads) a 9-sliced rounded glass card sprite for the InfoPanel and Launcher tiles.
        /// </summary>
        public static Sprite GetOrCreateCardSprite() {
            return GetOrCreateRoundedBoxSprite(CardSpritePath, 64, 14f, 1.6f, 18f,
                new Color(0.70f, 0.78f, 0.90f, 0.94f),
                new Color(0.46f, 0.54f, 0.68f, 0.92f),
                new Color(0.45f, 0.88f, 1.00f, 0.96f));
        }

        private static Sprite GetOrCreateRoundedBoxSprite(
            string path,
            int size,
            float cornerRadius,
            float borderThickness,
            float sliceBorder,
            Color topFill,
            Color bottomFill,
            Color rimColor) {
            Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (existing != null) {
                return existing;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Games/kmax-display-example/Materials")) {
                AssetDatabase.CreateFolder("Assets/Games/kmax-display-example", "Materials");
            }
            if (!AssetDatabase.IsValidFolder(UiSpritesFolder)) {
                AssetDatabase.CreateFolder("Assets/Games/kmax-display-example/Materials", "UI");
            }

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float half = size * 0.5f;
            Vector2 halfExtents = new Vector2(half - cornerRadius - 1.5f, half - cornerRadius - 1.5f);

            for (int y = 0; y < size; y++) {
                float v = y / (float)(size - 1);
                Color baseFill = Color.Lerp(bottomFill, topFill, v);

                for (int x = 0; x < size; x++) {
                    Vector2 p = new Vector2(Mathf.Abs(x + 0.5f - half), Mathf.Abs(y + 0.5f - half));
                    Vector2 q = p - halfExtents;
                    float dist = Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - cornerRadius;

                    // Outer anti-aliased alpha
                    float outerAlpha = Mathf.Clamp01(0.5f - dist);
                    if (outerAlpha <= 0f) {
                        tex.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    // Glowing border mask near dist == 0
                    float innerDist = dist + borderThickness;
                    float borderMask = Mathf.Clamp01(0.5f + innerDist);
                    // Top edge catches slightly more light
                    float topSpecular = Mathf.Lerp(0.68f, 1.0f, v);
                    Color pixel = Color.Lerp(baseFill, rimColor * topSpecular, borderMask * 0.88f);
                    pixel.a = baseFill.a * outerAlpha;
                    tex.SetPixel(x, y, pixel);
                }
            }

            tex.Apply();
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null) {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spriteBorder = new Vector4(sliceBorder, sliceBorder, sliceBorder, sliceBorder);
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = FilterMode.Bilinear;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// Applies the modern 9-sliced glass-pill sprite and vibrant interactive colour palette to
        /// an existing or newly created button.
        /// </summary>
        public static void ApplyModernButtonStyle(Button button, bool primaryAccent = false) {
            if (button == null) {
                return;
            }

            Image background = button.GetComponent<Image>();
            if (background != null) {
                Sprite pill = GetOrCreatePillSprite();
                if (pill != null) {
                    background.sprite = pill;
                    background.type = Image.Type.Sliced;
                    background.pixelsPerUnitMultiplier = 1.25f;
                }
                background.color = Color.white;
            }

            ColorBlock colors = button.colors;
            colors.colorMultiplier = 1f;
            if (primaryAccent) {
                colors.normalColor = new Color(0.14f, 0.36f, 0.58f, 0.95f);
                colors.highlightedColor = new Color(0.20f, 0.56f, 0.84f, 1.00f);
                colors.pressedColor = new Color(0.12f, 0.72f, 0.56f, 1.00f);
            } else {
                colors.normalColor = new Color(0.14f, 0.21f, 0.32f, 0.92f);
                colors.highlightedColor = new Color(0.22f, 0.44f, 0.68f, 0.98f);
                colors.pressedColor = new Color(0.14f, 0.62f, 0.52f, 1.00f);
            }
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(0.12f, 0.15f, 0.20f, 0.45f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }

        /// <summary>
        /// A button with its label, found before it is created so a rebuild keeps the one that is
        /// already there.
        /// </summary>
        /// <summary>
        /// As above, with a glyph down the left-hand end and the label moved over to clear it.
        ///
        /// Icon *and* label, rather than icon alone. The buttons could be a third the width without
        /// the words, but this is a museum exhibit read by someone who has never seen it before and
        /// will not hover anything to find out what it does - an unlabelled glyph is a guess. The
        /// icon is what lets the row be scanned; the word is what makes it certain.
        /// </summary>
        public static Button BuildIconButton(RectTransform parent, string name, string label, string glyph,
            Vector2 anchor, Vector2 anchoredPosition, Vector2 size, float fontSize = 15f) {

            Button button = BuildButton(parent, name, label, anchor, anchoredPosition, size, fontSize);
            RectTransform rect = button.GetComponent<RectTransform>();

            Sprite sprite = ExhibitIconFactory.GetOrCreate(glyph);
            if (sprite == null) {
                return button;
            }

            GameObject iconObject = FindOrCreateRect(rect, "Icon");
            RectTransform icon = iconObject.GetComponent<RectTransform>();
            icon.anchorMin = new Vector2(0f, 0.5f);
            icon.anchorMax = new Vector2(0f, 0.5f);
            icon.pivot = new Vector2(0f, 0.5f);
            icon.sizeDelta = new Vector2(size.y * 0.50f, size.y * 0.50f);
            icon.anchoredPosition = new Vector2(size.y * 0.28f, 0f);

            Image image = EyeAnatomySceneUpgrader.GetOrAdd<Image>(iconObject);
            image.sprite = sprite;
            image.color = new Color(0.56f, 0.90f, 1f, 0.98f);
            image.raycastTarget = false;
            image.preserveAspect = true;

            // The label stretches the whole button, so it has to be inset past the glyph or it
            // centres itself underneath it.
            TextMeshProUGUI text = EyeAnatomySceneUpgrader.FindChildComponent<TextMeshProUGUI>(rect, "Label");
            if (text != null) {
                float inset = size.y * 0.28f + icon.sizeDelta.x + size.y * 0.16f;
                text.rectTransform.offsetMin = new Vector2(inset, 0f);
                text.rectTransform.offsetMax = new Vector2(-8f, 0f);
                text.alignment = TextAlignmentOptions.Left;
            }

            return button;
        }

        public static Button BuildButton(RectTransform parent, string name, string label,
            Vector2 anchor, Vector2 anchoredPosition, Vector2 size, float fontSize = 15f) {
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

            bool isPrimary = name == "ExpandButton" || name == "TourButton" || name == "LoadButton";
            ApplyModernButtonStyle(button, isPrimary);

            // Cleared because the controllers subscribe in code. A persistent call left over from a
            // previous build would fire a second time alongside it.
            EyeAnatomySceneUpgrader.ClearPersistentCalls(button);

            TextMeshProUGUI text = EyeAnatomySceneUpgrader.FindChildComponent<TextMeshProUGUI>(rect, "Label");
            if (text == null) {
                text = BuildLabel(rect, "Label", label, fontSize, new Vector2(0.5f, 0.5f), Vector2.zero,
                    Vector2.zero, TextAlignmentOptions.Center);
                text.rectTransform.anchorMin = Vector2.zero;
                text.rectTransform.anchorMax = Vector2.one;
                text.rectTransform.offsetMin = Vector2.zero;
                text.rectTransform.offsetMax = Vector2.zero;
            }

            // Set on every run, not only on creation: a button found from a previous build keeps
            // its label object, so a size change here would otherwise never reach the scene.
            text.fontSize = fontSize;
            text.fontStyle = isPrimary ? FontStyles.Bold : FontStyles.Normal;
            text.color = new Color(0.95f, 0.98f, 1f, 0.98f);
            text.text = label;
            return button;
        }

        /// <summary>
        /// The panel a selected part's name and description are written into, found before it is
        /// created.
        ///
        /// The panel is left inactive, which is what <see cref="AnatomyInfoPanel"/> expects: it
        /// initialises lazily on the first Show precisely because Unity defers Awake on an inactive
        /// object.
        /// </summary>
        public static AnatomyInfoPanel BuildInfoPanel(RectTransform parent, Vector2 anchor,
            Vector2 anchoredPosition, Vector2 size) {
            Transform existing = parent.Find("InfoPanel");
            GameObject panel;

            if (existing != null) {
                panel = existing.gameObject;
            } else {
                panel = new GameObject("InfoPanel", typeof(RectTransform));
                panel.transform.SetParent(parent, false);
                Undo.RegisterCreatedObjectUndo(panel, "Create InfoPanel");
            }

            RectTransform rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = anchoredPosition;

            Image background = EyeAnatomySceneUpgrader.GetOrAdd<Image>(panel);
            Sprite cardSprite = GetOrCreateCardSprite();
            if (cardSprite != null) {
                background.sprite = cardSprite;
                background.type = Image.Type.Sliced;
                background.pixelsPerUnitMultiplier = 1.15f;
            }
            background.color = new Color(0.08f, 0.13f, 0.22f, 0.92f);
            background.raycastTarget = false;

            string titleChildName = rect.Find("Title") != null && rect.Find("Label") == null ? "Title" : "Label";
            TextMeshProUGUI label = BuildLabel(rect, titleChildName, "", 18f,
                new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(-28f, 26f), TextAlignmentOptions.TopLeft);
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(0.45f, 0.90f, 1.00f, 1f);
            StretchHorizontally(label.rectTransform);
            label.rectTransform.offsetMin = new Vector2(16f, label.rectTransform.offsetMin.y);
            label.rectTransform.offsetMax = new Vector2(-16f, label.rectTransform.offsetMax.y);

            TextMeshProUGUI description = BuildLabel(rect, "Description", "", 13.5f,
                new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(-28f, -48f), TextAlignmentOptions.TopLeft);
            description.textWrappingMode = TextWrappingModes.Normal;
            description.color = new Color(0.90f, 0.95f, 1.00f, 0.96f);
            StretchHorizontally(description.rectTransform);
            description.rectTransform.anchorMin = new Vector2(0f, 0f);
            description.rectTransform.anchorMax = new Vector2(1f, 1f);
            description.rectTransform.offsetMin = new Vector2(16f, 12f);
            description.rectTransform.offsetMax = new Vector2(-16f, -38f);

            description.enableAutoSizing = true;
            description.fontSizeMin = 10f;
            description.fontSizeMax = 14f;
            description.overflowMode = TextOverflowModes.Truncate;

            label.enableAutoSizing = true;
            label.fontSizeMin = 13f;
            label.fontSizeMax = 18f;
            label.overflowMode = TextOverflowModes.Truncate;

            AnatomyInfoPanel component = EyeAnatomySceneUpgrader.GetOrAdd<AnatomyInfoPanel>(panel);
            SerializedObject so = new SerializedObject(component);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "panelRoot", panel);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "titleLabel", label);
            EyeAnatomySceneUpgrader.SetIfPresent(so, "descriptionLabel", description);
            so.ApplyModifiedPropertiesWithoutUndo();

            panel.SetActive(false);
            return component;
        }

        private static void StretchHorizontally(RectTransform rect) {
            rect.anchorMin = new Vector2(0f, rect.anchorMin.y);
            rect.anchorMax = new Vector2(1f, rect.anchorMax.y);
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
