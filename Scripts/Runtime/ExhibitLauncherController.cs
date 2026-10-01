using System;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Controls the Kmax exhibit launcher: three scene tiles with thumbnails and titles, a
    /// non-interactive 3D background stage that activates the selected scene's model and rotates it
    /// at a stereoscopic pop-out distance in front of the display plane, and a Load button that
    /// appears below the tiles once a scene is selected.
    /// </summary>
    public class ExhibitLauncherController : MonoBehaviour {
        [Serializable]
        public class SceneTileOption {
            [Tooltip("Build Settings scene name to load when the Load button is pressed.")]
            public string sceneName;
            [Tooltip("Human-readable title displayed on the tile and status prompt.")]
            public string displayName;
            [Tooltip("Tile button clicked by the user to select this scene.")]
            public Button tileButton;
            [Tooltip("Optional border or accent image toggled when this tile is selected.")]
            public Image selectionHighlight;
            [Tooltip("Root pivot of the non-interactive 3D background model for this scene.")]
            public Transform previewModelRoot;
            [Tooltip("Initial Euler angles applied when this model becomes the active background.")]
            public Vector3 initialEulerAngles = new Vector3(14f, 215f, 0f);
        }

        [SerializeField] private SceneTileOption[] options = new SceneTileOption[0];
        [SerializeField] private Button loadButton;
        [SerializeField] private TextMeshProUGUI loadButtonLabel;
        [SerializeField] private TextMeshProUGUI statusPrompt;
        [SerializeField] private AnatomyAudioDirector audioDirector;
        [SerializeField, Tooltip("Index of the scene tile selected by default when the launcher opens (0 = Eye Anatomy).")]
        private int defaultSelectedIndex = 0;

        [Header("Background Model Stage")]
        [SerializeField, Tooltip("Continuous turntable rotation speed in degrees per second.")]
        private float rotationSpeed = 24f;
        [SerializeField, Tooltip("Stereoscopic pop-out distance in metres in front of the Z=0 screen plane.")]
        private float popOutDistance = 0.09f;
        [SerializeField, Tooltip("Vertical offset in metres so the rotating 3D model sits in the stage above the tiles.")]
        private float stageElevation = 0.022f;
        [SerializeField, Tooltip("Duration in seconds of the scale-in transition when switching models.")]
        private float transitionDuration = 0.28f;

        private static readonly Color RestingTileColor = new Color(0.12f, 0.17f, 0.26f, 0.90f);
        private static readonly Color SelectedTileColor = new Color(0.18f, 0.36f, 0.56f, 0.96f);
        private static readonly Color HighlightedTileColor = new Color(0.24f, 0.48f, 0.72f, 0.98f);
        private static readonly Color PressedTileColor = new Color(0.12f, 0.62f, 0.52f, 1f);

        private int selectedIndex = -1;
        private float transitionElapsed;
        private Vector3[] baseModelScales;
        private UiButtonMotion[] tileMotions;
        private UiButtonMotion loadButtonMotion;

        public int SelectedIndex {
            get { return selectedIndex; }
        }

        private void Awake() {
            int count = options != null ? options.Length : 0;
            baseModelScales = new Vector3[count];
            tileMotions = new UiButtonMotion[count];

            for (int i = 0; i < count; i++) {
                SceneTileOption option = options[i];
                if (option == null) {
                    continue;
                }

                if (option.previewModelRoot != null) {
                    baseModelScales[i] = option.previewModelRoot.localScale;
                    DisableAllColliders(option.previewModelRoot);
                    option.previewModelRoot.gameObject.SetActive(false);
                } else {
                    baseModelScales[i] = Vector3.one;
                }

                if (option.selectionHighlight != null) {
                    option.selectionHighlight.enabled = false;
                }

                if (option.tileButton != null) {
                    int capturedIndex = i;
                    option.tileButton.onClick.AddListener(delegate { SelectScene(capturedIndex, true); });
                    ApplyTileColors(option.tileButton, false);

                    tileMotions[i] = option.tileButton.GetComponent<UiButtonMotion>();
                    if (tileMotions[i] != null) {
                        tileMotions[i].HoverChanged += OnButtonHoverChanged;
                    }
                }
            }

            if (loadButton != null) {
                loadButton.onClick.AddListener(LoadSelectedScene);
                loadButton.gameObject.SetActive(false);

                loadButtonMotion = loadButton.GetComponent<UiButtonMotion>();
                if (loadButtonMotion != null) {
                    loadButtonMotion.HoverChanged += OnButtonHoverChanged;
                }
            }

            if (statusPrompt != null) {
                statusPrompt.text = "Select a scene tile below to preview its 3D model";
            }

            if (count > 0 && defaultSelectedIndex >= 0) {
                SelectScene(Mathf.Clamp(defaultSelectedIndex, 0, count - 1), false);
            }
        }

        private void OnDestroy() {
            if (options != null) {
                for (int i = 0; i < options.Length; i++) {
                    if (options[i] != null && options[i].tileButton != null) {
                        options[i].tileButton.onClick.RemoveAllListeners();
                    }

                    if (tileMotions != null && i < tileMotions.Length && tileMotions[i] != null) {
                        tileMotions[i].HoverChanged -= OnButtonHoverChanged;
                    }
                }
            }

            if (loadButton != null) {
                loadButton.onClick.RemoveListener(LoadSelectedScene);
            }

            if (loadButtonMotion != null) {
                loadButtonMotion.HoverChanged -= OnButtonHoverChanged;
            }
        }

        private void Update() {
            if (selectedIndex < 0 || options == null || selectedIndex >= options.Length) {
                return;
            }

            SceneTileOption active = options[selectedIndex];
            if (active == null || active.previewModelRoot == null) {
                return;
            }

            Transform model = active.previewModelRoot;
            float floatBob = 0.0035f * Mathf.Sin(Time.time * 1.8f);
            model.localPosition = new Vector3(0f, stageElevation + floatBob, -Mathf.Abs(popOutDistance));
            model.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);

            if (transitionElapsed < transitionDuration) {
                transitionElapsed += Time.deltaTime;
                float t = transitionDuration > 0.0001f ? Mathf.Clamp01(transitionElapsed / transitionDuration) : 1f;
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                float spring = Mathf.Sin(t * Mathf.PI) * 0.06f;
                model.localScale = baseModelScales[selectedIndex] * (Mathf.Lerp(0.76f, 1f, eased) + spring);
            } else {
                model.localScale = baseModelScales[selectedIndex];
            }
        }

        /// <summary>
        /// Selects a scene tile by index, switches the 3D background model to that scene's model
        /// rotating with stereo pop-out distance, and reveals the Load button below the tiles.
        /// </summary>
        public void SelectScene(int index) {
            SelectScene(index, true);
        }

        public void SelectScene(int index, bool playSound) {
            if (options == null || index < 0 || index >= options.Length) {
                return;
            }

            bool changed = selectedIndex != index;
            selectedIndex = index;
            transitionElapsed = playSound && changed ? 0f : transitionDuration;

            if (playSound && audioDirector != null) {
                audioDirector.PlaySelect();
            }

            for (int i = 0; i < options.Length; i++) {
                SceneTileOption option = options[i];
                if (option == null) {
                    continue;
                }

                bool isSelected = i == index;

                if (option.tileButton != null) {
                    ApplyTileColors(option.tileButton, isSelected);
                }

                if (option.selectionHighlight != null) {
                    option.selectionHighlight.enabled = isSelected;
                }

                if (option.previewModelRoot != null) {
                    option.previewModelRoot.gameObject.SetActive(isSelected);
                    if (isSelected && changed) {
                        option.previewModelRoot.localPosition = new Vector3(0f, stageElevation, -Mathf.Abs(popOutDistance));
                        option.previewModelRoot.localRotation = Quaternion.Euler(option.initialEulerAngles);
                        option.previewModelRoot.localScale = playSound ? baseModelScales[i] * 0.82f : baseModelScales[i];
                    }
                }
            }

            SceneTileOption chosen = options[index];
            string label = !string.IsNullOrEmpty(chosen.displayName) ? chosen.displayName : chosen.sceneName;

            if (loadButton != null) {
                if (!loadButton.gameObject.activeSelf) {
                    loadButton.gameObject.SetActive(true);
                }
            }

            if (loadButtonLabel != null) {
                loadButtonLabel.text = "Load " + label;
            }

            if (statusPrompt != null) {
                statusPrompt.text = label + "  \u2022  3D Stereo Pop-Out Preview";
            }
        }

        /// <summary>
        /// Loads the currently selected exhibit scene.
        /// </summary>
        public void LoadSelectedScene() {
            if (selectedIndex < 0 || options == null || selectedIndex >= options.Length) {
                return;
            }

            SceneTileOption chosen = options[selectedIndex];
            if (chosen == null || string.IsNullOrEmpty(chosen.sceneName)) {
                Debug.LogError($"{nameof(ExhibitLauncherController)} cannot load scene at index {selectedIndex} because sceneName is empty.", this);
                return;
            }

            if (audioDirector != null) {
                audioDirector.PlaySelect();
            }

            if (!Application.CanStreamedLevelBeLoaded(chosen.sceneName)) {
                Debug.LogError($"{nameof(ExhibitLauncherController)} cannot load '{chosen.sceneName}' because it is not enabled in Build Settings.", this);
                return;
            }

            SceneManager.LoadScene(chosen.sceneName);
        }

        private void OnButtonHoverChanged(bool hovered) {
            if (hovered && audioDirector != null) {
                audioDirector.PlayHover();
            }
        }

        private static void ApplyTileColors(Button button, bool selected) {
            ColorBlock colors = button.colors;
            colors.normalColor = selected ? SelectedTileColor : RestingTileColor;
            colors.highlightedColor = HighlightedTileColor;
            colors.pressedColor = PressedTileColor;
            colors.selectedColor = selected ? SelectedTileColor : HighlightedTileColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
        }

        private static void DisableAllColliders(Transform root) {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++) {
                if (colliders[i] != null) {
                    colliders[i].enabled = false;
                }
            }
        }
    }
}
