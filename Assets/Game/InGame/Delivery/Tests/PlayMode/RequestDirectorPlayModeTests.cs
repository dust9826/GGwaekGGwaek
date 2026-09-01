using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    /// <summary>도로 그래프 없이 최소 리그로 RequestDirector의 스폰 상한·집당 1건·TTL 만료·
    /// 일치 종류 수령 판정을 검증한다. 거리는 직선 폴백으로 잰다(도로망 null).</summary>
    public sealed class RequestDirectorPlayModeTests
    {
        private GameObject _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("__TEST__RequestDirectorPlayMode");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 스폰은_최대_동시_수를_넘지_않는다()
        {
            StageBalanceConfig config = Config();
            config.MaxActiveRequests = 2;
            RequestDirector director = BuildDirector(config, houseCount: 4);
            yield return null;

            director.SpawnRequest(0, EGiftBoxKind.Red);
            director.SpawnRequest(1, EGiftBoxKind.Red);
            director.SpawnRequest(2, EGiftBoxKind.Red);
            director.SpawnRequest(3, EGiftBoxKind.Red);

            Assert.That(director.ActiveRequests.Count, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator 집당_의뢰는_하나다()
        {
            StageBalanceConfig config = Config();
            config.MaxActiveRequests = 10;
            RequestDirector director = BuildDirector(config, houseCount: 3);
            yield return null;

            GiftRequest first = director.SpawnRequest(0, EGiftBoxKind.Red);
            GiftRequest second = director.SpawnRequest(0, EGiftBoxKind.Blue);

            Assert.IsNotNull(first);
            Assert.IsNull(second);
            Assert.That(director.ActiveRequests.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TTL이_지나면_사라지고_만료_이벤트가_난다()
        {
            StageBalanceConfig config = Config();
            config.TtlBase = 0.02f; // 난이도 곱해도 아주 짧게
            RequestDirector director = BuildDirector(config, houseCount: 2);
            bool expired = false;
            director.RequestExpired += _ => expired = true;
            yield return null;

            director.SpawnRequest(0, EGiftBoxKind.Red);

            for (int i = 0; i < 40 && director.ActiveRequests.Count > 0; i++)
                yield return new WaitForFixedUpdate();

            Assert.IsTrue(expired, "TTL 만료 이벤트가 나야 한다");
            Assert.That(director.ActiveRequests.Count, Is.EqualTo(0));
            Assert.That(director.ExpiredCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator 일치_종류_선물이_구역에_들어오면_완료되고_이벤트가_난다()
        {
            StageBalanceConfig config = Config();
            config.TtlBase = 100f; // 만료로 사라지지 않게
            RequestDirector director = BuildDirector(config, houseCount: 2);
            bool completed = false;
            director.RequestCompleted += _ => completed = true;
            yield return null;

            director.SpawnRequest(0, EGiftBoxKind.Red);
            SpawnGiftInHouse(0, EGiftBoxKind.Red);

            for (int i = 0; i < 10 && director.ActiveRequests.Count > 0; i++)
                yield return new WaitForFixedUpdate();

            Assert.IsTrue(completed, "일치 종류 배달 시 완료 이벤트가 나야 한다");
            Assert.That(director.ActiveRequests.Count, Is.EqualTo(0));
            Assert.That(director.CompletedCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator 종류가_다르면_완료되지_않는다()
        {
            StageBalanceConfig config = Config();
            config.TtlBase = 100f;
            RequestDirector director = BuildDirector(config, houseCount: 2);
            bool completed = false;
            director.RequestCompleted += _ => completed = true;
            yield return null;

            director.SpawnRequest(0, EGiftBoxKind.Red);
            SpawnGiftInHouse(0, EGiftBoxKind.Blue);

            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();

            Assert.IsFalse(completed, "종류가 다르면 완료되면 안 된다");
            Assert.That(director.ActiveRequests.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator 다른_운반자가_선점한_선물은_완료에_쓰지_않는다()
        {
            StageBalanceConfig config = Config();
            config.TtlBase = 100f;
            RequestDirector director = BuildDirector(config, houseCount: 2);
            yield return null;

            director.SpawnRequest(0, EGiftBoxKind.Red);
            Gift gift = SpawnGiftInHouse(0, EGiftBoxKind.Red);
            Assert.That(gift.TryClaim(_root), Is.True);

            for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();

            Assert.That(director.ActiveRequests.Count, Is.EqualTo(1));
            Assert.That(director.CompletedCount, Is.EqualTo(0));
        }

        [UnityTest]
        public IEnumerator 첫_배치는_전용_대기_뒤에_나온다()
        {
            StageBalanceConfig config = Config();
            config.FirstSpawnDelaySeconds = 0.4f;
            config.SpawnIntervalMin = 30f;   // 정규 간격이 첫 배치를 가리지 않게 멀리 둔다
            config.SpawnIntervalMax = 30f;
            config.BurstSize = new Vector2Int(1, 1);
            RequestDirector director = BuildDirector(config, houseCount: 4);

            yield return new WaitForFixedUpdate();
            Assert.That(director.ActiveRequests.Count, Is.EqualTo(0),
                "시작하자마자 나오면 안 된다");

            yield return new WaitForSeconds(0.7f);
            Assert.That(director.ActiveRequests.Count, Is.EqualTo(1),
                "전용 대기가 지나면 첫 배치가 나와야 한다");
        }

        [UnityTest]
        public IEnumerator 한_배치는_같은_프레임에_다_나오지_않는다()
        {
            StageBalanceConfig config = Config();
            config.FirstSpawnDelaySeconds = 0f;
            config.SpawnIntervalMin = 30f;
            config.SpawnIntervalMax = 30f;
            config.BurstSize = new Vector2Int(3, 3);
            config.BurstGapSecondsMin = 0.3f;
            config.BurstGapSecondsMax = 0.3f;
            RequestDirector director = BuildDirector(config, houseCount: 4);

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(director.ActiveRequests.Count, Is.EqualTo(1),
                "3개짜리 배치라도 한 프레임에 하나만 나와야 한다");

            yield return new WaitForSeconds(0.45f);
            Assert.That(director.ActiveRequests.Count, Is.EqualTo(2), "간격 뒤에 두 번째가 나온다");

            yield return new WaitForSeconds(0.35f);
            Assert.That(director.ActiveRequests.Count, Is.EqualTo(3), "간격 뒤에 세 번째가 나온다");
        }

        // --- rig ---

        private readonly List<DeliveryHouse> _houses = new List<DeliveryHouse>();

        private static StageBalanceConfig Config() => ScriptableObject.CreateInstance<StageBalanceConfig>();

        private RequestDirector BuildDirector(StageBalanceConfig config, int houseCount)
        {
            _houses.Clear();
            var baseObject = new GameObject("__TEST__Base");
            baseObject.transform.SetParent(_root.transform);
            baseObject.transform.position = Vector3.zero;

            for (int index = 0; index < houseCount; index++)
            {
                var houseObject = new GameObject($"__TEST__House{index}");
                houseObject.transform.SetParent(_root.transform);
                houseObject.transform.position = new Vector3(20f + index * 15f, 0f, 0f);
                GiftDropZone zone = houseObject.AddComponent<GiftDropZone>();
                DeliveryHouse house = houseObject.AddComponent<DeliveryHouse>();
                house.Configure(null, null, zone);
                _houses.Add(house);
            }

            var directorObject = new GameObject("__TEST__RequestDirector");
            directorObject.transform.SetParent(_root.transform);
            directorObject.SetActive(false);
            RequestDirector director = directorObject.AddComponent<RequestDirector>();
            director.Configure(null, baseObject.transform, null, _houses, config, null);
            directorObject.SetActive(true);
            director.Begin();
            return director;
        }

        private Gift SpawnGiftInHouse(int houseIndex, EGiftBoxKind kind)
        {
            var giftObject = new GameObject($"__TEST__Gift_{kind}");
            giftObject.transform.SetParent(_root.transform);
            giftObject.transform.position = _houses[houseIndex].DoorPosition;
            Gift gift = giftObject.AddComponent<Gift>();
            gift.SetKind(kind);
            return gift;
        }
    }
}
