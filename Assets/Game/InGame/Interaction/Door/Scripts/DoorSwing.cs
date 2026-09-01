using System;

namespace PPack
{
    /// <summary>
    /// 문 한 짝의 각도 적분기. 순수 C# 이고 <c>UnityEngine</c> 을 참조하지 않는다 — 데디 서버와
    /// EditMode 양쪽에서 그대로 돈다(<see cref="global::PPack.SnowBallCpu"/> 와 같은 이유).
    ///
    /// <para><b>상태는 각도 하나다.</b> 열림/닫힘 bool 은 없다 — <see cref="AngleDeg"/> 가 0 이면
    /// 닫힘이고, <c>-MaxAngleDeg..MaxAngleDeg</c> 어디든 갈 수 있다. <b>양방향</b>이다 — 밀어도
    /// 당겨도(어느 면에서 부딪히든) 0 을 중심으로 반대쪽까지 열린다.</para>
    ///
    /// <para><b>래치는 근접 판정이다.</b> 각도가 0 에 <see cref="LatchAngleDeg"/> 만큼 가까울 때
    /// (부호 무관, <c>|AngleDeg| &lt; LatchAngleDeg</c>) 충격의 크기가 <see cref="LatchBreakL"/> 를
    /// 못 넘으면 방향과 무관하게 각속도를 전혀 주지 않는다 — 문이 이미 열려 있으면 래치가 없다
    /// (살짝 밀면 살짝 움직인다).</para>
    /// </summary>
    public sealed class DoorSwing
    {
        private const float Rad2Deg = 57.29578f;

        /// <summary>임계 감쇠 스프링의 정점 변위를 목표값에 맞추는 상수(t=1/ω 에서의 e배).</summary>
        private const float CriticalPeakScale = 2.71828175f;

        /// <summary>
        /// 각속도 상한(도/초). 실제 경첩의 기계적 마찰·정지 걸쇠에 해당한다 — 없으면 무거운 충격이
        /// 한 스텝 안에 문을 몇백 도씩 돌리고, <c>Rigidbody.MoveRotation</c> 은 스윕 콜리전이 없어서
        /// 충돌체가 밀고 있던 물체를 그대로 관통해 접촉 해소가 폭발한다(실측: 인게임 눈덩이 반지름
        /// 0.18m 로도 발생). 360°/s 는 문이 0.25초 안에 완전히 열리는, 이미 "세게 걷어찼다"로 보이는
        /// 속도다.
        /// </summary>
        private const float MaxAngVelDegPerS = 180f;

        /// <summary>양방향 극값(도). 실제 범위는 <c>-MaxAngleDeg..MaxAngleDeg</c>.</summary>
        public float MaxAngleDeg { get; }
        public float LatchAngleDeg { get; }
        public float LatchBreakL { get; }

        private readonly float _inertiaKgM2;
        private readonly float _angularDampingPerSecond;
        private readonly float _bounce01;
        private readonly float _rattleMaxDeg;
        private readonly float _rattleOmega;

        /// <summary>권위 쪽의 실제 문 각도(도). 복제 대상은 이 값 하나다.</summary>
        public float AngleDeg { get; private set; }
        public float AngVelDegPerS { get; private set; }

        /// <summary>운동량 부족 충돌의 시각 피드백. 복제하지 않는다 — 각 피어가 <see cref="Kick"/> 로 로컬 재생한다.</summary>
        public float RattleDeg { get; private set; }
        private float _rattleVelDegPerS;

        /// <summary>렌더가 실제로 그려야 할 각도 — 권위 각도 + 로컬 덜컹.</summary>
        public float DisplayAngleDeg => AngleDeg + RattleDeg;

        public DoorSwing(float maxAngleDeg, float inertiaKgM2, float angularDampingPerSecond,
            float bounce01, float latchAngleDeg, float latchBreakL, float rattleMaxDeg,
            float rattleFrequencyHz)
        {
            MaxAngleDeg = maxAngleDeg;
            _inertiaKgM2 = inertiaKgM2;
            _angularDampingPerSecond = angularDampingPerSecond;
            _bounce01 = bounce01;
            LatchAngleDeg = latchAngleDeg;
            LatchBreakL = latchBreakL;
            _rattleMaxDeg = rattleMaxDeg;
            _rattleOmega = 2f * 3.14159265f * rattleFrequencyHz;
        }

        /// <summary>
        /// 서버(또는 러너 없는 단독 모드)만 부른다. <paramref name="angularImpulseL"/> 은 힌지에 대한
        /// 각운동량(kg·m²/s) — 부호는 어느 쪽으로 도는지일 뿐, <b>양쪽 다 여는 방향</b>이다(밀든
        /// 당기든 같은 규칙). 래치를 못 이기면 각속도를 주지 않고 false 를 돌려준다; 이때
        /// <paramref name="blockedStrength01"/> 이 얼마나 못 미쳤는지를 담는다.
        /// </summary>
        public bool TryApplyHit(float angularImpulseL, out float blockedStrength01)
        {
            blockedStrength01 = 0f;

            bool nearClosed = AngleDeg > -LatchAngleDeg && AngleDeg < LatchAngleDeg;
            float magnitude = angularImpulseL < 0f ? -angularImpulseL : angularImpulseL;

            if (nearClosed && magnitude < LatchBreakL)
            {
                blockedStrength01 = magnitude / LatchBreakL;
                if (blockedStrength01 > 1f) blockedStrength01 = 1f;
                return false;
            }

            AngVelDegPerS += angularImpulseL / _inertiaKgM2 * Rad2Deg;
            ClampAngVel();
            return true;
        }

        /// <summary>운동량이 부족했던 충돌의 덜컹을 로컬로 재생한다. 문을 열지 않는다 — <see cref="RattleDeg"/> 만 흔든다.</summary>
        public void Kick(float strength01)
        {
            if (strength01 < 0f) strength01 = 0f;
            else if (strength01 > 1f) strength01 = 1f;
            _rattleVelDegPerS += strength01 * _rattleMaxDeg * _rattleOmega * CriticalPeakScale;
        }

        /// <summary>매 물리 스텝 호출. 권위는 각도를 진짜로 적분하고, 비권위는 덜컹만 굴린다(별도로 Kick 호출).</summary>
        public void Step(float dt)
        {
            StepRattleOnly(dt);

            float damping = 1f - _angularDampingPerSecond * dt;
            if (damping < 0f) damping = 0f;
            AngVelDegPerS *= damping;
            AngleDeg += AngVelDegPerS * dt;

            if (AngleDeg < -MaxAngleDeg) Clamp(-MaxAngleDeg);
            else if (AngleDeg > MaxAngleDeg) Clamp(MaxAngleDeg);
        }

        /// <summary>비권위 피어가 복제된 각도를 그대로 받아들일 때 쓴다. 각속도는 건드리지 않는다(연출용 스무딩은 호출부 몫).</summary>
        public void SetAngleFromNetwork(float angleDeg) => AngleDeg = angleDeg;

        /// <summary>
        /// 비권위 피어용. 실제 각도는 <see cref="SetAngleFromNetwork"/> 가 매 <c>Render</c> 마다
        /// 덮어쓰므로 여기서는 <see cref="AngVelDegPerS"/> 를 건드리지 않는다 — 덜컹 스프링만 굴린다.
        ///
        /// <para><b>닫힌 형식으로 적분한다 — 오일러가 아니다.</b> 임계 감쇠 스프링을 전진 오일러로
        /// 풀면 ω·dt 가 1 에 가까울 때 발산한다(실측: 9 Hz 스프링을 60 Hz 로 스텝하면 1 초 만에
        /// 4×10⁸ 도까지 튄다 — <c>DoorSwingTests</c> 가 이 회귀를 잡는다). 임계 감쇠는 선형 상미분
        /// 방정식이라 닫힌 해가 있고, 그 해는 <c>dt</c> 가 얼마든 안정적이다.</para>
        /// </summary>
        public void StepRattleOnly(float dt)
        {
            float x0 = RattleDeg;
            float v0 = _rattleVelDegPerS;
            float decay = (float)Math.Exp(-_rattleOmega * dt);
            float linear = v0 + _rattleOmega * x0;

            RattleDeg = decay * (x0 + linear * dt);
            _rattleVelDegPerS = decay * (v0 - _rattleOmega * dt * linear);
        }

        private void Clamp(float bound)
        {
            AngleDeg = bound;
            AngVelDegPerS = -AngVelDegPerS * _bounce01;
            if (AngVelDegPerS > -0.5f && AngVelDegPerS < 0.5f) AngVelDegPerS = 0f;
        }

        private void ClampAngVel()
        {
            if (AngVelDegPerS > MaxAngVelDegPerS) AngVelDegPerS = MaxAngVelDegPerS;
            else if (AngVelDegPerS < -MaxAngVelDegPerS) AngVelDegPerS = -MaxAngVelDegPerS;
        }
    }
}
