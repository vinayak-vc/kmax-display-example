using System;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// A set of hinged panels that open and shut together, and a name to put on the button that
    /// does it - both doors on a side, all four doors, the hood on its own.
    /// </summary>
    [Serializable]
    public class VehiclePanelGroup {
        [SerializeField] private string displayName;
        [SerializeField] private VehiclePanel[] panels = new VehiclePanel[0];
        [SerializeField, Tooltip("Label while the group is shut, e.g. 'Open the doors'.")]
        private string openLabel;
        [SerializeField, Tooltip("Label while the group is open, e.g. 'Close the doors'.")]
        private string closeLabel;

        public VehiclePanelGroup(string displayName, VehiclePanel[] panels, string openLabel, string closeLabel) {
            this.displayName = displayName;
            this.panels = panels;
            this.openLabel = openLabel;
            this.closeLabel = closeLabel;
        }

        public string DisplayName {
            get { return displayName; }
        }

        public VehiclePanel[] Panels {
            get { return panels; }
        }

        public string OpenLabel {
            get { return openLabel; }
        }

        public string CloseLabel {
            get { return closeLabel; }
        }
    }

    /// <summary>
    /// Opens and shuts the car's panels in groups, and reports what is open.
    ///
    /// Groups rather than individual panels because that is how a viewer thinks about a car: the
    /// doors, the hood, the trunk. Nothing here knows about the interface - the build step makes
    /// one button per group and points it at this.
    /// </summary>
    public class VehiclePanelController : MonoBehaviour {
        [SerializeField] private VehiclePanelGroup[] groups = new VehiclePanelGroup[0];

        /// <summary>
        /// Raised whenever any group finishes opening or shutting. The argument is true when at
        /// least one panel anywhere on the car is open.
        /// </summary>
        public event Action<bool> AnyPanelChanged;

        public int GroupCount {
            get { return groups.Length; }
        }

        /// <summary>
        /// True when any panel on the car is open, which is what decides whether the interior is
        /// reachable.
        /// </summary>
        public bool IsAnyOpen {
            get {
                for (int i = 0; i < groups.Length; i++) {
                    if (IsGroupOpen(i)) {
                        return true;
                    }
                }

                return false;
            }
        }

        private void Awake() {
            for (int i = 0; i < groups.Length; i++) {
                VehiclePanel[] panels = groups[i].Panels;
                for (int j = 0; j < panels.Length; j++) {
                    if (panels[j] != null) {
                        panels[j].Settled += OnPanelSettled;
                    }
                }
            }
        }

        private void OnDestroy() {
            for (int i = 0; i < groups.Length; i++) {
                VehiclePanel[] panels = groups[i].Panels;
                for (int j = 0; j < panels.Length; j++) {
                    if (panels[j] != null) {
                        panels[j].Settled -= OnPanelSettled;
                    }
                }
            }
        }

        public VehiclePanelGroup GetGroup(int index) {
            if (index < 0 || index >= groups.Length) {
                return null;
            }

            return groups[index];
        }

        /// <summary>
        /// A group counts as open when any panel in it is, so the button offers to close a pair
        /// that is only half shut rather than opening the other half.
        /// </summary>
        public bool IsGroupOpen(int index) {
            if (index < 0 || index >= groups.Length) {
                return false;
            }

            VehiclePanel[] panels = groups[index].Panels;
            for (int i = 0; i < panels.Length; i++) {
                if (panels[i] != null && panels[i].IsOpen) {
                    return true;
                }
            }

            return false;
        }

        public void SetGroupOpen(int index, bool open) {
            if (index < 0 || index >= groups.Length) {
                Debug.LogError($"{nameof(VehiclePanelController)} was asked for panel group {index}, which does not exist.", this);
                return;
            }

            VehiclePanel[] panels = groups[index].Panels;
            for (int i = 0; i < panels.Length; i++) {
                if (panels[i] != null) {
                    panels[i].SetOpen(open);
                }
            }
        }

        public void ToggleGroup(int index) {
            SetGroupOpen(index, !IsGroupOpen(index));
        }

        public void CloseAll() {
            for (int i = 0; i < groups.Length; i++) {
                SetGroupOpen(i, false);
            }
        }

        private void OnPanelSettled(bool open) {
            if (AnyPanelChanged != null) {
                AnyPanelChanged(IsAnyOpen);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Used only by the build step. The runtime view of the groups stays read-only.
        /// </summary>
        public void SetGroups(VehiclePanelGroup[] value) {
            groups = value;
        }
#endif
    }
}
