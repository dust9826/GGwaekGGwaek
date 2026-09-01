using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace PPack
{
    /// <summary>
    /// 스펙 §11 합격 기준 1 — <b>헤드리스에서 권위와 감속이 돌고, 텍스처·연출은 생성되지 않는다.</b>
    ///
    /// 그래서 이 테스트는 <c>-batchmode -nographics</c> 로 돌릴 때가 본론이다. 에디터에서 돌리면
    /// GPU 가 있으므로 반대 조건(렌더러가 살아 있음)을 확인한다 — 두 경우를 같은 단정으로 덮는다.
    /// 루트 <c>AGENTS.md</c>: <b>에디터의 Server 모드는 가짜 통과다.</b>
    ///
    /// 씬을 로드하지 않고 코드로 세운다. 테스트 씬을 Build Settings 에 넣지 않는다는 규칙을
    /// 우회하는 것이 아니라, 그 규칙이 있으니 애초에 씬에 의존하지 않는 것이다.
    /// </summary>
    public sealed class SnowHeadlessTests
    {
        private GameObject _stageObject;
        private GameObject _vehicleObject;
        private SnowBallCarrier _probeBall;

        [TearDown]
        public void TearDown()
        {
            // <b>공을 반드시 치운다.</b> 터지지 못하고 살아남은 눈덩이는 다음 테스트의 필드를
            // 계속 파먹는다 - 스테이지는 씬의 모든 SnowBallCarrier 를 찾아 굴리기 때문이다.
            // 실측(2026-08-20): 남은 공 하나가 동기화 테스트의 차선 시작점에 서버에만 있는
            // 둔덕(0.192 m)을 만들어 그 테스트를 배치에서만 떨어뜨렸다. 단독으로는 통과해서
            // 원인이 자기 안에 있는 것처럼 보인다.
            if (_probeBall != null) Object.Destroy(_probeBall.gameObject);
            if (_stageObject != null) Object.Destroy(_stageObject);
            if (_vehicleObject != null) Object.Destroy(_vehicleObject);
        }

        private static bool IsHeadless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        private SnowStage BuildStage()
        {
            _stageObject = new GameObject("__TEST__SnowStage");
            SnowStage stage = _stageObject.AddComponent<SnowStage>();
            _stageObject.AddComponent<SnowSurfaceRenderer>();
            return stage;
        }

        [UnityTest]
        public IEnumerator 권위_격자는_그래픽_없이도_선다()
        {
            SnowStage stage = BuildStage();
            yield return null;

            Assert.IsNotNull(stage.Field, "SnowField 는 그래픽과 무관하게 만들어져야 한다");
            Assert.AreEqual(30, stage.DepthCmAtWorld(Vector3.zero), "기본값이 전면 적설이다");
        }

        [UnityTest]
        public IEnumerator 스탬프가_그래픽_없이도_적용된다()
        {
            SnowStage stage = BuildStage();
            yield return null;

            var area = new SnowStampArea(0f, 0f, 0f, 1f, 1.2f, 0.9f);
            int removed = stage.ApplyStamp(stampId: 1, area, -10);

            Assert.Greater(removed, 0, "제거량이 나와야 한다");
            Assert.AreEqual(20, stage.DepthCmAtWorld(Vector3.zero));
        }

        /// <summary>
        /// 눈덩이의 권위도 그래픽 없이 돈다. <b>확인하는 것은 "돈다" 가 아니라 "장부가 닫힌다" 다</b> —
        /// 걷은 양이 필드에서 빠진 양과 같고, 놓으면 정확히 돌아온다. 헤드리스에서 이것이 성립하지
        /// 않으면 데디 서버가 눈덩이를 판정할 수 없다.
        /// </summary>
        [UnityTest]
        public IEnumerator 눈덩이는_그래픽_없이도_굴러가고_장부가_닫힌다()
        {
            yield return null;

            var geo = new SnowFieldGeometry(24f, 24f, 0f, 0f);
            var field = new SnowHeightFieldCpu(geo, 300);
            long initial = field.TotalHeightMm;

            var ball = new SnowBallCpu(field, residueMm: 0);
            long cut = ball.Harvest(4f, 12f, 14f, 12f);

            Assert.Greater(cut, 0, "10 m 를 굴렸으면 걷은 것이 있어야 한다");
            Assert.AreEqual(cut, ball.MassMm);
            Assert.AreEqual(initial - field.TotalHeightMm, ball.MassMm, "필드 + 공 = 초기");
            Assert.AreEqual(field.RecomputeTotalHeightMm(), field.TotalHeightMm, "증분 원장이 갈라지지 않았다");
            Assert.Greater(ball.RadiusM, SnowBallCpu.SeedRadiusM, "커지지 않았다");

            long unplaced = ball.Release(14f, 12f, capMm: 2000, spreadRings: 2);
            Assert.AreEqual(0, unplaced, "놓지 못한 양이 있으면 그만큼이 공에 남는다");
            Assert.AreEqual(initial, field.TotalHeightMm, "놓은 뒤 총량이 초기로 돌아와야 한다");

            // 이 절이 실제로 GPU 없는 조건에서 돌았는지 로그로 남긴다 — 에디터 통과는 가짜 통과다.
            TestContext.WriteLine($"헤드리스={IsHeadless} graphicsDevice={SystemInfo.graphicsDeviceType} " +
                                  $"cut={cut:N0} mm·셀");
        }

        /// <summary>
        /// <b>공은 무한히 커지다 터진다 — 그리고 터진 눈은 바닥으로 돌아온다.</b>
        ///
        /// <para>단위 테스트(<c>SnowBallCpuTests</c>)는 성장과 되돌리기를 각각 확인하지만, 터짐을
        /// <b>판정하는 것은 스테이지</b>이므로 여기서 실제 경로를 밟는다 — 상한을 없앤 뒤 무엇이
        /// 공을 멈추는지가 이 테스트의 내용이다.</para>
        ///
        /// <para>공을 <b>트랜스폼으로 옮긴다.</b> 확인하려는 것은 걷기·터짐·보존이고 그 셋은 공이
        /// 어떻게 움직였는지와 무관하다 — 물리로 굴리면 씬에 바닥 콜라이더가 있는지에 결과가
        /// 걸린다(폴더 문서의 같은 함정).</para>
        /// </summary>
        [UnityTest]
        public IEnumerator 공은_상한_없이_커지고_누르면_터지고_눈은_바닥으로_돌아온다()
        {
            _stageObject = new GameObject("__TEST__SnowCpuStage");
            SnowCpuStage stage = _stageObject.AddComponent<SnowCpuStage>();
            yield return null;

            Assert.IsNotNull(stage.Field, "단독 모드에서 격자가 서야 한다");
            long initial = stage.TotalHeightMm;

            SnowBallCarrier ball = stage.TryCreateBall(new Vector3(6f, 0f, 6f));
            Assert.IsNotNull(ball, $"눈을 뭉칠 수 있어야 한다 (뭉친 양 {stage.LastGatheredMm})");
            _probeBall = ball;   // 터지지 않고 끝나면 TearDown 이 치운다

            int burstsBefore = SnowCpuStage.BurstsTotal;
            float grew = ball.RadiusM;

            // 지그재그로 끌고 다닌다. 한 줄만 밀면 그 줄의 눈을 다 걷고 더 자라지 못한다.
            //
            // <b>스스로는 터지지 않는다 (2026-08-21).</b> 크기 문턱이 사라졌으므로 여기서 끌고 다니는
            // 것만으로는 영원히 안 터진다 - 그래서 먼저 충분히 키우고, 그 다음 사람이 누르는 것과
            // 같은 요청을 넣는다. 이 테스트가 지키는 것도 그 순서다.
            var at = ball.transform.position;
            for (int step = 0; step < 120 && ball != null; step++)
            {
                at.x += 0.4f;
                if (at.x > 26f) { at.x = 6f; at.z += 0.8f; }
                if (at.z > 26f) at.z = 6f;

                ball.transform.position = at;
                yield return new WaitForFixedUpdate();

                if (ball.RadiusM > grew) grew = ball.RadiusM;
            }

            Assert.Greater(grew, SnowBallCpu.SeedRadiusM * 2f,
                "끌고 다녔으면 자랐어야 한다 - 상한이 없으니 멈출 이유가 없다");
            Assert.AreEqual(burstsBefore, SnowCpuStage.BurstsTotal,
                "누르지 않았는데 터졌다 - 크기로 터지는 경로가 남아 있다");

            ball.ServerBurstRequested = true;
            for (int step = 0; step < 60 && SnowCpuStage.BurstsTotal == burstsBefore; step++)
                yield return new WaitForFixedUpdate();

            Assert.AreEqual(burstsBefore + 1, SnowCpuStage.BurstsTotal,
                "요청했으면 터져야 한다");

            // Destroy 는 프레임 끝에 반영된다. 같은 프레임에 물으면 아직 살아 있다.
            yield return null;
            Assert.Greater(SnowCpuStage.LastBurstRadiusM, SnowBallCpu.SeedRadiusM,
                "터진 공은 씨앗보다 커야 한다");
            Assert.IsTrue(ball == null, "터진 공은 씬에서 사라져야 한다");

            // <b>보존.</b> 눈은 재분배됐을 뿐이다 — 장부 밖으로 나간 양까지 더하면 초기와 같아야 한다.
            Assert.AreEqual(initial, stage.TotalHeightMm + stage.UnaccountedOutMm,
                "필드 + 장부 밖 = 초기");
            Assert.AreEqual(stage.Field.RecomputeTotalHeightMm(), stage.Field.TotalHeightMm,
                "증분 원장이 갈라지지 않았다");

            TestContext.WriteLine($"[터짐] 반지름 {SnowCpuStage.LastBurstRadiusM:0.00} m · " +
                                  $"최대 성장 {grew:0.00} m · 장부 밖 {stage.UnaccountedOutMm} mm");
        }

        [UnityTest]
        public IEnumerator 렌더러는_그래픽_디바이스가_없으면_스스로_꺼진다()
        {
            SnowStage stage = BuildStage();
            SnowSurfaceRenderer renderer = stage.GetComponent<SnowSurfaceRenderer>();
            yield return null;

            if (IsHeadless)
            {
                Assert.IsFalse(renderer.enabled,
                    "헤드리스에서는 텍스처를 만들지 않아야 한다 — 권위는 SnowStage 에 있다");
            }
            else
            {
                Assert.IsTrue(renderer.enabled, "GPU 가 있으면 연출이 살아 있어야 한다");
            }
        }

        [UnityTest]
        public IEnumerator 감속은_텍스처가_아니라_격자를_읽는다()
        {
            SnowStage stage = BuildStage();

            _vehicleObject = new GameObject("__TEST__Vehicle");
            _vehicleObject.AddComponent<Rigidbody>();
            VehicleController controller = _vehicleObject.AddComponent<VehicleController>();
            SnowVehicleDrag drag = _vehicleObject.AddComponent<SnowVehicleDrag>();

            // 재는 곳은 바퀴가 아니라 블레이드 앞이다(Snow/AGENTS.md, 2026-08-14 정정) — 패드가
            // 축간거리를 덮어 바퀴는 항상 이미 치워진 자리에 놓인다. 좌표는 실제 리그
            // (PF_VehicleProto 의 SnowProbe_L/ML/MR/R)와 같은 z = +2.35, 폭 방향 넷이다.
            var samplePoints = new Transform[4];
            float[] offsetsX = { -1f, -0.35f, 0.35f, 1f };
            for (int i = 0; i < samplePoints.Length; i++)
            {
                var point = new GameObject($"__TEST__SamplePoint{i}");
                point.transform.SetParent(_vehicleObject.transform);
                point.transform.localPosition = new Vector3(offsetsX[i], -0.45f, 2.35f);
                samplePoints[i] = point.transform;
            }

            // 인스펙터 배선 없이 코드로 채운다. 스테이지는 컴포넌트가 스스로 찾고
            // (FindAnyObjectByType) 샘플 지점만 주입한다 — private 이라 리플렉션을 쓴다.
            //
            // ⚠ 이 문자열은 필드 이름을 바꾸면 조용히 깨진다. 컴파일러가 안 잡고 GetField 가
            // null 을 돌려주며 NullReferenceException 이 난다. [FormerlySerializedAs] 는
            // 직렬화 데이터만 따라가 주지 리플렉션 조회명까지 살려 주지 않는다 — 실제로
            // _wheels → _samplePoints 개명(cs:194) 이후 이 테스트가 계속 깨져 있었다.
            typeof(SnowVehicleDrag)
                .GetField("_samplePoints", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(drag, samplePoints);

            // 물리 스텝 몇 번 — 감속이 목표까지 보간된다.
            for (int i = 0; i < 40; i++) yield return new WaitForFixedUpdate();

            Assert.AreEqual(1f, drag.Covered, 0.001f, "네 지점 모두 눈 위다");
            Assert.Less(controller.GroundSpeedFactor, 1f, "눈 위에서는 최고속이 낮아져야 한다");

            // 궤도를 치우면 배율이 1 로 돌아온다 — 감속이 실제로 깊이를 읽는다는 증거.
            // 6 × 6 인 것은 샘플 지점이 차체 앞 z = +2.35 라 4 × 4(±2)로는 안 덮이기 때문이다.
            var wide = new SnowStampArea(0f, 0f, 0f, 1f, 6f, 6f);
            stage.ApplyStamp(stampId: 2, wide, -30);
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();

            Assert.AreEqual(0f, drag.Covered, 0.001f, "치운 자리에서는 눈에 잠긴 지점이 없다");
            Assert.AreEqual(1f, controller.GroundSpeedFactor, 0.01f);
        }
    }
}
