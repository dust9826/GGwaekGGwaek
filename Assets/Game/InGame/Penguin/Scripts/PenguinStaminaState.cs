using System;
using UnityEngine;

namespace PPack
{
    /// <summary>체력의 조정값. 디자이너가 인스펙터에서 만지는 쪽이다 —
    /// 런타임 상태는 <see cref="PenguinStaminaState"/> 가 따로 가진다.</summary>
    [Serializable]
    public struct PenguinStaminaTuning
    {
        [Tooltip("쉬지 않고 달릴 수 있는 시간(초).")]
        [Min(0.1f)] public float SprintSeconds;

        [Tooltip("바닥에서 만충까지 걸리는 시간(초).")]
        [Min(0.1f)] public float RefillSeconds;

        [Tooltip("달리기를 멈춘 뒤 회복이 시작되기까지의 지연(초). Shift를 톡톡 눌러 무한 질주하는 것을 막는다.")]
        [Min(0f)] public float RefillDelaySeconds;

        [Tooltip("탈진이 풀리는 체력 비율. 0에서 바로 풀면 끊기는 '딱딱이 달리기'가 된다.")]
        [Range(0f, 1f)] public float ExhaustExit01;

        public PenguinStaminaTuning(float sprintSeconds, float refillSeconds,
            float refillDelaySeconds, float exhaustExit01)
        {
            SprintSeconds = sprintSeconds;
            RefillSeconds = refillSeconds;
            RefillDelaySeconds = refillDelaySeconds;
            ExhaustExit01 = exhaustExit01;
        }
    }

    /// <summary>
    /// 달리기 체력의 런타임 상태. <b>순수 값이다</b> — <c>MonoBehaviour</c> 도
    /// <c>ScriptableObject</c> 도 아니라서 씬 없이 EditMode 에서 그대로 검증된다
    /// (<see cref="StageMetrics"/> 와 같은 패턴).
    ///
    /// <para><b>왜 컴포넌트가 아닌가.</b> 자기 <c>Update</c> 로 돌면 서버 틱과 어긋난다. 결국
    /// <see cref="PenguinLocomotion.Step"/> 이 틱해 줘야 하는데, 그러면 컴포넌트로 만든 대가로
    /// 프리팹 배선만 늘고 얻는 것이 없다.</para>
    ///
    /// <para><b>권위는 저절로 따라온다.</b> 예측을 켜지 않았으므로 <c>Step</c> 은 서버에서만
    /// 돈다 — 그 안에서 깎는 이 값은 그 자체로 서버가 정한 값이다.</para>
    /// </summary>
    public struct PenguinStaminaState
    {
        private float _value01;
        private float _refillDelayRemaining;
        private bool _exhausted;

        /// <summary>0~1. HUD 의 체력 바가 읽는 값이다.</summary>
        public float Value01 => _value01;

        /// <summary>다 써서 잠긴 상태인가. <see cref="PenguinStaminaTuning.ExhaustExit01"/> 을
        /// 넘어야 풀린다.</summary>
        public bool Exhausted => _exhausted;

        /// <summary>
        /// 지금 달려도 되는가. <b>저장된 상태만 본다 — 이번 스텝의 입력도, 지난 스텝의 결과도
        /// 보지 않는다.</b>
        ///
        /// <para><b>이것이 <see cref="Tick"/> 과 분리돼야 하는 이유.</b> 소모는 "실제로 달렸는가"
        /// 로 하고 게이트는 "달릴 수 있는가" 로 해야 한다. 한때 <c>Tick</c> 이 소모 입력을 받아
        /// 허용 여부를 함께 돌려줬는데, 호출부가 그 반환값으로 "실제로 달렸는가" 를 정하고 그것을
        /// 다음 <c>Tick</c> 의 입력으로 되먹이는 구조라 <b>순환이 생겨 영원히 달리지 못했다</b>
        /// (2026-08-26 실측). 게이트는 되먹임 고리 밖에 있어야 한다.</para>
        /// </summary>
        public bool CanSprint => !_exhausted && _value01 > 0f;

        public static PenguinStaminaState Full => new PenguinStaminaState { _value01 = 1f };

        /// <summary>복제된 값을 그대로 앉힌다. 비권위 피어는 <see cref="Tick"/> 을 돌리지 않으므로
        /// 회복 지연 같은 내부 상태가 없다 — 그릴 값만 있으면 된다.</summary>
        public static PenguinStaminaState Replicated(float value01, bool exhausted) =>
            new PenguinStaminaState { _value01 = Mathf.Clamp01(value01), _exhausted = exhausted };

        /// <summary>
        /// 한 스텝 진행한다. <paramref name="sprinted"/> 는 <b>지난 스텝에 실제로 달렸는가</b> —
        /// 입력이 아니라 결과다. 허용 여부는 <see cref="CanSprint"/> 로 따로 묻는다.
        /// </summary>
        public void Tick(float dt, bool sprinted, in PenguinStaminaTuning tuning)
        {
            if (sprinted)
            {
                _value01 = Mathf.Max(0f, _value01 - dt / Mathf.Max(0.01f, tuning.SprintSeconds));
                _refillDelayRemaining = tuning.RefillDelaySeconds;
                if (_value01 <= 0f) _exhausted = true;
                return;
            }

            // 거절당한 Shift 도 '달리지 않은 것'이라 여기로 온다. Shift 를 쥔 채로는 영영 회복
            // 못 하는 함정을 만들지 않으려면 이 갈래가 회복을 막으면 안 된다.
            if (_refillDelayRemaining > 0f)
            {
                _refillDelayRemaining = Mathf.Max(0f, _refillDelayRemaining - dt);
                return;
            }

            _value01 = Mathf.Min(1f, _value01 + dt / Mathf.Max(0.01f, tuning.RefillSeconds));
            if (_exhausted && _value01 >= tuning.ExhaustExit01) _exhausted = false;
        }
    }
}
