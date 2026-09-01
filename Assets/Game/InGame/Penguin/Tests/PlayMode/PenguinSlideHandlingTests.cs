using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class PenguinSlideHandlingTests
    {
        private readonly List<GameObject> _penguins = new();
        private GameObject _ground;
        private GameObject _penguin;

        [SetUp]
        public void SetUp() => Time.captureDeltaTime = 1f / 60f;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (GameObject penguin in _penguins)
                if (penguin != null) Object.DestroyImmediate(penguin);
            if (_ground != null) Object.DestroyImmediate(_ground);
        }

        [UnityTest]
        public IEnumerator 프리팹은_루트_Rigidbody_하나만_사용한다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(0f, 0.4f, 0f));

            Rigidbody body = _penguin.GetComponent<Rigidbody>();
            RigidbodyConstraints uprightConstraints = RigidbodyConstraints.FreezeRotationX
                                                       | RigidbodyConstraints.FreezeRotationZ;
            Assert.AreEqual(uprightConstraints, body.constraints);
            Rigidbody[] bodies = _penguin.GetComponentsInChildren<Rigidbody>();
            Assert.AreEqual(1, bodies.Length);
            Assert.AreEqual(0, _penguin.GetComponentsInChildren<CharacterJoint>(true).Length);
            Assert.IsNull(_penguin.transform.Find("PhysicsRig"));
        }

        [UnityTest]
        public IEnumerator 무입력_요각속도는_요토크로_감쇠한다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(0f, 0.01f, 0f));

            Rigidbody body = _penguin.GetComponent<Rigidbody>();
            body.angularVelocity = Vector3.up * 2f;
            for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();

            Assert.Less(Mathf.Abs(Vector3.Dot(body.angularVelocity, Vector3.up)), 0.1f,
                "옛 제자리 회전 버그는 각속도 대입이 아니라 요 모터의 감쇠로 막아야 한다");
            Assert.AreEqual(RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ,
                body.constraints);
        }

        [UnityTest]
        public IEnumerator Shift만_누르면_달리고_슬라이드는_시작하지_않는다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(0f, 0.01f, 0f));

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();
            Rigidbody body = _penguin.GetComponent<Rigidbody>();
            SetMoveInput(input, Vector2.up);
            SetSprintHeld(input, true);

            for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();

            Assert.IsFalse(locomotion.IsSliding);
            Assert.Greater(HorizontalSpeed(body), 4.5f, "Shift는 걷기보다 빠른 달리기를 만들어야 한다");
        }

        [UnityTest]
        public IEnumerator Shift_Space는_일반점프와_같은_높이로_뛰고_착지하면_슬라이드가_된다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(0f, 0.01f, 0f));

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();
            Rigidbody body = _penguin.GetComponent<Rigidbody>();
            SetMoveInput(input, Vector2.up);
            SetSprintHeld(input, true);
            SetJumpPressed(input, true);

            yield return new WaitForFixedUpdate();
            float expectedJumpSpeed = Mathf.Sqrt(2f * locomotion.GravityMagnitude * 0.5f);
            Assert.IsFalse(locomotion.IsSliding, "착지 전에는 슬라이딩 물리를 시작하면 안 된다");
            Assert.That(body.linearVelocity.y, Is.EqualTo(expectedJumpSpeed).Within(0.35f),
                "Shift+Space도 일반 점프와 같은 수직 속도를 써야 한다");

            SetJumpPressed(input, false);
            for (int i = 0; i < 12 && locomotion.IsGrounded; i++)
                yield return new WaitForFixedUpdate();

            Assert.IsTrue(locomotion.IsSlidePose,
                "지면을 떠난 뒤에는 일반 점프에서 슬라이딩 자세로 전환돼야 한다");
            Assert.IsTrue(locomotion.IsSliding, "공중 Shift는 즉시 슬라이딩 물리를 시작해야 한다");
            SetSprintHeld(input, false);

            for (int i = 0; i < 60 && !locomotion.IsGrounded; i++)
                yield return new WaitForFixedUpdate();

            Assert.IsTrue(locomotion.IsSliding, "Shift를 떼어도 착지 뒤 슬라이드가 유지돼야 한다");
        }

        [UnityTest]
        public IEnumerator Shift_Space와_일반_Space는_같은_점프_정점에_도달한다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(-10f, 0.01f, -10f));
            GameObject normalJump = _penguin;
            yield return CreatePenguin(new Vector3(10f, 0.01f, -10f));
            GameObject slideJump = _penguin;

            PenguinInputReader normalInput = normalJump.GetComponent<PenguinInputReader>();
            PenguinInputReader slideInput = slideJump.GetComponent<PenguinInputReader>();
            Rigidbody normalBody = normalJump.GetComponent<Rigidbody>();
            Rigidbody slideBody = slideJump.GetComponent<Rigidbody>();
            float normalStartY = normalBody.position.y;
            float slideStartY = slideBody.position.y;
            float normalApexY = normalStartY;
            float slideApexY = slideStartY;

            SetJumpPressed(normalInput, true);
            SetSprintHeld(slideInput, true);
            SetJumpPressed(slideInput, true);
            yield return new WaitForFixedUpdate();
            SetJumpPressed(normalInput, false);
            SetJumpPressed(slideInput, false);

            for (int i = 0; i < 40; i++)
            {
                normalApexY = Mathf.Max(normalApexY, normalBody.position.y);
                slideApexY = Mathf.Max(slideApexY, slideBody.position.y);
                yield return new WaitForFixedUpdate();
            }

            float normalRise = normalApexY - normalStartY;
            float slideRise = slideApexY - slideStartY;
            SetSprintHeld(slideInput, false);
            Assert.That(slideRise, Is.EqualTo(normalRise).Within(0.03f),
                $"Shift+Space는 슬라이딩 상태로 바뀌어도 점프 높이는 일반 Space와 같아야 한다 · " +
                $"normal={normalRise:F3}, shift={slideRise:F3}");
        }

        [UnityTest]
        public IEnumerator 일반점프_애니메이션은_반복하지_않고_마지막_자세를_유지한다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(0f, 0.01f, 0f));

            Animator animator = _penguin.GetComponentInChildren<Animator>();
            AnimationClip jumpClip = animator.runtimeAnimatorController.animationClips
                .FirstOrDefault(clip => clip.name == "Jump_Adelie_Once");

            Assert.IsNotNull(jumpClip, "Jump 상태는 프로젝트 소유의 단발 점프 클립을 써야 한다");
            Assert.IsFalse(jumpClip.isLooping,
                "점프 클립이 반복되면 체공 중 이륙 동작이 처음부터 다시 재생된다");
        }

        [UnityTest]
        public IEnumerator 일반점프_공중_Shift는_즉시_슬라이딩으로_전환되고_착지까지_유지된다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(0f, 1.8f, 0f));

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();
            Rigidbody body = _penguin.GetComponent<Rigidbody>();
            body.linearVelocity = Vector3.forward * 4f;
            SetSprintHeld(input, true);

            yield return new WaitForFixedUpdate();
            Assert.IsFalse(locomotion.IsGrounded);
            Assert.IsTrue(locomotion.IsSliding, "공중에서 Shift를 누른 물리 스텝에 슬라이딩으로 전환돼야 한다");
            Assert.IsTrue(locomotion.IsSlidePose, "슬라이딩 물리와 자세가 같은 시점에 시작돼야 한다");

            SetSprintHeld(input, false);
            for (int i = 0; i < 90 && !locomotion.IsGrounded; i++)
                yield return new WaitForFixedUpdate();

            Assert.IsTrue(locomotion.IsGrounded);
            Assert.IsTrue(locomotion.IsSliding, "공중에서 시작한 슬라이딩 상태가 착지 뒤에도 이어져야 한다");
        }

        [UnityTest]
        public IEnumerator 슬라이드는_저속이_일정시간_이어져야_자동_종료된다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(0f, 0.01f, 0f));

            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();
            InvokePrivate(locomotion, "EnterSliding");
            Assert.IsTrue(locomotion.IsSliding);

            for (int i = 0; i < 8; i++) yield return new WaitForFixedUpdate();
            Assert.IsTrue(locomotion.IsSliding, "0.2초보다 짧은 저속 구간에는 아직 슬라이드여야 한다");

            for (int i = 0; i < 8; i++) yield return new WaitForFixedUpdate();
            Assert.IsFalse(locomotion.IsSliding);
        }

        [UnityTest]
        public IEnumerator 슬라이드는_접선속도_1미터에서도_종료판정을_시작한다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(0f, 0.01f, 0f));

            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();
            Rigidbody body = _penguin.GetComponent<Rigidbody>();
            InvokePrivate(locomotion, "EnterSliding");
            body.linearVelocity = Vector3.forward * 0.9f;

            for (int i = 0; i < 16; i++) yield return new WaitForFixedUpdate();
            Assert.IsFalse(locomotion.IsSliding, "0.35m/s까지 기다리지 않고 1m/s 부근에서 종료돼야 한다");
        }

        [UnityTest]
        public IEnumerator 슬라이딩_S는_무입력과_같은_궤적을_만든다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(-3f, 0.01f, 0f));
            GameObject neutral = _penguin;
            yield return CreatePenguin(new Vector3(3f, 0.01f, 0f));
            GameObject braking = _penguin;

            PrepareSliding(neutral, Vector2.zero, 6f);
            PrepareSliding(braking, Vector2.down, 6f);
            for (int i = 0; i < 12; i++) yield return new WaitForFixedUpdate();

            Rigidbody neutralBody = neutral.GetComponent<Rigidbody>();
            Rigidbody brakingBody = braking.GetComponent<Rigidbody>();
            Assert.That(HorizontalSpeed(brakingBody), Is.EqualTo(HorizontalSpeed(neutralBody)).Within(0.02f));
            Assert.That(braking.transform.position.z,
                Is.EqualTo(neutral.transform.position.z).Within(0.02f), "S가 숨은 제동력을 만들면 안 된다");
        }

        [UnityTest]
        public IEnumerator 슬라이딩_W는_턱이지만_추진력을_만들지_않는다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(0f, 0.01f, 0f));

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();
            Rigidbody body = _penguin.GetComponent<Rigidbody>();
            InvokePrivate(locomotion, "EnterSliding");
            SetMoveInput(input, Vector2.up);
            body.linearVelocity = Vector3.forward * 4f;
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(locomotion.IsTucking);
            Assert.LessOrEqual(HorizontalSpeed(body), 4.05f, "W 턱은 직접 가속하면 안 된다");
        }

        [UnityTest]
        public IEnumerator 눈덮임에_따라_슬라이딩_전진마찰이_달라진다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(-10f, 0.01f, 0f));
            GameObject bare = _penguin;
            yield return CreatePenguin(new Vector3(10f, 0.01f, 0f));
            GameObject snowy = _penguin;

            PenguinLocomotion bareLocomotion = bare.GetComponent<PenguinLocomotion>();
            PenguinLocomotion snowyLocomotion = snowy.GetComponent<PenguinLocomotion>();
            Rigidbody bareBody = bare.GetComponent<Rigidbody>();
            Rigidbody snowyBody = snowy.GetComponent<Rigidbody>();
            foreach (PenguinLocomotion locomotion in new[] { bareLocomotion, snowyLocomotion })
            {
                SetPrivateField(locomotion, "_snowFrictionMu", 0.05f);
                SetPrivateField(locomotion, "_bareFrictionMu", 0.5f);
                SetPrivateField(locomotion, "_airDragForceN", 0f);
                SetPrivateField(locomotion, "_coverageLerpPerSecond", 0f);
                InvokePrivate(locomotion, "EnterSliding");
            }
            SetPrivateField(bareLocomotion, "_snowCoverage01", 0f);
            SetPrivateField(snowyLocomotion, "_snowCoverage01", 1f);
            bareBody.linearVelocity = Vector3.forward * 5f;
            snowyBody.linearVelocity = Vector3.forward * 5f;

            const int stepCount = 10;
            for (int i = 0; i < stepCount; i++) yield return new WaitForFixedUpdate();

            float expectedBareSpeed = 5f - 0.5f * bareLocomotion.GravityMagnitude
                * Time.fixedDeltaTime * stepCount;
            float expectedSnowSpeed = 5f - 0.05f * snowyLocomotion.GravityMagnitude
                * Time.fixedDeltaTime * stepCount;
            Assert.That(HorizontalSpeed(bareBody), Is.EqualTo(expectedBareSpeed).Within(0.08f));
            Assert.That(HorizontalSpeed(snowyBody), Is.EqualTo(expectedSnowSpeed).Within(0.08f));
            Assert.Less(HorizontalSpeed(bareBody), HorizontalSpeed(snowyBody),
                "맨바닥은 눈 위보다 큰 전진 마찰을 적용해야 한다");
        }

        [UnityTest]
        public IEnumerator 반지름_1점5미터_눈덩이_질량도_운반_Shift로_출발한다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(0f, 0.01f, 0f));

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinControlState state = _penguin.GetComponent<PenguinControlState>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();
            Rigidbody body = _penguin.GetComponent<Rigidbody>();
            float cargoMassKg = 4.18879032f * 1.5f * 1.5f * 1.5f
                                * SnowBallCarrier.SnowDensityKgPerM3;
            body.mass += cargoMassKg;
            SetPrivateField(locomotion, "_bareFrictionMu", 0.05f);
            SetPrivateField(locomotion, "_airDragForceN", 0f);
            Assert.IsTrue(state.TryTransitionTo(EPenguinControlState.Carrying));

            body.linearVelocity = Vector3.zero;
            for (int i = 0; i < 20; i++) yield return new WaitForFixedUpdate();
            Assert.Less(HorizontalSpeed(body), 0.05f,
                "운반물은 Shift 입력 없이 자체 추진하면 안 된다");

            SetSprintHeld(input, true);
            for (int i = 0; i < 75; i++) yield return new WaitForFixedUpdate();

            Assert.Greater(HorizontalSpeed(body), 1f,
                "예상 최대 반지름 1.5m의 눈덩이 질량도 Shift 1.5초 안에 답답하지 않게 출발해야 한다");
        }

        [UnityTest]
        public IEnumerator 운반_저속에서는_A_D가_출발방향을_빠르게_바꾼다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(0f, 0.01f, 0f));

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinControlState state = _penguin.GetComponent<PenguinControlState>();
            Rigidbody body = _penguin.GetComponent<Rigidbody>();
            float cargoMassKg = 4.18879032f * 1.5f * 1.5f * 1.5f
                                * SnowBallCarrier.SnowDensityKgPerM3;
            body.mass += cargoMassKg;
            Assert.IsTrue(state.TryTransitionTo(EPenguinControlState.Carrying));

            body.linearVelocity = Vector3.forward * 0.6f;
            SetMoveInput(input, Vector2.right);
            SetSprintHeld(input, true);
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();

            Vector3 facing = Vector3.ProjectOnPlane(_penguin.transform.forward, Vector3.up).normalized;
            Assert.Greater(Vector3.Dot(facing, Vector3.right), 0.5f,
                "운반 출발 구간의 A/D는 기존 고속 슬립각보다 큰 방향 전환 권한을 가져야 한다");
            Assert.Greater(HorizontalSpeed(body), 0.6f,
                "저속 선회 보조가 운반 출발 자체를 막아서는 안 된다");
        }

        [UnityTest]
        public IEnumerator 평지_Shift_순항속도는_일반_7점5_최대운반_3점5미터다()
        {
            CreateGround();
            _ground.transform.localScale = new Vector3(40f, 1f, 300f);
            yield return CreatePenguin(new Vector3(-10f, 0.01f, -10f));
            GameObject sliding = _penguin;
            yield return CreatePenguin(new Vector3(10f, 0.01f, -10f));
            GameObject carrying = _penguin;

            PenguinLocomotion slidingLocomotion = sliding.GetComponent<PenguinLocomotion>();
            PenguinInputReader slidingInput = sliding.GetComponent<PenguinInputReader>();
            Rigidbody slidingBody = sliding.GetComponent<Rigidbody>();
            SetPrivateField(slidingLocomotion, "_bareFrictionMu", 0.05f);
            InvokePrivate(slidingLocomotion, "EnterSliding");
            slidingBody.linearVelocity = Vector3.forward * 2f;
            SetSprintHeld(slidingInput, true);

            PenguinLocomotion carryingLocomotion = carrying.GetComponent<PenguinLocomotion>();
            PenguinInputReader carryingInput = carrying.GetComponent<PenguinInputReader>();
            PenguinControlState carryingState = carrying.GetComponent<PenguinControlState>();
            Rigidbody carryingBody = carrying.GetComponent<Rigidbody>();
            SetPrivateField(carryingLocomotion, "_bareFrictionMu", 0.05f);
            float cargoMassKg = 4.18879032f * 1.5f * 1.5f * 1.5f
                                * SnowBallCarrier.SnowDensityKgPerM3;
            carryingBody.mass += cargoMassKg;
            Assert.IsTrue(carryingState.TryTransitionTo(EPenguinControlState.Carrying));
            carryingBody.linearVelocity = Vector3.forward * 2f;
            SetSprintHeld(carryingInput, true);

            const int settleSteps = 600;
            const int sampleSteps = 150;
            float slidingSpeedSum = 0f;
            float carryingSpeedSum = 0f;
            for (int i = 0; i < settleSteps; i++)
            {
                yield return new WaitForFixedUpdate();
                if (i < settleSteps - sampleSteps) continue;
                slidingSpeedSum += HorizontalSpeed(slidingBody);
                carryingSpeedSum += HorizontalSpeed(carryingBody);
            }

            float slidingAverage = slidingSpeedSum / sampleSteps;
            float carryingAverage = carryingSpeedSum / sampleSteps;
            Assert.That(slidingAverage, Is.EqualTo(7.5f).Within(0.2f),
                $"평지 일반 슬라이딩의 지속 Shift 순항 속도는 7.5m/s여야 한다 · actual={slidingAverage:F3}");
            Assert.That(carryingAverage, Is.EqualTo(3.5f).Within(0.15f),
                $"반지름 1.5m 눈덩이 질량의 운반 순항 속도는 3.5m/s여야 한다 · actual={carryingAverage:F3}");
            Assert.Less(carryingAverage, slidingAverage,
                "최대 운반물의 순항 속도는 일반 슬라이딩보다 느려야 한다");
        }

        [UnityTest]
        public IEnumerator 슬라이딩_Shift는_AddForce로_가속하고_좌우발을_번갈아_쓴다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(-15f, 0.01f, -15f));

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();
            Rigidbody body = _penguin.GetComponent<Rigidbody>();
            SetPrivateField(locomotion, "_snowFrictionMu", 0f);
            SetPrivateField(locomotion, "_bareFrictionMu", 0f);
            SetPrivateField(locomotion, "_airDragForceN", 0f);
            InvokePrivate(locomotion, "EnterSliding");
            body.linearVelocity = Vector3.forward * 2f;
            SetSprintHeld(input, true);

            float speedBefore = HorizontalSpeed(body);
            bool sawLeft = false;
            bool sawRight = false;
            for (int i = 0; i < 45; i++)
            {
                yield return new WaitForFixedUpdate();
                sawLeft |= locomotion.ActiveSlideKickFoot == 0;
                sawRight |= locomotion.ActiveSlideKickFoot == 1;
            }

            Assert.IsTrue(sawLeft && sawRight, "한쪽 발만 반복하지 않고 좌우를 번갈아야 한다");
            Assert.Greater(HorizontalSpeed(body), speedBefore + 0.5f,
                "Shift 발 밀기는 Rigidbody AddForce로 실제 속도를 높여야 한다");
        }

        [UnityTest]
        public IEnumerator 고속_카빙은_목표_슬립각_안에서_속도벡터를_점진적으로_돌린다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(0f, 0.01f, 0f));

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();
            Rigidbody body = _penguin.GetComponent<Rigidbody>();
            InvokePrivate(locomotion, "EnterSliding");
            body.linearVelocity = Vector3.forward * 9f;
            SetMoveInput(input, Vector2.right);

            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();

            Assert.That(locomotion.TargetSlipAngleDeg, Is.InRange(25f, 31f));
            Assert.Less(Mathf.Abs(locomotion.SlipAngleDeg), 38f);
            Assert.Greater(Mathf.Abs(locomotion.TurnRateDegPerSec), 1f);
        }

        [UnityTest]
        public IEnumerator 맨바닥_슬라이딩_Space는_관성을_보존하며_작게_점프한다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(0f, 0.01f, 0f));

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();
            Rigidbody body = _penguin.GetComponent<Rigidbody>();
            InvokePrivate(locomotion, "EnterSliding");
            body.linearVelocity = Vector3.forward * 6f;
            float speedBefore = HorizontalSpeed(body);
            SetJumpPressed(input, true);
            yield return new WaitForFixedUpdate();
            SetJumpPressed(input, false);

            Assert.IsTrue(locomotion.IsSliding, "맨바닥 작은 점프는 슬라이딩 상태를 유지해야 한다");
            Assert.AreEqual(RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ,
                body.constraints);
            Assert.GreaterOrEqual(HorizontalSpeed(body), speedBefore - 0.25f,
                "Space 입력 자체가 선속도를 잘라서는 안 된다");
            Assert.That(body.linearVelocity.y, Is.InRange(1.5f, 2.2f),
                "맨바닥 Space는 일반 점프보다 낮은 실제 점프 속도를 줘야 한다");
            Assert.Less(body.angularVelocity.magnitude, 0.5f,
                "평지 점프에 강제 전도 회전을 넣으면 안 된다");
        }

        [UnityTest]
        public IEnumerator 맨바닥_슬라이딩_WASD_Space는_누른_방향으로_같은_크기만큼_이동한다()
        {
            CreateGround();

            yield return CreatePenguin(new Vector3(-8f, 0.01f, 0f));
            Vector3 forward = Vector3.zero;
            yield return TriggerBareHop(_penguin, Vector2.up, value => forward = value);

            yield return CreatePenguin(new Vector3(-4f, 0.01f, 0f));
            Vector3 backward = Vector3.zero;
            yield return TriggerBareHop(_penguin, Vector2.down, value => backward = value);

            yield return CreatePenguin(new Vector3(0f, 0.01f, 0f));
            Vector3 left = Vector3.zero;
            yield return TriggerBareHop(_penguin, Vector2.left, value => left = value);

            yield return CreatePenguin(new Vector3(4f, 0.01f, 0f));
            Vector3 right = Vector3.zero;
            yield return TriggerBareHop(_penguin, Vector2.right, value => right = value);

            yield return CreatePenguin(new Vector3(8f, 0.01f, 0f));
            Vector3 diagonal = Vector3.zero;
            yield return TriggerBareHop(_penguin, Vector2.one, value => diagonal = value);

            Assert.Greater(forward.z, 0.45f);
            Assert.Less(backward.z, -0.45f);
            Assert.Less(left.x, -0.45f);
            Assert.Greater(right.x, 0.45f);
            Assert.Greater(diagonal.x, 0f);
            Assert.Greater(diagonal.z, 0f);

            float cardinalSpeed = new Vector2(forward.x, forward.z).magnitude;
            Assert.That(new Vector2(backward.x, backward.z).magnitude,
                Is.EqualTo(cardinalSpeed).Within(0.05f), "S도 W와 같은 크기로 이동해야 한다");
            Assert.That(new Vector2(left.x, left.z).magnitude,
                Is.EqualTo(cardinalSpeed).Within(0.05f), "A도 W와 같은 크기로 이동해야 한다");
            Assert.That(new Vector2(right.x, right.z).magnitude,
                Is.EqualTo(cardinalSpeed).Within(0.05f), "D도 W와 같은 크기로 이동해야 한다");
            float diagonalSpeed = new Vector2(diagonal.x, diagonal.z).magnitude;
            Assert.That(diagonalSpeed, Is.EqualTo(cardinalSpeed).Within(0.05f),
                "대각선 입력이 단일 축보다 빠르면 안 된다");
        }

        [UnityTest]
        public IEnumerator 맨바닥_슬라이딩_W_Space_한번은_실제로_앞으로_이동한다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(0f, 0.01f, 0f));

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();
            Vector3 start = _penguin.transform.position;
            InvokePrivate(locomotion, "EnterSliding");
            SetMoveInput(input, Vector2.up);
            SetJumpPressed(input, true);
            yield return new WaitForFixedUpdate();
            SetJumpPressed(input, false);
            SetMoveInput(input, Vector2.zero);

            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();

            float distance = _penguin.transform.position.z - start.z;
            Assert.That(distance, Is.InRange(0.4f, 0.8f),
                "한 번의 방향 점프는 화면에서 식별 가능한 0.4~0.8m를 실제 이동해야 한다");
        }

        [UnityTest]
        public IEnumerator 카메라_높이는_루트의_미세상하진동을_직접복사하지_않는다()
        {
            CreateGround();
            yield return CreatePenguin(new Vector3(0f, 0.01f, 0f));

            Transform cameraRig = _penguin.transform.Find("SnowHeightPivot/CameraRig");
            PenguinCameraOrbit cameraOrbit = cameraRig.GetComponent<PenguinCameraOrbit>();
            Vector3 restPosition = _penguin.transform.position;
            float minRootY = float.MaxValue;
            float maxRootY = float.MinValue;
            float minCameraY = float.MaxValue;
            float maxCameraY = float.MinValue;

            for (int frame = 0; frame < 60; frame++)
            {
                _penguin.transform.position = restPosition
                                              + Vector3.up * (frame % 2 == 0 ? 0.04f : -0.04f);
                Physics.SyncTransforms();
                InvokePrivate(cameraOrbit, "LateUpdate");
                minRootY = Mathf.Min(minRootY, _penguin.transform.position.y);
                maxRootY = Mathf.Max(maxRootY, _penguin.transform.position.y);
                minCameraY = Mathf.Min(minCameraY, cameraRig.position.y);
                maxCameraY = Mathf.Max(maxCameraY, cameraRig.position.y);
            }

            float rootRange = maxRootY - minRootY;
            float cameraRange = maxCameraY - minCameraY;
            Assert.Greater(rootRange, 0.07f);
            Assert.Less(cameraRange, rootRange * 0.5f,
                $"물리 루트의 미세 상하진동을 카메라 높이에 1:1로 복사하면 안 된다 · " +
                $"root={rootRange:F6}, camera={cameraRange:F6}");
        }

        private void CreateGround()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "__TEST__SlideGround";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(40f, 1f, 40f);
        }

        private IEnumerator CreatePenguin(Vector3 position)
        {
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab");
#else
            GameObject prefab = null;
#endif
            Assert.IsNotNull(prefab);
            _penguin = Object.Instantiate(prefab, position, Quaternion.identity);
            _penguin.name = "__TEST__SlidePenguin";
            _penguins.Add(_penguin);

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            input.enabled = false;
            SetMoveInput(input, Vector2.zero);
            SetSprintHeld(input, false);
            SetJumpPressed(input, false);
            yield return new WaitForFixedUpdate();
        }

        private static void PrepareSliding(GameObject penguin, Vector2 moveInput, float speed)
        {
            PenguinInputReader input = penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = penguin.GetComponent<PenguinLocomotion>();
            Rigidbody body = penguin.GetComponent<Rigidbody>();
            InvokePrivate(locomotion, "EnterSliding");
            SetMoveInput(input, moveInput);
            body.linearVelocity = Vector3.forward * speed;
        }

        private static IEnumerator TriggerBareHop(GameObject penguin, Vector2 moveInput,
                                                   System.Action<Vector3> captureVelocity)
        {
            PenguinInputReader input = penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = penguin.GetComponent<PenguinLocomotion>();
            Rigidbody body = penguin.GetComponent<Rigidbody>();
            InvokePrivate(locomotion, "EnterSliding");
            body.linearVelocity = Vector3.zero;
            SetMoveInput(input, moveInput);
            SetJumpPressed(input, true);
            yield return new WaitForFixedUpdate();
            SetJumpPressed(input, false);

            Assert.IsTrue(locomotion.IsSliding);
            Assert.That(body.linearVelocity.y, Is.InRange(1.5f, 2.2f));
            captureVelocity(body.linearVelocity);
        }

        private static float HorizontalSpeed(Rigidbody body)
            => new Vector2(body.linearVelocity.x, body.linearVelocity.z).magnitude;

        private static void SetMoveInput(PenguinInputReader input, Vector2 value)
            => SetProperty(input, nameof(PenguinInputReader.MoveInput), value);

        private static void SetSprintHeld(PenguinInputReader input, bool value)
            => SetProperty(input, nameof(PenguinInputReader.SprintHeld), value);

        private static void SetJumpPressed(PenguinInputReader input, bool value)
            => SetProperty(input, nameof(PenguinInputReader.JumpPressedThisFrame), value);

        private static void SetProperty(object target, string name, object value)
            => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                .SetValue(target, value);

        private static object InvokePrivate(object target, string name, params object[] arguments)
            => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(target, arguments);

        private static void SetPrivateField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
    }
}
