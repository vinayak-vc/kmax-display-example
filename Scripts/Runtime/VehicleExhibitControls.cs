using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Wires the car exhibit's buttons to the things they operate, and keeps their labels honest.
    ///
    /// Presentation and state only: the panels, the ignition and the lamps each do their own work,
    /// and none of them knows an interface exists.
    /// </summary>
    public class VehicleExhibitControls : MonoBehaviour {
        [Header("Systems")]
        [SerializeField] private VehiclePanelController panels;
        [SerializeField] private VehicleIgnition ignition;
        [SerializeField] private VehicleLightRig lights;
        [SerializeField] private ViewerFlyController flyController;
        [SerializeField] private EyeManipulator manipulator;

        [Header("Buttons")]
        [SerializeField, Tooltip("One per panel group, in the controller's own order.")]
        private Button[] panelButtons = new Button[0];
        [SerializeField] private Button ignitionButton;
        [SerializeField] private Button lightsButton;
        [SerializeField] private Button resetButton;
        [SerializeField] private string startLabel = "Start the car";
        [SerializeField] private string stopLabel = "Switch it off";
        [SerializeField] private string lightsOnLabel = "Lights on";
        [SerializeField] private string lightsOffLabel = "Lights off";

        private bool lightsForced;

        private void Awake() {
            for (int i = 0; i < panelButtons.Length; i++) {
                if (panelButtons[i] == null) {
                    continue;
                }

                // Captured per iteration, or every button would drive the last group.
                int index = i;
                panelButtons[i].onClick.AddListener(delegate { OnPanelClicked(index); });
            }

            if (ignitionButton != null) {
                ignitionButton.onClick.AddListener(OnIgnitionClicked);
            }

            if (lightsButton != null) {
                lightsButton.onClick.AddListener(OnLightsClicked);
            }

            if (resetButton != null) {
                resetButton.onClick.AddListener(OnResetClicked);
            }

            if (ignition != null) {
                ignition.RunningChanged += OnRunningChanged;
            }

            if (panels != null) {
                panels.AnyPanelChanged += OnAnyPanelChanged;
            }
        }

        private void Start() {
            RefreshAll();
        }

        private void OnDestroy() {
            if (ignitionButton != null) {
                ignitionButton.onClick.RemoveListener(OnIgnitionClicked);
            }

            if (lightsButton != null) {
                lightsButton.onClick.RemoveListener(OnLightsClicked);
            }

            if (resetButton != null) {
                resetButton.onClick.RemoveListener(OnResetClicked);
            }

            if (ignition != null) {
                ignition.RunningChanged -= OnRunningChanged;
            }

            if (panels != null) {
                panels.AnyPanelChanged -= OnAnyPanelChanged;
            }
        }

        private void OnPanelClicked(int index) {
            if (panels == null) {
                return;
            }

            panels.ToggleGroup(index);
            RefreshPanelButton(index);

            if (AnatomyAudioDirector.Instance != null) {
                if (panels.IsGroupOpen(index)) {
                    AnatomyAudioDirector.Instance.PlayExpand();
                } else {
                    AnatomyAudioDirector.Instance.PlayCollapse();
                }
            }
        }

        private void OnIgnitionClicked() {
            if (ignition == null) {
                return;
            }

            ignition.Toggle();

            if (AnatomyAudioDirector.Instance != null) {
                AnatomyAudioDirector.Instance.PlaySelect();
            }
        }

        /// <summary>
        /// Lights the whole car independently of the ignition, so the lamps can be looked at
        /// without the engine running. Starting the car takes over from here, which is why the
        /// label is refreshed from the rig rather than from this flag alone.
        /// </summary>
        private void OnLightsClicked() {
            if (lights == null) {
                return;
            }

            lightsForced = !lightsForced;
            lights.SetAll(lightsForced);
            RefreshLightsButton();

            if (AnatomyAudioDirector.Instance != null) {
                AnatomyAudioDirector.Instance.PlaySelect();
            }
        }

        private void OnResetClicked() {
            if (panels != null) {
                panels.CloseAll();
            }

            if (flyController != null) {
                flyController.ResetView();
            }

            if (manipulator != null) {
                manipulator.ResetTransform(true);
            }

            RefreshAll();
        }

        private void OnRunningChanged(bool running) {
            // Starting the car owns the lamps from that point, so the manual override stands down
            // rather than fighting it.
            lightsForced = running;
            RefreshIgnitionButton();
            RefreshLightsButton();
        }

        private void OnAnyPanelChanged(bool anyOpen) {
            RefreshAll();
        }

        private void RefreshAll() {
            for (int i = 0; i < panelButtons.Length; i++) {
                RefreshPanelButton(i);
            }

            RefreshIgnitionButton();
            RefreshLightsButton();
        }

        private void RefreshPanelButton(int index) {
            if (panels == null || index < 0 || index >= panelButtons.Length || panelButtons[index] == null) {
                return;
            }

            VehiclePanelGroup group = panels.GetGroup(index);
            if (group == null) {
                return;
            }

            SetLabel(panelButtons[index], panels.IsGroupOpen(index) ? group.CloseLabel : group.OpenLabel);
        }

        private void RefreshIgnitionButton() {
            if (ignitionButton == null || ignition == null) {
                return;
            }

            SetLabel(ignitionButton, ignition.IsRunning ? stopLabel : startLabel);
        }

        private void RefreshLightsButton() {
            if (lightsButton == null) {
                return;
            }

            SetLabel(lightsButton, lightsForced ? lightsOffLabel : lightsOnLabel);
        }

        private void SetLabel(Button button, string text) {
            if (button == null || string.IsNullOrEmpty(text)) {
                return;
            }

            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label != null) {
                label.text = text;
            }
        }
    }
}
