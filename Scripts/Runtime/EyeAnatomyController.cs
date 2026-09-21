using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Drives the whole exhibit: closed on load, expand on the button, hotspots once open,
    /// and a framed close-up plus a description when one is picked.
    /// </summary>
    public class EyeAnatomyController : MonoBehaviour {
        [SerializeField] private EyeExplodeView explodeView;
        [SerializeField] private EyeFocusView focusView;
        [SerializeField] private EyeAnatomyCatalog catalog;
        [SerializeField] private AnatomyInfoPanel infoPanel;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private EyeHotspot hotspotPrefab;
        [SerializeField] private Button expandButton;
        [SerializeField] private TextMeshProUGUI expandButtonLabel;
        [SerializeField] private Button backButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private EyeManipulator manipulator;
        [SerializeField, Tooltip("Optional. Plays a burst when a part is selected.")]
        private AnatomyParticleDirector particles;
        [SerializeField] private string expandLabel = "Expand eye";
        [SerializeField] private string collapseLabel = "Close eye";
        [SerializeField, Range(0.001f, 0.05f)] private float hotspotWorldRadius = 0.005f;
        [SerializeField, Tooltip("Gap in metres between a part's front face and its marker. Markers are " +
            "depth-tested, so they sit in front of the part's nearest surface rather than at its centre.")]
        private float hotspotFrontGap = 0.008f;

        private EyeHotspot[] hotspots;
        private Transform[] partTransforms;
        private EyePartDefinition[] partDefinitions;
        private int partCount;
        private int selectedIndex = -1;

        private void Start() {
            if (!HasRequiredReferences()) {
                enabled = false;
                return;
            }

            BuildHotspots();

            explodeView.TransitionCompleted += OnExplodeTransitionCompleted;
            expandButton.onClick.AddListener(OnExpandButtonClicked);

            if (backButton != null) {
                backButton.onClick.AddListener(OnBackButtonClicked);
            }

            if (resetButton != null) {
                resetButton.onClick.AddListener(OnResetButtonClicked);
            }

            if (manipulator == null) {
                manipulator = GetComponent<EyeManipulator>();
            }

            explodeView.SetExpansionImmediate(0f);
            SetHotspotsVisible(false);
            infoPanel.Hide();
            SetBackButtonVisible(false);
            RefreshExpandLabel();
        }

        private void OnDestroy() {
            if (explodeView != null) {
                explodeView.TransitionCompleted -= OnExplodeTransitionCompleted;
            }

            if (expandButton != null) {
                expandButton.onClick.RemoveListener(OnExpandButtonClicked);
            }

            if (backButton != null) {
                backButton.onClick.RemoveListener(OnBackButtonClicked);
            }

            if (resetButton != null) {
                resetButton.onClick.RemoveListener(OnResetButtonClicked);
            }

            for (int i = 0; i < partCount; i++) {
                if (hotspots[i] != null) {
                    hotspots[i].Clicked -= OnHotspotClicked;
                }
            }
        }

        private void Update() {
            if (Input.GetKeyDown(KeyCode.R)) {
                ResetToHome();
            }
        }

        /// <summary>
        /// Smoothly resets the entire exhibit back to the default forward-facing overview state.
        /// </summary>
        public void ResetToHome() {
            if (focusView != null && focusView.IsFocused) {
                ReturnToOverview();
            }

            if (manipulator != null) {
                manipulator.ResetTransform(true);
            }
        }

        private void OnResetButtonClicked() {
            ResetToHome();
        }

        private void OnExpandButtonClicked() {
            if (focusView.IsFocused) {
                ReturnToOverview();
                return;
            }

            bool willExpand = !explodeView.IsExpanded;
            if (!willExpand) {
                SetHotspotsVisible(false);
            }

            explodeView.SetExpanded(willExpand);
            RefreshExpandLabel();
        }

        private void OnExplodeTransitionCompleted(bool expanded) {
            SetHotspotsVisible(expanded && !focusView.IsFocused);
        }

        private void OnHotspotClicked(EyeHotspot hotspot) {
            int index = hotspot.PartIndex;
            if (index < 0 || index >= partCount) {
                Debug.LogError($"{nameof(EyeAnatomyController)} received a click from a hotspot with an out-of-range index {index}.", this);
                return;
            }

            selectedIndex = index;
            hotspot.SetSelected(true);

            focusView.Focus(partTransforms[index]);
            infoPanel.Show(partDefinitions[index].DisplayName, partDefinitions[index].Description);

            if (particles != null) {
                particles.PlayInteractionBurst(hotspot.transform.position);
            }

            SetHotspotsVisible(false);
            SetBackButtonVisible(true);
            RefreshExpandLabel();
        }

        private void OnBackButtonClicked() {
            ReturnToOverview();
        }

        private void ReturnToOverview() {
            if (selectedIndex >= 0 && selectedIndex < partCount) {
                hotspots[selectedIndex].SetSelected(false);
            }

            selectedIndex = -1;
            focusView.ClearFocus();
            infoPanel.Hide();
            SetBackButtonVisible(false);
            SetHotspotsVisible(explodeView.IsExpanded);
            RefreshExpandLabel();
        }

        private void BuildHotspots() {
            EyePartDefinition[] definitions = catalog.Parts;
            hotspots = new EyeHotspot[definitions.Length];
            partTransforms = new Transform[definitions.Length];
            partDefinitions = new EyePartDefinition[definitions.Length];
            partCount = 0;

            for (int i = 0; i < definitions.Length; i++) {
                Transform part = modelRoot.Find(definitions[i].PartPath);
                if (part == null) {
                    Debug.LogError($"{nameof(EyeAnatomyController)} could not resolve part path '{definitions[i].PartPath}' under '{modelRoot.name}'; '{definitions[i].DisplayName}' will have no hotspot.", this);
                    continue;
                }

                Bounds worldBounds;
                if (!EyePartBounds.TryGet(part, out worldBounds)) {
                    Debug.LogError($"{nameof(EyeAnatomyController)} found no renderer under '{part.name}'; '{definitions[i].DisplayName}' will have no hotspot.", this);
                    continue;
                }

                EyeHotspot hotspot = Instantiate(hotspotPrefab, part, false);
                hotspot.name = "Hotspot_" + definitions[i].DisplayName;

                // The viewer looks along +Z, so the part's front face is at its bounds minimum.
                hotspot.transform.position = new Vector3(
                    worldBounds.center.x,
                    worldBounds.center.y,
                    worldBounds.min.z - hotspotFrontGap - hotspotWorldRadius);

                float parentScale = part.lossyScale.x;
                if (parentScale > Mathf.Epsilon) {
                    hotspot.transform.localScale = Vector3.one * (hotspotWorldRadius * 2f / parentScale);
                }

                hotspot.Initialize(partCount);
                hotspot.Clicked += OnHotspotClicked;

                hotspots[partCount] = hotspot;
                partTransforms[partCount] = part;
                partDefinitions[partCount] = definitions[i];
                partCount++;
            }

            if (partCount == 0) {
                Debug.LogError($"{nameof(EyeAnatomyController)} built no hotspots; check the catalog's part paths.", this);
            }
        }

        private void SetHotspotsVisible(bool visible) {
            for (int i = 0; i < partCount; i++) {
                hotspots[i].gameObject.SetActive(visible);
            }
        }

        private void SetBackButtonVisible(bool visible) {
            if (backButton == null) {
                return;
            }

            backButton.gameObject.SetActive(visible);
        }

        private void RefreshExpandLabel() {
            if (expandButtonLabel == null) {
                return;
            }

            expandButtonLabel.text = explodeView.IsExpanded ? collapseLabel : expandLabel;
        }

        private bool HasRequiredReferences() {
            bool complete = true;
            complete &= RequireReference(explodeView, nameof(explodeView));
            complete &= RequireReference(focusView, nameof(focusView));
            complete &= RequireReference(catalog, nameof(catalog));
            complete &= RequireReference(infoPanel, nameof(infoPanel));
            complete &= RequireReference(modelRoot, nameof(modelRoot));
            complete &= RequireReference(hotspotPrefab, nameof(hotspotPrefab));
            complete &= RequireReference(expandButton, nameof(expandButton));
            return complete;
        }

        private bool RequireReference(Object reference, string fieldName) {
            if (reference != null) {
                return true;
            }

            Debug.LogError($"{nameof(EyeAnatomyController)} on '{name}' has no {fieldName} assigned; the exhibit is disabled.", this);
            return false;
        }
    }
}