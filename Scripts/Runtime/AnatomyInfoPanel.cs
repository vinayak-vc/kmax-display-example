using TMPro;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Shows the name and description of the selected part. Presentation only - it holds no
    /// knowledge of the model or the selection.
    ///
    /// The panel starts inactive in the scene, so Unity defers this component's Awake until the
    /// first Show. Setup therefore has to be lazy rather than done in Awake, and Awake must not
    /// hide the panel - that would undo the Show that just activated it.
    /// </summary>
    public class AnatomyInfoPanel : MonoBehaviour {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI titleLabel;
        [SerializeField] private TextMeshProUGUI descriptionLabel;

        private bool isInitialised;

        public void Show(string title, string description) {
            EnsureInitialised();

            if (titleLabel != null) {
                titleLabel.text = title;
            }

            if (descriptionLabel != null) {
                descriptionLabel.text = description;
            }

            panelRoot.SetActive(true);
        }

        public void Hide() {
            EnsureInitialised();
            panelRoot.SetActive(false);
        }

        private void EnsureInitialised() {
            if (isInitialised) {
                return;
            }

            isInitialised = true;

            if (panelRoot == null) {
                panelRoot = gameObject;
            }

            if (titleLabel == null || descriptionLabel == null) {
                Debug.LogError($"{nameof(AnatomyInfoPanel)} on '{name}' is missing a label reference; part information will not be shown.", this);
            }
        }
    }
}