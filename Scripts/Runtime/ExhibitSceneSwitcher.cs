using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Advances to the next exhibit scene in Build Settings when its button is pressed, wrapping
    /// back to the first exhibit after the last.
    /// </summary>
    public class ExhibitSceneSwitcher : MonoBehaviour {
        [SerializeField] private Button nextSceneButton;
        [SerializeField, Tooltip("Optional. Plays the hover cue when the pointer enters the button.")]
        private AnatomyAudioDirector audioDirector;
        [SerializeField, Tooltip("Optional scene name to load next. When empty, steps to the next " +
            "scene in Build Settings by buildIndex.")]
        private string nextSceneName;

        private UiButtonMotion buttonMotion;

        private void Awake() {
            if (nextSceneButton == null) {
                Debug.LogError($"{nameof(ExhibitSceneSwitcher)} on '{name}' has no {nameof(nextSceneButton)} assigned.", this);
                enabled = false;
                return;
            }

            nextSceneButton.onClick.AddListener(LoadNextScene);

            buttonMotion = nextSceneButton.GetComponent<UiButtonMotion>();
            if (buttonMotion != null) {
                buttonMotion.HoverChanged += OnButtonHoverChanged;
            }
        }

        private void OnDestroy() {
            if (nextSceneButton != null) {
                nextSceneButton.onClick.RemoveListener(LoadNextScene);
            }

            if (buttonMotion != null) {
                buttonMotion.HoverChanged -= OnButtonHoverChanged;
            }
        }

        public void LoadNextScene() {
            AnatomyAudioDirector activeAudio = AnatomyAudioDirector.Instance != null ? AnatomyAudioDirector.Instance : audioDirector;
            if (activeAudio != null) {
                activeAudio.PlaySelect();
            }

            if (!string.IsNullOrEmpty(nextSceneName) && Application.CanStreamedLevelBeLoaded(nextSceneName)) {
                SceneManager.LoadScene(nextSceneName);
                return;
            }

            int totalScenes = SceneManager.sceneCountInBuildSettings;
            if (totalScenes <= 0) {
                Debug.LogError($"{nameof(ExhibitSceneSwitcher)} cannot load the next scene because Build Settings has no enabled scenes.", this);
                return;
            }

            int currentIndex = SceneManager.GetActiveScene().buildIndex;
            int nextIndex = currentIndex >= 0 ? (currentIndex + 1) % totalScenes : 0;
            for (int step = 0; step < totalScenes; step++) {
                string path = SceneUtility.GetScenePathByBuildIndex(nextIndex);
                if (string.IsNullOrEmpty(path) ||
                    !path.EndsWith("/Launcher.unity", System.StringComparison.OrdinalIgnoreCase)) {
                    break;
                }

                nextIndex = (nextIndex + 1) % totalScenes;
            }

            SceneManager.LoadScene(nextIndex);
        }

        private void OnButtonHoverChanged(bool hovered) {
            if (hovered && audioDirector != null) {
                audioDirector.PlayHover();
            }
        }
    }
}
