using TMPro;
using UnityEngine;

namespace ViitorCloud.KmaxDisplayExample {
    /// <summary>
    /// Shows the name and description of the selected part with a smooth slide-in, scale-spring,
    /// and fade animation.
    ///
    /// The panel starts inactive in the scene, so Unity defers this component's Awake until the
    /// first Show. Setup therefore has to be lazy rather than done in Awake, and Awake must not
    /// hide the panel - that would undo the Show that just activated it.
    /// </summary>
    public class AnatomyInfoPanel : MonoBehaviour {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TextMeshProUGUI titleLabel;
        [SerializeField] private TextMeshProUGUI descriptionLabel;
        [SerializeField] private float entranceDuration = 0.26f;

        private bool isInitialised;
        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;
        private Vector2 _restAnchoredPosition;
        private Vector3 _restScale = Vector3.one;
        private float _animProgress = 1f;
        private bool _wasActiveOnShow;

        public void Show(string title, string description) {
            EnsureInitialised();

            _wasActiveOnShow = panelRoot.activeSelf;

            if (titleLabel != null) {
                titleLabel.text = title;
            }

            if (descriptionLabel != null) {
                descriptionLabel.text = description;
            }

            panelRoot.SetActive(true);
            _animProgress = 0f;
            ApplyAnimation(0f);
        }

        public void Hide() {
            EnsureInitialised();
            if (_rectTransform != null) {
                _rectTransform.anchoredPosition = _restAnchoredPosition;
                _rectTransform.localScale = _restScale;
            }
            if (_canvasGroup != null) {
                _canvasGroup.alpha = 1f;
            }
            panelRoot.SetActive(false);
        }

        private void Update() {
            if (!isInitialised || _animProgress >= 1f) {
                return;
            }

            float step = entranceDuration > 0.001f ? Time.unscaledDeltaTime / entranceDuration : 1f;
            _animProgress = Mathf.MoveTowards(_animProgress, 1f, step);
            ApplyAnimation(_animProgress);
        }

        private void ApplyAnimation(float t) {
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float spring = Mathf.Sin(t * Mathf.PI) * 0.045f;

            if (_rectTransform != null) {
                Vector2 slideStart = _wasActiveOnShow
                    ? _restAnchoredPosition + new Vector2(14f, 0f)
                    : _restAnchoredPosition + new Vector2(36f, 8f);
                _rectTransform.anchoredPosition = Vector2.LerpUnclamped(slideStart, _restAnchoredPosition, eased);

                float startScale = _wasActiveOnShow ? 0.96f : 0.88f;
                _rectTransform.localScale = _restScale * (Mathf.Lerp(startScale, 1f, eased) + spring);
            }

            if (_canvasGroup != null) {
                _canvasGroup.alpha = _wasActiveOnShow ? Mathf.Lerp(0.65f, 1f, eased) : eased;
            }
        }

        private void EnsureInitialised() {
            if (isInitialised) {
                return;
            }

            isInitialised = true;

            if (panelRoot == null) {
                panelRoot = gameObject;
            }

            _rectTransform = panelRoot.GetComponent<RectTransform>();
            if (_rectTransform != null) {
                _restAnchoredPosition = _rectTransform.anchoredPosition;
                _restScale = _rectTransform.localScale;
            }

            _canvasGroup = panelRoot.GetComponent<CanvasGroup>();
            if (_canvasGroup == null) {
                _canvasGroup = panelRoot.AddComponent<CanvasGroup>();
            }

            if (titleLabel == null || descriptionLabel == null) {
                Debug.LogError($"{nameof(AnatomyInfoPanel)} on '{name}' is missing a label reference; part information will not be shown.", this);
            }
        }
    }
}