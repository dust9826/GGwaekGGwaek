using System.Collections;
using System.Reflection;
using MoreMountains.Feedbacks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PPack
{
    /// <summary>
    /// 눈덩이 밀기 — <b>카메라 기준 조작 스택(<see cref="PenguinLocomotion"/>) 위에서</b>.
    ///
    /// <para><b>왜 테스트로 재는가:</b> 조작감은 화면으로 봐야 하지만, "미는 힘이 실제로 공에
    /// 전달되는가" 는 숫자다. 그리고 CLI 에서 키 입력을 주입하는 방법은 창 포커스에 걸려 조용히
    /// 실패한다(실측) — 리더를 끄고 값을 직접 넣으면 프레임 경쟁 없이 같은 경로를 밟는다.</para>
    ///
    /// <para><see cref="PenguinInputReader"/> 를 <b>비활성</b>으로 두는 것이 핵심이다. 컴포넌트가
    /// 꺼져 있어도 다른 컴포넌트가 그 속성을 읽는 것은 그대로 되므로, 리더의 <c>Update</c> 가
    /// 내 값을 덮어쓰지 않는다.</para>
    /// </summary>
    public sealed class PenguinSnowballPushTests
    {
        private GameObject _stageObject;
        private GameObject _groundObject;
        private GameObject _penguinObject;
        private GameObject _pusherOne;
        private GameObject _pusherTwo;
        private SnowBallCarrier _ball;
        private Scene _probeScene;

        /// <summary>
        /// <b>프레임 시간을 고정한다.</b> 배치 테스트는 프레임이 극단적으로 짧아
        /// <c>Time.deltaTime</c> 이 0.0001 초 수준이다. <see cref="PenguinLocomotion"/>은
        /// 2026-08-22 Rigidbody 재작성 이후 <c>FixedUpdate</c>(고정 타임스텝)에서 움직이지만,
        /// <c>Time.captureDeltaTime</c>은 렌더 프레임 간격뿐 아니라 그 프레임 동안 도는
        /// <c>FixedUpdate</c> 횟수도 같이 맞춰 주므로 여전히 필요하다 — 없으면 120프레임을
        /// 돌려도 물리 스텝이 실질적으로 거의 안 돈다.
        /// </summary>
        [SetUp]
        public void SetUp() => Time.captureDeltaTime = 1f / 60f;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            // 남은 공은 다음 테스트의 필드를 파먹는다 - 스테이지가 씬의 모든 공을 굴리기 때문이다.
            // <b>즉시 파괴한다.</b> Play Mode 테스트 스위트는 테스트 사이에 씬을
            // 다시 로드하지 않는다. 지연 파괴는 다음 테스트가 시작한 뒤에 일어나고,
            // 그동안 남은 공이 그 테스트의 필드를 파먹는다(실측).
            if (_ball != null) Object.DestroyImmediate(_ball.gameObject);
            if (_penguinObject != null) Object.DestroyImmediate(_penguinObject);
            if (_pusherOne != null) Object.DestroyImmediate(_pusherOne);
            if (_pusherTwo != null) Object.DestroyImmediate(_pusherTwo);
            if (_stageObject != null) Object.DestroyImmediate(_stageObject);
            if (_groundObject != null) Object.DestroyImmediate(_groundObject);
            if (_probeScene.IsValid() && _probeScene.isLoaded) SceneManager.UnloadSceneAsync(_probeScene);
        }

        /// <summary>
        /// 프리팹은 <c>Resources</c> 밖에 있으므로 에디터 API 로 읽는다. 이 프로젝트의 PlayMode
        /// 테스트는 에디터에서만 돌지만(플레이어 테스트 런은 없다), 그 가정을 코드에 박아 두는
        /// 대신 조건부로 가둔다 — 플레이어 빌드에서 컴파일이 깨지지 않는다.
        /// </summary>
        private static GameObject LoadPenguinPrefab()
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab");
#else
            return null;
#endif
        }

        /// <summary>
        /// 공을 향하는 세계 방향을 <b>카메라 축</b>으로 되돌린다 — <see cref="PenguinLocomotion"/> 이
        /// 입력을 카메라 기준으로 읽으므로, 사람이 화면을 보고 누르는 것과 같은 값을 만들어야 한다.
        /// </summary>
        private static Vector2 TowardBall(Transform penguin, Transform ball, PenguinLocomotion loco)
        {
            Transform pivot = (Transform)typeof(PenguinLocomotion)
                .GetField("_cameraPivot", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(loco);
            if (pivot == null) pivot = penguin;

            Vector3 to = ball.position - penguin.position;
            to.y = 0f;
            if (to.sqrMagnitude < 1e-6f) return Vector2.zero;
            to.Normalize();

            Vector3 fwd = pivot.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 right = pivot.right;  right.y = 0f; right.Normalize();

            return new Vector2(Vector3.Dot(to, right), Vector3.Dot(to, fwd));
        }

        private static void SetMoveInput(PenguinInputReader reader, Vector2 value)
        {
            PropertyInfo p = typeof(PenguinInputReader).GetProperty(
                nameof(PenguinInputReader.MoveInput),
                BindingFlags.Instance | BindingFlags.Public);
            p.SetValue(reader, value);
        }

        private static void SetCreateSnowballPressed(PenguinInputReader reader, bool value)
            => SetPublicProperty(reader, nameof(PenguinInputReader.CreateSnowballPressedThisFrame), value);

        private static void SetJumpPressed(PenguinInputReader reader, bool value)
            => SetPublicProperty(reader, nameof(PenguinInputReader.JumpPressedThisFrame), value);

        private static void SetPublicProperty(object target, string name, object value)
            => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                .SetValue(target, value);

        private void CreateFlatGround(Scene scene, float topY)
        {
            _groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _groundObject.name = "__TEST__Ground";
            _groundObject.transform.localScale = new Vector3(20f, 1f, 20f);
            _groundObject.transform.position = new Vector3(0f, topY - 0.5f, 0f);
            SceneManager.MoveGameObjectToScene(_groundObject, scene);
        }

        [UnityTest]
        public IEnumerator CPU_눈을_슬라이딩_눈으로_판정한다()
        {
            _stageObject = new GameObject("__TEST__SnowCpuStage");
            SnowCpuStage stage = _stageObject.AddComponent<SnowCpuStage>();
            yield return null;
            Assert.IsNotNull(stage.Field, "CPU 눈 필드가 초기화돼야 한다");

            GameObject penguinPrefab = LoadPenguinPrefab();
            Assert.IsNotNull(penguinPrefab, "PF_Penguin 이 있어야 한다");
            _penguinObject = Object.Instantiate(penguinPrefab, Vector3.zero, Quaternion.identity);
            _penguinObject.name = "__TEST__SnowSamplingPenguin";

            PenguinLocomotion locomotion = _penguinObject.GetComponent<PenguinLocomotion>();
            typeof(PenguinLocomotion)
                .GetField("_snowCpuStage", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(locomotion, stage);

            float coverage = (float)typeof(PenguinLocomotion)
                .GetMethod("SampleSnowCoverage", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(locomotion, null);

            Assert.That(coverage, Is.EqualTo(1f),
                "300 mm CPU 눈 위인데 맨바닥 마찰을 선택했다");
        }

        [UnityTest]
        public IEnumerator 앞의_눈덩이는_E로_선택하고_Space를_눌러도_옆_밀기를_유지한다()
        {
            CreateFlatGround(SceneManager.GetActiveScene(), 0f);
            _stageObject = new GameObject("__TEST__SnowCpuStage");
            SnowCpuStage stage = _stageObject.AddComponent<SnowCpuStage>();
            yield return null;

            _ball = stage.TryCreateBall(Vector3.zero);
            Assert.IsNotNull(_ball, "눈 위에 선택할 씨앗 눈덩이를 만들 수 있어야 한다");

            GameObject penguinPrefab = LoadPenguinPrefab();
            _penguinObject = Object.Instantiate(penguinPrefab, new Vector3(0f, 0.01f, -1f),
                Quaternion.identity);
            _penguinObject.name = "__TEST__SelectAndMountPenguin";
            PenguinInputReader reader = _penguinObject.GetComponent<PenguinInputReader>();
            PenguinSnowball snowball = _penguinObject.GetComponent<PenguinSnowball>();
            reader.enabled = false;

            Physics.SyncTransforms();
            yield return null;
            Assert.IsTrue(snowball.CanSelectNearbyBall, "전방 눈덩이에는 E 안내가 떠야 한다");

            SetCreateSnowballPressed(reader, true);
            yield return new WaitForFixedUpdate();
            SetCreateSnowballPressed(reader, false);
            Assert.AreSame(_ball, snowball.Held, "E는 새 공 생성보다 전방 공 선택을 우선해야 한다");
            Assert.IsFalse(snowball.IsMountedOnTop, "E 선택만으로 공 위에 올라가면 안 된다");
            Assert.AreEqual(EPenguinControlState.SnowballSide,
                _penguinObject.GetComponent<PenguinControlState>().Current);

            SetJumpPressed(reader, true);
            yield return new WaitForFixedUpdate();
            SetJumpPressed(reader, false);
            Assert.IsFalse(snowball.IsMountedOnTop, "눈 밀기 상태의 Space 탑승은 현재 막혀 있어야 한다");
            Assert.AreSame(_ball, snowball.Held, "Space를 눌러도 선택한 눈덩이를 놓치면 안 된다");
            Assert.AreEqual(EPenguinControlState.SnowballSide,
                _penguinObject.GetComponent<PenguinControlState>().Current);
            Assert.IsFalse(_penguinObject.GetComponent<Rigidbody>().isKinematic,
                "Space를 눌렀다고 펭귄 바디가 탑승용 키네마틱으로 바뀌면 안 된다");
        }

        [UnityTest]
        public IEnumerator 눈덩이_옆_밀기_상태는_W없이_AD만_눌러도_양날개_접점을_유지한다()
        {
            CreateFlatGround(SceneManager.GetActiveScene(), 0f);
            _stageObject = new GameObject("__TEST__SnowCpuStage");
            SnowCpuStage stage = _stageObject.AddComponent<SnowCpuStage>();
            yield return null;

            _ball = stage.TryCreateBall(Vector3.zero);
            Assert.IsNotNull(_ball);

            GameObject penguinPrefab = LoadPenguinPrefab();
            Assert.IsNotNull(penguinPrefab);
            _penguinObject = Object.Instantiate(penguinPrefab,
                new Vector3(0f, 0.01f, -1f), Quaternion.identity);
            _penguinObject.name = "__TEST__SideGripPenguin";

            PenguinInputReader reader = _penguinObject.GetComponent<PenguinInputReader>();
            PenguinSnowball snowball = _penguinObject.GetComponent<PenguinSnowball>();
            PenguinSnowballPush pushPose = _penguinObject.GetComponentInChildren<PenguinSnowballPush>();
            reader.enabled = false;

            Physics.SyncTransforms();
            yield return null;
            snowball.BeginPush();
            Assert.AreSame(_ball, snowball.Held);

            SetMoveInput(reader, Vector2.right);
            for (int i = 0; i < 20; i++)
            {
                yield return new WaitForFixedUpdate();
                yield return null;
            }

            Assert.IsFalse(snowball.IsPushing, "W가 없으므로 공에 전진 힘을 제출하면 안 된다");
            Assert.That(pushPose.Weight, Is.GreaterThan(0.99f),
                "A/D 둘레 이동 중에도 팔 자세가 완전히 유지돼야 한다");
            Assert.IsTrue(pushPose.CanPush, "양 날개가 계속 눈덩이 표면에 닿아야 한다");
        }

        [UnityTest]
        public IEnumerator 눈덩이가_없으면_E는_발밑_눈으로_새_공을_만든다()
        {
            CreateFlatGround(SceneManager.GetActiveScene(), 0f);
            _stageObject = new GameObject("__TEST__SnowCpuStage");
            SnowCpuStage stage = _stageObject.AddComponent<SnowCpuStage>();
            yield return null;

            GameObject penguinPrefab = LoadPenguinPrefab();
            Vector3 start = new Vector3(4f, 0.01f, 4f);
            _penguinObject = Object.Instantiate(penguinPrefab, start, Quaternion.identity);
            _penguinObject.name = "__TEST__CreateAtFeetPenguin";
            PenguinInputReader reader = _penguinObject.GetComponent<PenguinInputReader>();
            PenguinSnowball snowball = _penguinObject.GetComponent<PenguinSnowball>();
            reader.enabled = false;

            SetCreateSnowballPressed(reader, true);
            yield return new WaitForFixedUpdate();
            SetCreateSnowballPressed(reader, false);

            Assert.IsNotNull(snowball.Held, $"발밑 눈으로 공을 만들 수 있어야 한다 ({snowball.LastFailure})");
            _ball = snowball.Held;
            Vector2 ballXZ = new(_ball.transform.position.x, _ball.transform.position.z);
            Vector2 feetXZ = new(start.x, start.z);
            Assert.Less(Vector2.Distance(ballXZ, feetXZ), 0.35f,
                "생성 위치는 펭귄 앞이 아니라 서 있던 눈 공간이어야 한다");
        }

        /// <summary>
        /// 밀면 공이 움직이고, 커지면 <b>둘 다 느려진다.</b> 그것이 이 조작의 전부다 —
        /// 무거움은 공의 질량 하나에서 나오고 속도 배수는 없다(`Snow/AGENTS.md`).
        /// </summary>
        [UnityTest]
        public IEnumerator 밀면_공이_움직이고_커지면_같이_느려진다()
        {
            _groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _groundObject.name = "__TEST__Ground";
            _groundObject.transform.localScale = new Vector3(60f, 1f, 60f);
            _groundObject.transform.position = new Vector3(0f, -0.5f, 0f);

            _stageObject = new GameObject("__TEST__SnowCpuStage");
            SnowCpuStage stage = _stageObject.AddComponent<SnowCpuStage>();
            yield return null;
            Assert.IsNotNull(stage.Field, "단독 모드에서 격자가 서야 한다");

            var prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab, "PF_SnowBall 이 Resources 에 있어야 한다");

            GameObject penguinPrefab = LoadPenguinPrefab();
            Assert.IsNotNull(penguinPrefab, "PF_Penguin 이 있어야 한다");

            _penguinObject = Object.Instantiate(penguinPrefab, new Vector3(4f, 1.2f, 4f), Quaternion.identity);
            _penguinObject.name = "__TEST__Penguin";
            yield return null;

            var reader = _penguinObject.GetComponent<PenguinInputReader>();
            var loco = _penguinObject.GetComponent<PenguinLocomotion>();
            var ballCtrl = _penguinObject.GetComponent<PenguinSnowball>();
            Assert.IsNotNull(ballCtrl, "PF_Penguin 에 PenguinSnowball 이 붙어 있어야 한다");

            // 리더를 끄고 입력을 직접 넣는다 - 그래야 내 값이 덮이지 않는다.
            reader.enabled = false;
            SetMoveInput(reader, Vector2.zero);
            yield return null;

            ballCtrl.BeginPush();
            Assert.IsNotNull(ballCtrl.Held, $"눈을 뭉칠 수 있어야 한다 ({ballCtrl.LastFailure})");
            _ball = ballCtrl.Held;

            float seedRadius = _ball.RadiusM;
            var body = _ball.GetComponent<Rigidbody>();
            Assert.IsNotNull(body);

            // <b>입력은 카메라 기준이다.</b> +Z 를 넣으면 펭귄 정면이 아니라 카메라 정면으로 간다 -
            // 처음에 그렇게 썼다가 펭귄이 공을 등지고 걸어가 공이 한 번도 안 움직였다(실측).
            // 그래서 매 프레임 공을 향하는 세계 방향을 카메라 축으로 되돌려 넣는다.
            Vector3 startPos = _penguinObject.transform.position;
            var penguinBody = _penguinObject.GetComponent<Rigidbody>();

            float earlySpeed = 0f;
            float walkCap = 0f;
            for (int frame = 0; frame < 120; frame++)
            {
                SetMoveInput(reader, TowardBall(_penguinObject.transform, _ball.transform, loco));
                yield return null;

                float s = new Vector2(body.linearVelocity.x, body.linearVelocity.z).magnitude;
                if (s > earlySpeed) earlySpeed = s;

                // 상한은 <b>미는 동안</b>의 의도 속도다. 루프가 끝나면 입력을 멈추므로 0 이 된다 -
                // 밖에서 읽었다가 "공이 걷기보다 빠르다" 로 잘못 떨어졌다(실측).
                float want = loco.DesiredVelocity.magnitude;
                if (want > walkCap) walkCap = want;
            }

            float grownRadius = _ball.RadiusM;
            float dist = Vector3.Distance(
                new Vector3(_penguinObject.transform.position.x, 0f, _penguinObject.transform.position.z),
                new Vector3(_ball.transform.position.x, 0f, _ball.transform.position.z));

            TestContext.WriteLine($"[밀기] 씨앗 {seedRadius:0.00} m -> {grownRadius:0.00} m · " +
                                  $"{body.mass:0} kg · 공 최고 {earlySpeed:0.00} m/s · " +
                                  $"펭귄 {loco.Speed:0.00} m/s · 거리 {dist:0.00} m · " +
                                  $"밀기 {PenguinSnowball.DebugPushes}");
            TestContext.WriteLine($"[진단] 펭귄 {startPos:F2} -> {_penguinObject.transform.position:F2} " +
                                  $"이동 {Vector3.Distance(startPos, _penguinObject.transform.position):0.00} m · " +
                                  $"접지 {loco.IsGrounded} · 펭귄 속도={penguinBody.linearVelocity:F2} · " +
                                  $"의도 {loco.DesiredVelocity:F2} · 공 {_ball.transform.position:F2}");

            Assert.Greater(earlySpeed, 0.05f, "밀었는데 공이 전혀 안 움직였다 - 힘이 전달되지 않는다");
            Assert.Greater(grownRadius, seedRadius, "굴렀으면 커져야 한다");

            // <b>걷는 속도를 넘지 않는다.</b> 넘으면 던지는 것이지 미는 것이 아니다.
            Assert.LessOrEqual(earlySpeed, walkCap + 0.5f,
                "공이 걷는 속도보다 빠르다 - 미는 것이 아니라 발사된 것이다");
        }

        [UnityTest]
        public IEnumerator 같은_방향의_두_힘은_합쳐지고_반대_힘은_상쇄된다()
        {
            _probeScene = SceneManager.CreateScene("__TEST__CoopForceScene");
            CreateFlatGround(_probeScene, 0f);
            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);
            _ball = Object.Instantiate(prefab, new Vector3(0f, 1.3f, 0f), Quaternion.identity)
                .GetComponent<SnowBallCarrier>();
            _ball.gameObject.name = "__TEST__CoopBall";
            SceneManager.MoveGameObjectToScene(_ball.gameObject, _probeScene);
            _ball.ServerApplyMass(_ball.MassMmForRadius(1.3f));

            Rigidbody body = _ball.GetComponent<Rigidbody>();
            body.useGravity = false;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.sleepThreshold = 0f;
            yield return null;

            _ball.SubmitPush(Vector3.right * 3.5f, SnowBallCarrier.PlayerPushForceN);
            yield return new WaitForFixedUpdate();
            float onePusher = body.linearVelocity.x;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            _ball.SubmitPush(Vector3.right * 3.5f, SnowBallCarrier.PlayerPushForceN);
            _ball.SubmitPush(Vector3.right * 3.5f, SnowBallCarrier.PlayerPushForceN);
            yield return new WaitForFixedUpdate();
            float twoPushers = body.linearVelocity.x;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            _ball.SubmitPush(Vector3.right * 3.5f, SnowBallCarrier.PlayerPushForceN);
            _ball.SubmitPush(Vector3.left * 3.5f, SnowBallCarrier.PlayerPushForceN);
            yield return new WaitForFixedUpdate();
            float opposed = Mathf.Abs(body.linearVelocity.x);

            TestContext.WriteLine($"[협동 힘] 1명 {onePusher:0.000} · 2명 {twoPushers:0.000} · 반대 {opposed:0.000} m/s");
            Assert.Greater(onePusher, 0.001f, "한 명의 힘이 공에 전달되지 않았다");
            Assert.Greater(twoPushers, onePusher * 1.5f, "같은 방향의 두 힘이 합쳐지지 않았다");
            Assert.Less(opposed, onePusher * 0.1f, "반대 방향의 같은 힘이 상쇄되지 않았다");
        }

        [UnityTest]
        public IEnumerator 각_눈덩이는_반지름에_맞는_현재_성장_단계를_보관한다()
        {
            _probeScene = SceneManager.CreateScene("__TEST__SnowBallGrowthStageScene");
            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);

            _ball = Object.Instantiate(prefab).GetComponent<SnowBallCarrier>();
            _ball.gameObject.name = "__TEST__GrowthStageBall";
            SceneManager.MoveGameObjectToScene(_ball.gameObject, _probeScene);
            yield return null;

            _ball.ServerApplyMass(_ball.MassMmForRadius(SnowballStageModel.MinRadiusM));
            Assert.AreEqual(ESnowBallGrowthStage.Seed, _ball.GrowthStage);

            _ball.ServerApplyMass(_ball.MassMmForRadius(
                SnowballStageModel.GetStageRepresentativeRadius(ESnowBallGrowthStage.Stage1)));
            Assert.AreEqual(ESnowBallGrowthStage.Stage1, _ball.GrowthStage);

            _ball.ServerApplyMass(_ball.MassMmForRadius(
                SnowballStageModel.GetStageRepresentativeRadius(ESnowBallGrowthStage.Stage2)));
            Assert.AreEqual(ESnowBallGrowthStage.Stage2, _ball.GrowthStage);

            _ball.ServerApplyMass(_ball.MassMmForRadius(
                SnowballStageModel.GetStageRepresentativeRadius(ESnowBallGrowthStage.Stage3)));
            Assert.AreEqual(ESnowBallGrowthStage.Stage3, _ball.GrowthStage);

            _ball.ServerApplyMass(_ball.VisibleMaxMassMm);
            Assert.AreEqual(ESnowBallGrowthStage.Stage4, _ball.GrowthStage);
        }

        [UnityTest]
        public IEnumerator 크기_상한과_초과_질량에서도_이동성이_연속적으로_남는다()
        {
            _probeScene = SceneManager.CreateScene("__TEST__MaximumSnowBallScene");
            CreateFlatGround(_probeScene, 0f);
            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);

            _ball = Object.Instantiate(prefab, new Vector3(0f, 1.5f, 0f), Quaternion.identity)
                .GetComponent<SnowBallCarrier>();
            _ball.gameObject.name = "__TEST__MaximumSnowBall";
            SceneManager.MoveGameObjectToScene(_ball.gameObject, _probeScene);

            Rigidbody body = _ball.GetComponent<Rigidbody>();
            body.useGravity = false;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.sleepThreshold = 0f;
            _ball.ServerApplyMass(_ball.VisibleMaxMassMm);
            yield return null;

            Assert.AreEqual(SnowBallCpu.MaxRadiusM, _ball.RadiusM, 0.0001f);
            Assert.AreEqual(0.45f, _ball.Mobility01, 0.01f,
                "1.5 m에서 계획한 이동 계수 45%가 유지돼야 한다");
            Assert.AreNotEqual(RigidbodyConstraints.FreezeAll, body.constraints,
                "1.5 m 공을 물리적으로 잠그면 안 된다");

            for (int step = 0; step < 20; step++)
            {
                _ball.SubmitPush(Vector3.right * 3.5f, SnowBallCarrier.PlayerPushForceN);
                yield return new WaitForFixedUpdate();
            }
            Assert.Greater(body.linearVelocity.x, 0.01f, "1.5 m 공에 밀기 입력이 전달되지 않았다");

            _ball.ServerApplyMass(_ball.MassMmForRadius(1.499f));
            float below = _ball.Mobility01;
            _ball.ServerApplyMass(_ball.MassMmForRadius(1.501f));
            float above = _ball.Mobility01;
            Assert.Less(Mathf.Abs(below - above), 0.01f,
                "1.5 m 경계에서 이동 계수가 갑자기 바뀐다");

            _ball.ServerApplyMass(_ball.MassMmForRadius(2f));
            Assert.AreEqual(SnowBallCpu.MaxRadiusM, _ball.RadiusM, 0.0001f,
                "보이는 반지름은 1.5 m에 머물러야 한다");
            Assert.AreEqual(2f, _ball.EquivalentRadiusM, 0.001f);
            Assert.That(_ball.Mobility01, Is.InRange(0.12f, 0.15f),
                "초과 질량은 움직일 수 있지만 매우 느려야 한다");
            Assert.Greater(_ball.Mobility01, 0f, "초과 질량에서도 이동 계수가 0이 되면 안 된다");
        }

        [UnityTest]
        [Ignore("함께 밀기는 2026-09-01 에 껐다 — SnowBallCarrier.CoopPushEnabled. 되살리면 이 줄만 지우면 된다.")]
        public IEnumerator 무거운_공은_두_명이_우클릭_타이밍을_모두_맞춰야_한번_밀쳐진다()
        {
            _probeScene = SceneManager.CreateScene("__TEST__CoopShoveScene");
            CreateFlatGround(_probeScene, 0f);
            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);

            _ball = Object.Instantiate(prefab,
                    new Vector3(0f, SnowBallCpu.MaxRadiusM, 0f), Quaternion.identity)
                .GetComponent<SnowBallCarrier>();
            _ball.gameObject.name = "__TEST__CoopShoveBall";
            SceneManager.MoveGameObjectToScene(_ball.gameObject, _probeScene);
            _ball.ServerApplyMass(_ball.MassMmForRadius(5f));

            Rigidbody body = _ball.GetComponent<Rigidbody>();
            body.useGravity = false;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.sleepThreshold = 0f;

            _pusherOne = new GameObject("__TEST__PusherOne");
            _pusherTwo = new GameObject("__TEST__PusherTwo");
            SceneManager.MoveGameObjectToScene(_pusherOne, _probeScene);
            SceneManager.MoveGameObjectToScene(_pusherTwo, _probeScene);
            yield return null;

            // 0.75초 동안 두 힘을 받아도 거의 움직이지 않는 상태를 만든다.
            for (int i = 0; i < 50; i++)
            {
                _ball.SubmitPush(_pusherOne.transform, Vector3.right * 3.5f,
                    SnowBallCarrier.PlayerPushForceN);
                _ball.SubmitPush(_pusherTwo.transform, Vector3.right * 3.5f,
                    SnowBallCarrier.PlayerPushForceN);
                yield return new WaitForFixedUpdate();
            }

            Assert.IsTrue(_ball.TryGetCoopTiming(_pusherOne.transform, out float phase01,
                out bool firstSubmitted, out int participants), "힘이 딸리는데 협동 타이밍이 시작되지 않았다");
            Assert.AreEqual(2, participants);
            Assert.IsFalse(firstSubmitted);

            while (phase01 < 0.5f)
            {
                _ball.SubmitPush(_pusherOne.transform, Vector3.right * 3.5f,
                    SnowBallCarrier.PlayerPushForceN);
                _ball.SubmitPush(_pusherTwo.transform, Vector3.right * 3.5f,
                    SnowBallCarrier.PlayerPushForceN);
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(_ball.TryGetCoopTiming(_pusherOne.transform, out phase01,
                    out firstSubmitted, out participants));
            }

            int boostsBefore = _ball.CoopBoostCount;
            _ball.SubmitCoopTiming(_pusherOne.transform, true);
            Assert.AreEqual(boostsBefore, _ball.CoopBoostCount,
                "한 명만 성공했는데 밀치기가 먼저 적용됐다");

            float speedBefore = body.linearVelocity.x;
            _ball.SubmitCoopTiming(_pusherTwo.transform, true);
            Assert.AreEqual(boostsBefore + 1, _ball.CoopBoostCount,
                "전원이 성공했는데 Impulse가 적용되지 않았다");
            yield return new WaitForFixedUpdate();

            TestContext.WriteLine($"[협동 밀치기] phase={phase01:0.00} · " +
                                  $"speed {speedBefore:0.000} -> {body.linearVelocity.x:0.000} m/s");
            Assert.Greater(body.linearVelocity.x, speedBefore,
                "전원 성공 뒤 공의 속도가 늘지 않았다");
        }

        [UnityTest]
        public IEnumerator 펭귄_몸은_눈덩이의_지지면이_아니다()
        {
            _probeScene = SceneManager.CreateScene("__TEST__WedgedPenguinScene");
            CreateFlatGround(_probeScene, 0f);

            _pusherOne = new GameObject("__TEST__WedgedPenguin");
            CharacterController controller = _pusherOne.AddComponent<CharacterController>();
            controller.height = 0.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.4f, 0f);
            _pusherOne.transform.position = new Vector3(0.25f, 0f, 0f);
            SceneManager.MoveGameObjectToScene(_pusherOne, _probeScene);

            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);
            _ball = Object.Instantiate(prefab, new Vector3(0f, 1.1f, 0f), Quaternion.identity)
                .GetComponent<SnowBallCarrier>();
            _ball.gameObject.name = "__TEST__WedgedBall";
            SceneManager.MoveGameObjectToScene(_ball.gameObject, _probeScene);
            _ball.ServerApplyMass(_ball.MassMmForRadius(0.5f));

            Rigidbody body = _ball.GetComponent<Rigidbody>();
            body.useGravity = false;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.sleepThreshold = 0f;
            Physics.IgnoreCollision(_ball.GetComponent<Collider>(), controller, true);
            Physics.SyncTransforms();
            yield return null;

            _ball.SubmitPush(_pusherOne.transform, Vector3.right * 3.5f,
                SnowBallCarrier.PlayerPushForceN);
            Assert.Greater(Vector3.Dot(_ball.SupportNormal, Vector3.up), 0.99f,
                "펭귄 캡슐을 지면으로 오인했다");

            yield return new WaitForFixedUpdate();

            TestContext.WriteLine($"[끼임 방지] normal={_ball.SupportNormal:F2} velocity={body.linearVelocity:F3}");
            Assert.Greater(body.linearVelocity.x, 0.001f, "실제 바닥이 있는데 밀기 힘이 사라졌다");
            Assert.Less(Mathf.Abs(body.linearVelocity.y), 0.001f,
                "펭귄 캡슐의 법선 때문에 눈덩이가 위로 들렸다");
        }

        /// <summary>
        /// 선택하지 않은 공은 WASD로 부딪혀도 의도적인 밀기 채널에 참가하지 않는다. 진짜
        /// Rigidbody 충돌로 조금 움직일 수는 있지만 <see cref="SnowBallCarrier.SubmitPush"/>는
        /// E로 선택한 공에만 제출돼야 한다.
        /// </summary>
        [UnityTest]
        public IEnumerator 경사에서_선택하지_않은_공은_의도적으로_밀지_않는다()
        {
            _probeScene = SceneManager.CreateScene("__TEST__PassiveSlopeCollisionScene");
            _groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _groundObject.name = "__TEST__PassiveSlope";
            _groundObject.transform.localScale = new Vector3(20f, 1f, 20f);
            _groundObject.transform.rotation = Quaternion.Euler(-20f, 0f, 0f);
            Vector3 slopeNormal = _groundObject.transform.up;
            Vector3 uphill = Vector3.ProjectOnPlane(Vector3.forward, slopeNormal).normalized;
            _groundObject.transform.position = -slopeNormal * 0.5f;
            SceneManager.MoveGameObjectToScene(_groundObject, _probeScene);

            GameObject ballPrefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(ballPrefab);
            Vector3 ballPosition = uphill * 1.5f + slopeNormal * 0.5f;
            _ball = Object.Instantiate(ballPrefab, ballPosition, Quaternion.identity)
                .GetComponent<SnowBallCarrier>();
            _ball.gameObject.name = "__TEST__PassiveSlopeBall";
            SceneManager.MoveGameObjectToScene(_ball.gameObject, _probeScene);
            _ball.ServerApplyMass(_ball.MassMmForRadius(0.5f));
            Rigidbody body = _ball.GetComponent<Rigidbody>();
            body.useGravity = false;
            body.linearDamping = 0f;
            body.angularDamping = 0f;

            GameObject penguinPrefab = LoadPenguinPrefab();
            Assert.IsNotNull(penguinPrefab);
            Vector3 penguinPosition = uphill * 0.55f + slopeNormal * 0.05f;
            _penguinObject = Object.Instantiate(penguinPrefab, penguinPosition,
                Quaternion.LookRotation(uphill, Vector3.up));
            _penguinObject.name = "__TEST__PassiveSlopePenguin";
            SceneManager.MoveGameObjectToScene(_penguinObject, _probeScene);

            PenguinInputReader reader = _penguinObject.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = _penguinObject.GetComponent<PenguinLocomotion>();
            reader.enabled = false;
            Physics.SyncTransforms();
            yield return null;

            int pushesBefore = PenguinSnowball.DebugPushes;
            Vector3 ballStart = body.position;
            for (int frame = 0; frame < 30; frame++)
            {
                SetMoveInput(reader, TowardBall(_penguinObject.transform, _ball.transform, locomotion));
                yield return null;
            }

            float delta = Vector3.Distance(ballStart, body.position);
            TestContext.WriteLine($"[비입력 경사 충돌] ball delta={delta:0.0000} m · " +
                                  $"DebugPushes {pushesBefore} -> {PenguinSnowball.DebugPushes}");

            Assert.AreEqual(pushesBefore, PenguinSnowball.DebugPushes,
                "E로 선택하지 않은 공에 SubmitPush가 제출됐다");
            // 참고용 상한. 진짜 충돌이라 스치면 약간은 밀린다 — 30프레임 동안 계속 접촉한
            // 상태라 실측 0.33m 정도 나온다(2026-08-22). 두 배 여유를 두고 잡는다 — 여기서
            // 잡는 것은 "밀기로 착각할 만큼 폭주했는가"뿐이다.
            Assert.Less(delta, 0.7f,
                "선택하지 않고 스쳤을 뿐인데 눈덩이가 크게 움직였다 — 우발적 충돌치고 너무 크다");
        }

        [UnityTest]
        public IEnumerator 밀기_방향은_경사면의_접선에_놓인다()
        {
            _probeScene = SceneManager.CreateScene("__TEST__SlopeForceScene");
            _groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _groundObject.name = "__TEST__Slope";
            _groundObject.transform.localScale = new Vector3(20f, 1f, 20f);
            _groundObject.transform.rotation = Quaternion.Euler(0f, 0f, 15f);
            Vector3 slopeNormal = _groundObject.transform.up;
            _groundObject.transform.position = -slopeNormal * 0.5f;
            SceneManager.MoveGameObjectToScene(_groundObject, _probeScene);

            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);
            _ball = Object.Instantiate(prefab, slopeNormal * 1.5f, Quaternion.identity)
                .GetComponent<SnowBallCarrier>();
            _ball.gameObject.name = "__TEST__SlopeBall";
            SceneManager.MoveGameObjectToScene(_ball.gameObject, _probeScene);
            _ball.ServerApplyMass(_ball.MassMmForRadius(1.3f));

            Rigidbody body = _ball.GetComponent<Rigidbody>();
            body.useGravity = false;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.sleepThreshold = 0f;
            Physics.SyncTransforms();
            yield return null;

            Vector3 expected = Vector3.ProjectOnPlane(Vector3.right, slopeNormal).normalized;
            _ball.SubmitPush(Vector3.right * 3.5f, SnowBallCarrier.PlayerPushForceN);
            Assert.Greater(Vector3.Dot(_ball.SupportNormal, slopeNormal), 0.98f,
                "공이 아래 경사면 법선을 찾지 못했다");

            yield return new WaitForFixedUpdate();
            yield return null;

            Vector3 actual = body.linearVelocity.normalized;
            TestContext.WriteLine($"[경사 힘] normal={_ball.SupportNormal:F2} expected={expected:F2} actual={actual:F2}");
            Assert.Greater(Vector3.Dot(actual, expected), 0.98f,
                "입력 힘이 경사면 접선 방향으로 적용되지 않았다");
            Assert.Greater(Mathf.Abs(body.linearVelocity.y), 0.001f,
                "경사인데 힘의 수직 성분이 0이다");
        }

        /// <summary>Q 는 붙어 있는 공만 터뜨린다. 크기는 보지 않는다.</summary>
        [UnityTest]
        public IEnumerator Q_는_붙어_있는_공을_터뜨린다()
        {
            _groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _groundObject.name = "__TEST__Ground";
            _groundObject.transform.localScale = new Vector3(60f, 1f, 60f);
            _groundObject.transform.position = new Vector3(0f, -0.5f, 0f);

            _stageObject = new GameObject("__TEST__SnowCpuStage");
            SnowCpuStage stage = _stageObject.AddComponent<SnowCpuStage>();
            yield return null;

            GameObject penguinPrefab = LoadPenguinPrefab();
            Assert.IsNotNull(penguinPrefab, "PF_Penguin 이 있어야 한다");
            _penguinObject = Object.Instantiate(penguinPrefab, new Vector3(4f, 1.2f, 4f), Quaternion.identity);
            _penguinObject.name = "__TEST__Penguin";
            var reader = _penguinObject.GetComponent<PenguinInputReader>();
            reader.enabled = false;
            yield return null;

            var ballCtrl = _penguinObject.GetComponent<PenguinSnowball>();
            ballCtrl.BeginPush();
            Assert.IsNotNull(ballCtrl.Held, $"뭉치기 실패 ({ballCtrl.LastFailure})");

            long initial = stage.TotalHeightMm + ballCtrl.Held.MassMm;
            int burstsBefore = SnowCpuStage.BurstsTotal;

            ballCtrl.Burst();
            Assert.IsNull(ballCtrl.Held, "터뜨렸으면 손을 떼야 한다");

            for (int frame = 0; frame < 120 && SnowCpuStage.BurstsTotal == burstsBefore; frame++)
                yield return null;

            Assert.AreEqual(burstsBefore + 1, SnowCpuStage.BurstsTotal, "Q 를 눌렀는데 안 터졌다");
            yield return null;

            Assert.AreEqual(initial, stage.TotalHeightMm + stage.UnaccountedOutMm,
                "터진 뒤 눈이 사라졌다 - 필드 + 장부 밖 = 초기");

            _ball = null;   // 터져서 사라졌다
            TestContext.WriteLine($"[Q터짐] 반지름 {SnowCpuStage.LastBurstRadiusM:0.00} m · " +
                                  $"장부 밖 {stage.UnaccountedOutMm} mm");
        }

        /// <summary>
        /// 협동 타이밍이 <b>실제로 떠 있는</b> 무거운 공을 만든다.
        ///
        /// <para>환산 반지름 5 m의 압축 질량은 보이는 크기가 1.5 m에 머물러도 난이도가 1에 붙는다.
        /// 그래서 실패 뒤 쿨타임 없이 버티는 시간만 지나면 협동 타이밍을 다시 띄울 수 있다.</para>
        /// </summary>
        private IEnumerator ArmHeavyCoopTiming(string sceneName)
        {
            _probeScene = SceneManager.CreateScene(sceneName);
            CreateFlatGround(_probeScene, 0f);
            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);

            _ball = Object.Instantiate(prefab,
                    new Vector3(0f, SnowBallCpu.MaxRadiusM, 0f), Quaternion.identity)
                .GetComponent<SnowBallCarrier>();
            _ball.gameObject.name = "__TEST__HeavyCoopBall";
            SceneManager.MoveGameObjectToScene(_ball.gameObject, _probeScene);
            _ball.ServerApplyMass(_ball.MassMmForRadius(5f));

            Rigidbody body = _ball.GetComponent<Rigidbody>();
            body.useGravity = false;
            body.linearDamping = 0f;
            body.angularDamping = 0f;
            body.sleepThreshold = 0f;

            _pusherOne = new GameObject("__TEST__PusherOne");
            _pusherTwo = new GameObject("__TEST__PusherTwo");
            SceneManager.MoveGameObjectToScene(_pusherOne, _probeScene);
            SceneManager.MoveGameObjectToScene(_pusherTwo, _probeScene);
            yield return null;

            for (int step = 0; step < 200; step++)
            {
                PushBoth();
                yield return new WaitForFixedUpdate();
                if (_ball.TryGetCoopTiming(_pusherOne.transform, out _, out _, out _)) yield break;
            }

            Assert.Fail("힘이 딸리는데 협동 타이밍이 시작되지 않았다");
        }

        private void PushBoth()
        {
            _ball.SubmitPush(_pusherOne.transform, Vector3.right * 3.5f,
                SnowBallCarrier.PlayerPushForceN);
            _ball.SubmitPush(_pusherTwo.transform, Vector3.right * 3.5f,
                SnowBallCarrier.PlayerPushForceN);
        }

        /// <summary>
        /// <b>작은 공은 드릴처럼 돌지 않는다.</b> ω = v/r 이라 반지름이 작을수록 발산한다 —
        /// 씨앗(0.18 m)이 걷기 속도(3.5 m/s)로 미끄러짐 없이 구르면 19.4 rad/s, 초당 3.1 바퀴다.
        ///
        /// <para>밀기를 매 스텝 제출하는 이유는 <c>StopIdleDriftOnFlatGround</c> 때문이다 — 아무도
        /// 밀지 않으면 그쪽이 평지에서 각속도를 <b>0 으로 지워</b> 상한을 안 걸어도 테스트가
        /// 통과해 버린다.</para>
        /// </summary>
        [UnityTest]
        public IEnumerator 작은_눈덩이의_회전_각속도는_상한을_넘지_않는다()
        {
            _probeScene = SceneManager.CreateScene("__TEST__SpinCapScene");
            CreateFlatGround(_probeScene, 0f);
            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);

            _ball = Object.Instantiate(prefab,
                new Vector3(0f, SnowBallCpu.SeedRadiusM, 0f), Quaternion.identity)
                .GetComponent<SnowBallCarrier>();
            _ball.gameObject.name = "__TEST__SpinCapBall";
            SceneManager.MoveGameObjectToScene(_ball.gameObject, _probeScene);
            _ball.ServerApplyMass(_ball.MassMmForRadius(SnowBallCpu.SeedRadiusM));
            Rigidbody body = _ball.GetComponent<Rigidbody>();
            body.useGravity = false;
            body.angularDamping = 0f;
            body.sleepThreshold = 0f;
            yield return null;

            const float walkSpeedMps = 3.5f;
            float freeRollingRadPerSec = walkSpeedMps / SnowBallCpu.SeedRadiusM;

            for (int step = 0; step < 3; step++)
            {
                _ball.SubmitPush(Vector3.right * walkSpeedMps, SnowBallCarrier.PlayerPushForceN);
                body.angularVelocity = Vector3.right * freeRollingRadPerSec;
                yield return new WaitForFixedUpdate();
            }

            float spun = body.angularVelocity.magnitude;
            TestContext.WriteLine($"[회전 상한] 자유 구름 {freeRollingRadPerSec:0.0} rad/s " +
                                  $"({freeRollingRadPerSec / (2f * Mathf.PI):0.00} 바퀴/초) -> " +
                                  $"실제 {spun:0.0} rad/s " +
                                  $"({spun / (2f * Mathf.PI):0.00} 바퀴/초) · " +
                                  $"상한 {_ball.MaxAngularVelocityRadPerSec:0.0} rad/s");

            Assert.LessOrEqual(spun, _ball.MaxAngularVelocityRadPerSec + 0.05f,
                "설정한 회전 상한을 넘었다");
            Assert.Less(spun, freeRollingRadPerSec * 0.75f,
                "씨앗 크기 공이 자유 구름 각속도 그대로 돈다 - 상한이 걸리지 않았다");
        }

        /// <summary>
        /// 전원 성공은 <b>연출이 읽을 수 있는 신호</b>를 남긴다 — 횟수와 그때의 난이도.
        ///
        /// <para>난이도가 같이 필요한 이유는 세기가 그것을 타기 때문이다. 임펄스만 보면 연출은
        /// "얼마나 어려운 성공이었는지" 를 알 방법이 없다.</para>
        /// </summary>
        [UnityTest]
        [Ignore("함께 밀기는 2026-09-01 에 껐다 — SnowBallCarrier.CoopPushEnabled. 되살리면 이 줄만 지우면 된다.")]
        public IEnumerator 협동_전원_성공은_난이도와_함께_연출_신호를_남긴다()
        {
            yield return ArmHeavyCoopTiming("__TEST__CoopFeelScene");

            int before = _ball.CoopBoostCount;

            float phase01;
            do
            {
                PushBoth();
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(_ball.TryGetCoopTiming(_pusherOne.transform, out phase01,
                    out _, out _), "판정 창 안으로 들어가기 전에 타이밍이 끝났다");
            } while (!SnowBallCarrier.IsCoopTimingSuccess(phase01));

            _ball.SubmitCoopTiming(_pusherOne.transform, true);
            _ball.SubmitCoopTiming(_pusherTwo.transform, true);

            TestContext.WriteLine($"[협동 연출] 신호 {before} -> {_ball.CoopBoostCount} · " +
                                  $"난이도 {_ball.LastCoopBoostDifficulty01:0.00}");

            Assert.AreEqual(before + 1, _ball.CoopBoostCount,
                "전원 성공인데 연출 신호가 올라가지 않았다");
            Assert.Greater(_ball.LastCoopBoostDifficulty01, 0.9f,
                "가속도 0.075 m/s² 인 공인데 난이도가 낮게 기록됐다");
        }

        /// <summary>
        /// <b>무거우면 재시도를 기다리지 않는다.</b> 난이도 1 에서 쿨타임은 0 이고, 다시 뜨기까지
        /// 필요한 것은 버티는 시간(0.35 s ≈ 18 고정스텝)뿐이다.
        ///
        /// <para>하한 1.5 초짜리 쿨타임이 남아 있으면 93 스텝이 필요하다 — 예산 40 스텝이 그 둘을
        /// 가른다.</para>
        /// </summary>
        [UnityTest]
        [Ignore("함께 밀기는 2026-09-01 에 껐다 — SnowBallCarrier.CoopPushEnabled. 되살리면 이 줄만 지우면 된다.")]
        public IEnumerator 무거운_공은_협동_밀치기_재시도를_기다리지_않는다()
        {
            yield return ArmHeavyCoopTiming("__TEST__CoopRetryScene");

            _ball.SubmitCoopTiming(_pusherOne.transform, false);
            Assert.IsFalse(_ball.TryGetCoopTiming(_pusherOne.transform, out _, out _, out _),
                "한 명이 실패했는데 타이밍이 그대로 떠 있다");

            const int budgetSteps = 40;
            int steps = 0;
            while (steps < budgetSteps &&
                   !_ball.TryGetCoopTiming(_pusherOne.transform, out _, out _, out _))
            {
                PushBoth();
                yield return new WaitForFixedUpdate();
                steps++;
            }

            TestContext.WriteLine($"[재시도] {steps} 고정스텝 " +
                                  $"({steps * Time.fixedDeltaTime:0.00} s) 만에 다시 떴다");
            Assert.Less(steps, budgetSteps,
                $"무거운 공인데 재시도까지 {budgetSteps} 스텝을 넘게 기다렸다 - 쿨타임 하한이 남아 있다");
        }

        /// <summary>
        /// 연출은 <b>신호의 에지에서만</b> 한 번 재생되고, 세기는 그 성공의 난이도를 탄다.
        ///
        /// <para>에지를 확인하는 이유는 <see cref="SnowBallCarrier.CoopBoostCount"/> 가 상태이지
        /// 이벤트가 아니기 때문이다 — 매 프레임 값을 보고 재생하면 성공 한 번이 계속 터진다.</para>
        ///
        /// <para><c>MMF_Player</c> 없이도 판정 자체는 서야 한다. 어느 피드백을 재생할지는 프리팹
        /// 저작이고, <b>언제·얼마나 세게</b> 만 이 컴포넌트의 몫이다.</para>
        /// </summary>
        [UnityTest]
        [Ignore("함께 밀기는 2026-09-01 에 껐다 — SnowBallCarrier.CoopPushEnabled. 되살리면 이 줄만 지우면 된다.")]
        public IEnumerator 협동_성공_연출은_신호의_에지에서만_난이도_세기로_재생된다()
        {
            yield return ArmHeavyCoopTiming("__TEST__CoopFeedbackScene");

            // <b>프리팹에 붙어 있는 것을 쓴다</b> - 여기서 AddComponent 하면 배선이 빠져도 테스트가
            // 통과해 버린다. 이렇게 두면 PF_SnowBall 의 저작 자체가 이 테스트의 검증 대상이 된다.
            var feedback = _ball.GetComponent<SnowBallCoopFeedback>();
            Assert.IsNotNull(feedback, "PF_SnowBall 에 SnowBallCoopFeedback 이 없다");
            Assert.IsNotNull(_ball.GetComponent<MMF_Player>(), "PF_SnowBall 에 MMF_Player 가 없다");

            AudioSource source = _ball.GetComponent<AudioSource>();
            Assert.IsNotNull(source, "PF_SnowBall 에 AudioSource 가 없다");
            Assert.IsNotNull(source.clip,
                "AudioSource 에 클립이 없다 - MMF_AudioSource 가 clip.length 를 읽다 죽는다");
            Assert.IsFalse(source.playOnAwake, "공이 생기자마자 소리가 난다");

            Assert.AreEqual(0, feedback.PlayCount, "아무 일도 없었는데 연출이 재생됐다");

            float phase01;
            do
            {
                PushBoth();
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(_ball.TryGetCoopTiming(_pusherOne.transform, out phase01,
                    out _, out _), "판정 창 안으로 들어가기 전에 타이밍이 끝났다");
            } while (!SnowBallCarrier.IsCoopTimingSuccess(phase01));

            _ball.SubmitCoopTiming(_pusherOne.transform, true);
            _ball.SubmitCoopTiming(_pusherTwo.transform, true);
            yield return null;

            TestContext.WriteLine($"[성공 연출] 재생 {feedback.PlayCount} 회 · " +
                                  $"세기 {feedback.LastStrength01:0.00}");

            Assert.AreEqual(1, feedback.PlayCount, "전원 성공했는데 연출이 재생되지 않았다");
            Assert.Greater(feedback.LastStrength01, 0.9f, "난이도 1 인 성공인데 세기가 낮다");

            yield return null;
            yield return null;
            Assert.AreEqual(1, feedback.PlayCount, "신호가 안 올랐는데 다시 재생됐다");
        }
    }
}
