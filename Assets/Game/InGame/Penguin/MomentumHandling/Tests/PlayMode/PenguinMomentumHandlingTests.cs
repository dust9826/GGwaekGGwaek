using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace PPack
{
    public sealed class PenguinMomentumHandlingTests
    {
        private GameObject _ground;
        private GameObject _stageObject;
        private GameObject _profileObject;
        private SnowBallCarrier _ball;
        private GameObject _penguinOne;
        private GameObject _penguinTwo;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (_ball != null) Object.DestroyImmediate(_ball.gameObject);
            if (_penguinOne != null) Object.DestroyImmediate(_penguinOne);
            if (_penguinTwo != null) Object.DestroyImmediate(_penguinTwo);
            if (_profileObject != null) Object.DestroyImmediate(_profileObject);
            if (_stageObject != null) Object.DestroyImmediate(_stageObject);
            if (_ground != null) Object.DestroyImmediate(_ground);
        }

        [Test]
        public void 무거운_운반물과_눈덩이는_더_높은_최종_목표속도를_갖는다()
        {
            PenguinMomentumHandling profile = CreateProfile();

            float lightCarry = profile.CarryTargetSpeedMps(0f, 1f);
            float heavyCarry = profile.CarryTargetSpeedMps(1f, 1f);
            float lightBall = profile.SnowballTargetSpeedMps(3.5f, 0f, 1f);
            float heavyBall = profile.SnowballTargetSpeedMps(3.5f, 1f, 1f);

            Assert.Greater(heavyCarry, lightCarry);
            Assert.Greater(heavyBall, lightBall);
        }

        [Test]
        public void 운반과_눈덩이는_누적가속하고_슬라이딩은_기존속도를_유지한다()
        {
            PenguinMomentumHandling profile = CreateProfile();

            Assert.AreEqual(7.5f, profile.SlideTargetSpeedMps(7.5f, 0f), 0.0001f);
            Assert.AreEqual(7.5f, profile.SlideTargetSpeedMps(7.5f, 1f), 0.0001f);
            Assert.Greater(profile.CarryTargetSpeedMps(0.8f, 1f),
                profile.CarryTargetSpeedMps(0.8f, 0f));
            Assert.Greater(profile.SnowballTargetSpeedMps(3.5f, 0.8f, 1f),
                profile.SnowballTargetSpeedMps(3.5f, 0.8f, 0f));
        }

        [Test]
        public void 완전히_누적되어도_큰눈덩이는_단계별_조향상한을_유지한다()
        {
            PenguinMomentumHandling profile = CreateProfile();

            float lightMoving = profile.SnowballSteerAuthority(0f, 6f, 1f);
            float heavyMoving = profile.SnowballSteerAuthority(1f, 6f, 1f);
            float heavyFast = profile.SnowballSteerAuthority(1f, 9f, 1f);

            Assert.Less(heavyMoving, lightMoving,
                "완전히 누적된 입력에서도 큰 눈덩이의 단계별 조향 상한이 남아야 한다");
            Assert.AreEqual(0.33f,
                profile.SnowballSteerAuthority(1f, 5f, 1f), 0.0001f,
                "최대 눈덩이는 기준 최고속도에서 완전 누적 조향이 33%여야 한다");
            Assert.Less(heavyFast, heavyMoving);
        }

        [Test]
        public void 구미터를_넘어도_같은_기울기로_감소해_십이점오미터에서_영이_된다()
        {
            PenguinMomentumHandling profile = CreateProfile();

            Assert.AreEqual(1f, profile.SnowballSpeedSteerAuthority(0f), 0.0001f);
            Assert.AreEqual(0.28f, profile.SnowballSpeedSteerAuthority(9f), 0.0001f);
            Assert.AreEqual(0.16f, profile.SnowballSpeedSteerAuthority(10.5f), 0.0001f);
            Assert.AreEqual(0f, profile.SnowballSpeedSteerAuthority(12.5f), 0.0001f);
            Assert.AreEqual(0f, profile.SnowballSpeedSteerAuthority(20f), 0.0001f);
        }

        [Test]
        public void 작은_눈덩이도_짧지만_체감가능한_조향반응시간을_갖는다()
        {
            PenguinMomentumHandling profile = CreateProfile();

            float initial = profile.SnowballSteerAuthority(
                ESnowBallGrowthStage.Seed, 0f, 4f, 0f);
            float committed = profile.SnowballSteerAuthority(
                ESnowBallGrowthStage.Seed, 0f, 4f, 1f);

            Assert.Less(initial, committed,
                "최소 공도 방향 전환 직후와 입력을 유지한 뒤의 차이가 보여야 한다");
            Assert.Greater(initial, 0.5f,
                "최소 공의 첫 조향 반응까지 사라지면 안 된다");
            Assert.AreEqual(0.35f, profile.SnowballSteerResponseSeconds(
                ESnowBallGrowthStage.Seed, 0f), 0.0001f);
        }

        [Test]
        public void 커질수록_방향전환직후의_조향권한이_연속해서_낮아진다()
        {
            PenguinMomentumHandling profile = CreateProfile();
            float previous = 2f;

            for (int step = 0; step <= 12; step++)
            {
                float radius01 = step / 12f;
                float radiusM = Mathf.Lerp(SnowballStageModel.MinRadiusM,
                    SnowballStageModel.MaxRadiusM, radius01);
                ESnowBallGrowthStage stage = SnowballStageModel.GetStage(radiusM);
                float stageProgress01 = SnowballStageModel.GetStageProgress01(radiusM);
                float speedMps = Mathf.Lerp(4f, 9f, radius01);
                float authority = profile.SnowballSteerAuthority(stage, stageProgress01,
                    speedMps, 0f);

                Assert.LessOrEqual(authority, previous + 0.0001f);
                previous = authority;
            }
        }

        [TestCase(ESnowBallGrowthStage.Seed, 0f, 3.2f, 0.646f, 0.744f)]
        [TestCase(ESnowBallGrowthStage.Stage1, 0f, 3.65f, 0.4845f, 0.6405f)]
        [TestCase(ESnowBallGrowthStage.Stage2, 0f, 4.1f, 0.323f, 0.537f)]
        [TestCase(ESnowBallGrowthStage.Stage3, 0f, 4.55f, 0.1615f, 0.4335f)]
        [TestCase(ESnowBallGrowthStage.Stage4, 1f, 5f, 0f, 0.33f)]
        public void 단계경계의_방향전환직후와_완전누적_조향량이_선형감소한다(
            ESnowBallGrowthStage stage, float stageProgress01, float speedMps,
            float expectedImmediate, float expectedCommitted)
        {
            PenguinMomentumHandling profile = CreateProfile();

            Assert.AreEqual(expectedImmediate, profile.SnowballSteerAuthority(
                stage, stageProgress01, speedMps, 0f), 0.0001f);
            Assert.AreEqual(expectedCommitted, profile.SnowballSteerAuthority(
                stage, stageProgress01, speedMps, 1f), 0.0001f);
        }

        [Test]
        public void 눈덩이_단계_안에서_속도와_추진력은_연속증가하고_조향은_연속감소한다()
        {
            PenguinMomentumHandling profile = CreateProfile();
            float previousEndSpeed = -1f;
            float previousEndDrive = -1f;
            float previousEndSteer = 2f;

            for (int value = 0; value < SnowballStageModel.StageCount; value++)
            {
                var stage = (ESnowBallGrowthStage)value;
                float startSpeed = profile.SnowballTargetSpeedMps(stage, 0f, 1f);
                float middleSpeed = profile.SnowballTargetSpeedMps(stage, 0.5f, 1f);
                float endSpeed = profile.SnowballTargetSpeedMps(stage, 1f, 1f);
                float startDrive = profile.SnowballDriveMultiplier(stage, 0f);
                float endDrive = profile.SnowballDriveMultiplier(stage, 1f);
                float startSteer = profile.SnowballSteerAuthority(stage, 0f,
                    startSpeed, 1f);
                float endSteer = profile.SnowballSteerAuthority(stage, 1f,
                    endSpeed, 1f);

                Assert.Less(startSpeed, middleSpeed);
                Assert.Less(middleSpeed, endSpeed);
                Assert.Less(startDrive, endDrive);
                Assert.Greater(startSteer, endSteer);
                if (value > 0)
                {
                    Assert.AreEqual(previousEndSpeed, startSpeed, 0.0001f);
                    Assert.AreEqual(previousEndDrive, startDrive, 0.0001f);
                    Assert.AreEqual(previousEndSteer, startSteer, 0.0001f);
                }

                previousEndSpeed = endSpeed;
                previousEndDrive = endDrive;
                previousEndSteer = endSteer;
            }

            Assert.AreEqual(5f, previousEndSpeed, 0.0001f);
            Assert.AreEqual(4.2f,
                profile.SnowballBuildUpSeconds(ESnowBallGrowthStage.Stage4, 1f), 0.0001f);
        }

        [Test]
        public void 눈덩이_최고속도는_걷기보다_느린_씨앗에서_달리기보다_느린_최대공까지_증가한다()
        {
            PenguinMomentumHandling profile = CreateProfile();

            Assert.AreEqual(2.4f, profile.SnowballInitialTargetSpeedMps(
                ESnowBallGrowthStage.Seed, 0f), 0.0001f);
            Assert.AreEqual(3.2f, profile.SnowballMaximumSpeedMps(
                ESnowBallGrowthStage.Seed, 0f), 0.0001f);
            Assert.AreEqual(0.65f, profile.SnowballBuildUpSeconds(
                ESnowBallGrowthStage.Seed, 0f), 0.0001f);
            Assert.Less(profile.SnowballMaximumSpeedMps(
                ESnowBallGrowthStage.Seed, 0f), 3.5f,
                "최소 공의 최고속도는 걷기보다 조금 느려야 한다");
            Assert.AreEqual(1.1f, profile.SnowballInitialTargetSpeedMps(
                ESnowBallGrowthStage.Stage4, 1f), 0.0001f);
            Assert.AreEqual(5f, profile.SnowballMaximumSpeedMps(
                ESnowBallGrowthStage.Stage4, 1f), 0.0001f);
            Assert.Less(profile.SnowballMaximumSpeedMps(
                ESnowBallGrowthStage.Stage4, 1f), 5.5f,
                "최대 공도 달리기보다 조금 느려야 한다");
            Assert.AreEqual(3f, profile.SnowballSteerResponseSeconds(
                ESnowBallGrowthStage.Stage4, 1f), 0.0001f);
            Assert.AreEqual(5.2f, profile.SnowballCoastStopSeconds(
                ESnowBallGrowthStage.Stage4, 1f), 0.0001f);
            Assert.AreEqual(1.9f, profile.SnowballBrakeStopSeconds(
                ESnowBallGrowthStage.Stage4, 1f), 0.0001f);

            Assert.AreEqual(3.8f, profile.CarryInitialTargetSpeedMps(0f), 0.0001f);
            Assert.AreEqual(7.2f, profile.CarryMaximumSpeedMps(0f), 0.0001f);
            Assert.AreEqual(2.2f, profile.CarryInitialTargetSpeedMps(1f), 0.0001f);
            Assert.AreEqual(9f, profile.CarryMaximumSpeedMps(1f), 0.0001f);
        }

        [Test]
        public void W를_놓으면_추진누적만_영점이초에_해제된다()
        {
            PenguinMomentumHandling profile = CreateProfile();

            Assert.AreEqual(0.2f, profile.SnowballDriveReleaseSeconds, 0.0001f,
                "W 해제 반응이 눈덩이의 긴 자연 정지시간을 그대로 따라가면 안 된다");
        }

        [Test]
        public void 눈덩이_저항과_제동과_조향력은_반지름제곱으로_증가한다()
        {
            PenguinMomentumHandling profile = CreateProfile();
            float smallRadiusM = SnowballStageModel.MinRadiusM;
            float largeRadiusM = SnowballStageModel.MaxRadiusM;
            float expectedRatio = Mathf.Pow(largeRadiusM / smallRadiusM, 2f);

            Assert.AreEqual(expectedRatio,
                profile.SnowballCoastResistanceForceN(largeRadiusM) /
                profile.SnowballCoastResistanceForceN(smallRadiusM), 0.001f);
            Assert.AreEqual(expectedRatio,
                profile.SnowballBrakeForceNForRadius(largeRadiusM) /
                profile.SnowballBrakeForceNForRadius(smallRadiusM), 0.001f);
            Assert.AreEqual(expectedRatio,
                profile.SnowballSteerForceN(largeRadiusM) /
                profile.SnowballSteerForceN(smallRadiusM), 0.001f);
        }

        [Test]
        public void 질량은_반지름세제곱이라_큰공일수록_정지와_방향전환이_어려워진다()
        {
            PenguinMomentumHandling profile = CreateProfile();
            float previousCoastSeconds = 0f;
            float previousBrakeSeconds = 0f;
            float previousSteerAccelerationMps2 = float.PositiveInfinity;

            for (int value = 1; value <= SnowballStageModel.StageCount; value++)
            {
                var stage = (ESnowBallGrowthStage)value;
                float radiusM = value == SnowballStageModel.StageCount
                    ? SnowballStageModel.MaxRadiusM
                    : SnowballStageModel.GetStageRepresentativeRadius(stage);
                float stageProgress01 = SnowballStageModel.GetStageProgress01(radiusM);
                float massKg = SnowballStageModel.GetEffectiveHandlingMassKg(radiusM);
                float coastSeconds = profile.SnowballCoastStopSeconds(stage, stageProgress01);
                float brakeSeconds = profile.SnowballBrakeStopSeconds(stage, stageProgress01);
                float steerAccelerationMps2 = profile.SnowballSteerForceN(radiusM) / massKg;

                Assert.Greater(coastSeconds, previousCoastSeconds);
                Assert.Greater(brakeSeconds, previousBrakeSeconds);
                Assert.Less(steerAccelerationMps2, previousSteerAccelerationMps2);
                previousCoastSeconds = coastSeconds;
                previousBrakeSeconds = brakeSeconds;
                previousSteerAccelerationMps2 = steerAccelerationMps2;
            }

            Assert.AreEqual(5.2f, previousCoastSeconds, 0.0001f);
            Assert.AreEqual(1.9f, previousBrakeSeconds, 0.0001f);
        }

        [Test]
        public void 짐이_없는_슬라이딩은_관성프로필이_조향을_감쇠하지_않는다()
        {
            PenguinMomentumHandling profile = CreateProfile();

            Assert.AreEqual(1f, profile.SlideSteerAuthority(0f, 0f), 0.0001f);
            Assert.Less(profile.SlideSteerAuthority(0.8f, 0f), 1f);
        }

        [UnityTest]
        public IEnumerator 슬라이딩_S는_제동하고_무거운_하중은_설정대로_더_오래_감속한다()
        {
            CreateGround();
            PenguinLocomotion light = CreatePenguin(new Vector3(-3f, 0.02f, 0f), 30f,
                out _penguinOne);
            PenguinLocomotion heavy = CreatePenguin(new Vector3(3f, 0.02f, 0f), 150f,
                out _penguinTwo);
            Rigidbody lightBody = _penguinOne.GetComponent<Rigidbody>();
            Rigidbody heavyBody = _penguinTwo.GetComponent<Rigidbody>();
            lightBody.linearVelocity = Vector3.forward * 6f;
            heavyBody.linearVelocity = Vector3.forward * 6f;
            var brake = new PenguinMoveInput { Move = Vector2.down };

            for (int step = 0; step < 12; step++)
            {
                light.Step(Time.fixedDeltaTime, brake);
                heavy.Step(Time.fixedDeltaTime, brake);
                yield return new WaitForFixedUpdate();
            }

            float lightSpeed = Vector3.ProjectOnPlane(lightBody.linearVelocity, Vector3.up).magnitude;
            float heavySpeed = Vector3.ProjectOnPlane(heavyBody.linearVelocity, Vector3.up).magnitude;
            Assert.Less(lightSpeed, 6f, "S가 실제 감속을 만들어야 한다");
            Assert.Less(heavySpeed, 6f, "무거운 몸에도 질량에 맞춘 제동력이 적용돼야 한다");
            Assert.Greater(heavySpeed, lightSpeed,
                "무거운 하중의 더 긴 브레이크 정지시간이 실제 감속에 반영돼야 한다");
        }

        [UnityTest]
        public IEnumerator 눈덩이_브레이크는_접선속도를_줄이고_역추진하지_않는다()
        {
            yield return CreateBall();
            Rigidbody body = _ball.GetComponent<Rigidbody>();
            body.useGravity = false;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.linearVelocity = Vector3.right * 4f;

            for (int step = 0; step < 20; step++)
            {
                _ball.SubmitMomentumCoast();
                _ball.SubmitBrake(null, SnowBallCarrier.PlayerPushForceN);
                yield return new WaitForFixedUpdate();
            }

            Assert.That(body.linearVelocity.x, Is.InRange(-0.01f, 0.05f),
                "브레이크가 정지점을 지나 반대 방향 추진이 되면 안 된다");
        }

        [UnityTest]
        public IEnumerator W해제후_작은공은_빨리멈추고_큰공은_자기운동량으로_계속간다()
        {
            CreateGround();
            GameObject penguinPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/InGame/Penguin/MomentumHandling/Prefabs/" +
                "PF_Penguin_MomentumHandling.prefab");
            Assert.IsNotNull(penguinPrefab);
            _penguinOne = Object.Instantiate(penguinPrefab, Vector3.zero,
                Quaternion.identity);
            _penguinOne.name = "__TEST__MomentumCoastPenguin";
            PenguinSnowball snowball = _penguinOne.GetComponent<PenguinSnowball>();
            snowball.NetworkDriven = true;
            PenguinControlState control = _penguinOne.GetComponent<PenguinControlState>();
            Assert.IsTrue(control.TryTransitionTo(EPenguinControlState.SnowballSide));

            GameObject ballPrefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(ballPrefab);
            _ball = Object.Instantiate(ballPrefab).GetComponent<SnowBallCarrier>();
            _ball.gameObject.name = "__TEST__MomentumCoastBall";
            SnowballMomentumMass momentumMass =
                _ball.gameObject.AddComponent<SnowballMomentumMass>();
            momentumMass.Configure(300f, 0.015f, 0.47f);

            PropertyInfo heldProperty = typeof(PenguinSnowball).GetProperty(
                nameof(PenguinSnowball.Held), BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(heldProperty);
            heldProperty.SetValue(snowball, _ball);
            yield return null; // 바인더가 owner와 handling을 공에 연결한다.

            CapsuleCollider capsule = _penguinOne.GetComponent<CapsuleCollider>();
            float bodyRadiusM = capsule.radius * Mathf.Max(
                Mathf.Abs(_penguinOne.transform.lossyScale.x),
                Mathf.Abs(_penguinOne.transform.lossyScale.z));
            Rigidbody ballBody = _ball.GetComponent<Rigidbody>();
            Rigidbody penguinBody = _penguinOne.GetComponent<Rigidbody>();
            var released = new PenguinMoveInput { Move = Vector2.zero };

            _ball.ServerApplyMass(_ball.MassMmForRadius(SnowballStageModel.MinRadiusM));
            momentumMass.RefreshMass();
            PlaceForPush(_ball, _penguinOne, bodyRadiusM);
            ballBody.linearVelocity = Vector3.forward * 2.6f;
            ballBody.angularVelocity = Vector3.right * (2.6f / _ball.RadiusM);
            Vector3 smallStart = ballBody.position;
            for (int step = 0; step < 25; step++)
            {
                snowball.Step(Time.fixedDeltaTime, released);
                yield return new WaitForFixedUpdate();
            }

            float smallSpeedMps = Vector3.ProjectOnPlane(
                ballBody.linearVelocity, Vector3.up).magnitude;
            float smallDistanceM = Vector3.ProjectOnPlane(
                ballBody.position - smallStart, Vector3.up).magnitude;
            Assert.Less(smallSpeedMps, 0.15f,
                "최소 공은 W 해제 후 펭귄 관성처럼 계속 미끄러지면 안 된다");
            Assert.Less(smallDistanceM, 0.6f);

            _ball.ServerApplyMass(_ball.MassMmForRadius(SnowballStageModel.MaxRadiusM));
            momentumMass.RefreshMass();
            PlaceForPush(_ball, _penguinOne, bodyRadiusM);
            ballBody.linearVelocity = Vector3.forward * 9f;
            ballBody.angularVelocity = Vector3.right * (9f / _ball.RadiusM);
            penguinBody.linearVelocity = Vector3.zero;
            Vector3 largeStart = ballBody.position;
            for (int step = 0; step < 25; step++)
            {
                snowball.Step(Time.fixedDeltaTime, released);
                yield return new WaitForFixedUpdate();
            }

            float largeSpeedMps = Vector3.ProjectOnPlane(
                ballBody.linearVelocity, Vector3.up).magnitude;
            float largeDistanceM = Vector3.ProjectOnPlane(
                ballBody.position - largeStart, Vector3.up).magnitude;
            Assert.Greater(largeSpeedMps, 7f,
                "최대 공의 속도는 펭귄이 아니라 공 자체의 큰 운동량으로 남아야 한다");
            Assert.Greater(largeDistanceM, 3.5f);
        }

        [UnityTest]
        public IEnumerator E해제후_큰공은_평지정지보정에_잘리지않고_자기운동량으로_계속간다()
        {
            CreateGround();
            _ground.transform.localScale = new Vector3(30f, 1f, 80f);
            GameObject penguinPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/InGame/Penguin/MomentumHandling/Prefabs/" +
                "PF_Penguin_MomentumHandling.prefab");
            Assert.IsNotNull(penguinPrefab);
            _penguinOne = Object.Instantiate(penguinPrefab, Vector3.zero,
                Quaternion.identity);
            _penguinOne.name = "__TEST__MomentumDetachPenguin";
            PenguinSnowball snowball = _penguinOne.GetComponent<PenguinSnowball>();
            snowball.NetworkDriven = true;
            PenguinControlState control = _penguinOne.GetComponent<PenguinControlState>();
            Assert.IsTrue(control.TryTransitionTo(EPenguinControlState.SnowballSide));

            GameObject ballPrefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(ballPrefab);
            _ball = Object.Instantiate(ballPrefab).GetComponent<SnowBallCarrier>();
            _ball.gameObject.name = "__TEST__MomentumDetachBall";
            _ball.ServerApplyMass(_ball.MassMmForRadius(SnowballStageModel.MaxRadiusM));

            PropertyInfo heldProperty = typeof(PenguinSnowball).GetProperty(
                nameof(PenguinSnowball.Held), BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(heldProperty);
            heldProperty.SetValue(snowball, _ball);
            yield return null; // 바인더가 공에 관성 질량과 밀기 소유자를 연결한다.

            SnowballMomentumMass momentumMass = _ball.GetComponent<SnowballMomentumMass>();
            Assert.IsNotNull(momentumMass);
            momentumMass.RefreshMass();
            CapsuleCollider capsule = _penguinOne.GetComponent<CapsuleCollider>();
            float bodyRadiusM = capsule.radius * Mathf.Max(
                Mathf.Abs(_penguinOne.transform.lossyScale.x),
                Mathf.Abs(_penguinOne.transform.lossyScale.z));
            PlaceForPush(_ball, _penguinOne, bodyRadiusM);

            Rigidbody ballBody = _ball.GetComponent<Rigidbody>();
            ballBody.linearVelocity = Vector3.forward * 5f;
            ballBody.angularVelocity = Vector3.right * (5f / _ball.RadiusM);
            Vector3 start = ballBody.position;

            snowball.Step(Time.fixedDeltaTime, new PenguinMoveInput
            {
                CreateSnowballPressed = true
            });
            Assert.IsNull(snowball.Held, "E 입력은 밀기 상태를 즉시 해제해야 한다");

            for (int step = 0; step < 25; step++)
                yield return new WaitForFixedUpdate();

            float speedMps = Vector3.ProjectOnPlane(
                ballBody.linearVelocity, Vector3.up).magnitude;
            float distanceM = Vector3.ProjectOnPlane(
                ballBody.position - start, Vector3.up).magnitude;
            Assert.Greater(speedMps, 3.5f,
                "E 해제 뒤에도 큰 공의 속도가 평지 정지 보정에 즉시 잘리면 안 된다");
            Assert.Greater(distanceM, 1.5f,
                "E 해제 뒤에는 펭귄이 아니라 공 자체의 운동량으로 계속 이동해야 한다");

            for (int step = 0; step < 350; step++)
                yield return new WaitForFixedUpdate();

            float stoppedSpeedMps = Vector3.ProjectOnPlane(
                ballBody.linearVelocity, Vector3.up).magnitude;
            Assert.Less(stoppedSpeedMps, 0.06f,
                "해제 관성은 영구 드리프트가 아니라 설정된 자연 정지시간 뒤 끝나야 한다");
        }

        [UnityTest]
        public IEnumerator 목표보다_빠른_관성_눈덩이는_W를_눌러도_자동제동하지_않는다()
        {
            yield return CreateBall();
            Rigidbody body = _ball.GetComponent<Rigidbody>();
            body.useGravity = false;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.linearVelocity = Vector3.right * 6f;

            _ball.SubmitMomentumPush(null, Vector3.right * 3.5f,
                SnowBallCarrier.PlayerPushForceN);
            yield return new WaitForFixedUpdate();

            Assert.That(body.linearVelocity.x, Is.EqualTo(6f).Within(0.03f),
                "목표 속도는 추진 상한이며 실제 속도를 깎는 제한이어서는 안 된다");
        }

        [UnityTest]
        public IEnumerator 같은_힘에서는_무거운_눈덩이가_더_천천히_출발한다()
        {
            yield return CreateBall();
            Rigidbody lightBody = _ball.GetComponent<Rigidbody>();
            lightBody.useGravity = false;
            lightBody.linearDamping = 0f;
            lightBody.angularDamping = 0f;

            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            SnowBallCarrier heavy = Object.Instantiate(prefab, new Vector3(0f, 1f, 3f),
                Quaternion.identity).GetComponent<SnowBallCarrier>();
            Rigidbody heavyBody = heavy.GetComponent<Rigidbody>();
            heavyBody.useGravity = false;
            heavyBody.linearDamping = 0f;
            heavyBody.angularDamping = 0f;
            heavy.ServerApplyMass(heavy.VisibleMaxMassMm);

            for (int step = 0; step < 10; step++)
            {
                _ball.SubmitMomentumPush(null, Vector3.right * 8f,
                    SnowBallCarrier.PlayerPushForceN);
                heavy.SubmitMomentumPush(null, Vector3.right * 8f,
                    SnowBallCarrier.PlayerPushForceN);
                yield return new WaitForFixedUpdate();
            }

            Assert.Less(heavyBody.linearVelocity.x, lightBody.linearVelocity.x);
            Object.DestroyImmediate(heavy.gameObject);
        }

        [UnityTest]
        public IEnumerator 조작용_질량은_눈덩이_성장후에도_영점오일팔사에서_300kg_범위를_유지한다()
        {
            yield return CreateBall();
            SnowballMomentumMass momentumMass =
                _ball.gameObject.AddComponent<SnowballMomentumMass>();
            momentumMass.Configure(300f, 0f, 0f);
            Rigidbody body = _ball.GetComponent<Rigidbody>();
            float previousMass = 0f;
            float[] radiiM = { 0.18f, 0.51f, 0.84f, 1.17f, 1.5f };

            foreach (float radiusM in radiiM)
            {
                _ball.ServerApplyMass(_ball.MassMmForRadius(radiusM));
                yield return new WaitForFixedUpdate();

                Assert.AreEqual(momentumMass.EffectiveMassKg, body.mass, 0.1f,
                    "SnowBallCarrier가 성장 질량으로 Rigidbody 질량을 다시 덮으면 안 된다");
                Assert.Greater(body.mass, previousMass);
                previousMass = body.mass;
            }

            Assert.AreEqual(0.5184f,
                SnowballStageModel.GetEffectiveHandlingMassKg(0.18f), 0.0001f);
            Assert.AreEqual(300f, body.mass, 0.1f);
        }

        [UnityTest]
        public IEnumerator 성장타이머가_질량을_갱신한_틱에도_관성용_질량이_마지막에_적용된다()
        {
            yield return CreateBall();
            SnowballGrowthStageTimer timer =
                _ball.gameObject.AddComponent<SnowballGrowthStageTimer>();
            timer.Initialize(_ball);
            timer.ConfigureDuration(SnowballStageModel.DefaultStageDurationSeconds);

            SnowballMomentumMass momentumMass =
                _ball.gameObject.AddComponent<SnowballMomentumMass>();
            momentumMass.Configure(300f, 0f, 0f);
            Rigidbody body = _ball.GetComponent<Rigidbody>();

            _ball.ServerApplyMass(_ball.MassMmForRadius(
                SnowballStageModel.Stage3StartRadiusM));
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(momentumMass.EffectiveMassKg, body.mass, 0.1f,
                "성장 타이머의 원본 눈 질량이 관성용 Rigidbody 질량을 덮으면 안 된다");
            Assert.LessOrEqual(body.mass,
                SnowballStageModel.DefaultMaximumHandlingMassKg + 0.1f);
        }

        [UnityTest]
        public IEnumerator 실제_접촉경로에서_모든단계_눈덩이를_계속_밀수있다()
        {
            CreateGround();
            GameObject penguinPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/InGame/Penguin/MomentumHandling/Prefabs/" +
                "PF_Penguin_MomentumHandling.prefab");
            Assert.IsNotNull(penguinPrefab);
            _penguinOne = Object.Instantiate(penguinPrefab, Vector3.zero,
                Quaternion.identity);
            _penguinOne.name = "__TEST__MomentumContactPenguin";
            PenguinSnowball snowball = _penguinOne.GetComponent<PenguinSnowball>();
            snowball.NetworkDriven = true;
            PenguinControlState control = _penguinOne.GetComponent<PenguinControlState>();
            Assert.IsTrue(control.TryTransitionTo(EPenguinControlState.SnowballSide));

            GameObject ballPrefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(ballPrefab);
            _ball = Object.Instantiate(ballPrefab).GetComponent<SnowBallCarrier>();
            _ball.gameObject.name = "__TEST__MomentumContactBall";
            SnowballMomentumMass momentumMass =
                _ball.gameObject.AddComponent<SnowballMomentumMass>();
            momentumMass.Configure(300f, 0.015f, 0.47f);

            PropertyInfo heldProperty = typeof(PenguinSnowball).GetProperty(
                nameof(PenguinSnowball.Held), BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(heldProperty);
            heldProperty.SetValue(snowball, _ball);

            Rigidbody ballBody = _ball.GetComponent<Rigidbody>();
            Rigidbody penguinBody = _penguinOne.GetComponent<Rigidbody>();
            CapsuleCollider capsule = _penguinOne.GetComponent<CapsuleCollider>();
            float bodyRadiusM = capsule.radius * Mathf.Max(
                Mathf.Abs(_penguinOne.transform.lossyScale.x),
                Mathf.Abs(_penguinOne.transform.lossyScale.z));
            var forward = new PenguinMoveInput { Move = Vector2.up };

            ESnowBallGrowthStage[] stages =
            {
                ESnowBallGrowthStage.Stage1,
                ESnowBallGrowthStage.Stage2,
                ESnowBallGrowthStage.Stage3,
                ESnowBallGrowthStage.Stage4
            };
            foreach (ESnowBallGrowthStage stage in stages)
            {
                float radiusM = stage == ESnowBallGrowthStage.Stage4
                    ? SnowballStageModel.MaxRadiusM
                    : SnowballStageModel.GetStageRepresentativeRadius(stage);
                _ball.ServerApplyMass(_ball.MassMmForRadius(radiusM));
                momentumMass.RefreshMass();
                _ball.transform.position = new Vector3(0f, _ball.RadiusM + 0.02f, 0f);
                _penguinOne.transform.position = new Vector3(0f, 0.02f,
                    -(_ball.RadiusM + bodyRadiusM + 0.02f));
                ballBody.linearVelocity = Vector3.zero;
                ballBody.angularVelocity = Vector3.zero;
                penguinBody.linearVelocity = Vector3.zero;
                yield return new WaitForFixedUpdate();

                int pushingSteps = 0;
                for (int step = 0; step < 50; step++)
                {
                    snowball.Step(Time.fixedDeltaTime, forward);
                    if (snowball.IsPushing) pushingSteps++;
                    yield return new WaitForFixedUpdate();
                }

                float forwardSpeedMps = Vector3.Dot(
                    Vector3.ProjectOnPlane(ballBody.linearVelocity, Vector3.up),
                    Vector3.forward);
                Assert.GreaterOrEqual(pushingSteps, 48,
                    $"{stage}에서 접촉 판정이 반복적으로 끊기면 안 된다");
                Assert.Greater(forwardSpeedMps, 1.25f,
                    $"{stage}가 1초 동안 W를 눌러도 사실상 움직이지 않는 상태면 안 된다");
            }
        }

        [UnityTest]
        public IEnumerator 실제_Seed_접촉경로에서_AD로_눈덩이_둘레를_회전한다()
        {
            CreateGround();
            GameObject penguinPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/InGame/Penguin/MomentumHandling/Prefabs/" +
                "PF_Penguin_MomentumHandling.prefab");
            Assert.IsNotNull(penguinPrefab);
            _penguinOne = Object.Instantiate(penguinPrefab, Vector3.zero,
                Quaternion.identity);
            _penguinOne.name = "__TEST__MomentumOrbitPenguin";
            PenguinSnowball snowball = _penguinOne.GetComponent<PenguinSnowball>();
            snowball.NetworkDriven = true;
            PenguinControlState control = _penguinOne.GetComponent<PenguinControlState>();
            Assert.IsTrue(control.TryTransitionTo(EPenguinControlState.SnowballSide));

            GameObject ballPrefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(ballPrefab);
            _ball = Object.Instantiate(ballPrefab).GetComponent<SnowBallCarrier>();
            _ball.gameObject.name = "__TEST__MomentumOrbitBall";
            _ball.ServerApplyMass(_ball.MassMmForRadius(SnowballStageModel.MinRadiusM));
            SnowballMomentumMass momentumMass =
                _ball.gameObject.AddComponent<SnowballMomentumMass>();
            momentumMass.Configure(300f, 0.015f, 0.47f);

            PropertyInfo heldProperty = typeof(PenguinSnowball).GetProperty(
                nameof(PenguinSnowball.Held), BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(heldProperty);
            heldProperty.SetValue(snowball, _ball);

            CapsuleCollider capsule = _penguinOne.GetComponent<CapsuleCollider>();
            float bodyRadiusM = capsule.radius * Mathf.Max(
                Mathf.Abs(_penguinOne.transform.lossyScale.x),
                Mathf.Abs(_penguinOne.transform.lossyScale.z));
            _ball.transform.position = new Vector3(0f, _ball.RadiusM + 0.02f, 0f);
            _penguinOne.transform.position = new Vector3(0f, 0.02f,
                -(_ball.RadiusM + bodyRadiusM + 0.02f));
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            Vector3 startRadial = Vector3.ProjectOnPlane(
                _penguinOne.transform.TransformPoint(capsule.center) - _ball.transform.position,
                Vector3.up).normalized;
            var steerRight = new PenguinMoveInput { Move = Vector2.right };
            for (int step = 0; step < 10; step++)
            {
                snowball.Step(Time.fixedDeltaTime, steerRight);
                yield return new WaitForFixedUpdate();
            }

            Vector3 endRadial = Vector3.ProjectOnPlane(
                _penguinOne.transform.TransformPoint(capsule.center) - _ball.transform.position,
                Vector3.up).normalized;
            float orbitAngleDeg = Mathf.Abs(Vector3.SignedAngle(
                startRadial, endRadial, Vector3.up));
            float horizontalDistanceM = Vector3.ProjectOnPlane(
                _penguinOne.transform.TransformPoint(capsule.center) - _ball.transform.position,
                Vector3.up).magnitude;
            float expectedDistanceM = _ball.RadiusM + bodyRadiusM + 0.02f;

            Assert.Greater(orbitAngleDeg, 40f,
                "정지한 Seed 눈덩이에서 A/D가 눈덩이 둘레 회전으로 충분히 반응해야 한다");
            Assert.AreEqual(expectedDistanceM, horizontalDistanceM, 0.12f,
                "회전 중 접촉 반지름을 잃어 밀기 위치에서 이탈하면 안 된다");
        }

        [UnityTest]
        public IEnumerator 실제_테스트씬의_로컬입력순서에서도_생성한_공을_밀고_방향을_바꾼다()
        {
            CreateGround();
            _stageObject = new GameObject("__TEST__MomentumSnowStage");
            SnowCpuStage stage = _stageObject.AddComponent<SnowCpuStage>();
            yield return null;

            GameObject penguinPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/InGame/Penguin/MomentumHandling/Prefabs/" +
                "PF_Penguin_MomentumHandling.prefab");
            Assert.IsNotNull(penguinPrefab);
            _penguinOne = Object.Instantiate(penguinPrefab,
                new Vector3(0f, 0.02f, 0f), Quaternion.identity);
            _penguinOne.name = "__TEST__MomentumLocalInputPenguin";
            PenguinInputReader reader = _penguinOne.GetComponent<PenguinInputReader>();
            PenguinSnowball snowball = _penguinOne.GetComponent<PenguinSnowball>();
            reader.enabled = false;
            snowball.BeginPush();
            Assert.IsNotNull(snowball.Held, $"테스트 씬의 눈에서 공을 만들 수 있어야 한다 ({snowball.LastFailure})");
            _ball = snowball.Held;
            SnowballGrowthStageTimer timer =
                _ball.gameObject.AddComponent<SnowballGrowthStageTimer>();
            timer.Initialize(_ball);
            timer.ConfigureDuration(SnowballStageModel.DefaultStageDurationSeconds);
            timer.ConfigureContinuousGrowthShare(SnowballStageModel.DefaultContinuousGrowthShare);
            SnowballGrowthFootprintSync footprint =
                _ball.GetComponent<SnowballGrowthFootprintSync>();
            if (footprint == null)
                footprint = _ball.gameObject.AddComponent<SnowballGrowthFootprintSync>();
            footprint.Configure(_ball, timer, stage);
            SnowballGrowthPlayablePresentation presentation =
                _ball.gameObject.AddComponent<SnowballGrowthPlayablePresentation>();
            presentation.ConfigureStageTimer(timer);
            presentation.Initialize(_ball);
            Rigidbody ballBody = _ball.GetComponent<Rigidbody>();
            yield return null;
            Assert.IsNotNull(_ball.GetComponent<SnowballMomentumMass>(),
                "테스트 프리팹의 바인더가 생성한 공에 관성 질량을 연결해야 한다");

            SetMoveInput(reader, Vector2.zero);
            for (int step = 0; step < 5; step++)
                yield return new WaitForFixedUpdate();

            SetMoveInput(reader, Vector2.up);
            int straightPushSteps = 0;
            for (int step = 0; step < 25; step++)
            {
                yield return new WaitForFixedUpdate();
                if (snowball.IsPushing) straightPushSteps++;
            }

            Vector3 straightVelocity = Vector3.ProjectOnPlane(
                ballBody.linearVelocity, Vector3.up);
            Assert.Greater(straightVelocity.magnitude, 1f,
                $"씬과 같은 로컬 입력 경로에서 W가 실제 추진으로 이어져야 한다 · " +
                $"push={straightPushSteps}/25, radius={_ball.RadiusM:0.000}, " +
                $"mass={ballBody.mass:0.000}, target={snowball.MomentumTargetSpeedMps:0.000}, " +
                $"build={snowball.MomentumBuildUp01:0.000}, " +
                $"ballPos={_ball.transform.position}, penguinPos={_penguinOne.transform.position}, " +
                $"penguinVelocity={_penguinOne.GetComponent<Rigidbody>().linearVelocity}");

            float startDisplayRadiusM = presentation.DisplayRadiusM;
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                for (int frame = 0; frame < 120; frame++)
                    yield return null;
            }
            finally
            {
                Time.captureDeltaTime = 0f;
            }

            Assert.Greater(presentation.DisplayRadiusM - startDisplayRadiusM, 0.01f,
                "계측 중 실제로 눈을 수확해 표시 반지름이 성장해야 한다");

            SetMoveInput(reader, new Vector2(0.7f, 0.7f));
            for (int step = 0; step < 35; step++)
                yield return new WaitForFixedUpdate();

            Vector3 turnedVelocity = Vector3.ProjectOnPlane(
                ballBody.linearVelocity, Vector3.up);
            float directionChangeDeg = Vector3.Angle(straightVelocity, turnedVelocity);
            Assert.IsTrue(snowball.IsPushing,
                "W+D를 유지하는 동안 접촉이 끊겨 추진 상태가 사라지면 안 된다");
            Assert.Greater(directionChangeDeg, 15f,
                "씬과 같은 W+D 입력이 눈덩이 진행 방향을 체감 가능하게 바꿔야 한다");

            SetMoveInput(reader, Vector2.zero);
        }

        private PenguinMomentumHandling CreateProfile()
        {
            _profileObject = new GameObject("__TEST__MomentumProfile");
            return _profileObject.AddComponent<PenguinMomentumHandling>();
        }

        private IEnumerator CreateBall()
        {
            CreateGround();

            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);
            _ball = Object.Instantiate(prefab, new Vector3(0f, 0.5f, 0f),
                Quaternion.identity).GetComponent<SnowBallCarrier>();
            _ball.gameObject.name = "__TEST__MomentumBall";
            yield return new WaitForFixedUpdate();
        }

        private void CreateGround()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "__TEST__MomentumGround";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(30f, 1f, 20f);
        }

        private static void PlaceForPush(SnowBallCarrier ball, GameObject penguin,
            float bodyRadiusM)
        {
            ball.transform.position = new Vector3(0f, ball.RadiusM + 0.02f, 0f);
            penguin.transform.position = new Vector3(0f, 0.02f,
                -(ball.RadiusM + bodyRadiusM + 0.02f));
            Physics.SyncTransforms();
        }

        private static PenguinLocomotion CreatePenguin(Vector3 position, float massKg,
            out GameObject instance)
        {
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab");
            Assert.IsNotNull(prefab);
            instance = Object.Instantiate(prefab, position, Quaternion.identity);
            instance.name = "__TEST__MomentumPenguin";
            instance.AddComponent<PenguinMomentumHandling>();
            Rigidbody body = instance.GetComponent<Rigidbody>();
            body.mass = massKg;
            PenguinLocomotion locomotion = instance.GetComponent<PenguinLocomotion>();
            locomotion.NetworkDriven = true;
            MethodInfo enterSliding = typeof(PenguinLocomotion).GetMethod("EnterSliding",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(enterSliding);
            enterSliding.Invoke(locomotion, null);
            return locomotion;
        }

        private static void SetMoveInput(PenguinInputReader reader, Vector2 value)
        {
            PropertyInfo property = typeof(PenguinInputReader).GetProperty(
                nameof(PenguinInputReader.MoveInput),
                BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(property);
            property.SetValue(reader, value);
        }
    }

    public sealed class PenguinMomentumAccelerationMeasurementTests
    {
        private const float ReachRatio = 0.95f;
        private const float MaximumMeasurementSeconds = 5f;

        private GameObject _ground;
        private readonly GameObject[] _balls = new GameObject[4];
        private readonly GameObject[] _penguins = new GameObject[2];

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject ball in _balls)
                if (ball != null) Object.DestroyImmediate(ball);
            foreach (GameObject penguin in _penguins)
                if (penguin != null) Object.DestroyImmediate(penguin);
            if (_ground != null) Object.DestroyImmediate(_ground);
        }

        [UnityTest]
        public IEnumerator 평지에서_각_하중의_최종목표속도_95퍼센트_도달시간을_측정한다()
        {
            CreateGround();
            GameObject penguinPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/InGame/Penguin/MomentumHandling/Prefabs/" +
                "PF_Penguin_MomentumHandling.prefab");
            Assert.IsNotNull(penguinPrefab);
            PenguinMomentumHandling profile = penguinPrefab.GetComponent<PenguinMomentumHandling>();
            Assert.IsNotNull(profile);

            var ballCarriers = new SnowBallCarrier[SnowballStageModel.StageCount];
            var ballBodies = new Rigidbody[SnowballStageModel.StageCount];
            var ballTargets = new float[SnowballStageModel.StageCount];
            var ballTimes = new float[SnowballStageModel.StageCount];
            var ballPeaks = new float[SnowballStageModel.StageCount];
            var ballBuildUp01 = new float[SnowballStageModel.StageCount];
            for (int i = 0; i < ballTimes.Length; i++) ballTimes[i] = -1f;

            GameObject snowballPrefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(snowballPrefab);
            for (int i = 0; i < SnowballStageModel.StageCount; i++)
            {
                _balls[i] = Object.Instantiate(snowballPrefab,
                    new Vector3(-45f + i * 30f, 2f, -100f), Quaternion.identity);
                _balls[i].name = $"__TEST__MomentumStage{i + 1}";
                ballCarriers[i] = _balls[i].GetComponent<SnowBallCarrier>();
                var stage = (ESnowBallGrowthStage)(i + 1);
                float representativeRadiusM =
                    SnowballStageModel.GetStageRepresentativeRadius(stage);
                ballCarriers[i].ServerApplyMass(
                    ballCarriers[i].MassMmForRadius(representativeRadiusM));
                Assert.AreEqual(stage,
                    SnowballStageModel.GetStage(ballCarriers[i].RadiusM));
                SnowballMomentumMass momentumMass = _balls[i].AddComponent<SnowballMomentumMass>();
                momentumMass.Configure(300f, 0.015f, 0.47f);
                momentumMass.RefreshMass();
                _balls[i].transform.position = new Vector3(-45f + i * 30f,
                    ballCarriers[i].RadiusM + 0.02f, -100f);
                ballBodies[i] = _balls[i].GetComponent<Rigidbody>();
                ballTargets[i] = profile.SnowballTargetSpeedMps(stage, 0.5f, 1f);
            }

            float[] cargoMassKg = { 8f, 120f };
            var locomotions = new PenguinLocomotion[cargoMassKg.Length];
            var penguinBodies = new Rigidbody[cargoMassKg.Length];
            var carryTargets = new float[cargoMassKg.Length];
            var carryTimes = new float[cargoMassKg.Length];
            var carryPeaks = new float[cargoMassKg.Length];
            for (int i = 0; i < carryTimes.Length; i++) carryTimes[i] = -1f;

            for (int i = 0; i < cargoMassKg.Length; i++)
            {
                _penguins[i] = Object.Instantiate(penguinPrefab,
                    new Vector3(75f + i * 20f, 0.02f, -100f), Quaternion.identity);
                _penguins[i].name = $"__TEST__MomentumGift{cargoMassKg[i]:0}kg";
                penguinBodies[i] = _penguins[i].GetComponent<Rigidbody>();
                penguinBodies[i].mass = 30f + cargoMassKg[i];
                locomotions[i] = _penguins[i].GetComponent<PenguinLocomotion>();
                locomotions[i].NetworkDriven = true;
                PenguinControlState control = _penguins[i].GetComponent<PenguinControlState>();
                Assert.IsTrue(control.TryTransitionTo(EPenguinControlState.Carrying));
                float cargoShare01 = cargoMassKg[i] / (30f + cargoMassKg[i]);
                carryTargets[i] = profile.CarryTargetSpeedMps(cargoShare01, 1f);
            }

            for (int i = 0; i < 3; i++) yield return new WaitForFixedUpdate();
            foreach (Rigidbody body in ballBodies) body.linearVelocity = Vector3.zero;
            foreach (Rigidbody body in penguinBodies) body.linearVelocity = Vector3.zero;

            float elapsed = 0f;
            var carryInput = new PenguinMoveInput { SprintHeld = true };
            while (elapsed < MaximumMeasurementSeconds &&
                   (HasPending(ballTimes) || HasPending(carryTimes)))
            {
                float dt = Time.fixedDeltaTime;
                for (int i = 0; i < ballCarriers.Length; i++)
                {
                    var stage = (ESnowBallGrowthStage)(i + 1);
                    ballBuildUp01[i] = Mathf.MoveTowards(ballBuildUp01[i], 1f,
                        dt / profile.SnowballBuildUpSeconds(stage, 0.5f));
                    float target = profile.SnowballTargetSpeedMps(
                        stage, 0.5f, ballBuildUp01[i]);
                    ballCarriers[i].SubmitMomentumPush(null, Vector3.forward * target,
                        SnowBallCarrier.PlayerPushForceN *
                        profile.SnowballDriveMultiplier(stage, 0.5f));
                }
                foreach (PenguinLocomotion locomotion in locomotions)
                    locomotion.Step(dt, carryInput);

                yield return new WaitForFixedUpdate();
                elapsed += dt;

                for (int i = 0; i < ballBodies.Length; i++)
                {
                    float speed = HorizontalSpeed(ballBodies[i]);
                    ballPeaks[i] = Mathf.Max(ballPeaks[i], speed);
                    if (ballTimes[i] < 0f && speed >= ballTargets[i] * ReachRatio)
                        ballTimes[i] = elapsed;
                }
                for (int i = 0; i < penguinBodies.Length; i++)
                {
                    float speed = HorizontalSpeed(penguinBodies[i]);
                    carryPeaks[i] = Mathf.Max(carryPeaks[i], speed);
                    if (carryTimes[i] < 0f && speed >= carryTargets[i] * ReachRatio)
                        carryTimes[i] = elapsed;
                }
            }

            WriteResults(ballCarriers, ballBodies, ballTargets, ballTimes, ballPeaks,
                cargoMassKg, carryTargets, carryTimes, carryPeaks);
            for (int i = 0; i < ballTimes.Length; i++)
                Assert.GreaterOrEqual(ballTimes[i], 0f,
                    $"Stage {i + 1} 눈덩이가 5초 안에 목표 속도의 95%에 도달해야 한다");
            for (int i = 1; i < ballTimes.Length; i++)
                Assert.Greater(ballTimes[i], ballTimes[i - 1],
                    "큰 눈덩이일수록 최고 속도 도달 시간이 길어야 한다");
            for (int i = 0; i < carryTimes.Length; i++)
                Assert.GreaterOrEqual(carryTimes[i], 0f,
                    $"{cargoMassKg[i]:0}kg 선물이 5초 안에 목표 속도의 95%에 도달해야 한다");
        }

        private void CreateGround()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "__TEST__MomentumMeasurementGround";
            _ground.transform.position = new Vector3(25f, -0.5f, 100f);
            _ground.transform.localScale = new Vector3(220f, 1f, 500f);
        }

        private static bool HasPending(float[] values)
        {
            foreach (float value in values)
                if (value < 0f) return true;
            return false;
        }

        private static float HorizontalSpeed(Rigidbody body)
            => Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude;

        private static void WriteResults(SnowBallCarrier[] balls, Rigidbody[] ballBodies,
            float[] ballTargets, float[] ballTimes, float[] ballPeaks, float[] cargoMassKg,
            float[] carryTargets, float[] carryTimes, float[] carryPeaks)
        {
            var result = new StringBuilder();
            result.AppendLine("definition=first fixed step at or above 95% of final flat-ground target speed");
            result.AppendLine("fixedDeltaSeconds=" +
                              Time.fixedDeltaTime.ToString("0.000", CultureInfo.InvariantCulture));
            result.AppendLine("kind\tlabel\tmassKg\tgrowth01\ttargetMps\ttimeTo95Seconds\tpeakMps");
            for (int i = 0; i < balls.Length; i++)
            {
                result.Append("snowball\tStage").Append(i + 1).Append('\t')
                    .Append(ballBodies[i].mass.ToString("0.00", CultureInfo.InvariantCulture)).Append('\t')
                    .Append(balls[i].GrowthProgress01.ToString("0.000", CultureInfo.InvariantCulture)).Append('\t')
                    .Append(ballTargets[i].ToString("0.000", CultureInfo.InvariantCulture)).Append('\t')
                    .Append(ballTimes[i].ToString("0.000", CultureInfo.InvariantCulture)).Append('\t')
                    .AppendLine(ballPeaks[i].ToString("0.000", CultureInfo.InvariantCulture));
            }
            for (int i = 0; i < cargoMassKg.Length; i++)
            {
                result.Append("gift\t").Append(cargoMassKg[i].ToString("0", CultureInfo.InvariantCulture))
                    .Append("kg\t").Append(cargoMassKg[i].ToString("0.00", CultureInfo.InvariantCulture))
                    .Append("\t-\t")
                    .Append(carryTargets[i].ToString("0.000", CultureInfo.InvariantCulture)).Append('\t')
                    .Append(carryTimes[i].ToString("0.000", CultureInfo.InvariantCulture)).Append('\t')
                    .AppendLine(carryPeaks[i].ToString("0.000", CultureInfo.InvariantCulture));
            }
            TestContext.WriteLine(result.ToString());
        }
    }
}
