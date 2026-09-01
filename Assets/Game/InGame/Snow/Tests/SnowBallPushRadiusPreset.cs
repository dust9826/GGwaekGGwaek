#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    /// <summary>
    /// <c>Snow_BallPush_Test</c> 전용 반지름 입력 UI. 생산 씬과 프리팹에는 붙이지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowBallPushRadiusPreset : MonoBehaviour
    {
        private const string RadiusInputControl = "SnowBallRadiusInput";
        private const string TestScenePath =
            "Assets/Game/InGame/Snow/Tests/Snow_BallPush_Test.unity";
        private const float MaximumPresetEquivalentRadiusM = 3f;

        private static SnowBallPushRadiusPreset _instance;

        [SerializeField] private string _radiusText = "1.5";

        private bool _applyRequested;
        private bool _activeInTestScene;
        private bool _editing = true;
        private bool _focusInput = true;
        private string _status = "반지름을 입력하고 Enter 또는 적용 버튼을 누르세요.";
        private readonly List<GrowthSample> _growthSamples = new List<GrowthSample>(4096);
        private SnowBallCarrier _measuredBall;
        private int _lastMeasuredMassMm;
        private float _measurementStartedAt = -1f;
        private bool _measurementCompleted;

        private readonly struct GrowthSample
        {
            public readonly float TimeSeconds;
            public readonly int MassMm;

            public GrowthSample(float timeSeconds, int massMm)
            {
                TimeSeconds = timeSeconds;
                MassMm = massMm;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallInTestScene()
        {
            if (!IsTestScene(SceneManager.GetActiveScene())) return;
            if (_instance != null) return;

            var host = new GameObject("__TEST__SnowBallRadiusInput");
            _instance = host.AddComponent<SnowBallPushRadiusPreset>();
        }

        [InitializeOnEnterPlayMode]
        private static void ScheduleInstall(EnterPlayModeOptions options)
        {
            EditorApplication.update -= InstallAfterPlayModeStarts;
            EditorApplication.update += InstallAfterPlayModeStarts;
        }

        private static void InstallAfterPlayModeStarts()
        {
            if (!EditorApplication.isPlaying) return;

            EditorApplication.update -= InstallAfterPlayModeStarts;
            InstallInTestScene();
        }

        private void OnEnable()
        {
            if (!IsRunningInTestScene())
            {
                enabled = false;
                return;
            }

            _activeInTestScene = true;
            _instance = this;
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
            BeginEditing();
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
            if (!_activeInTestScene) return;

            _activeInTestScene = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void HandleActiveSceneChanged(Scene previous, Scene next)
        {
            if (IsTestScene(next)) return;
            enabled = false;
        }

        private void FixedUpdate()
        {
            RecordNaturalGrowth();
            if (!_applyRequested) return;
            _applyRequested = false;

            string normalized = _radiusText.Replace(',', '.');
            if (!float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float radiusM) || float.IsNaN(radiusM) || float.IsInfinity(radiusM))
            {
                _status = "숫자를 입력하세요. 예: 3 또는 2.5";
                return;
            }

            if (ApplyRadius(radiusM)) EndEditing();
        }

        private void RecordNaturalGrowth()
        {
            SnowBallCarrier ball = FindFirstObjectByType<SnowBallCarrier>();
            if (ball != _measuredBall)
            {
                _measuredBall = ball;
                _lastMeasuredMassMm = ball != null ? ball.MassMm : 0;
                _measurementStartedAt = -1f;
                _measurementCompleted = false;
                _growthSamples.Clear();
                return;
            }

            if (ball == null || _measurementCompleted || ball.MassMm <= _lastMeasuredMassMm) return;
            if (_measurementStartedAt < 0f) _measurementStartedAt = Time.fixedTime;

            float elapsed = Time.fixedTime - _measurementStartedAt;
            _growthSamples.Add(new GrowthSample(elapsed, ball.MassMm));
            _lastMeasuredMassMm = ball.MassMm;
            if (!ball.IsOverSizeThreshold || elapsed < 0.5f) return;

            var thresholds = new Vector3(
                SampleMass01At(elapsed * 0.25f, ball.VisibleMaxMassMm),
                SampleMass01At(elapsed * 0.50f, ball.VisibleMaxMassMm),
                SampleMass01At(elapsed * 0.75f, ball.VisibleMaxMassMm));
            _measurementCompleted = true;
            _status = $"자연 성장 {elapsed:0.0}s · 단계 질량 {thresholds.x:0.000} / " +
                      $"{thresholds.y:0.000} / {thresholds.z:0.000}";
            Debug.Log($"{nameof(SnowBallPushRadiusPreset)}: {_status}", ball);
        }

        private float SampleMass01At(float targetSeconds, long maximumMassMm)
        {
            if (_growthSamples.Count == 0 || maximumMassMm <= 0) return 0f;

            GrowthSample nearest = _growthSamples[0];
            float nearestDelta = Mathf.Abs(nearest.TimeSeconds - targetSeconds);
            for (int i = 1; i < _growthSamples.Count; i++)
            {
                float delta = Mathf.Abs(_growthSamples[i].TimeSeconds - targetSeconds);
                if (delta >= nearestDelta) continue;
                nearest = _growthSamples[i];
                nearestDelta = delta;
            }
            return nearest.MassMm / (float)maximumMassMm;
        }

        /// <summary>입력 UI와 자동 검증이 공유하는 즉시 적용 경로.</summary>
        public bool ApplyRadius(float radiusM)
        {
            if (!IsRunningInTestScene()) return false;

            if (radiusM < SnowBallCpu.SeedRadiusM || radiusM > MaximumPresetEquivalentRadiusM)
            {
                _status = $"환산 반지름은 {SnowBallCpu.SeedRadiusM:0.00}~" +
                          $"{MaximumPresetEquivalentRadiusM:0.00}m 사이여야 합니다.";
                return false;
            }

            PenguinSnowball penguin = FindFirstObjectByType<PenguinSnowball>();
            if (penguin != null && penguin.Held == null) penguin.BeginPush();

            SnowBallCarrier ball = penguin != null ? penguin.Held : null;
            if (ball == null) ball = FindFirstObjectByType<SnowBallCarrier>();

            if (ball == null)
            {
                _status = "눈덩이를 만들 수 없습니다. 펭귄을 눈밭 위로 옮겨 다시 시도하세요.";
                Debug.LogWarning($"{nameof(SnowBallPushRadiusPreset)}: {_status}", this);
                return false;
            }

            float previousRadiusM = ball.RadiusM;
            Vector3 supportNormal = ball.HasSupport ? ball.SupportNormal.normalized : Vector3.up;
            if (supportNormal.sqrMagnitude < 0.5f) supportNormal = Vector3.up;

            ball.ServerApplyMass(SnowBallCpu.MassMmForRadius(radiusM));
            float radiusDeltaM = ball.RadiusM - previousRadiusM;

            Rigidbody ballBody = ball.GetComponent<Rigidbody>();
            if (ballBody != null)
            {
                ballBody.position += supportNormal * radiusDeltaM;
                ballBody.linearVelocity = Vector3.zero;
                ballBody.angularVelocity = Vector3.zero;
            }
            else
            {
                ball.transform.position += supportNormal * radiusDeltaM;
            }

            MovePenguinOutsideBall(penguin, ball);

            // 필드에서 같은 질량을 걷지 않고 크기만 맞춘다. 밀기 감각을 빠르게 비교하기 위한
            // 테스트 프리셋이며, 눈 보존량이나 자연 성장 속도 검증에는 쓰지 않는다.
            _status = $"적용 완료: 표시 {ball.RadiusM:0.00}m / 환산 {ball.EquivalentRadiusM:0.00}m / " +
                      $"이동 {ball.Mobility01:P0}";
            Debug.Log($"{nameof(SnowBallPushRadiusPreset)}: {_status}", ball);
            return true;
        }

        private static void MovePenguinOutsideBall(PenguinSnowball penguin, SnowBallCarrier ball)
        {
            if (penguin == null || penguin.Held != ball) return;

            Vector3 outward = penguin.transform.position - ball.transform.position;
            outward.y = 0f;
            if (outward.sqrMagnitude < 1e-4f) outward = -penguin.transform.forward;

            float currentDistanceM = outward.magnitude;
            CapsuleCollider capsule = penguin.GetComponent<CapsuleCollider>();
            float bodyRadiusM = capsule != null
                ? capsule.radius * Mathf.Max(Mathf.Abs(penguin.transform.localScale.x),
                    Mathf.Abs(penguin.transform.localScale.z))
                : 0.4f;
            float moveDistanceM = ball.RadiusM + bodyRadiusM + 0.02f - currentDistanceM;
            if (moveDistanceM <= 0f) return;

            outward.Normalize();

            Rigidbody penguinBody = penguin.GetComponent<Rigidbody>();
            if (penguinBody != null)
            {
                penguinBody.position += outward * moveDistanceM;
                penguinBody.linearVelocity = Vector3.zero;
                penguinBody.angularVelocity = Vector3.zero;
                return;
            }

            penguin.transform.position += outward * moveDistanceM;
        }

        private void OnGUI()
        {
            if (!IsRunningInTestScene()) return;

            SnowBallCarrier ball = FindFirstObjectByType<SnowBallCarrier>();
            string currentRadius = ball == null
                ? "현재 눈덩이 없음"
                : $"표시 {ball.RadiusM:0.00}m / 환산 {ball.EquivalentRadiusM:0.00}m / " +
                  $"{ball.GrowthStage} / 이동 {ball.Mobility01:P0}";

            Event current = Event.current;
            if (!_editing)
            {
                GUI.Box(new Rect(8f, 8f, 650f, 54f), GUIContent.none);
                GUI.Label(new Rect(18f, 14f, 630f, 22f),
                    $"{currentRadius}  |  R: 환산 반지름 입력");
                GUI.Label(new Rect(18f, 36f, 630f, 22f), "W/A/D: 눈덩이 밀기");

                if (current.type == EventType.KeyDown && current.keyCode == KeyCode.R)
                {
                    BeginEditing();
                    current.Use();
                }
                return;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            GUI.Box(new Rect(8f, 8f, 620f, 112f), GUIContent.none);
            GUI.Label(new Rect(18f, 14f, 130f, 24f), "환산 반지름(m)");

            GUI.SetNextControlName(RadiusInputControl);
            _radiusText = GUI.TextField(new Rect(150f, 14f, 90f, 24f), _radiusText, 8);
            bool submit = GUI.Button(new Rect(248f, 14f, 100f, 24f), "생성/적용");

            if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
            {
                EndEditing();
                current.Use();
            }
            else if (current.type == EventType.KeyDown &&
                (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter))
            {
                submit = true;
                current.Use();
            }

            if (_focusInput)
            {
                GUI.FocusControl(RadiusInputControl);
                _focusInput = false;
            }

            if (submit) _applyRequested = true;

            GUI.Label(new Rect(18f, 44f, 590f, 22f), $"{currentRadius}  |  {_status}");
            GUI.Label(new Rect(18f, 70f, 590f, 22f), "1.5m 초과 입력은 보이는 크기 대신 압축 질량과 감속을 늘립니다.");
            GUI.Label(new Rect(18f, 92f, 590f, 22f), "Enter: 적용 후 조작 복귀 · Esc: 취소 · 밀기 감각 전용");
        }

        private void BeginEditing()
        {
            _editing = true;
            _focusInput = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void EndEditing()
        {
            _editing = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private bool IsRunningInTestScene()
        {
            return IsTestScene(gameObject.scene) && IsTestScene(SceneManager.GetActiveScene());
        }

        private static bool IsTestScene(Scene scene)
        {
            return scene.IsValid() && scene.isLoaded && scene.path == TestScenePath;
        }
    }
}
#endif
