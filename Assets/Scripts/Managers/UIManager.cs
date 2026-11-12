using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RealityDefender
{
    /// <summary>
    /// All screen UI. Two screens: Scanning (placement prompt) and Gameplay (score, crosshair,
    /// fire, move base), plus tracking warnings and the WAVE CLEARED panel.
    /// The scanning prompt and tracking warning are built in code, so they need no scene setup.
    /// Buttons are wired here in code — leave their OnClick lists empty in the Inspector.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        #region Inspector

        [Header("Screens")]
        [Tooltip("Parent of the in-game HUD (score, debug, crosshair, fire, move). Shown only after the base is placed.")]
        [SerializeField] GameObject gameplayHud;

        [Header("Prompts (created in code)")]
        [SerializeField] float promptFontSize = 46f;
        [SerializeField] Vector2 placementPromptPosition = new(0f, -420f);
        [SerializeField] Vector2 trackingPromptPosition = new(0f, 420f);
        [SerializeField] Color placementPromptColor = Color.white;
        [SerializeField] Color trackingPromptColor = new(1f, 0.84f, 0.29f); // FFD54A

        [Header("Gameplay HUD")]
        [SerializeField] TMP_Text scoreText;
        [SerializeField] TMP_Text debugText;
        [SerializeField] GameObject crosshair;
        [SerializeField] Button fireButton;
        [Tooltip("Optional. Lets the player pick up the base and place it again.")]
        [SerializeField] Button moveBaseButton;

        [Header("Wave Cleared")]
        [SerializeField] RectTransform waveClearedPanel;
        [SerializeField] Button restartButton;

        [Header("Animation")]
        [SerializeField] float scorePunchScale = 1.35f;
        [SerializeField] float scorePunchDuration = 0.2f;
        [SerializeField] float panelScaleInDuration = 0.35f;

        [Header("Hit Marker")]
        [SerializeField] Color hitMarkerColor = new(1f, 0.17f, 0.84f); // FF2BD6
        [SerializeField] float hitMarkerScale = 1.8f;
        [SerializeField] float hitMarkerDuration = 0.18f;

        #endregion

        #region Private Fields

        Coroutine _scorePunch;
        Coroutine _panelScaleIn;
        Coroutine _hitMarker;
        int _shownProjectileCount = -1;

        Graphic _crosshairGraphic;
        Color _crosshairColor;

        GameState _state = GameState.Scanning;
        string _placementHint = "";

        GameObject _placementPrompt;   // background panel + text, toggled as one
        TMP_Text _placementText;
        GameObject _trackingPrompt;
        TMP_Text _trackingText;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            _crosshairGraphic = crosshair.GetComponent<Graphic>();
            if (_crosshairGraphic != null) _crosshairColor = _crosshairGraphic.color;

            if (gameplayHud == null)
                Debug.LogError("[UIManager] 'Gameplay Hud' is not assigned - the score/fire screen will show while scanning.");

            (_placementPrompt, _placementText) = CreatePrompt("PlacementPrompt", placementPromptPosition, placementPromptColor);
            (_trackingPrompt, _trackingText) = CreatePrompt("TrackingPrompt", trackingPromptPosition, trackingPromptColor);
            _placementPrompt.SetActive(false);
            _trackingPrompt.SetActive(false);
        }

        void Start()
        {
            fireButton.onClick.AddListener(() => GameManager.Instance.Fire());
            restartButton.onClick.AddListener(() => GameManager.Instance.Restart());
            if (moveBaseButton != null)
                moveBaseButton.onClick.AddListener(() => GameManager.Instance.MoveBase());
        }

        void Update()
        {
            UpdateDebugText();
            PulsePlacementPrompt();
        }

        #endregion

        #region Public API (called by GameManager / ARManager)

        public void ShowState(GameState state)
        {
            _state = state;
            bool scanning = state == GameState.Scanning;
            bool playing = state == GameState.Playing;
            bool cleared = state == GameState.WaveCleared;

            // Scanning screen vs gameplay screen.
            if (gameplayHud != null) gameplayHud.SetActive(!scanning);
            RefreshPlacementHint();

            crosshair.SetActive(playing);
            fireButton.gameObject.SetActive(playing);
            if (moveBaseButton != null) moveBaseButton.gameObject.SetActive(!scanning);

            if (_panelScaleIn != null) StopCoroutine(_panelScaleIn);
            waveClearedPanel.gameObject.SetActive(cleared);
            if (cleared) _panelScaleIn = StartCoroutine(ScaleIn(waveClearedPanel));
        }

        public void SetScore(int score, bool animate)
        {
            scoreText.text = $"Score: {score}";

            if (_scorePunch != null) StopCoroutine(_scorePunch);
            scoreText.transform.localScale = Vector3.one;
            if (animate) _scorePunch = StartCoroutine(Punch(scoreText.transform));
        }

        /// <summary>Crosshair flashes magenta and grows briefly, like a shooter hit marker.</summary>
        public void ShowHitMarker()
        {
            if (!crosshair.activeInHierarchy) return;
            if (_hitMarker != null) StopCoroutine(_hitMarker);
            _hitMarker = StartCoroutine(HitMarker());
        }

        /// <summary>Shows an AR tracking problem, or hides the message when null/empty.</summary>
        public void SetTrackingMessage(string message)
        {
            bool show = !string.IsNullOrEmpty(message);
            _trackingPrompt.SetActive(show);
            if (show) _trackingText.text = message;
        }

        /// <summary>Scanning instruction from ARManager ("Scan the floor..." / "Tap to place"). Null hides it.</summary>
        public void SetPlacementHint(string message)
        {
            _placementHint = message;
            RefreshPlacementHint();
        }

        #endregion

        #region HUD Updates

        void UpdateDebugText()
        {
            if (debugText == null || GameManager.Instance == null) return;

            int count = GameManager.Instance.ActiveProjectiles;
            if (count == _shownProjectileCount) return;

            _shownProjectileCount = count;
            debugText.text = $"Projectiles alive: {count}";
        }

        void RefreshPlacementHint()
        {
            if (_placementPrompt == null) return;

            bool show = _state == GameState.Scanning && !string.IsNullOrEmpty(_placementHint);
            _placementPrompt.SetActive(show);
            if (show) _placementText.text = _placementHint;
        }

        /// <summary>Gentle breathing so the instruction draws the eye.</summary>
        void PulsePlacementPrompt()
        {
            if (_placementPrompt == null || !_placementPrompt.activeSelf) return;
            _placementText.alpha = 0.7f + 0.3f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f));
        }

        #endregion

        #region Prompt Builder

        /// <summary>
        /// Builds a centred, rounded-looking prompt (dark translucent panel + TMP text) under this Canvas.
        /// Created in code so the scanning flow works without any scene setup.
        /// </summary>
        (GameObject root, TMP_Text text) CreatePrompt(string name, Vector2 anchoredPosition, Color textColor)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(transform, false);
            rootRect.anchorMin = rootRect.anchorMax = rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = anchoredPosition;
            rootRect.sizeDelta = new Vector2(920f, 170f);

            var panel = root.GetComponent<Image>();
            panel.color = new Color(0f, 0f, 0f, 0.55f);
            panel.raycastTarget = false;   // never block taps on the floor

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var textRect = (RectTransform)textGo.transform;
            textRect.SetParent(rootRect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(30f, 12f);
            textRect.offsetMax = new Vector2(-30f, -12f);

            var tmp = textGo.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = promptFontSize;
            tmp.color = textColor;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.raycastTarget = false;
            tmp.richText = true;

            return (root, tmp);
        }

        #endregion

        #region Animations

        IEnumerator Punch(Transform t)
        {
            for (float time = 0f; time < scorePunchDuration; time += Time.unscaledDeltaTime)
            {
                float k = time / scorePunchDuration;
                t.localScale = Vector3.one * Mathf.Lerp(scorePunchScale, 1f, k * k);
                yield return null;
            }
            t.localScale = Vector3.one;
        }

        IEnumerator HitMarker()
        {
            Transform t = crosshair.transform;
            for (float time = 0f; time < hitMarkerDuration; time += Time.unscaledDeltaTime)
            {
                float k = time / hitMarkerDuration;
                t.localScale = Vector3.one * Mathf.Lerp(hitMarkerScale, 1f, k * k);
                if (_crosshairGraphic != null)
                    _crosshairGraphic.color = Color.Lerp(hitMarkerColor, _crosshairColor, k);
                yield return null;
            }
            t.localScale = Vector3.one;
            if (_crosshairGraphic != null) _crosshairGraphic.color = _crosshairColor;
            _hitMarker = null;
        }

        IEnumerator ScaleIn(Transform t)
        {
            for (float time = 0f; time < panelScaleInDuration; time += Time.unscaledDeltaTime)
            {
                t.localScale = Vector3.one * GameManager.EaseOutBack(time / panelScaleInDuration);
                yield return null;
            }
            t.localScale = Vector3.one;
        }

        #endregion
    }
}