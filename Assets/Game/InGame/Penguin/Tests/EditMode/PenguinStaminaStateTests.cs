using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// <b>이 테스트는 <see cref="PenguinLocomotion.Step"/> 의 호출 순서를 그대로 모사한다.</b>
    /// 처음에는 <c>Tick</c> 에 원하는 값을 직접 넣어 검사했는데, 그러면 실제 코드에 있는
    /// 되먹임 고리(소모 입력 ← 지난 스텝의 결과)를 우회하게 된다. 그 우회 때문에
    /// "게이트가 자기 결과에 의존해 영영 달리지 못하는" 순환 버그를 8개 테스트가 전부
    /// 통과시켰다(2026-08-26). 그래서 <see cref="Driver"/> 를 거쳐서만 검사한다.
    /// </summary>
    public sealed class PenguinStaminaStateTests
    {
        private const float Dt = 1f / 50f;

        // 계산이 눈으로 읽히는 값 — 2초면 바닥, 4초면 만충, 0.5초 지연, 30%에서 탈진 해제.
        private static PenguinStaminaTuning Tuning() => new PenguinStaminaTuning(
            sprintSeconds: 2f, refillSeconds: 4f, refillDelaySeconds: 0.5f, exhaustExit01: 0.3f);

        /// <summary>로코모션과 같은 순서로 돈다: 진행 → 게이트 조회 → 이번 스텝의 결과 기록.</summary>
        private sealed class Driver
        {
            public PenguinStaminaState Stamina = PenguinStaminaState.Full;
            private readonly PenguinStaminaTuning _tuning = Tuning();
            private bool _sprintedLastStep;

            public float Value01 => Stamina.Value01;
            public bool Exhausted => Stamina.Exhausted;

            /// <summary>한 스텝. <b>이번 스텝에 실제로 달렸는지</b>를 돌려준다.</summary>
            public bool Step(bool holdShift)
            {
                Stamina.Tick(Dt, _sprintedLastStep, _tuning);
                _sprintedLastStep = holdShift && Stamina.CanSprint;
                return _sprintedLastStep;
            }

            public void Run(float seconds, bool holdShift)
            {
                int steps = Mathf.RoundToInt(seconds / Dt);
                for (int i = 0; i < steps; i++) Step(holdShift);
            }
        }

        [Test]
        public void 가득_찬_상태로_시작한다()
        {
            Assert.That(PenguinStaminaState.Full.Value01, Is.EqualTo(1f));
            Assert.That(PenguinStaminaState.Full.Exhausted, Is.False);
            Assert.That(PenguinStaminaState.Full.CanSprint, Is.True);
        }

        // 순환 버그의 회귀 테스트. 게이트가 "지난 스텝에 달렸는가"에 의존하면 아무도 첫 스텝을
        // 시작할 수 없어 영원히 false 가 된다 — 실제로 그렇게 만들어 놓고 못 잡았다.
        [Test]
        public void 가만히_있다가_Shift를_누르면_그_스텝에_바로_달린다()
        {
            var driver = new Driver();
            driver.Run(1f, holdShift: false);

            Assert.That(driver.Step(holdShift: true), Is.True,
                "게이트가 자기 결과에 의존하면 여기서 영원히 false 다");
        }

        [Test]
        public void 계속_누르고_있으면_계속_달린다()
        {
            var driver = new Driver();

            for (int i = 0; i < 25; i++)
            {
                Assert.That(driver.Step(holdShift: true), Is.True, $"{i}번째 스텝에서 끊겼다");
            }
        }

        [Test]
        public void 달리면_지속시간만큼_쓰고_바닥난다()
        {
            var driver = new Driver();

            driver.Run(1f, holdShift: true);
            Assert.That(driver.Value01, Is.EqualTo(0.5f).Within(0.02f), "절반 지점");

            driver.Run(1f, holdShift: true);
            Assert.That(driver.Value01, Is.EqualTo(0f).Within(0.02f));
        }

        [Test]
        public void 다_쓰면_탈진하고_달리기가_거절된다()
        {
            var driver = new Driver();
            driver.Run(2.2f, holdShift: true);

            Assert.That(driver.Exhausted, Is.True);
            Assert.That(driver.Step(holdShift: true), Is.False);
        }

        // 이게 없으면 0 근처에서 한 틱 달리고 끊기기를 반복하는 '딱딱이 달리기'가 된다.
        // 특정 시각을 박아 두면 타이밍에 취약하므로 불변식으로 검사한다.
        [Test]
        public void 탈진은_문턱을_넘어야_풀린다()
        {
            var driver = new Driver();
            driver.Run(2.2f, holdShift: true);
            Assert.That(driver.Exhausted, Is.True);

            // 검사는 스텝 <b>뒤에</b> 한다. 스텝 전 값으로 판정하면 바로 그 스텝의 회복이
            // 문턱을 넘는 경우를 오탐한다(0.285 → 0.305).
            int guard = 0;
            while (!driver.Step(holdShift: true))
            {
                Assert.That(++guard, Is.LessThan(1000), "회복이 아예 안 된다 — 무한 루프");
            }

            Assert.That(driver.Value01, Is.GreaterThanOrEqualTo(0.3f),
                "문턱을 넘기 전에 달려졌다");
            Assert.That(driver.Exhausted, Is.False, "문턱을 넘으면 탈진이 풀려야 한다");
        }

        [Test]
        public void 탈진_중_Shift를_눌러도_회복은_된다()
        {
            var driver = new Driver();
            driver.Run(2.2f, holdShift: true);
            Assert.That(driver.Value01, Is.EqualTo(0f));

            driver.Run(1.5f, holdShift: true);

            Assert.That(driver.Value01, Is.GreaterThan(0f),
                "Shift를 쥔 채로는 영영 회복 못 하는 함정");
        }

        [Test]
        public void 회복은_지연이_지나야_시작한다()
        {
            var driver = new Driver();
            driver.Run(1f, holdShift: true);
            driver.Step(holdShift: false);
            float afterSprint = driver.Value01;

            driver.Run(0.4f, holdShift: false);
            Assert.That(driver.Value01, Is.EqualTo(afterSprint).Within(0.001f), "지연 안에서는 그대로");

            driver.Run(0.4f, holdShift: false);
            Assert.That(driver.Value01, Is.GreaterThan(afterSprint), "지연 뒤에는 찬다");
        }

        [Test]
        public void 체력은_1을_넘지_않는다()
        {
            var driver = new Driver();

            driver.Run(10f, holdShift: false);

            Assert.That(driver.Value01, Is.EqualTo(1f));
        }

        [Test]
        public void 달리는_동안은_회복하지_않는다()
        {
            var driver = new Driver();
            driver.Run(1f, holdShift: true);
            float afterSprint = driver.Value01;

            driver.Run(0.5f, holdShift: true);

            Assert.That(driver.Value01, Is.LessThan(afterSprint));
        }
    }
}
