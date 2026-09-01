using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PPack
{
    [ExecuteAlways]
    [RequireComponent(typeof(UIDocument))]
    public sealed class LoadingScreenController : MonoBehaviour
    {
        private static readonly string[] LoadingDotTexts =
        {
            string.Empty, ".", "..", "..."
        };
        private static readonly string[] CleanGlintNames = { "clean-glint-0", "clean-glint-1", "clean-glint-2" };
        private static readonly string[] SnowArcNames = { "snow-arc-0", "snow-arc-1", "snow-arc-2" };
        private static readonly string[] SnowChipNames = { "snow-chip-0", "snow-chip-1", "snow-chip-2", "snow-chip-3" };
        private static readonly string[] DecorRingClasses = { "decor-ring-a", "decor-ring-b", "decor-ring-c", "decor-ring-d", "decor-ring-e" };
        private static readonly string[] DecorStripeClasses = { "decor-stripes-a", "decor-stripes-b", "decor-stripes-c" };
        private static readonly string[] DecorSparkleClasses = { "decor-sparkle-a", "decor-sparkle-b", "decor-sparkle-c", "decor-sparkle-d" };
        private static readonly string[] DecorSwooshClasses = { "decor-swoosh-a", "decor-swoosh-b" };
        [Header("Feel Motion")]
        [Tooltip("Feel이 로컬 X를 0~1로 움직이는 중립 Transform입니다.")]
        [SerializeField] private Transform _feelMotionDriver;
        [Tooltip("Feel이 로컬 X를 -1~1로 왕복시키는 청소 동작 Transform입니다.")]
        [SerializeField] private Transform _scrubFeelDriver;
        [Tooltip("Feel이 로컬 Y를 -1~1로 왕복시키는 배경 장식 Transform입니다.")]
        [SerializeField] private Transform _backgroundFeelDriver;
        [Tooltip("Inspector에서 MMF_Player.PlayFeedbacks를 연결합니다.")]
        [SerializeField] private UnityEvent _playFeelMotion = new UnityEvent();

        [Header("Timing")]
        [SerializeField, Min(1f)] private float _traversalDuration = 6.4f;
        [SerializeField, Min(0f)] private float _minimumDisplaySeconds = 6.1f;
        [SerializeField] private bool _autoLoadNextScene = true;
        [SerializeField] private bool _previewWhenStartedDirectlyInEditor = true;

        private readonly List<VisualElement> _cleanGlints = new List<VisualElement>();
        private readonly List<VisualElement> _snowArcs = new List<VisualElement>();
        private readonly List<VisualElement> _snowChips = new List<VisualElement>();
        private readonly List<VisualElement> _decorRings = new List<VisualElement>();
        private readonly List<VisualElement> _decorStripes = new List<VisualElement>();
        private readonly List<VisualElement> _decorSparkles = new List<VisualElement>();
        private readonly List<VisualElement> _decorSwooshes = new List<VisualElement>();
        private VisualElement _root;
        private VisualElement _backgroundDecor;
        private VisualElement _animationStage;
        private VisualElement _cleanedFloor;
        private VisualElement _cleanSheen;
        private VisualElement _snowBank;
        private VisualElement _snowSpray;
        private VisualElement _slidePulse;
        private VisualElement _snowball;
        private VisualElement _snowballShadow;
        private Label _loadingDots;
        private Coroutine _feelStartRoutine;
        private Coroutine _loadRoutine;
        private bool _editorPreviewReady;
        private static string _pendingScenePath;
        private string _nextScenePath;

        // 현재 목적지는 하나뿐이다(SinglePlay 요청 흐름) — Local Route의 다른 스테이지가 생기면
        // PPack.SelectedStage PlayerPrefs 값을 읽어 경로를 고르는 조회로 바뀔 것이다.
        private const string GameplayScenePath =
            "Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity";
        private const string LoadingScenePath = "Assets/Game/OutGame/UI/LoadingScreen/Scenes/LoadingScreen.unity";

        public static void Open()
        {
            Open(GameplayScenePath);
        }

        public static void Open(string nextScenePath)
        {
            _pendingScenePath = nextScenePath;
            SceneManager.LoadScene(LoadingScenePath, LoadSceneMode.Single);
        }

#if UNITY_EDITOR
        private const string DirectEditorPreviewSessionKey = "PPack.LoadingScreen.DirectEditorPreview";
        private double _editorPreviewStartedAt;

        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditorPlayModeGuard()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= CaptureEditorPlayModeOrigin;
            UnityEditor.EditorApplication.playModeStateChanged += CaptureEditorPlayModeOrigin;
        }

        private static void CaptureEditorPlayModeOrigin(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingEditMode)
            {
                bool startsFromLoadingScene = SceneManager.GetActiveScene().path == LoadingScenePath;
                UnityEditor.SessionState.SetBool(DirectEditorPreviewSessionKey, startsFromLoadingScene);
            }
            else if (state == UnityEditor.PlayModeStateChange.EnteredEditMode)
            {
                UnityEditor.SessionState.SetBool(DirectEditorPreviewSessionKey, false);
            }
        }
#endif

        private void OnEnable()
        {
            _editorPreviewReady = false;
            _root = GetComponent<UIDocument>().rootVisualElement;

            if (Application.isPlaying)
            {
                _nextScenePath = string.IsNullOrEmpty(_pendingScenePath) ? GameplayScenePath : _pendingScenePath;
                _pendingScenePath = null;
            }

            if (!Application.isPlaying)
            {
#if UNITY_EDITOR
                _editorPreviewStartedAt = UnityEditor.EditorApplication.timeSinceStartup;
                UnityEditor.EditorApplication.update -= UpdateEditorPreview;
                UnityEditor.EditorApplication.update += UpdateEditorPreview;
#endif
                ShowEditorPreview();
                _root.schedule.Execute(ShowEditorPreview).StartingIn(80);
                return;
            }

            CacheElements();
            ResetPresentation();
            _feelStartRoutine = StartCoroutine(StartFeelMotionAfterFirstFrames());
            _loadRoutine = StartCoroutine(LoadNextScene());
        }

        private void Update()
        {
            if (!Application.isPlaying || _feelMotionDriver == null)
            {
                return;
            }

            UpdatePresentation(Mathf.Clamp01(_feelMotionDriver.localPosition.x));
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= UpdateEditorPreview;
#endif
            _editorPreviewReady = false;

            if (_feelStartRoutine != null)
            {
                StopCoroutine(_feelStartRoutine);
                _feelStartRoutine = null;
            }

            if (_loadRoutine != null)
            {
                StopCoroutine(_loadRoutine);
                _loadRoutine = null;
            }
        }

#if UNITY_EDITOR
        private void UpdateEditorPreview()
        {
            if (this == null || Application.isPlaying)
            {
                return;
            }

            UIDocument document = GetComponent<UIDocument>();
            VisualElement liveRoot = document != null ? document.rootVisualElement : null;
            if (liveRoot == null)
            {
                return;
            }

            bool previewIsCurrent = _editorPreviewReady && ReferenceEquals(_root, liveRoot);
            if (!previewIsCurrent)
            {
                _root = liveRoot;
                CacheElements();
                _editorPreviewReady = _animationStage != null;
            }

            if (!_editorPreviewReady)
            {
                return;
            }

            float elapsed = (float)(UnityEditor.EditorApplication.timeSinceStartup - _editorPreviewStartedAt);
            float previewProgress = Mathf.Repeat(elapsed / _traversalDuration, 1f);
            UpdatePresentation(previewProgress);

            _root.MarkDirtyRepaint();
        }
#endif

        private void CacheElements()
        {
            _backgroundDecor = _root.Q<VisualElement>("background-decor");
            _animationStage = _root.Q<VisualElement>("animation-stage");
            _cleanedFloor = _root.Q<VisualElement>("cleaned-floor");
            _cleanSheen = _root.Q<VisualElement>("clean-sheen");
            _snowBank = _root.Q<VisualElement>("dust-line");
            _snowSpray = _root.Q<VisualElement>("snow-spray");
            _slidePulse = _root.Q<VisualElement>("slide-pulse");
            _snowball = _root.Q<VisualElement>("snowball");
            _snowballShadow = _root.Q<VisualElement>("snowball-shadow");
            _loadingDots = _root.Q<Label>("loading-dots");

            _cleanGlints.Clear();
            for (int i = 0; i < CleanGlintNames.Length; i++)
            {
                VisualElement glint = _root.Q<VisualElement>(CleanGlintNames[i]);
                if (glint != null)
                {
                    _cleanGlints.Add(glint);
                }
            }

            _snowArcs.Clear();
            for (int i = 0; i < SnowArcNames.Length; i++)
            {
                VisualElement arc = _root.Q<VisualElement>(SnowArcNames[i]);
                if (arc != null)
                {
                    _snowArcs.Add(arc);
                }
            }

            _snowChips.Clear();
            for (int i = 0; i < SnowChipNames.Length; i++)
            {
                VisualElement chip = _root.Q<VisualElement>(SnowChipNames[i]);
                if (chip != null)
                {
                    _snowChips.Add(chip);
                }
            }

            CacheElementsByClass(DecorRingClasses, _decorRings);
            CacheElementsByClass(DecorStripeClasses, _decorStripes);
            CacheElementsByClass(DecorSparkleClasses, _decorSparkles);
            CacheElementsByClass(DecorSwooshClasses, _decorSwooshes);

        }

        private void CacheElementsByClass(string[] classNames, List<VisualElement> elements)
        {
            elements.Clear();
            for (int i = 0; i < classNames.Length; i++)
            {
                VisualElement element = _root.Q<VisualElement>(className: classNames[i]);
                if (element != null)
                {
                    elements.Add(element);
                }
            }
        }

        private void ResetPresentation()
        {
            UpdatePresentation(0f);
        }

        private void ShowEditorPreview()
        {
            if (Application.isPlaying || _root == null)
            {
                return;
            }

            CacheElements();
            UpdatePresentation(0.12f);

            _editorPreviewReady = _animationStage != null;
        }

        private void UpdatePresentation(float normalizedTravel)
        {
            if (_root == null || _animationStage == null)
            {
                return;
            }

            float stageWidth = _animationStage.resolvedStyle.width;
            if (float.IsNaN(stageWidth) || stageWidth <= 0f)
            {
                stageWidth = _root.resolvedStyle.width;
            }

            float travelPixels = normalizedTravel * stageWidth;
            float elapsed = normalizedTravel * _traversalDuration;
            float scrubFeel = Application.isPlaying && _scrubFeelDriver != null
                ? Mathf.Clamp(_scrubFeelDriver.localPosition.x, -1f, 1f)
                : Mathf.Sin(elapsed * Mathf.PI * 3.6f);
            float scrubPixels = scrubFeel * 22f;
            float visualTravelPixels = Mathf.Clamp(travelPixels + scrubPixels, 0f, stageWidth);

            UpdateSnowball(normalizedTravel, visualTravelPixels, elapsed, scrubFeel);
            UpdateBackgroundDecor(elapsed);
            float visualTravel = stageWidth > 0f ? visualTravelPixels / stageWidth : normalizedTravel;
            UpdateRouteClearing(visualTravel, stageWidth, elapsed);
            UpdateLoadingText(Application.isPlaying ? Time.unscaledTime : elapsed);
        }

        private void UpdateSnowball(float normalizedTravel, float travelPixels, float elapsed, float scrubFeel)
        {
            float growth = Mathf.SmoothStep(0.72f, 1.18f, normalizedTravel);
            float bounce = Mathf.Abs(Mathf.Sin(elapsed * Mathf.PI * 2.8f)) * 3.5f;

            if (_snowball != null)
            {
                _snowball.style.translate = new Translate(travelPixels, -bounce, 0f);
                _snowball.style.rotate = new Rotate(-normalizedTravel * 1080f - scrubFeel * 4f);
                _snowball.style.scale = new Scale(new Vector2(growth, growth));
                _snowball.style.opacity = 1f;
            }

            if (_snowballShadow != null)
            {
                float shadowScaleX = growth * (1f - bounce * 0.015f);
                _snowballShadow.style.translate = new Translate(travelPixels, 0f, 0f);
                _snowballShadow.style.scale = new Scale(new Vector2(shadowScaleX, 0.82f + growth * 0.16f));
                _snowballShadow.style.opacity = 0.3f - bounce * 0.012f;
            }
        }

        private void UpdateBackgroundDecor(float elapsed)
        {
            float feel = Application.isPlaying && _backgroundFeelDriver != null
                ? Mathf.Clamp(_backgroundFeelDriver.localPosition.y, -1f, 1f)
                : Mathf.Sin(elapsed * Mathf.PI * 0.56f);

            if (_backgroundDecor != null)
            {
                _backgroundDecor.style.translate = new Translate(feel * 8f, feel * -4f, 0f);
                float rootScale = 1f + feel * 0.006f;
                _backgroundDecor.style.scale = new Scale(new Vector2(rootScale, rootScale));
            }

            for (int i = 0; i < _decorRings.Count; i++)
            {
                float direction = i % 2 == 0 ? 1f : -1f;
                float scale = 1f + feel * direction * (0.035f + i * 0.006f);
                _decorRings[i].style.scale = new Scale(new Vector2(scale, scale));
                _decorRings[i].style.translate = new Translate(feel * direction * (3f + i * 1.5f), 0f, 0f);
            }

            for (int i = 0; i < _decorStripes.Count; i++)
            {
                float direction = i % 2 == 0 ? -1f : 1f;
                _decorStripes[i].style.translate = new Translate(feel * direction * (5f + i * 2f), feel * (2f + i), 0f);
            }

            for (int i = 0; i < _decorSwooshes.Count; i++)
            {
                float direction = i == 0 ? 1f : -1f;
                _decorSwooshes[i].style.translate = new Translate(feel * direction * 12f, feel * direction * 3f, 0f);
                _decorSwooshes[i].style.scale = new Scale(new Vector2(1f + feel * direction * 0.012f, 1f));
            }

            for (int i = 0; i < _decorSparkles.Count; i++)
            {
                float direction = i % 2 == 0 ? 1f : -1f;
                float pulse = 1f + feel * direction * 0.14f;
                _decorSparkles[i].style.scale = new Scale(new Vector2(pulse, pulse));
                _decorSparkles[i].style.opacity = 0.72f + feel * direction * 0.2f;
            }
        }

        private void UpdateLoadingText(float elapsed)
        {
            if (_loadingDots == null)
            {
                return;
            }

            int index = Mathf.FloorToInt(elapsed / 0.42f) % LoadingDotTexts.Length;
            _loadingDots.text = LoadingDotTexts[index];
        }

        private void UpdateRouteClearing(float normalizedTravel, float stageWidth, float elapsed)
        {
            float routeStart = stageWidth * 0.04f;
            float contactPosition = normalizedTravel * stageWidth;
            float cleanBoundary = Mathf.Clamp(contactPosition, routeStart, stageWidth * 0.96f);
            bool isClearing = normalizedTravel > 0.035f && normalizedTravel < 0.965f;

            if (_cleanedFloor != null)
            {
                _cleanedFloor.style.left = routeStart;
                _cleanedFloor.style.width = Mathf.Max(0f, cleanBoundary - routeStart);
            }

            if (_cleanSheen != null)
            {
                float sheenPulse = 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(elapsed * Mathf.PI * 3.2f));
                _cleanSheen.style.opacity = normalizedTravel > 0.08f ? 0.58f * sheenPulse : 0f;
                _cleanSheen.style.scale = new Scale(new Vector2(0.7f + 0.3f * sheenPulse, 1f));
            }

            if (_snowBank != null)
            {
                float routeEnd = stageWidth * 0.96f;
                _snowBank.style.left = cleanBoundary;
                _snowBank.style.width = Mathf.Max(0f, routeEnd - cleanBoundary);
            }

            UpdateSnowSpray(elapsed, contactPosition, isClearing);

            for (int i = 0; i < _cleanGlints.Count; i++)
            {
                float pulse = 0.18f + 0.82f * Mathf.Abs(Mathf.Sin((normalizedTravel * 18f - i * 0.8f) * Mathf.PI));
                _cleanGlints[i].style.opacity = normalizedTravel > 0.08f ? pulse : 0f;
                _cleanGlints[i].style.scale = new Scale(new Vector2(pulse, pulse));
            }
        }

        private void UpdateSnowSpray(float elapsed, float contactPosition, bool isClearing)
        {
            if (_snowSpray != null)
            {
                _snowSpray.style.translate = new Translate(contactPosition - 30f, 0f, 0f);
                _snowSpray.style.opacity = isClearing ? 1f : 0f;
            }

            for (int i = 0; i < _snowArcs.Count; i++)
            {
                float cycle = Mathf.Repeat(elapsed * 1.55f + i * 0.27f, 1f);
                float x = Mathf.Lerp(184f, 34f, cycle);
                float y = 43f + i * 19f + Mathf.Sin(cycle * Mathf.PI) * (i - 1) * 9f;
                float alpha = Mathf.Sin(cycle * Mathf.PI) * 0.92f;
                float scaleX = Mathf.Lerp(1.05f, 0.24f, cycle);
                float scaleY = Mathf.Lerp(0.8f, 1.2f, cycle);

                _snowArcs[i].style.translate = new Translate(x, y, 0f);
                _snowArcs[i].style.rotate = new Rotate(Mathf.Lerp(i * 7f - 9f, 0f, cycle));
                _snowArcs[i].style.scale = new Scale(new Vector2(scaleX, scaleY));
                _snowArcs[i].style.opacity = isClearing ? alpha : 0f;
            }

            for (int i = 0; i < _snowChips.Count; i++)
            {
                float cycle = Mathf.Repeat(elapsed * 2.25f + i * 0.23f, 1f);
                float x = Mathf.Lerp(212f, 38f, cycle);
                float wave = Mathf.Sin((cycle * 2f + i * 0.4f) * Mathf.PI);
                float y = 34f + i * 18f + wave * 13f;
                float alpha = Mathf.Sin(cycle * Mathf.PI) * 0.9f;
                float scale = Mathf.Lerp(1f, 0.24f, cycle);

                _snowChips[i].style.translate = new Translate(x, y, 0f);
                _snowChips[i].style.rotate = new Rotate(cycle * 220f + i * 31f);
                _snowChips[i].style.scale = new Scale(new Vector2(scale, scale));
                _snowChips[i].style.opacity = isClearing ? alpha : 0f;
            }

            if (_slidePulse != null)
            {
                float pulse = Mathf.Repeat(elapsed * 2.8f, 1f);
                float scale = Mathf.Lerp(0.5f, 1.35f, pulse);
                _slidePulse.style.scale = new Scale(new Vector2(scale, scale));
                _slidePulse.style.opacity = isClearing ? (1f - pulse) * 0.76f : 0f;
            }
        }

        private IEnumerator StartFeelMotionAfterFirstFrames()
        {
            yield return null;
            yield return null;
            _playFeelMotion?.Invoke();
            _feelStartRoutine = null;
        }

        private IEnumerator LoadNextScene()
        {
#if UNITY_EDITOR
            if (_previewWhenStartedDirectlyInEditor &&
                UnityEditor.SessionState.GetBool(DirectEditorPreviewSessionKey, false))
            {
                SetPreviewStatus();
                yield break;
            }
#endif

            if (!_autoLoadNextScene)
            {
                SetPreviewStatus();
                yield break;
            }

            // SceneManager.LoadScene activates this scene at the end of the current frame.
            // Starting another scene load from OnEnable in that same frame can let a small
            // destination scene activate before allowSceneActivation is disabled, skipping
            // the loading screen entirely. Give UI Toolkit one rendered frame first.
            float startedAt = Time.realtimeSinceStartup;
            yield return null;

            AsyncOperation operation = SceneManager.LoadSceneAsync(_nextScenePath, LoadSceneMode.Single);
            if (operation == null)
            {
                SetPreviewStatus();
                yield break;
            }

            operation.allowSceneActivation = false;

            while (!operation.isDone)
            {
                bool minimumTimeReached = Time.realtimeSinceStartup - startedAt >= _minimumDisplaySeconds;
                if (operation.progress >= 0.9f && minimumTimeReached)
                {
                    yield return new WaitForSecondsRealtime(0.18f);
                    operation.allowSceneActivation = true;
                    _loadRoutine = null;
                    yield break;
                }

                yield return null;
            }

            _loadRoutine = null;
        }

        private void SetPreviewStatus()
        {
            _loadRoutine = null;
        }
    }
}
