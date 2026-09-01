using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class GiftDeliveryLoopPlayModeTests
    {
        private GameObject _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("__TEST__GiftDeliveryLoopPlayMode");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 주문_성공후_스태거를_거쳐_다른_집으로_다음_주문이_시작된다()
        {
            DeliveryRoadNode origin = Node("Origin", Vector3.zero);
            DeliveryRoadNode nodeNear = Node("Near", new Vector3(0f, 0f, 10f));
            DeliveryRoadNode nodeFar = Node("Far", new Vector3(0f, 0f, 15f));
            DeliveryRoadNetwork network = Network(new[] { origin, nodeNear, nodeFar },
                new[] { Segment("OriginNear", origin, nodeNear), Segment("OriginFar", origin, nodeFar) });
            DeliveryHouse houseNear = House(nodeNear);
            DeliveryHouse houseFar = House(nodeFar);
            Transform participant = Node("Participant", Vector3.zero).transform;

            GiftDeliveryDirector director = BuildDirector(network, new[] { houseNear, houseFar },
                new[] { participant }, EasySettings(targetLength: 10f), orderStaggerSeconds: 0.05f);
            director.Begin();
            yield return new WaitForFixedUpdate();

            Assert.That(director.ActiveOrders.Count, Is.EqualTo(1));
            int firstHouseIndex = director.ActiveOrders[0].HouseIndex;

            Vector3 firstHousePosition = firstHouseIndex == 0
                ? nodeNear.transform.position
                : nodeFar.transform.position;
            SpawnGift(firstHousePosition, 1, director.ActiveOrders[0].GiftKind);
            yield return WaitUntil(() => director.CompletedCount >= 1, 60);

            Assert.That(director.CompletedCount, Is.EqualTo(1));

            yield return WaitUntil(() => director.ActiveOrders.Count >= 1, 60);

            Assert.That(director.ActiveOrders[0].HouseIndex, Is.Not.EqualTo(firstHouseIndex),
                "직전 집은 최근 사용 제외라 다른 집이 선택돼야 한다");
        }

        [UnityTest]
        public IEnumerator 시간_초과시_게임오버_이벤트가_한번만_발생한다()
        {
            DeliveryRoadNode origin = Node("Origin", Vector3.zero);
            DeliveryRoadNode house = Node("House", new Vector3(0f, 0f, 10f));
            DeliveryRoadNetwork network = Network(new[] { origin, house }, new[] { Segment("Road", origin, house) });
            DeliveryHouse deliveryHouse = House(house);
            Transform participant = Node("Participant", Vector3.zero).transform;

            GiftDeliveryDirector director = BuildDirector(network, new[] { deliveryHouse },
                new[] { participant }, TimeoutSettings(), orderStaggerSeconds: 0f);

            int gameOverCount = 0;
            EGiftDeliveryFailReason? failReason = null;
            director.GameOver += () => gameOverCount++;
            director.OrderFailed += (order, reason) => failReason = reason;

            director.Begin();
            yield return WaitUntil(() => director.Phase == EGiftDeliveryPhase.GameOver, 60);

            Assert.That(gameOverCount, Is.EqualTo(1));
            Assert.That(failReason, Is.EqualTo(EGiftDeliveryFailReason.TimeExpired));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(gameOverCount, Is.EqualTo(1), "GameOver 이후에도 계속 틱이 발생하면 안 된다");
        }

        [UnityTest]
        public IEnumerator 정원_초과시_값어치_낮은_선물이_사라진다()
        {
            DeliveryRoadNode origin = Node("Origin", Vector3.zero);
            DeliveryRoadNode house = Node("House", new Vector3(0f, 0f, 10f));
            DeliveryRoadNetwork network = Network(new[] { origin, house }, new[] { Segment("Road", origin, house) });
            DeliveryHouse deliveryHouse = House(house, capacity: 1);
            Transform participant = Node("Participant", Vector3.zero).transform;

            GiftDeliveryDirector director = BuildDirector(network, new[] { deliveryHouse },
                new[] { participant }, EasySettings(targetLength: 10f), orderStaggerSeconds: 0.05f);
            director.Begin();
            yield return new WaitForFixedUpdate();

            EGiftBoxKind requiredKind = director.ActiveOrders[0].GiftKind;
            Gift lowValue = SpawnGift(house.transform.position, 1, requiredKind);
            Gift highValue = SpawnGift(house.transform.position, 9, requiredKind);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(highValue != null, "값어치 높은 선물은 남아야 한다");
            Assert.IsTrue(lowValue == null, "정원을 초과한 값어치 낮은 선물은 사라져야 한다");
        }

        [UnityTest]
        public IEnumerator 오배달_옵션이_꺼져있으면_실패하지_않는다()
        {
            DeliveryRoadNode origin = Node("Origin", Vector3.zero);
            DeliveryRoadNode nodeNear = Node("Near", new Vector3(0f, 0f, 10f));
            DeliveryRoadNode nodeFar = Node("Far", new Vector3(0f, 0f, 20f));
            DeliveryRoadNetwork network = Network(new[] { origin, nodeNear, nodeFar },
                new[] { Segment("OriginNear", origin, nodeNear), Segment("OriginFar", origin, nodeFar) });
            DeliveryHouse houseNear = House(nodeNear);
            DeliveryHouse houseFar = House(nodeFar);
            Transform participant = Node("Participant", Vector3.zero).transform;

            GiftDeliveryDirector director = BuildDirector(network, new[] { houseNear, houseFar },
                new[] { participant }, EasySettings(targetLength: 10f), orderStaggerSeconds: 0.05f,
                wrongHouseFails: false);
            director.Begin();
            yield return new WaitForFixedUpdate();

            int wrongHouseIndex = 1 - director.ActiveOrders[0].HouseIndex;
            SpawnGift(wrongHouseIndex == 0 ? nodeNear.transform.position : nodeFar.transform.position, 1);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(director.Phase, Is.EqualTo(EGiftDeliveryPhase.Running));
        }

        [UnityTest]
        public IEnumerator 오배달_옵션이_켜지면_실패한다()
        {
            DeliveryRoadNode origin = Node("Origin", Vector3.zero);
            DeliveryRoadNode nodeNear = Node("Near", new Vector3(0f, 0f, 10f));
            DeliveryRoadNode nodeFar = Node("Far", new Vector3(0f, 0f, 20f));
            DeliveryRoadNetwork network = Network(new[] { origin, nodeNear, nodeFar },
                new[] { Segment("OriginNear", origin, nodeNear), Segment("OriginFar", origin, nodeFar) });
            DeliveryHouse houseNear = House(nodeNear);
            DeliveryHouse houseFar = House(nodeFar);
            Transform participant = Node("Participant", Vector3.zero).transform;

            GiftDeliveryDirector director = BuildDirector(network, new[] { houseNear, houseFar },
                new[] { participant }, EasySettings(targetLength: 10f), orderStaggerSeconds: 0.05f,
                wrongHouseFails: true);
            director.Begin();
            yield return new WaitForFixedUpdate();

            int wrongHouseIndex = 1 - director.ActiveOrders[0].HouseIndex;
            SpawnGift(wrongHouseIndex == 0 ? nodeNear.transform.position : nodeFar.transform.position, 1);
            yield return WaitUntil(() => director.Phase == EGiftDeliveryPhase.GameOver, 60);

            Assert.That(director.Phase, Is.EqualTo(EGiftDeliveryPhase.GameOver));
        }

        [UnityTest]
        public IEnumerator 참가자_둘이면_동시_주문_두건이_서로_다른_집을_잡는다()
        {
            DeliveryRoadNode originA = Node("OriginA", Vector3.zero);
            DeliveryRoadNode houseNodeA = Node("HouseA", new Vector3(0f, 0f, 5f));
            DeliveryRoadNode originB = Node("OriginB", new Vector3(100f, 0f, 0f));
            DeliveryRoadNode houseNodeB = Node("HouseB", new Vector3(100f, 0f, 5f));
            DeliveryRoadNetwork network = Network(new[] { originA, houseNodeA, originB, houseNodeB },
                new[] { Segment("RoadA", originA, houseNodeA), Segment("RoadB", originB, houseNodeB) });
            DeliveryHouse houseA = House(houseNodeA);
            DeliveryHouse houseB = House(houseNodeB);
            Transform participantA = Node("ParticipantA", Vector3.zero).transform;
            Transform participantB = Node("ParticipantB", new Vector3(100f, 0f, 0f)).transform;

            GiftDeliveryDirector director = BuildDirector(network, new[] { houseA, houseB },
                new[] { participantA, participantB }, EasySettings(targetLength: 5f), orderStaggerSeconds: 0.05f);
            director.Begin();
            yield return new WaitForFixedUpdate();

            Assert.That(director.MaxConcurrentOrders, Is.EqualTo(2));
            Assert.That(director.ActiveOrders.Count, Is.EqualTo(2));
            Assert.That(director.ActiveOrders[0].HouseIndex, Is.Not.EqualTo(director.ActiveOrders[1].HouseIndex));
        }

        [UnityTest]
        public IEnumerator 동시_주문_중_하나만_시간초과해도_게임오버다()
        {
            DeliveryRoadNode originA = Node("OriginA", Vector3.zero);
            DeliveryRoadNode houseNodeA = Node("HouseA", new Vector3(0f, 0f, 5f));
            DeliveryRoadNode originB = Node("OriginB", new Vector3(100f, 0f, 0f));
            DeliveryRoadNode houseNodeB = Node("HouseB", new Vector3(100f, 0f, 5f));
            DeliveryRoadNetwork network = Network(new[] { originA, houseNodeA, originB, houseNodeB },
                new[] { Segment("RoadA", originA, houseNodeA), Segment("RoadB", originB, houseNodeB) });
            DeliveryHouse houseA = House(houseNodeA);
            DeliveryHouse houseB = House(houseNodeB);
            Transform participantA = Node("ParticipantA", Vector3.zero).transform;
            Transform participantB = Node("ParticipantB", new Vector3(100f, 0f, 0f)).transform;

            GiftDeliveryDirector director = BuildDirector(network, new[] { houseA, houseB },
                new[] { participantA, participantB }, TimeoutSettings(), orderStaggerSeconds: 0f);

            int gameOverCount = 0;
            director.GameOver += () => gameOverCount++;

            director.Begin();
            yield return WaitUntil(() => director.Phase == EGiftDeliveryPhase.GameOver, 60);

            Assert.That(gameOverCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator 도움_예고가_끝난_뒤에만_주문과_제한시간이_시작된다()
        {
            DeliveryRoadNode origin = Node("Origin", Vector3.zero);
            DeliveryRoadNode houseNode = Node("House", new Vector3(0f, 0f, 10f));
            DeliveryRoadNetwork network = Network(new[] { origin, houseNode },
                new[] { Segment("Road", origin, houseNode) });
            DeliveryHouse house = House(houseNode);
            Transform participant = Node("Participant", Vector3.zero).transform;

            GiftDeliveryDirector director = BuildDirector(network, new[] { house }, new[] { participant },
                EasySettings(targetLength: 10f), orderStaggerSeconds: 0f, announcementSeconds: 0.06f);

            var eventSequence = new List<string>();
            GiftDeliveryOrder announcedOrder = null;
            GiftDeliveryOrder startedOrder = null;
            director.OrderAnnounced += order =>
            {
                announcedOrder = order;
                eventSequence.Add("announced");
            };
            director.OrderStarted += order =>
            {
                startedOrder = order;
                eventSequence.Add("started");
            };

            director.Begin();
            yield return new WaitForFixedUpdate();

            Assert.That(announcedOrder, Is.Not.Null);
            Assert.That(director.PendingAnnouncementCount, Is.EqualTo(1));
            Assert.That(director.ActiveOrders, Is.Empty, "HELP 표시 중에는 HUD/지붕이 사용하는 활성 주문이 없어야 한다");
            float untouchedTimeLimit = announcedOrder.RemainingSeconds;

            yield return WaitUntil(() => director.ActiveOrders.Count == 1, 20);

            Assert.That(startedOrder, Is.SameAs(announcedOrder));
            Assert.That(eventSequence, Is.EqualTo(new[] { "announced", "started" }));
            Assert.That(startedOrder.RemainingSeconds, Is.LessThanOrEqualTo(untouchedTimeLimit));
            Assert.That(startedOrder.RemainingSeconds, Is.GreaterThan(untouchedTimeLimit - 0.03f),
                "예고 시간 동안에는 주문 제한시간이 줄면 안 된다");
        }

        [UnityTest]
        public IEnumerator 도움_문구는_세초_유지후_페이드되고_한초_공백뒤_다음_문구가_나온다()
        {
            Transform anchor = Child("HelpAnchor").transform;
            GiftDeliveryHouseHelpEffect effect = Child("HelpEffect")
                .AddComponent<GiftDeliveryHouseHelpEffect>();
            typeof(GiftDeliveryHouseHelpEffect).GetMethod("Configure").Invoke(effect, new object[]
            {
                anchor, Color.yellow, null, Vector3.zero, 1f, null, null
            });

            CanvasGroup primary = GetHelpGroup(effect, "HelpCard_Primary");
            CanvasGroup secondary = GetHelpGroup(effect, "HelpCard_Secondary");

            EvaluateHelpAt(effect, 1f);
            Assert.That(primary.alpha, Is.EqualTo(1f).Within(0.01f));
            Assert.That(secondary.alpha, Is.Zero);

            EvaluateHelpAt(effect, 3.7f);
            Assert.That(primary.alpha, Is.InRange(0.05f, 0.95f), "3초 유지 뒤 첫 문구가 페이드되어야 한다");
            Assert.That(secondary.alpha, Is.Zero, "첫 문구 페이드 중 다음 문구가 겹치면 안 된다");

            EvaluateHelpAt(effect, 4.4f);
            Assert.That(primary.alpha, Is.Zero);
            Assert.That(secondary.alpha, Is.Zero, "두 문구 사이에 1초 공백이 있어야 한다");

            EvaluateHelpAt(effect, 5.8f);
            Assert.That(primary.alpha, Is.Zero);
            Assert.That(secondary.alpha, Is.EqualTo(1f).Within(0.01f));

            EvaluateHelpAt(effect, 9.3f);
            Assert.That(primary.alpha, Is.Zero);
            Assert.That(secondary.alpha, Is.Zero, "두 번째 문구 뒤에도 같은 공백을 유지해야 한다");

            yield return null;
        }

        private static GiftDeliveryDifficultySettings EasySettings(float targetLength) => new GiftDeliveryDifficultySettings
        {
            StartRouteLengthM = targetLength,
            RouteLengthPerOrderM = 0f,
            MaxRouteLengthM = targetLength,
            StartTimeSlackMultiplier = 100f,
            TimeSlackDecayPerOrder = 0f,
            MinTimeSlackMultiplier = 100f,
            AssumedSpeedMps = 1f,
            MinTimeLimitSeconds = 100f,
            StartGiftCount = 1,
            OrdersPerGiftCountStep = 1,
            MaxGiftCount = 1,
            StartRequiredValue = 0,
            RequiredValuePerOrder = 0,
            MaxRequiredValue = 0
        };

        private static GiftDeliveryDifficultySettings TimeoutSettings() => new GiftDeliveryDifficultySettings
        {
            StartRouteLengthM = 10f,
            RouteLengthPerOrderM = 0f,
            MaxRouteLengthM = 10f,
            StartTimeSlackMultiplier = 0f,
            TimeSlackDecayPerOrder = 0f,
            MinTimeSlackMultiplier = 0f,
            AssumedSpeedMps = 1f,
            MinTimeLimitSeconds = 0.05f,
            StartGiftCount = 1,
            OrdersPerGiftCountStep = 1,
            MaxGiftCount = 1,
            StartRequiredValue = 0,
            RequiredValuePerOrder = 0,
            MaxRequiredValue = 0
        };

        private GiftDeliveryDirector BuildDirector(DeliveryRoadNetwork network, IReadOnlyList<DeliveryHouse> houses,
                                                    IReadOnlyList<Transform> participants,
                                                    GiftDeliveryDifficultySettings difficulty,
                                                    float orderStaggerSeconds, bool wrongHouseFails = false,
                                                    float announcementSeconds = 0f)
        {
            GameObject gameObject = Child("Director");
            GiftDeliveryDirector director = gameObject.AddComponent<GiftDeliveryDirector>();
            director.Configure(network, houses);
            director.SetParticipants(participants);
            SetPrivate(director, "_difficulty", difficulty);
            SetPrivate(director, "_orderStaggerSeconds", orderStaggerSeconds);
            SetPrivate(director, "_announcementSeconds", announcementSeconds);
            SetPrivate(director, "_randomSeed", 73421);
            SetPrivate(director, "_wrongHouseFails", wrongHouseFails);
            return director;
        }

        private static void SetPrivate(object target, string field, object value)
        {
            typeof(GiftDeliveryDirector)
                .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(target, value);
        }

        private static CanvasGroup GetHelpGroup(GiftDeliveryHouseHelpEffect effect, string cardName)
        {
            Transform card = effect.transform.Find($"HelpWorldCanvas/{cardName}");
            Assert.That(card, Is.Not.Null, $"{cardName}을 찾을 수 없다");
            return card.GetComponent<CanvasGroup>();
        }

        private static void EvaluateHelpAt(GiftDeliveryHouseHelpEffect effect, float time)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(GiftDeliveryHouseHelpEffect).GetField("_animationTime", flags).SetValue(effect, time);
            typeof(GiftDeliveryHouseHelpEffect).GetMethod("UpdateContinuousAnimation", flags).Invoke(effect, null);
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition, int maxFixedUpdates)
        {
            for (int i = 0; i < maxFixedUpdates && !condition(); i++)
                yield return new WaitForFixedUpdate();
        }

        private Gift SpawnGift(Vector3 position, int value, EGiftBoxKind? kind = null)
        {
            GameObject gameObject = Child("Gift");
            gameObject.transform.position = position;
            Gift gift = gameObject.AddComponent<Gift>();
            gift.SetValue(value);
            // 배달이 성립하려면 주문이 요구하는 종류여야 한다. 기본값에 기대면 주문 색 순환이
            // 바뀔 때마다 조용히 깨진다.
            if (kind.HasValue) gift.SetKind(kind.Value);
            return gift;
        }

        private DeliveryRoadNode Node(string id, Vector3 position)
        {
            GameObject gameObject = Child(id);
            gameObject.transform.position = position;
            DeliveryRoadNode node = gameObject.AddComponent<DeliveryRoadNode>();
            node.Configure(id);
            return node;
        }

        private DeliveryRoadSegment Segment(string name, DeliveryRoadNode start, DeliveryRoadNode end)
        {
            DeliveryRoadSegment segment = Child(name).AddComponent<DeliveryRoadSegment>();
            segment.Configure(start, end, null, 6f, 0f, 0.25f);
            return segment;
        }

        private DeliveryHouse House(DeliveryRoadNode node, int capacity = 3)
        {
            GameObject gameObject = Child("House");
            gameObject.transform.position = node.transform.position;
            GiftDropZone zone = gameObject.AddComponent<GiftDropZone>();
            SetZoneCapacity(zone, capacity);
            DeliveryHouse house = gameObject.AddComponent<DeliveryHouse>();
            house.Configure(node, null, zone);
            return house;
        }

        private static void SetZoneCapacity(GiftDropZone zone, int capacity)
        {
            typeof(GiftDropZone)
                .GetField("_capacity", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(zone, capacity);
        }

        private DeliveryRoadNetwork Network(IReadOnlyList<DeliveryRoadNode> nodes,
                                            IReadOnlyList<DeliveryRoadSegment> segments)
        {
            DeliveryRoadNetwork network = Child("Network").AddComponent<DeliveryRoadNetwork>();
            network.Configure(nodes, segments, new DeliveryFactory[0]);
            return network;
        }

        private GameObject Child(string name)
        {
            var gameObject = new GameObject("__TEST__" + name);
            gameObject.transform.SetParent(_root.transform);
            return gameObject;
        }
    }
}
