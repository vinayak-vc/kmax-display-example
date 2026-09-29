using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Drives the two features a model can offer beyond being pulled apart: seeing through its
    /// outer casing, and swapping between build variants of it.
    ///
    /// Presentation and state only. The work is done by whatever implements
    /// <see cref="IExhibitMachinery"/> - for the engine that is <c>Enginei4</c>, which already had
    /// both features but could only be reached from the screen-space canvas it shipped with.
    /// </summary>
    public class ExhibitFeaturePanel : MonoBehaviour {
        [Header("Model")]
        [SerializeField, Tooltip("The behaviour implementing IExhibitMachinery. Held as a plain " +
            "MonoBehaviour because Unity will not serialise an interface field, and because the " +
            "implementation may live in an assembly this one cannot reference.")]
        private MonoBehaviour machinerySource;
        [SerializeField, Tooltip("Optional. Focusing a part rewrites every material, so the " +
            "see-through state has to stand down while one is in focus.")]
        private EyeFocusView focusView;

        [Header("See-through")]
        [SerializeField] private Button transparencyButton;
        [SerializeField] private TextMeshProUGUI transparencyLabel;
        [SerializeField] private string showCasingLabel = "See inside";
        [SerializeField] private string hideCasingLabel = "Solid again";

        [Header("Build variants")]
        [SerializeField, Tooltip("One per variant, in the model's own order.")]
        private Button[] variationButtons = new Button[0];
        [SerializeField] private Color selectedColor = new Color(0.22f, 0.42f, 0.62f, 0.95f);
        [SerializeField] private Color unselectedColor = new Color(0.16f, 0.20f, 0.27f, 0.92f);

        private IExhibitMachinery machinery;
        private bool isTransparent;
        private int selectedVariation;

        /// <summary>
        /// True while the model's casing is faded out.
        /// </summary>
        public bool IsTransparent {
            get { return isTransparent; }
        }

        private void Awake() {
            machinery = machinerySource as IExhibitMachinery;

            if (machinery == null) {
                Debug.LogError($"{nameof(ExhibitFeaturePanel)} on '{name}' has no {nameof(machinerySource)} " +
                    $"implementing {nameof(IExhibitMachinery)}; the see-through and build controls are hidden.", this);
                SetControlsActive(false);
                enabled = false;
                return;
            }

            if (transparencyButton != null) {
                transparencyButton.onClick.AddListener(OnTransparencyClicked);
            }

            for (int i = 0; i < variationButtons.Length; i++) {
                if (variationButtons[i] == null) {
                    continue;
                }

                // Captured per iteration, or every button would apply the last index.
                int index = i;
                variationButtons[i].onClick.AddListener(delegate { OnVariationClicked(index); });
            }

            if (focusView != null) {
                focusView.FocusChanged += OnFocusChanged;
            }

            RefreshTransparencyLabel();
            RefreshVariationButtons();
        }

        private void Start() {
            // In Start rather than Awake: the machinery caches its own rest state in its Start, and
            // applying a variant before that would hide parts it is about to measure.
            if (machinery != null) {
                machinery.ApplyVariation(selectedVariation);
                RefreshVariationButtons();
            }
        }

        private void OnDestroy() {
            if (transparencyButton != null) {
                transparencyButton.onClick.RemoveListener(OnTransparencyClicked);
            }

            if (focusView != null) {
                focusView.FocusChanged -= OnFocusChanged;
            }
        }

        private void OnTransparencyClicked() {
            SetTransparent(!isTransparent);
        }

        public void SetTransparent(bool transparent) {
            if (machinery == null || transparent == isTransparent) {
                return;
            }

            isTransparent = transparent;
            machinery.SetTransparent(transparent);
            RefreshTransparencyLabel();
        }

        private void OnVariationClicked(int index) {
            if (machinery == null || index == selectedVariation) {
                return;
            }

            selectedVariation = index;
            machinery.ApplyVariation(index);
            RefreshVariationButtons();
        }

        /// <summary>
        /// Stands the see-through state down while a part is in focus.
        ///
        /// <see cref="EyeFocusView"/> puts a ghost material on every part except the focused one
        /// and restores the originals on the way out, so the casing ends up solid again whatever
        /// this does. Only the flag and the label have to catch up: calling back into the machinery
        /// would start a fade that fights the ghosting for the same renderers.
        /// </summary>
        private void OnFocusChanged(bool focused) {
            if (focused && isTransparent) {
                isTransparent = false;
                RefreshTransparencyLabel();
            }

            if (transparencyButton != null) {
                transparencyButton.interactable = !focused;
            }
        }

        private void RefreshTransparencyLabel() {
            if (transparencyLabel == null) {
                return;
            }

            transparencyLabel.text = isTransparent ? hideCasingLabel : showCasingLabel;
        }

        /// <summary>
        /// Marks the active build.
        ///
        /// Written into the button's own <see cref="ColorBlock"/> rather than onto its image. A
        /// <see cref="Selectable"/> with a colour transition drives the canvas renderer on every
        /// state change, so a colour set on the graphic survives only until the pointer next
        /// touches that button - hovering any build would clear the highlight on the selected one.
        /// </summary>
        private void RefreshVariationButtons() {
            for (int i = 0; i < variationButtons.Length; i++) {
                if (variationButtons[i] == null) {
                    continue;
                }

                Color resting = i == selectedVariation ? selectedColor : unselectedColor;
                ColorBlock colors = variationButtons[i].colors;
                colors.normalColor = resting;
                colors.selectedColor = resting;
                variationButtons[i].colors = colors;
            }
        }

        private void SetControlsActive(bool active) {
            if (transparencyButton != null) {
                transparencyButton.gameObject.SetActive(active);
            }

            for (int i = 0; i < variationButtons.Length; i++) {
                if (variationButtons[i] != null) {
                    variationButtons[i].gameObject.SetActive(active);
                }
            }
        }
    }
}
