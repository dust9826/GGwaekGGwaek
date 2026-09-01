using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    /// <summary>씬 없이 최소 리그로 GameManager의 전역 시간 카운트다운·의뢰 완료 정산·시간 0 종료를
    /// 검증한다. 의뢰 만료가 게임을 끝내지 않는다는 것도 실제 RequestDirector와 배선해 확인한다.</summary>
    public sealed class GameManagerPlayModeTests
    {
        private GameObject _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("__TEST__GameManagerPlayMode");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Playing에서_전역_시간이_줄어든다()
        {
            StageBalanceConfig config = Config();
            config.StartSeconds = 5f;
            GameManager manager = BuildManager(config, null);
            manager.BeginPlaying();

            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();

            Assert.That(manager.Phase, Is.EqualTo(EGamePhase.Playing));
            Assert.That(manager.RemainingSeconds, Is.LessThan(5f));
        }

        [UnityTest]
        public IEnumerator Playing_시작은_중복_호출해도_한_번만_발생한다()
        {
            StageBalanceConfig config = Config();
            config.StartSeconds = 5f;
            GameManager manager = BuildManager(config, null);
            int startedCount = 0;
            manager.GameStarted += () => startedCount++;

            manager.BeginPlaying();
            yield return new WaitForFixedUpdate();
            float progressedSeconds = manager.RemainingSeconds;
            manager.BeginPlaying();

            Assert.That(startedCount, Is.EqualTo(1));
            Assert.That(manager.RemainingSeconds, Is.EqualTo(progressedSeconds).Within(0.001f),
                "중복 시작이 전역 시간을 초기값으로 되돌리면 안 된다");
        }

        [UnityTest]
        public IEnumerator 의뢰_완료하면_점수와_전역_시간이_는다()
        {
            StageBalanceConfig config = Config();
            config.StartSeconds = 60f;
            GameManager manager = BuildManager(config, null);
            manager.BeginPlaying();

            var balance = new RequestBalanceResult(2f, 20, 50f, 8f);
            var request = new GiftRequest(0, 0, EGiftBoxKind.Red, 100f, balance);
            manager.NotifyRequestCompleted(request);

            Assert.That(manager.Score, Is.EqualTo(20));
            Assert.That(manager.RemainingSeconds, Is.GreaterThan(67f)); // 60 + 8 - 소량 카운트다운
            yield return null;
        }

        [UnityTest]
        public IEnumerator 전역_시간이_0이_되면_Ended로_전이한다()
        {
            StageBalanceConfig config = Config();
            config.StartSeconds = 0.05f;
            GameManager manager = BuildManager(config, null);
            bool ended = false;
            manager.GameEnded += () => ended = true;
            manager.BeginPlaying();

            for (int i = 0; i < 30 && manager.Phase != EGamePhase.Ended; i++)
                yield return new WaitForFixedUpdate();

            Assert.That(manager.Phase, Is.EqualTo(EGamePhase.Ended));
            Assert.IsTrue(ended);
        }

        [UnityTest]
        public IEnumerator 의뢰_만료는_게임을_끝내지_않는다()
        {
            StageBalanceConfig config = Config();
            config.StartSeconds = 60f;
            config.TtlBase = 0.02f;

            RequestDirector director = BuildDirector(config);
            GameManager manager = BuildManager(config, director);
            bool expired = false;
            director.RequestExpired += _ => expired = true;

            manager.BeginPlaying();
            director.Begin();
            director.SpawnRequest(0, EGiftBoxKind.Red);

            for (int i = 0; i < 40 && !expired; i++) yield return new WaitForFixedUpdate();

            Assert.IsTrue(expired, "의뢰가 만료돼야 한다");
            Assert.That(manager.Phase, Is.EqualTo(EGamePhase.Playing), "만료로는 게임이 끝나면 안 된다");
        }

        // --- rig ---

        private static StageBalanceConfig Config() => ScriptableObject.CreateInstance<StageBalanceConfig>();

        private GameManager BuildManager(StageBalanceConfig config, RequestDirector requests)
        {
            var managerObject = new GameObject("__TEST__GameManager");
            managerObject.transform.SetParent(_root.transform);
            managerObject.SetActive(false);
            GameManager manager = managerObject.AddComponent<GameManager>();
            manager.Configure(config, requests);
            managerObject.SetActive(true);
            return manager;
        }

        private RequestDirector BuildDirector(StageBalanceConfig config)
        {
            var baseObject = new GameObject("__TEST__Base");
            baseObject.transform.SetParent(_root.transform);

            var houses = new List<DeliveryHouse>();
            var houseObject = new GameObject("__TEST__House0");
            houseObject.transform.SetParent(_root.transform);
            houseObject.transform.position = new Vector3(30f, 0f, 0f);
            GiftDropZone zone = houseObject.AddComponent<GiftDropZone>();
            DeliveryHouse house = houseObject.AddComponent<DeliveryHouse>();
            house.Configure(null, null, zone);
            houses.Add(house);

            var directorObject = new GameObject("__TEST__RequestDirector");
            directorObject.transform.SetParent(_root.transform);
            directorObject.SetActive(false);
            RequestDirector director = directorObject.AddComponent<RequestDirector>();
            director.Configure(null, baseObject.transform, null, houses, config, null);
            directorObject.SetActive(true);
            return director;
        }

    }
}
