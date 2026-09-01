using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace PPack
{
    public sealed class SnowGiftMachineConversionPlayModeTests
    {
        private const string SnowDeliveryScenePath =
            "Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity";
        private const string SnowGiftMachinePrefabPath =
            "Assets/Game/InGame/Delivery/Prefabs/GiftProductionFlow/PF_SnowGiftMachine.prefab";

        private Scene _originalScene;
        private Scene _testScene;
        private Scene _loadedSnowDeliveryScene;
        private bool _ownsLoadedSnowDeliveryScene;
        private GameObject _rampTestPenguin;
        private GameObject _rampTestBall;
        private GameObject _giftCarryTestBall;
        private GameObject _giftCarryTestGift;

        [SetUp]
        public void SetUp()
        {
            _originalScene = SceneManager.GetActiveScene();
            _testScene = SceneManager.CreateScene("__TEST__SnowDeliveryGiftWrapping");
            SceneManager.SetActiveScene(_testScene);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_rampTestPenguin != null) Object.Destroy(_rampTestPenguin);
            if (_rampTestBall != null) Object.Destroy(_rampTestBall);
            if (_giftCarryTestBall != null) Object.Destroy(_giftCarryTestBall);
            if (_giftCarryTestGift != null) Object.Destroy(_giftCarryTestGift);
            if (_rampTestPenguin != null || _rampTestBall != null ||
                _giftCarryTestBall != null || _giftCarryTestGift != null) yield return null;
            if (_originalScene.IsValid() && _originalScene.isLoaded)
                SceneManager.SetActiveScene(_originalScene);
            if (_ownsLoadedSnowDeliveryScene && _loadedSnowDeliveryScene.IsValid() &&
                _loadedSnowDeliveryScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(_loadedSnowDeliveryScene);
            if (_testScene.IsValid() && _testScene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(_testScene);
        }

        [TestCase(ESnowBallGrowthStage.Seed, EGiftBoxKind.Blue)]
        [TestCase(ESnowBallGrowthStage.Stage1, EGiftBoxKind.Blue)]
        [TestCase(ESnowBallGrowthStage.Stage2, EGiftBoxKind.Green)]
        [TestCase(ESnowBallGrowthStage.Stage3, EGiftBoxKind.Yellow)]
        [TestCase(ESnowBallGrowthStage.Stage4, EGiftBoxKind.Red)]
        public void GiftKindForGrowthStage_UsesFixedSnowDeliveryMapping(
            ESnowBallGrowthStage stage, EGiftBoxKind expected)
        {
            Assert.That(SnowGiftMachinePresentation.GiftKindForGrowthStage(stage), Is.EqualTo(expected));
        }

        [UnityTest]
        public IEnumerator SnowDeliveryAfterIntro_EKeyCreatesBallAtPlayerStart()
        {
#if UNITY_EDITOR
            _loadedSnowDeliveryScene = SceneManager.GetSceneByPath(SnowDeliveryScenePath);
            if (!_loadedSnowDeliveryScene.IsValid() || !_loadedSnowDeliveryScene.isLoaded)
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                    SnowDeliveryScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
                _loadedSnowDeliveryScene = SceneManager.GetSceneByPath(SnowDeliveryScenePath);
                _ownsLoadedSnowDeliveryScene = true;
            }
            Assert.That(_loadedSnowDeliveryScene.IsValid(), Is.True, "SnowDelivery 씬을 열지 못했다");
            SceneManager.SetActiveScene(_loadedSnowDeliveryScene);
            RequestDirector requestDirector = FindInScene<RequestDirector>(_loadedSnowDeliveryScene);
            if (requestDirector != null) requestDirector.enabled = false;
            yield return null;

            PenguinSnowball penguin = FindInScene<PenguinSnowball>(_loadedSnowDeliveryScene);
            PenguinInputReader input = FindInScene<PenguinInputReader>(_loadedSnowDeliveryScene);
            SnowCpuStage stage = FindInScene<SnowCpuStage>(_loadedSnowDeliveryScene);
            Assert.That(penguin, Is.Not.Null, "SnowDelivery 씬에 PenguinSnowball이 없다");
            Assert.That(input, Is.Not.Null, "SnowDelivery 씬에 PenguinInputReader가 없다");
            Assert.That(stage, Is.Not.Null, "SnowDelivery 씬에 SnowCpuStage가 없다");
            Assert.That(penguin.enabled, Is.True, "SnowDelivery 씬에서 PenguinSnowball이 꺼져 있다");

            float inputDeadline = Time.realtimeSinceStartup + 8f;
            while (!input.enabled && Time.realtimeSinceStartup < inputDeadline) yield return null;
            Assert.That(input.enabled, Is.True,
                "SnowDelivery 인트로가 끝난 뒤에도 PenguinInputReader가 켜지지 않았다");

            InputAction createSnowballAction =
                (InputAction)typeof(PenguinInputReader).GetField("_createSnowballAction",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic)
                    ?.GetValue(input);
            Assert.That(createSnowballAction, Is.Not.Null);
            bool hasEKeyBinding = false;
            foreach (InputBinding binding in createSnowballAction.bindings)
            {
                if (binding.effectivePath == "<Keyboard>/e") hasEKeyBinding = true;
            }
            Assert.That(hasEKeyBinding, Is.True,
                "SnowDelivery의 CreateSnowball 액션이 E키에 연결되어 있어야 한다");

            // headless에는 물리 키보드가 없고, 런타임에 추가한 가상 장치는 이미 활성화된
            // InputAction의 디바이스 해석 시점에 따라 달라진다. 바인딩은 위에서 별도로 검증하고,
            // 여기서는 InputReader가 물리 스텝에 공개하는 동일한 래치를 결정적으로 주입한다.
            input.enabled = false;
            SetCreateSnowballPressed(input, true);
            yield return new WaitForFixedUpdate();
            SetCreateSnowballPressed(input, false);

            Assert.That(penguin.Held, Is.Not.Null,
                $"플레이어 시작점에서 E 동작으로 눈덩이를 만들지 못했다: {penguin.LastFailure}");
            Assert.That(stage.BallHeldMm, Is.GreaterThan(0L));

            SnowBallCarrier ball = penguin.Held;
            penguin.Release();
            Assert.That(stage.TryConsumeBallForLocalConversion(ball, out _), Is.True);
#else
            Assert.Ignore("SnowDelivery 테스트 씬 로드는 Unity Editor에서만 검증한다.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator SnowDelivery_UsesMomentumPenguinAndGrowthHud()
        {
#if UNITY_EDITOR
            _loadedSnowDeliveryScene = SceneManager.GetSceneByPath(SnowDeliveryScenePath);
            if (!_loadedSnowDeliveryScene.IsValid() || !_loadedSnowDeliveryScene.isLoaded)
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                    SnowDeliveryScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
                _loadedSnowDeliveryScene = SceneManager.GetSceneByPath(SnowDeliveryScenePath);
                _ownsLoadedSnowDeliveryScene = true;
            }
            Assert.That(_loadedSnowDeliveryScene.IsValid(), Is.True, "SnowDelivery 씬을 열지 못했다");
            SceneManager.SetActiveScene(_loadedSnowDeliveryScene);
            yield return null;

            PenguinSnowball penguin = FindInScene<PenguinSnowball>(_loadedSnowDeliveryScene);
            PenguinMomentumHandling momentum =
                FindInScene<PenguinMomentumHandling>(_loadedSnowDeliveryScene);
            PenguinMomentumSnowballBinder binder =
                FindInScene<PenguinMomentumSnowballBinder>(_loadedSnowDeliveryScene);
            SnowballGrowthArcHud hud = FindInScene<SnowballGrowthArcHud>(_loadedSnowDeliveryScene);
            SnowballGrowthPlayableSceneController controller =
                FindInScene<SnowballGrowthPlayableSceneController>(_loadedSnowDeliveryScene);

            Assert.That(penguin, Is.Not.Null, "SinglePlay에 PenguinSnowball이 없다");
            Assert.That(momentum, Is.Not.Null, "SinglePlay가 관성 펭귄 Variant를 사용하지 않는다");
            Assert.That(binder, Is.Not.Null, "SinglePlay에 눈덩이 관성 바인더가 없다");
            Assert.That(momentum.gameObject, Is.SameAs(penguin.gameObject));
            Assert.That(binder.gameObject, Is.SameAs(penguin.gameObject));
            Assert.That(hud, Is.Not.Null, "SinglePlay에 성장 HUD가 없다");
            Assert.That(controller, Is.Not.Null, "SinglePlay에 성장 런타임 컨트롤러가 없다");
            Assert.That(controller.gameObject, Is.SameAs(hud.gameObject));
            Assert.That(hud.transform.parent.name, Is.EqualTo("SnowDeliveryRig"));
            UnityEngine.UIElements.UIDocument document =
                hud.GetComponent<UnityEngine.UIElements.UIDocument>();
            Assert.That(document, Is.Not.Null);
            Assert.That(document.panelSettings, Is.Not.Null);
            Assert.That(document.visualTreeAsset, Is.Not.Null);
#else
            Assert.Ignore("SnowDelivery 테스트 씬 로드는 Unity Editor에서만 검증한다.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator SnowDeliveryTraversal_UsesOneRoadRampSetAndFourPlazaApproaches()
        {
#if UNITY_EDITOR
            _loadedSnowDeliveryScene = SceneManager.GetSceneByPath(SnowDeliveryScenePath);
            if (!_loadedSnowDeliveryScene.IsValid() || !_loadedSnowDeliveryScene.isLoaded)
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                    SnowDeliveryScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
                _loadedSnowDeliveryScene = SceneManager.GetSceneByPath(SnowDeliveryScenePath);
                _ownsLoadedSnowDeliveryScene = true;
            }
            Assert.That(_loadedSnowDeliveryScene.IsValid(), Is.True, "SnowDelivery 씬을 열지 못했다");
            SceneManager.SetActiveScene(_loadedSnowDeliveryScene);
            yield return null;

            Transform mapRamps = FindNamedTransformInScene(_loadedSnowDeliveryScene, "VehicleCurbRamps");
            Assert.That(mapRamps, Is.Not.Null, "맵 소유 VehicleCurbRamps가 없다");
            Assert.That(mapRamps.parent.name, Is.EqualTo("Routes"));
            Assert.That(mapRamps.gameObject.activeSelf, Is.False,
                "도로가 평지와 같은 높이인데 기존 커브 램프가 활성 상태다");
            BoxCollider[] mapRampColliders = mapRamps.GetComponentsInChildren<BoxCollider>(true);
            Assert.That(mapRampColliders.Length, Is.EqualTo(24));
            foreach (BoxCollider mapRampCollider in mapRampColliders)
                Assert.That(mapRampCollider.enabled, Is.False, mapRampCollider.name);
            Assert.That(FindNamedTransformInScene(_loadedSnowDeliveryScene, "CurbRamps"), Is.Null,
                "SnowDeliveryRig 아래에 중복 도로 램프가 남았다");

            Transform routes = mapRamps.parent;
            int roadRendererCount = 0;
            foreach (Renderer renderer in routes.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.name.StartsWith("Road_", System.StringComparison.Ordinal)) continue;
                roadRendererCount++;
                Assert.That(renderer.bounds.max.y, Is.EqualTo(0.01f).Within(0.002f),
                    renderer.name + " 상판이 평지와 같은 높이가 아니다");
            }
            Assert.That(roadRendererCount, Is.EqualTo(12));

            Assert.That(FindInScene<GiftWrappingZone>(_loadedSnowDeliveryScene), Is.Null,
                "광장에 기존 GiftWrappingZone 트리거가 남았다");
            Assert.That(FindInScene<GiftSpawner>(_loadedSnowDeliveryScene), Is.Null,
                "랜덤 GiftSpawner가 SnowDelivery에 남았다");
            Assert.That(CountInScene<SnowGiftMachinePresentation>(_loadedSnowDeliveryScene), Is.EqualTo(1),
                "SnowDelivery에는 PF_SnowGiftMachine이 정확히 하나 있어야 한다");
            SnowGiftMachineSuctionTrigger suctionTrigger =
                FindInScene<SnowGiftMachineSuctionTrigger>(_loadedSnowDeliveryScene);
            Assert.That(suctionTrigger, Is.Not.Null, "SnowDelivery 머신에 흡입 트리거가 없다");
            Assert.That(suctionTrigger.OpeningRadius, Is.EqualTo(1.35f).Within(0.001f));
            Assert.That(suctionTrigger.FrontDepth, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(suctionTrigger.RearTolerance, Is.EqualTo(0.15f).Within(0.001f));
            Assert.That(suctionTrigger.SurfaceTolerance, Is.EqualTo(0.2f).Within(0.001f));
            BoxCollider suctionCollider = suctionTrigger.GetComponent<BoxCollider>();
            Assert.That(suctionCollider, Is.Not.Null);
            Assert.That(suctionCollider.size, Is.EqualTo(new Vector3(2.9f, 2.9f, 1.5f)));

            Transform plaza = FindNamedTransformInScene(_loadedSnowDeliveryScene, "CentralTreePlaza");
            Assert.That(plaza, Is.Not.Null);
            Transform inner = plaza.Find("PlazaInner");
            Assert.That(inner, Is.Not.Null);
            float plazaSurfaceY = inner.GetComponent<Renderer>().bounds.max.y;
            float approachFallbackY = plaza.Find("PlazaOuter").GetComponent<Renderer>().bounds.min.y - 0.04f;
            Transform accessRamps = plaza.Find("GiftWrappingAccessRamps");
            Assert.That(accessRamps, Is.Not.Null);
            Assert.That(accessRamps.childCount, Is.EqualTo(4));

            string[] expectedNames =
            {
                "GiftWrappingAccessRamp_North",
                "GiftWrappingAccessRamp_East",
                "GiftWrappingAccessRamp_South",
                "GiftWrappingAccessRamp_West"
            };
            foreach (string expectedName in expectedNames)
            {
                Transform ramp = accessRamps.Find(expectedName);
                Assert.That(ramp, Is.Not.Null, expectedName);
                BoxCollider collider = ramp.GetComponent<BoxCollider>();
                Assert.That(collider, Is.Not.Null, expectedName);
                float slopeAngle = Vector3.Angle(ramp.up, Vector3.up);
                Assert.That(slopeAngle, Is.GreaterThan(0.1f).And.LessThan(10f), expectedName);
                Assert.That(ramp.localScale.x, Is.GreaterThanOrEqualTo(6f),
                    expectedName + " 운반 폭");
                Assert.That(ramp.localScale.z, Is.GreaterThanOrEqualTo(5f),
                    expectedName + " 운반 경사 길이");
                Vector3 lowerEdge = ramp.TransformPoint(collider.center +
                    new Vector3(0f, collider.size.y * 0.5f, -collider.size.z * 0.5f));
                Vector3 upperEdge = ramp.TransformPoint(collider.center +
                    new Vector3(0f, collider.size.y * 0.5f, collider.size.z * 0.5f));
                Vector3 approachDirection = Vector3.ProjectOnPlane(ramp.forward, Vector3.up).normalized;
                Vector3 probeOrigin = lowerEdge - approachDirection * 0.5f + Vector3.up * 5f;
                float approachY = FindUnderlyingApproachY(probeOrigin, approachFallbackY);
                Assert.That(lowerEdge.y, Is.EqualTo(approachY - 0.12f).Within(0.02f),
                    expectedName + " 하단이 접근 지면 아래로 매립되지 않았다");
                Assert.That(upperEdge.y, Is.EqualTo(plazaSurfaceY).Within(0.01f), expectedName);
            }
#else
            Assert.Ignore("SnowDelivery 테스트 씬 로드는 Unity Editor에서만 검증한다.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator SupportedSeedBall_MachineOutputsOneBlueGiftAndClosesSnowLedger()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "__TEST__WrappingGround";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);

            var stageObject = new GameObject("__TEST__SnowCpuStage");
            SnowCpuStage stage = stageObject.AddComponent<SnowCpuStage>();
            yield return null;

            SnowBallCarrier ball = stage.TryCreateBall(Vector3.zero);
            Assert.That(ball, Is.Not.Null, "테스트 눈으로 Seed 눈덩이를 만들지 못했다");
            for (int index = 0; index < 4 && !ball.HasSupport; index++) yield return new WaitForFixedUpdate();
            Assert.That(ball.HasSupport, Is.True, "눈덩이가 평면에 접지하지 않았다");

            long heldBefore = stage.BallHeldMm;
            Assert.That(heldBefore, Is.GreaterThan(0L));

            var giftTemplateObject = new GameObject("__TEST__GiftTemplate");
            giftTemplateObject.AddComponent<BoxCollider>();
            Gift giftTemplate = giftTemplateObject.AddComponent<Gift>();
            giftTemplateObject.SetActive(false);

            SnowGiftMachinePresentation machine = CreateMachine(stage, giftTemplate);

            Assert.That(machine.TryConsume(ball), Is.True);
            yield return new WaitForSeconds(0.2f);

            Gift wrapped = null;
            foreach (Gift gift in Gift.All)
            {
                if (gift != null && gift.gameObject.scene == _testScene) wrapped = gift;
            }

            Assert.That(wrapped, Is.Not.Null);
            Assert.That(wrapped.Kind, Is.EqualTo(EGiftBoxKind.Blue));
            Assert.That(stage.BallHeldMm, Is.Zero);
            Assert.That(stage.ConvertedOutMm, Is.EqualTo(heldBefore));
            Assert.That(ball == null, Is.True, "소비된 눈덩이가 씬에 남았다");
        }

        [UnityTest]
        public IEnumerator PrefabTrigger_SurfaceOverlapConsumesEveryGrowthStage()
        {
#if UNITY_EDITOR
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "__TEST__SnowGiftMachineGround";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(30f, 1f, 30f);
            var listenerObject = new GameObject("__TEST__SnowGiftMachineAudioListener");
            listenerObject.AddComponent<AudioListener>();

            var stageObject = new GameObject("__TEST__SnowGiftMachineStage");
            SnowCpuStage stage = stageObject.AddComponent<SnowCpuStage>();
            yield return null;
            // 각 단계의 표시 질량을 고정한다. 스테이지 스텝은 내부 Stage1 시뮬 질량으로 다시 덮어쓴다.
            stage.enabled = false;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SnowGiftMachinePrefabPath);
            Assert.That(prefab, Is.Not.Null);
            GameObject machineObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _testScene);
            SnowGiftMachinePresentation machine = machineObject.GetComponent<SnowGiftMachinePresentation>();
            SnowGiftMachineSuctionTrigger trigger =
                machineObject.GetComponentInChildren<SnowGiftMachineSuctionTrigger>(true);
            BoxCollider triggerCollider = trigger != null ? trigger.GetComponent<BoxCollider>() : null;
            Assert.That(machine, Is.Not.Null);
            Assert.That(trigger, Is.Not.Null);
            Assert.That(triggerCollider, Is.Not.Null);

            foreach (ParticleSystem particles in machineObject.GetComponentsInChildren<ParticleSystem>(true))
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particles.gameObject.SetActive(false);
            }
            machine.ConfigureSnowDeliveryConversion(stage);
            SetPrivateField(machine, "_intakeDuration", 0.05f);
            SetPrivateField(machine, "_cycleDuration", 0.1f);
            SetPrivateField(machine, "_giftBurstVfx", System.Array.Empty<ParticleSystem>());
            machine.ConfigureSuctionFeedback(System.Array.Empty<ParticleSystem>(), null, null);
            trigger.Configure(machine, 1.35f, 1.5f, 0.15f, 0.2f);
            triggerCollider.size = new Vector3(2.9f, 2.9f, 1.5f);

            float[] radii =
            {
                SnowballStageModel.GetStageRepresentativeRadius(ESnowBallGrowthStage.Stage1),
                SnowballStageModel.GetStageRepresentativeRadius(ESnowBallGrowthStage.Stage2),
                SnowballStageModel.GetStageRepresentativeRadius(ESnowBallGrowthStage.Stage3),
                SnowballStageModel.MaxRadiusM,
            };
            EGiftBoxKind[] expectedKinds =
            {
                EGiftBoxKind.Blue,
                EGiftBoxKind.Green,
                EGiftBoxKind.Yellow,
                EGiftBoxKind.Red,
            };

            for (int index = 0; index < radii.Length; index++)
            {
                SnowBallCarrier ball = stage.TryCreateBall(new Vector3(4f + index * 2f, 0f, 4f));
                Assert.That(ball, Is.Not.Null, $"Stage {index + 1} 테스트 눈덩이를 만들지 못했다");
                ball.ServerApplyMass(ball.MassMmForRadius(radii[index]));
                Assert.That((int)ball.GrowthStage, Is.EqualTo(index + 1));

                Rigidbody body = ball.GetComponent<Rigidbody>();
                Assert.That(body, Is.Not.Null);
                body.isKinematic = false;
                body.useGravity = false;
                body.constraints = RigidbodyConstraints.FreezeAll;
                body.interpolation = RigidbodyInterpolation.None;
                Vector3 frontFace = triggerCollider.transform.TransformPoint(
                    triggerCollider.center + Vector3.back * triggerCollider.size.z * 0.5f);
                Vector3 ballPosition =
                    frontFace - triggerCollider.transform.forward * (radii[index] * 0.5f);
                ball.transform.position = ballPosition;
                body.position = ballPosition;
                Physics.SyncTransforms();

                Collider ballCollider = ball.GetComponent<Collider>();
                Assert.That(ballCollider, Is.Not.Null);
                Assert.That(trigger.IsColliderWithinFrontIntake(ballCollider), Is.True,
                    $"Stage {index + 1} 눈덩이 표면이 흡입 판정 범위에 들지 않았다");
                Assert.That(Physics.ComputePenetration(
                        ballCollider, ballCollider.transform.position, ballCollider.transform.rotation,
                        triggerCollider, triggerCollider.transform.position, triggerCollider.transform.rotation,
                        out _, out _),
                    Is.True, $"Stage {index + 1} 눈덩이 표면이 실제 트리거와 겹치지 않았다");

                float intakeDeadline = Time.realtimeSinceStartup + 0.5f;
                while (!machine.IsProcessing && ball != null && Time.realtimeSinceStartup < intakeDeadline)
                    yield return new WaitForFixedUpdate();
                Assert.That(machine.IsProcessing || ball == null, Is.True,
                    $"Stage {index + 1} 눈덩이 표면이 닿았지만 흡입이 시작되지 않았다");

                float deadline = Time.realtimeSinceStartup + 2f;
                while ((machine.IsProcessing || ball != null) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(ball == null, Is.True, $"Stage {index + 1} 눈덩이가 소비되지 않았다");

                Gift wrapped = null;
                foreach (Gift gift in Gift.All)
                {
                    if (gift != null && gift.gameObject.scene == _testScene) wrapped = gift;
                }
                Assert.That(wrapped, Is.Not.Null, $"Stage {index + 1} 선물이 생성되지 않았다");
                Assert.That(wrapped.Kind, Is.EqualTo(expectedKinds[index]));
                Object.Destroy(wrapped.gameObject);
                yield return null;
            }

            GameObject sideProbe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sideProbe.name = "__TEST__SnowGiftMachineSideProbe";
            sideProbe.transform.position = machine.IntakeAnchor.TransformPoint(new Vector3(3f, 0f, -0.5f));
            GameObject rearProbe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rearProbe.name = "__TEST__SnowGiftMachineRearProbe";
            rearProbe.transform.position = machine.IntakeAnchor.TransformPoint(new Vector3(0f, 0f, 1f));
            Physics.SyncTransforms();
            Assert.That(trigger.IsColliderWithinFrontIntake(sideProbe.GetComponent<Collider>()), Is.False);
            Assert.That(trigger.IsColliderWithinFrontIntake(rearProbe.GetComponent<Collider>()), Is.False);
#else
            Assert.Ignore("프리팹 물리 트리거 검증은 Unity Editor에서만 실행한다.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator SnowDeliveryMachineGift_CanBeCarriedFromIntakeSideWithF()
        {
#if UNITY_EDITOR
            _loadedSnowDeliveryScene = SceneManager.GetSceneByPath(SnowDeliveryScenePath);
            if (!_loadedSnowDeliveryScene.IsValid() || !_loadedSnowDeliveryScene.isLoaded)
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                    SnowDeliveryScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
                _loadedSnowDeliveryScene = SceneManager.GetSceneByPath(SnowDeliveryScenePath);
                _ownsLoadedSnowDeliveryScene = true;
            }
            Assert.That(_loadedSnowDeliveryScene.IsValid(), Is.True, "SnowDelivery 씬을 열지 못했다");
            SceneManager.SetActiveScene(_loadedSnowDeliveryScene);
            RequestDirector requestDirector = FindInScene<RequestDirector>(_loadedSnowDeliveryScene);
            if (requestDirector != null) requestDirector.enabled = false;
            yield return null;

            SnowGiftMachinePresentation machine =
                FindInScene<SnowGiftMachinePresentation>(_loadedSnowDeliveryScene);
            SnowCpuStage stage = FindInScene<SnowCpuStage>(_loadedSnowDeliveryScene);
            PenguinInputReader input = FindInScene<PenguinInputReader>(_loadedSnowDeliveryScene);
            PenguinCarry carry = input != null ? input.GetComponent<PenguinCarry>() : null;
            Assert.That(machine, Is.Not.Null);
            Assert.That(stage, Is.Not.Null);
            Assert.That(input, Is.Not.Null);
            Assert.That(carry, Is.Not.Null);

            input.enabled = false;
            SnowBallCarrier ball = stage.TryCreateBall(machine.IntakeAnchor.position + Vector3.up * 2f);
            Assert.That(ball, Is.Not.Null);
            _giftCarryTestBall = ball.gameObject;
            Assert.That(machine.TryConsume(ball), Is.True);

            float outputDeadline = Time.realtimeSinceStartup + 5f;
            Gift gift = null;
            while (gift == null && Time.realtimeSinceStartup < outputDeadline)
            {
                foreach (Gift candidate in Gift.All)
                {
                    if (candidate == null || candidate.gameObject.scene != _loadedSnowDeliveryScene ||
                        candidate.GetComponent<Rigidbody>() == null) continue;
                    gift = candidate;
                }
                yield return null;
            }
            Assert.That(gift, Is.Not.Null, "기계가 운반 가능한 선물을 출력하지 않았다");
            _giftCarryTestGift = gift.gameObject;
            _giftCarryTestBall = null;

            Rigidbody giftBody = gift.GetComponent<Rigidbody>();
            float settleDeadline = Time.realtimeSinceStartup + 4f;
            while (giftBody.linearVelocity.sqrMagnitude > 0.04f &&
                   Time.realtimeSinceStartup < settleDeadline)
                yield return new WaitForFixedUpdate();

            Rigidbody playerBody = input.GetComponent<Rigidbody>();
            Vector3 playerPosition = machine.IntakeAnchor.position - machine.transform.forward * 1.2f;
            playerPosition.y = 0.01f;
            playerBody.position = playerPosition;
            playerBody.rotation = Quaternion.LookRotation(machine.transform.right, Vector3.up);
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            SetPickupPressed(input, true);
            carry.Step(Time.fixedDeltaTime, input.PickupPressedThisFrame);
            SetPickupPressed(input, false);
            Assert.That(carry.Cargo, Is.SameAs(gift),
                $"투입구 쪽에서 F로 출력 선물을 고르지 못했다 · player={playerBody.position}, gift={giftBody.position}");

            for (int index = 0; index < 220 && !carry.IsHolding; index++)
                yield return new WaitForFixedUpdate();
            Assert.That(carry.IsHolding, Is.True, "기계가 만든 선물이 등에 안착하지 않았다");
            Assert.That(gift.IsCarried, Is.True);

            SetPickupPressed(input, true);
            carry.Step(Time.fixedDeltaTime, input.PickupPressedThisFrame);
            SetPickupPressed(input, false);
            for (int index = 0; index < 40 && carry.IsCarrying; index++)
                yield return new WaitForFixedUpdate();
            Assert.That(carry.IsCarrying, Is.False, "F로 선물을 내려놓지 못했다");
            Assert.That(gift.IsCarried, Is.False);
#else
            Assert.Ignore("SnowDelivery 기계 선물 운반 검증은 Unity Editor에서만 실행한다.");
            yield break;
#endif
        }

        [UnityTest]
        public IEnumerator CarriedStage2Snowball_CanAscendSouthPlazaRamp()
        {
#if UNITY_EDITOR
            _loadedSnowDeliveryScene = SceneManager.GetSceneByPath(SnowDeliveryScenePath);
            if (!_loadedSnowDeliveryScene.IsValid() || !_loadedSnowDeliveryScene.isLoaded)
            {
                yield return EditorSceneManager.LoadSceneAsyncInPlayMode(
                    SnowDeliveryScenePath, new LoadSceneParameters(LoadSceneMode.Additive));
                _loadedSnowDeliveryScene = SceneManager.GetSceneByPath(SnowDeliveryScenePath);
                _ownsLoadedSnowDeliveryScene = true;
            }
            Assert.That(_loadedSnowDeliveryScene.IsValid(), Is.True, "SnowDelivery 씬을 열지 못했다");
            SceneManager.SetActiveScene(_loadedSnowDeliveryScene);
            RequestDirector requestDirector = FindInScene<RequestDirector>(_loadedSnowDeliveryScene);
            if (requestDirector != null) requestDirector.enabled = false;
            yield return null;

            Transform ramp = FindNamedTransformInScene(
                _loadedSnowDeliveryScene, "GiftWrappingAccessRamp_South");
            Assert.That(ramp, Is.Not.Null);

            GameObject penguinPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab");
            GameObject ballPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/InGame/Snow/Resources/PF_SnowBall.prefab");
            Assert.That(penguinPrefab, Is.Not.Null);
            Assert.That(ballPrefab, Is.Not.Null);

            GameObject penguinObject = Object.Instantiate(penguinPrefab);
            _rampTestPenguin = penguinObject;
            penguinObject.name = "__TEST__RampCarryPenguin";
            SceneManager.MoveGameObjectToScene(penguinObject, _loadedSnowDeliveryScene);
            foreach (Camera camera in penguinObject.GetComponentsInChildren<Camera>(true))
                camera.enabled = false;
            foreach (AudioListener listener in penguinObject.GetComponentsInChildren<AudioListener>(true))
                listener.enabled = false;

            PenguinInputReader input = penguinObject.GetComponent<PenguinInputReader>();
            PenguinCarry carry = penguinObject.GetComponent<PenguinCarry>();
            Assert.That(input, Is.Not.Null);
            Assert.That(carry, Is.Not.Null);
            input.enabled = false;

            Rigidbody playerBody = input.GetComponent<Rigidbody>();
            BoxCollider rampCollider = ramp.GetComponent<BoxCollider>();
            Vector3 uphill = Vector3.ProjectOnPlane(ramp.forward, Vector3.up).normalized;
            Vector3 lowerEdge = ramp.TransformPoint(rampCollider.center +
                new Vector3(0f, rampCollider.size.y * 0.5f, -rampCollider.size.z * 0.5f));
            Vector3 upperEdge = ramp.TransformPoint(rampCollider.center +
                new Vector3(0f, rampCollider.size.y * 0.5f, rampCollider.size.z * 0.5f));
            Vector3 start = lowerEdge - uphill * 0.65f + Vector3.up * 0.02f;
            Vector3 pickupStart = lowerEdge - uphill * 2.4f + Vector3.up * 0.02f;
            playerBody.position = pickupStart;
            playerBody.rotation = Quaternion.LookRotation(uphill, Vector3.up);
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();

            GameObject ballObject = Object.Instantiate(ballPrefab, pickupStart,
                Quaternion.identity);
            _rampTestBall = ballObject;
            ballObject.name = "__TEST__RampCarrySnowball";
            SceneManager.MoveGameObjectToScene(ballObject, _loadedSnowDeliveryScene);
            SnowBallCarrier ball = ballObject.GetComponent<SnowBallCarrier>();
            Assert.That(ball, Is.Not.Null);
            ball.ServerApplyMass(ball.VisibleMaxMassMm / 5L);
            Rigidbody ballBody = ballObject.GetComponent<Rigidbody>();
            Assert.That(ballBody, Is.Not.Null);
            ballBody.position = pickupStart + uphill * (ball.RadiusM + 0.75f) +
                Vector3.up * ball.RadiusM;
            ballBody.linearVelocity = Vector3.zero;
            ballBody.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            for (int index = 0; index < 8 && !ball.HasSupport; index++)
                yield return new WaitForFixedUpdate();
            Assert.That(ball.GrowthStage, Is.EqualTo(ESnowBallGrowthStage.Stage2));
            Physics.SyncTransforms();
            for (int index = 0; index < 8 && !carry.CanApproachCargo; index++)
                yield return null;
            Assert.That(carry.CanApproachCargo, Is.True,
                "Stage2 눈덩이가 운반 가능한 접근 후보로 감지되지 않았다");

            SetPickupPressed(input, true);
            yield return new WaitForFixedUpdate();
            SetPickupPressed(input, false);
            for (int index = 0; index < 220 && !carry.IsHolding; index++)
                yield return new WaitForFixedUpdate();
            Assert.That(carry.IsHolding, Is.True, "Stage2 눈덩이를 등에 싣지 못했다");

            playerBody.position = start;
            playerBody.rotation = Quaternion.LookRotation(uphill, Vector3.up);
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            SetMoveInput(input, Vector2.up);
            SetSprintHeld(input, true);
            float requiredProgress = Vector3.Dot(upperEdge - start, uphill) + 0.35f;
            float progress = 0f;
            for (int index = 0; index < 600 && progress < requiredProgress; index++)
            {
                yield return new WaitForFixedUpdate();
                progress = Vector3.Dot(playerBody.position - start, uphill);
            }
            SetSprintHeld(input, false);
            SetMoveInput(input, Vector2.zero);

            Collider[] nearby = Physics.OverlapSphere(playerBody.position, 1f);
            string nearbyNames = string.Join(", ", System.Array.ConvertAll(
                nearby, collider => $"{collider.name}@{collider.bounds.center}/{collider.bounds.size}" +
                    $" local={collider.transform.InverseTransformPoint(playerBody.position)}"));
            Assert.That(progress, Is.GreaterThanOrEqualTo(requiredProgress),
                $"Stage2 눈덩이를 등에 멘 펭귄이 남쪽 경사로 상단까지 오르지 못했다 · " +
                $"position={playerBody.position}, velocity={playerBody.linearVelocity}, " +
                $"forward={penguinObject.transform.forward}, nearby=[{nearbyNames}]");
            Assert.That(playerBody.position.y, Is.GreaterThanOrEqualTo(upperEdge.y - 0.08f));

            Object.Destroy(penguinObject);
            if (ballObject != null) Object.Destroy(ballObject);
            _rampTestPenguin = null;
            _rampTestBall = null;
            yield return null;
#else
            Assert.Ignore("SnowDelivery 테스트 씬 로드는 Unity Editor에서만 검증한다.");
            yield break;
#endif
        }

        private static SnowGiftMachinePresentation CreateMachine(SnowCpuStage stage, Gift giftTemplate)
        {
            var machineObject = new GameObject("__TEST__SnowGiftMachine");
            SnowGiftMachinePresentation machine = machineObject.AddComponent<SnowGiftMachinePresentation>();

            Transform intakeAnchor = new GameObject("IntakeAnchor").transform;
            intakeAnchor.SetParent(machineObject.transform, false);
            Transform outputAnchor = new GameObject("GiftOutputAnchor").transform;
            outputAnchor.SetParent(machineObject.transform, false);
            outputAnchor.localPosition = Vector3.up;

            Transform intakeVisual = new GameObject("IntakeVisual").transform;
            intakeVisual.SetParent(machineObject.transform, false);
            MeshFilter intakeFilter = intakeVisual.gameObject.AddComponent<MeshFilter>();
            MeshRenderer intakeRenderer = intakeVisual.gameObject.AddComponent<MeshRenderer>();

            Transform giftDriver = new GameObject("GiftPopDriver").transform;
            giftDriver.SetParent(machineObject.transform, false);
            giftDriver.localPosition = Vector3.up;

            machine.Configure(intakeAnchor, outputAnchor, null);
            SetPrivateField(machine, "_intakeVisual", intakeVisual);
            SetPrivateField(machine, "_intakeVisualMeshFilter", intakeFilter);
            SetPrivateField(machine, "_intakeVisualRenderer", intakeRenderer);
            SetPrivateField(machine, "_giftPopDriver", giftDriver);
            SetPrivateField(machine, "_giftPrefab", giftTemplate);
            SetPrivateField(machine, "_giftBurstVfx", new ParticleSystem[0]);
            machine.ConfigureSnowDeliveryConversion(stage);
            SetPrivateField(machine, "_intakeDuration", 0.05f);
            SetPrivateField(machine, "_cycleDuration", 0.1f);
            return machine;
        }

        private static T FindInScene<T>(Scene scene) where T : Component
        {
            foreach (T candidate in Object.FindObjectsByType<T>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene == scene) return candidate;
            }

            return null;
        }

        private static int CountInScene<T>(Scene scene) where T : Component
        {
            int count = 0;
            foreach (T candidate in Object.FindObjectsByType<T>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene == scene) count++;
            }

            return count;
        }

        private static Transform FindNamedTransformInScene(Scene scene, string name)
        {
            foreach (Transform candidate in Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene == scene && candidate.name == name) return candidate;
            }

            return null;
        }

        private static float FindUnderlyingApproachY(Vector3 origin, float fallbackY)
        {
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 10f);
            System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.isTrigger || hit.collider.name.StartsWith("Curb_") ||
                    hit.collider.name.StartsWith("Road_")) continue;
                return hit.point.y;
            }

            return fallbackY;
        }

        private static void SetCreateSnowballPressed(PenguinInputReader input, bool value)
        {
            typeof(PenguinInputReader).GetProperty(
                    nameof(PenguinInputReader.CreateSnowballPressedThisFrame),
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public)
                ?.SetValue(input, value);
        }

        private static void SetPickupPressed(PenguinInputReader input, bool value)
        {
            typeof(PenguinInputReader).GetProperty(
                    nameof(PenguinInputReader.PickupPressedThisFrame),
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public)
                ?.SetValue(input, value);
        }

        private static void SetMoveInput(PenguinInputReader input, Vector2 value)
        {
            typeof(PenguinInputReader).GetProperty(
                    nameof(PenguinInputReader.MoveInput),
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public)
                ?.SetValue(input, value);
        }

        private static void SetSprintHeld(PenguinInputReader input, bool value)
        {
            typeof(PenguinInputReader).GetProperty(
                    nameof(PenguinInputReader.SprintHeld),
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public)
                ?.SetValue(input, value);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            typeof(SnowGiftMachinePresentation).GetField(fieldName,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }
    }
}
