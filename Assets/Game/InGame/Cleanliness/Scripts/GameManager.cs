using System;
using UnityEngine;

namespace PPack
{
    public enum EGamePhase
    {
        Intro,
        Playing,
        Ended,
    }

    /// <summary>전역 게임 진행의 권위. 페이즈(Intro→Playing→Ended), 전역 시간 풀, 점수(=돈)만
    /// 소유한다. 눈덩이도 의뢰 내용도 모른다 — 의뢰 완료 이벤트를 받아 점수·시간만 갱신한다.
    ///
    /// <para><b>전역 시간 0이 유일한 종료다.</b> 의뢰 만료(<see cref="RequestDirector.RequestExpired"/>)는
    /// 구독조차 하지 않으므로 게임을 끝낼 수 없다 — "시간 못 받는 것"이 만료의 유일한 처벌이다.
    /// 루트·Cleanliness AGENTS 규칙대로 오케스트레이션 자리(<see cref="SinglePlayDirector"/> 선례)다.</para>
    ///
    /// <para>밸런스는 <see cref="StageBalanceConfig"/>를 매 틱 읽어 실시간 반영한다.
    /// Fusion 전이 대비로 <c>FixedUpdate</c>/<c>Time.fixedDeltaTime</c>으로 돈다.</para></summary>
    public sealed class GameManager : MonoBehaviour
    {
        [SerializeField] private StageBalanceConfig _config;
        [SerializeField] private RequestDirector _requests;

        [Tooltip("판이 얻은 증강. 비어 있으면 효과가 없고 기존 동작 그대로다.")]
        [SerializeField] private AugmentLoadout _augments;

        /// <summary>
        /// 쉬는 시간 게이트. <b>비어 있으면 게이트가 없는 것과 같다</b> — 증강을 놓지 않은 씬과
        /// 테스트가 영향받지 않게 하려는 것이고, <see cref="RequestDirector"/> 와 같은 규약이다.
        /// </summary>
        [SerializeField] private AugmentSelectionDirector _intermission;

        public EGamePhase Phase { get; private set; }
        public float RemainingSeconds { get; private set; }
        public int Score { get; private set; }

        public event Action<float> TimeChanged;

        /// <summary>의뢰 완료로 실제 더해진 시간(초). 상한에 걸리면 요청한 보너스보다 작다 —
        /// 화면에 띄우는 값은 요청량이 아니라 이쪽이어야 거짓말을 안 한다.</summary>
        public event Action<float> TimeGranted;
        public event Action<int> ScoreChanged;
        public event Action GameStarted;
        public event Action GameEnded;

        public void Configure(StageBalanceConfig config, RequestDirector requests)
        {
            UnsubscribeRequests();
            _config = config;
            _requests = requests;
            SubscribeRequests();
        }

        /// <summary>증강 로드아웃을 꽂는다. 씬에서는 인스펙터가 채우고, 테스트는 이것을 쓴다.</summary>
        public void SetAugments(AugmentLoadout augments) => _augments = augments;

        private void OnEnable() => SubscribeRequests();
        private void OnDisable() => UnsubscribeRequests();

        private void SubscribeRequests()
        {
            if (_requests == null) return;
            _requests.RequestCompleted -= OnRequestCompleted;
            _requests.RequestCompleted += OnRequestCompleted;
        }

        private void UnsubscribeRequests()
        {
            if (_requests == null) return;
            _requests.RequestCompleted -= OnRequestCompleted;
        }

        /// <summary>Intro를 건너뛰고 바로 Playing으로 들어간다(테스트·간이 씬용).</summary>
        public void BeginPlaying()
        {
            if (Phase != EGamePhase.Intro) return;
            Phase = EGamePhase.Playing;
            RemainingSeconds = _config != null ? _config.StartSeconds : 60f;
            TimeChanged?.Invoke(RemainingSeconds);
            if (_requests != null && !_requests.IsRunning) _requests.Begin();
            GameStarted?.Invoke();
        }

        private void FixedUpdate()
        {
            if (Phase != EGamePhase.Playing) return;

            // 쉬는 시간에는 스테이지 시계도 멈춘다 (2026-09-01). RequestDirector 의 스폰과 TTL 은
            // 이미 멈추는데 이것만 계속 깎이면 증강을 고르는 동안 판이 끝날 수 있고,
            // 그러면 "고르느라 졌다" 가 된다. 고르는 시간은 판의 시간이 아니다.
            if (IsIntermission) return;

            RemainingSeconds -= Time.fixedDeltaTime;
            if (RemainingSeconds <= 0f)
            {
                RemainingSeconds = 0f;
                TimeChanged?.Invoke(RemainingSeconds);
                EndGame();
                return;
            }
            TimeChanged?.Invoke(RemainingSeconds);
        }

        /// <summary>테스트가 씬 배선 없이 게이트를 문다.</summary>
        public void SetIntermission(AugmentSelectionDirector intermission) =>
            _intermission = intermission;

        private bool IsIntermission => _intermission != null && _intermission.IsOpen;

        private void OnRequestCompleted(GiftRequest request) => NotifyRequestCompleted(request);

        /// <summary>의뢰 완료 시 점수·전역 시간을 더한다. 디렉터 구독 핸들러이자 테스트가 직접 부를 수
        /// 있는 정산 진입점이다.</summary>
        public void NotifyRequestCompleted(GiftRequest request)
        {
            if (Phase != EGamePhase.Playing || request == null) return;

            // 증강은 값이 실제로 쓰이는 순간에 곱한다 — 보상과 추가시간은 스폰이 아니라 여기서
            // 지급되므로, 방금 고른 증강이 이미 떠 있던 의뢰에도 바로 걸린다(스펙 §7).
            int reward = request.Reward;
            float bonusSeconds = request.TimeBonusSeconds;
            if (_augments != null)
            {
                reward = Mathf.RoundToInt(reward * _augments.GetMultiplier(EAugmentStat.Reward));
                bonusSeconds *= _augments.GetMultiplier(EAugmentStat.ClearTimeBonus);
            }

            Score += reward;
            ScoreChanged?.Invoke(Score);

            float before = RemainingSeconds;
            RemainingSeconds += bonusSeconds;
            if (_config != null && _config.MaxSeconds > 0f)
                RemainingSeconds = Mathf.Min(RemainingSeconds, _config.MaxSeconds);
            TimeChanged?.Invoke(RemainingSeconds);

            float granted = RemainingSeconds - before;
            if (granted > 0f) TimeGranted?.Invoke(granted);
        }

        private void EndGame()
        {
            if (Phase == EGamePhase.Ended) return;
            Phase = EGamePhase.Ended;
            RemainingSeconds = 0f;
            GameEnded?.Invoke();
        }
    }
}
