using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Drives the whole exhibit: closed on load, expand on the button, hotspots once open,
    /// and a framed close-up plus a description when one is picked.
    ///
    /// A part can be chosen three ways, all of which funnel through <see cref="SelectPart"/>:
    /// its numbered badge, the structure's own geometry, or the Next and Back buttons. The last
    /// exists because several structures sit inside the shells around them - in the exploded view
    /// their badges are occluded from most angles, and before the navigator those parts could not
    /// be reached at all without hunting for a viewpoint that exposed them.
    /// </summary>
    public class EyeAnatomyController : MonoBehaviour {
        [Header("Views")]
        [SerializeField] private EyeExplodeView explodeView;
        [SerializeField] private EyeFocusView focusView;
        [SerializeField] private EyeAnatomyCatalog catalog;
        [SerializeField] private AnatomyInfoPanel infoPanel;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private EyeHotspot hotspotPrefab;
        [SerializeField] private EyeManipulator manipulator;
        [SerializeField] private ViewerFlyController flyController;

        [Header("Buttons")]
        [SerializeField] private Button expandButton;
        [SerializeField] private TextMeshProUGUI expandButtonLabel;
        [SerializeField] private Button backButton;
        [SerializeField] private Button resetButton;
        [SerializeField, Tooltip("Steps forward through the catalog and flies the camera to that part.")]
        private Button nextButton;
        [SerializeField, Tooltip("Steps backward through the catalog and flies the camera to that part.")]
        private Button previousButton;
        [SerializeField, Tooltip("Optional. Shows which structure of how many is selected.")]
        private TextMeshProUGUI partCounterLabel;

        [Header("Presentation")]
        [SerializeField, Tooltip("Optional. Plays a burst when a part is selected.")]
        private AnatomyParticleDirector particles;
        [SerializeField, Tooltip("Optional. Plays the ambience and the interaction cues.")]
        private AnatomyAudioDirector audioDirector;
        [SerializeField] private string expandLabel = "Expand eye";
        [SerializeField] private string collapseLabel = "Close eye";

        [Header("Hotspots")]
        [SerializeField, Range(0.001f, 0.05f)] private float hotspotWorldRadius = 0.005f;
        [SerializeField, Tooltip("Gap in metres between a part's front face and its marker. Markers are " +
            "depth-tested, so they sit in front of the part's nearest surface rather than at its centre.")]
        private float hotspotFrontGap = 0.008f;

        [Header("Direct Picking")]
        [SerializeField, Tooltip("Fit colliders to the model so the stylus beam stops on the eye and " +
            "each structure can be selected by pointing at the geometry itself.")]
        private bool enableDirectPartPicking = true;

        [Header("Camera Flight")]
        [SerializeField, Tooltip("Fly the camera to a viewpoint that shows the selected structure, " +
            "rather than leaving the viewer wherever they happened to be looking from.")]
        private bool flyCameraOnSelect = true;
        [SerializeField, Tooltip("Seconds the flight to a selected part takes.")]
        private float flyDuration = 0.7f;
        [SerializeField, Tooltip("Camera distance in metres while a part is framed.")]
        private float flyDistance = 0.42f;
        [SerializeField, Range(0f, 1f), Tooltip("How much of the part's own elevation the camera adopts. " +
            "Full elevation is disorienting for parts high above or below the eye's axis.")]
        private float flyPitchDamping = 0.6f;

        private EyeHotspot[] hotspots;
        private Transform[] partTransforms;
        private EyePartDefinition[] partDefinitions;
        private EyePartPicker[] partPickers;
        private Vector3[] partViewDirections;
        private bool hasViewDirections;
        private int partCount;
        private int selectedIndex = -1;

        /// <summary>
        /// A part the navigator asked for while the eye was still closed, applied once the explode
        /// settles. -1 when there is none.
        /// </summary>
        private int pendingSelection = -1;

        /// <summary>
        /// Index of the structure currently in focus, or -1 when the exhibit is in overview.
        /// </summary>
        public int SelectedIndex {
            get { return selectedIndex; }
        }

        /// <summary>
        /// How many catalogued structures resolved against the model.
        /// </summary>
        public int PartCount {
            get { return partCount; }
        }

        private void Start() {
            if (!HasRequiredReferences()) {
                enabled = false;
                return;
            }

            if (manipulator == null) {
                manipulator = GetComponent<EyeManipulator>();
            }

            if (flyController == null) {
                flyController = GetComponent<ViewerFlyController>();
            }

            BuildHotspots();

            explodeView.TransitionCompleted += OnExplodeTransitionCompleted;
            expandButton.onClick.AddListener(OnExpandButtonClicked);
            AddListener(backButton, OnBackButtonClicked);
            AddListener(resetButton, OnResetButtonClicked);
            AddListener(nextButton, OnNextButtonClicked);
            AddListener(previousButton, OnPreviousButtonClicked);
            SubscribeButtonHover(expandButton);
            SubscribeButtonHover(backButton);
            SubscribeButtonHover(resetButton);
            SubscribeButtonHover(nextButton);
            SubscribeButtonHover(previousButton);

            explodeView.SetExpansionImmediate(0f);
            SetHotspotsVisible(false);
            infoPanel.Hide();
            SetBackButtonVisible(false);
            SetNavigationVisible(false);
            RefreshExpandLabel();
            RefreshPartCounter();
        }

        private void OnDestroy() {
            if (explodeView != null) {
                explodeView.TransitionCompleted -= OnExplodeTransitionCompleted;
            }

            if (expandButton != null) {
                expandButton.onClick.RemoveListener(OnExpandButtonClicked);
            }

            RemoveListener(backButton, OnBackButtonClicked);
            RemoveListener(resetButton, OnResetButtonClicked);
            RemoveListener(nextButton, OnNextButtonClicked);
            RemoveListener(previousButton, OnPreviousButtonClicked);
            UnsubscribeButtonHover(expandButton);
            UnsubscribeButtonHover(backButton);
            UnsubscribeButtonHover(resetButton);
            UnsubscribeButtonHover(nextButton);
            UnsubscribeButtonHover(previousButton);

            for (int i = 0; i < partCount; i++) {
                if (hotspots[i] != null) {
                    hotspots[i].Clicked -= OnHotspotClicked;
                    hotspots[i].HoverChanged -= OnHotspotHoverChanged;
                }

                if (partPickers[i] != null) {
                    partPickers[i].Picked -= OnPartPicked;
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
            } else {
                PlayCue(AudioCue.Reset);
            }

            if (manipulator != null) {
                manipulator.ResetTransform(true);
            }

            if (flyController != null) {
                flyController.ResetView();
            }
        }

        /// <summary>
        /// Focuses one catalogued structure, whatever route the viewer took to ask for it.
        /// </summary>
        /// <param name="index">Index into the resolved parts.</param>
        /// <param name="burstOrigin">Where to play the selection burst, in world space. Null plays
        /// only the quieter ring, which is right when the viewer pressed a button rather than
        /// touching the model - a burst at a point they did not press reads as a glitch.</param>
        public void SelectPart(int index, Vector3? burstOrigin = null) {
            if (index < 0 || index >= partCount) {
                Debug.LogError($"{nameof(EyeAnatomyController)} was asked to select an out-of-range part index {index}.", this);
                return;
            }

            if (selectedIndex >= 0 && selectedIndex < partCount && selectedIndex != index) {
                hotspots[selectedIndex].SetSelected(false);
            }

            selectedIndex = index;
            hotspots[index].SetSelected(true);

            focusView.Focus(partTransforms[index]);
            infoPanel.Show(partDefinitions[index].DisplayName, partDefinitions[index].Description);

            if (particles != null) {
                if (burstOrigin.HasValue) {
                    particles.PlayInteractionBurst(burstOrigin.Value);
                } else {
                    particles.PlayPopupRing(hotspots[index].transform.position);
                }
            }

            FlyToPart(index);

            SetBackButtonVisible(true);
            RefreshExpandLabel();
            RefreshPartCounter();
        }

        /// <summary>
        /// Steps to the next structure in the catalog, wrapping at the end. With nothing selected
        /// it starts at the first.
        /// </summary>
        public void SelectNextPart() {
            StepSelection(1);
        }

        /// <summary>
        /// Steps to the previous structure in the catalog, wrapping at the start.
        /// </summary>
        public void SelectPreviousPart() {
            StepSelection(-1);
        }

        private void StepSelection(int direction) {
            if (partCount == 0) {
                return;
            }

            int next = selectedIndex < 0
                ? (direction > 0 ? 0 : partCount - 1)
                : ((selectedIndex + direction) % partCount + partCount) % partCount;

            PlayCue(direction > 0 ? AudioCue.NavigateForward : AudioCue.NavigateBack);

            // Stepping implies the eye is open. Without this the buttons would appear to do
            // nothing while the eye is closed, which is worse than opening it for them.
            //
            // The selection has to wait for the explode to finish rather than being applied now:
            // framing reads the part's bounds, and mid-transition those describe a pose the part
            // is only passing through, so the part would be framed at the wrong size.
            if (!explodeView.IsExpanded) {
                pendingSelection = next;
                explodeView.SetExpanded(true);
                RefreshExpandLabel();
                PlayCue(AudioCue.Expand);
                return;
            }

            SelectPart(next);
        }

        /// <summary>
        /// Eases the camera round to a viewpoint on the selected structure's own side of the eye,
        /// so it is the nearest thing to the viewer rather than buried behind its neighbours.
        /// </summary>
        private void FlyToPart(int index) {
            if (!flyCameraOnSelect || flyController == null || !hasViewDirections) {
                return;
            }

            Vector3 direction = partViewDirections[index];
            if (direction.sqrMagnitude <= Mathf.Epsilon) {
                return;
            }

            // The rig is placed at focalCenter + rot * (0, 0, -distance), so aiming the camera's
            // offset direction along the part's direction puts the camera on that side.
            float yaw = Mathf.Atan2(-direction.x, -direction.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg * flyPitchDamping;

            flyController.FlyTo(yaw, pitch, flyDistance, true, flyDuration);
        }

        private void OnResetButtonClicked() {
            ResetToHome();
        }

        private void OnNextButtonClicked() {
            SelectNextPart();
        }

        private void OnPreviousButtonClicked() {
            SelectPreviousPart();
        }

        private void OnExpandButtonClicked() {
            if (focusView.IsFocused) {
                ReturnToOverview();
                return;
            }

            bool willExpand = !explodeView.IsExpanded;
            if (!willExpand) {
                SetHotspotsVisible(false);
                SetNavigationVisible(false);
            }

            explodeView.SetExpanded(willExpand);
            RefreshExpandLabel();
            PlayCue(willExpand ? AudioCue.Expand : AudioCue.Collapse);
        }

        private void OnExplodeTransitionCompleted(bool expanded) {
            // Badges stay up while a part is focused so another part is one click away rather
            // than a trip through Back. They hold their on-screen size themselves.
            SetHotspotsVisible(expanded);
            SetNavigationVisible(expanded);

            if (!expanded) {
                pendingSelection = -1;
                RefreshPartCounter();
                return;
            }

            // Directions first: the flight angle for the pending part is read from them.
            CacheViewDirections();
            RefreshPartCounter();

            if (pendingSelection < 0) {
                return;
            }

            int index = pendingSelection;
            pendingSelection = -1;
            SelectPart(index);
        }

        private void OnHotspotClicked(EyeHotspot hotspot) {
            SelectPart(hotspot.PartIndex, hotspot.transform.position);
            PlayCue(AudioCue.Select);
        }

        private void OnHotspotHoverChanged(EyeHotspot hotspot, bool hovered) {
            if (hovered) {
                PlayCue(AudioCue.Hover);
            }
        }

        private void OnPartPicked(int index) {
            if (index < 0 || index >= partCount) {
                return;
            }

            // Burst on the badge rather than on the exact triangle that was touched: the badge is
            // where the viewer's attention already is, and the triangle may be inside a shell.
            SelectPart(index, hotspots[index].transform.position);
            PlayCue(AudioCue.Select);
        }

        private void OnBackButtonClicked() {
            ReturnToOverview();
        }

        private void ReturnToOverview() {
            if (selectedIndex >= 0 && selectedIndex < partCount) {
                hotspots[selectedIndex].SetSelected(false);
            }

            selectedIndex = -1;

            // Back pressed during the explode that a navigator step started: the viewer has
            // changed their mind, so that queued selection must not land afterwards.
            pendingSelection = -1;

            focusView.ClearFocus();
            infoPanel.Hide();
            SetBackButtonVisible(false);
            SetHotspotsVisible(explodeView.IsExpanded);
            SetNavigationVisible(explodeView.IsExpanded);
            RefreshExpandLabel();
            RefreshPartCounter();
            PlayCue(AudioCue.Back);

            if (flyController != null) {
                flyController.ResetView(true);
            }

            if (manipulator != null) {
                manipulator.ResetTransform(true);
            }
        }

        private void BuildHotspots() {
            EyePartDefinition[] definitions = catalog.Parts;
            hotspots = new EyeHotspot[definitions.Length];
            partTransforms = new Transform[definitions.Length];
            partDefinitions = new EyePartDefinition[definitions.Length];
            partPickers = new EyePartPicker[definitions.Length];
            partViewDirections = new Vector3[definitions.Length];
            partCount = 0;

            Camera activeCamera = ResolveActiveCamera();
            EyePartColliders.Result colliderResult = new EyePartColliders.Result();

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

                // Colliders are fitted before the badge is parented in, so the badge's own sphere
                // collider is never mistaken for part of the anatomy.
                if (enableDirectPartPicking) {
                    colliderResult.Add(EyePartColliders.Fit(part));

                    EyePartPicker picker = part.GetComponent<EyePartPicker>();
                    if (picker == null) {
                        picker = part.gameObject.AddComponent<EyePartPicker>();
                    }

                    picker.Initialize(partCount);
                    picker.Picked += OnPartPicked;
                    partPickers[partCount] = picker;
                }

                EyeHotspot hotspot = Instantiate(hotspotPrefab, part, false);
                hotspot.name = "Hotspot_" + definitions[i].DisplayName;

                // The viewer looks along +Z, so the part's front face is at its bounds minimum.
                hotspot.transform.position = new Vector3(
                    worldBounds.center.x,
                    worldBounds.center.y,
                    worldBounds.min.z - hotspotFrontGap - hotspotWorldRadius);

                // The badge holds this size itself from here on, counter-scaling whenever the
                // model is scaled up to frame a part.
                hotspot.ConfigureSize(hotspotWorldRadius * 2f);

                hotspot.Initialize(partCount, activeCamera);
                hotspot.Clicked += OnHotspotClicked;
                hotspot.HoverChanged += OnHotspotHoverChanged;

                hotspots[partCount] = hotspot;
                partTransforms[partCount] = part;
                partDefinitions[partCount] = definitions[i];
                partCount++;
            }

            if (enableDirectPartPicking) {
                EyePartColliders.Report(colliderResult, this);
            }

            if (partCount == 0) {
                Debug.LogError($"{nameof(EyeAnatomyController)} built no hotspots; check the catalog's part paths.", this);
            }
        }

        /// <summary>
        /// Records which way each structure lies from the centre of the eye, used to pick a camera
        /// angle that shows it.
        ///
        /// Taken from the exploded pose rather than at build time, because assembled the parts are
        /// nested shells sharing one centre and the directions are meaningless. Expansion only ever
        /// completes while nothing is focused, so the model is at rest whenever this runs.
        /// </summary>
        private void CacheViewDirections() {
            if (hasViewDirections || partCount == 0) {
                return;
            }

            Bounds modelBounds;
            if (!EyePartBounds.TryGet(modelRoot, out modelBounds)) {
                return;
            }

            for (int i = 0; i < partCount; i++) {
                Bounds partBounds;
                if (!EyePartBounds.TryGet(partTransforms[i], out partBounds)) {
                    partViewDirections[i] = Vector3.zero;
                    continue;
                }

                Vector3 offset = partBounds.center - modelBounds.center;

                // A part sitting dead on the centre gives no usable direction. Pulling it towards
                // the viewer is the honest default: face-on is how the eye is meant to be read.
                partViewDirections[i] = offset.sqrMagnitude <= Mathf.Epsilon
                    ? Vector3.back
                    : offset.normalized;
            }

            hasViewDirections = true;
        }

        private Camera ResolveActiveCamera() {
            Camera main = Camera.main;
            if (main != null && main.isActiveAndEnabled) {
                return main;
            }

            Camera[] all = Camera.allCameras;
            for (int i = 0; i < all.Length; i++) {
                if (all[i] != null && all[i].isActiveAndEnabled) {
                    return all[i];
                }
            }

            return null;
        }

        private void SetHotspotsVisible(bool visible) {
            for (int i = 0; i < partCount; i++) {
                hotspots[i].gameObject.SetActive(visible);
            }
        }

        private void SetBackButtonVisible(bool visible) {
            if (backButton != null) {
                backButton.gameObject.SetActive(visible);
            }
        }

        private void SetNavigationVisible(bool visible) {
            if (nextButton != null) {
                nextButton.gameObject.SetActive(visible);
            }

            if (previousButton != null) {
                previousButton.gameObject.SetActive(visible);
            }

            if (partCounterLabel != null) {
                partCounterLabel.gameObject.SetActive(visible);
            }
        }

        private void RefreshExpandLabel() {
            if (expandButtonLabel != null) {
                expandButtonLabel.text = explodeView.IsExpanded ? collapseLabel : expandLabel;
            }
        }

        private void RefreshPartCounter() {
            if (partCounterLabel == null) {
                return;
            }

            partCounterLabel.text = selectedIndex >= 0
                ? $"{selectedIndex + 1} / {partCount}"
                : $"{partCount} structures";
        }

        private enum AudioCue {
            Hover,
            Select,
            NavigateForward,
            NavigateBack,
            Back,
            Expand,
            Collapse,
            Reset
        }

        private void PlayCue(AudioCue cue) {
            if (audioDirector == null) {
                return;
            }

            switch (cue) {
                case AudioCue.Hover:
                    audioDirector.PlayHover();
                    break;
                case AudioCue.Select:
                    audioDirector.PlaySelect();
                    break;
                case AudioCue.NavigateForward:
                    audioDirector.PlayNavigate(true);
                    break;
                case AudioCue.NavigateBack:
                    audioDirector.PlayNavigate(false);
                    break;
                case AudioCue.Back:
                    audioDirector.PlayBack();
                    break;
                case AudioCue.Expand:
                    audioDirector.PlayExpand();
                    break;
                case AudioCue.Collapse:
                    audioDirector.PlayCollapse();
                    break;
                case AudioCue.Reset:
                    audioDirector.PlayReset();
                    break;
            }
        }

        private static void AddListener(Button button, UnityEngine.Events.UnityAction action) {
            if (button != null) {
                button.onClick.AddListener(action);
            }
        }

        private static void RemoveListener(Button button, UnityEngine.Events.UnityAction action) {
            if (button != null) {
                button.onClick.RemoveListener(action);
            }
        }

        private void SubscribeButtonHover(Button button) {
            if (button == null) {
                return;
            }

            UiButtonMotion motion = button.GetComponent<UiButtonMotion>();
            if (motion != null) {
                motion.HoverChanged += OnButtonHoverChanged;
            }
        }

        private void UnsubscribeButtonHover(Button button) {
            if (button == null) {
                return;
            }

            UiButtonMotion motion = button.GetComponent<UiButtonMotion>();
            if (motion != null) {
                motion.HoverChanged -= OnButtonHoverChanged;
            }
        }

        private void OnButtonHoverChanged(bool hovered) {
            if (hovered) {
                PlayCue(AudioCue.Hover);
            }
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
