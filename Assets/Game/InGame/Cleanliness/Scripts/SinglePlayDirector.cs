using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    /// <summary>싱글플레이 무한 배송 루프 권위. Intro→Playing→Ended 전이와 주문 실패 시점의
    /// 기록 스냅샷을 소유한다. 결과 화면의 존재를 모른다.</summary>
    public sealed class SinglePlayDirector : MonoBehaviour
    {
        /// <summary>주변 차량 차선에서 3.86m 떨어진 남쪽 공터의 SinglePlay 시작점.</summary>
        public static readonly Vector3 PlayerStart = new Vector3(-4f, 0.31f, -12f);

        [Header("씬 리그")]
        [SerializeField] private StageIntroController _introController;
        [SerializeField] private GiftDeliveryDirector _giftDeliveryDirector;
        [SerializeField] private SnowCpuStage _snowStage;
        [SerializeField] private PenguinInputReader _playerInput;

        [Header("타이밍")]
        [SerializeField, Min(0f)] private float _autoReturnToMenuSeconds = 3f;
        [SerializeField] private int _mainMenuBuildIndex;

        private long _initialSnowAmount;
        private float _elapsedSeconds;

        public EStagePhase Phase { get; private set; }
        public StageMetrics LastMetrics { get; private set; }
        public float ElapsedSeconds => _elapsedSeconds;

        public event Action StageEnded;

        private IEnumerator Start()
        {
            Rigidbody body = _playerInput != null ? _playerInput.GetComponent<Rigidbody>() : null;
            bool detectedCollisions = body != null && body.detectCollisions;
            if (body != null) body.detectCollisions = false;
            PlacePlayerAtSafeStart();

            // 원본 씬의 예전 시작점에서 안전 지점으로 옮길 때 ContinuousDynamic Rigidbody가
            // 그 거리를 한 스텝 속도로 해석하지 않도록 물리 장면이 새 자세를 받은 뒤 충돌을 켠다.
            yield return new WaitForFixedUpdate();
            yield return null;
            if (body != null) body.detectCollisions = detectedCollisions;
        }

        private void OnEnable()
        {
            Phase = EStagePhase.Intro;
            _elapsedSeconds = 0f;

            if (_playerInput != null) _playerInput.enabled = false;
            if (_giftDeliveryDirector != null)
            {
                _giftDeliveryDirector.GameOver -= OnDeliveryGameOver;
                _giftDeliveryDirector.GameOver += OnDeliveryGameOver;
                _giftDeliveryDirector.enabled = false;
            }

            if (_snowStage != null && _snowStage.Field != null)
                _initialSnowAmount = _snowStage.TotalHeightMm;

            if (_introController != null) _introController.Play();
        }

        private void OnDisable()
        {
            if (_giftDeliveryDirector != null) _giftDeliveryDirector.GameOver -= OnDeliveryGameOver;
        }

        /// <summary><see cref="StageIntroController"/>의 비공개 _introCompleted UnityEvent에
        /// 인스펙터에서 이 메서드를 연결한다. 코드에서 강제로 구독하지 않는다 — 컨트롤러가
        /// 이벤트를 공개로 노출하지 않기 때문이다.</summary>
        public void OnIntroFinished()
        {
            if (Phase != EStagePhase.Intro) return;

            Phase = EStagePhase.Playing;

            if (_playerInput != null) _playerInput.enabled = true;
            if (_giftDeliveryDirector != null)
            {
                _giftDeliveryDirector.enabled = true;
                _giftDeliveryDirector.Begin();
            }
        }

        private void Update()
        {
            if (Phase != EStagePhase.Playing) return;
            _elapsedSeconds += Time.deltaTime;
        }

        private void EndStage()
        {
            if (Phase == EStagePhase.Ended) return;
            Phase = EStagePhase.Ended;

            int completed = _giftDeliveryDirector != null ? _giftDeliveryDirector.CompletedCount : 0;
            int cancelled = _giftDeliveryDirector != null &&
                            _giftDeliveryDirector.Phase == EGiftDeliveryPhase.GameOver ? 1 : 0;
            int totalPoints = _giftDeliveryDirector != null ? _giftDeliveryDirector.TotalScore : 0;
            long currentSnowAmount = _snowStage != null ? _snowStage.TotalHeightMm : _initialSnowAmount;
            StageMetrics metrics = StageMetrics.Capture(
                completed, cancelled, totalPoints, currentSnowAmount, _initialSnowAmount);
            LastMetrics = metrics;
            if (_playerInput != null) _playerInput.enabled = false;
            if (_giftDeliveryDirector != null) _giftDeliveryDirector.enabled = false;

            Debug.Log($"SinglePlay 종료 — 기록 {_elapsedSeconds:F1}초 · " +
                      $"배송 {metrics.DeliveriesCompleted}건 · 실패 {metrics.DeliveriesCancelled}건 · " +
                      $"제설 {metrics.SnowClearedPercent01 * 100f:F0}% · 총점 {metrics.TotalPoints}");

            StageEnded?.Invoke();

            // 0이면 자동 복귀를 아예 안 켠다 — 결과 화면의 RETRY/CONTINUE 버튼이 대신 씬 전환을
            // 맡을 때 쓴다(InGame/UI/StageOutro/Scripts/StageOutroPresenter.cs). 이 기능은 그
            // 화면의 존재를 모르는 채로, 자기 필드값만 보고 결정한다.
            if (_autoReturnToMenuSeconds > 0f) StartCoroutine(ReturnToMenuAfterDelay());
        }

        private void OnDeliveryGameOver()
        {
            if (Phase == EStagePhase.Playing) EndStage();
        }

        private IEnumerator ReturnToMenuAfterDelay()
        {
            yield return new WaitForSeconds(_autoReturnToMenuSeconds);
            SceneManager.LoadScene(_mainMenuBuildIndex);
        }

        private void PlacePlayerAtSafeStart()
        {
            if (_playerInput == null) return;

            Transform player = _playerInput.transform;
            foreach (VehicleRespawnPoint spawn in FindObjectsByType<VehicleRespawnPoint>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (spawn.Player == player) spawn.transform.position = PlayerStart;
            }

            Rigidbody body = player.GetComponent<Rigidbody>();
            if (body == null)
            {
                player.position = PlayerStart;
                return;
            }

            body.position = PlayerStart;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.PublishTransform();
        }
    }
}
