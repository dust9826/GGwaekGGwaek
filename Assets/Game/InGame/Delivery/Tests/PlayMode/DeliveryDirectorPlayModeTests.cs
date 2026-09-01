using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class DeliveryDirectorPlayModeTests
    {
        private static readonly MethodInfo GenerateRandomRequestMethod =
            typeof(DeliveryDirector).GetMethod("GenerateRandomRequest",
                BindingFlags.NonPublic | BindingFlags.Instance);

        private GameObject _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("__TEST__DeliveryDirectorPlayMode");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 시드_0은_매_판마다_다른_시드를_뽑는다()
        {
            DeliveryDirector first = BuildDirector(0);
            yield return null;
            DeliveryDirector second = BuildDirector(0);
            yield return null;

            // System.Guid 충돌 확률은 사실상 0이다 — 매 Play 마다 다른 시드로 뽑힌다는 것을
            // 이 정도 확실성으로 확인한다.
            Assert.AreNotEqual(0, first.ActiveRandomSeed, "0이 그대로 쓰이면 고정 시드로 되돌아간 것이다");
            Assert.AreNotEqual(0, second.ActiveRandomSeed);
            Assert.AreNotEqual(first.ActiveRandomSeed, second.ActiveRandomSeed,
                               "매 Play 마다 다른 시드가 뽑혀야 한다");
        }

        [UnityTest]
        public IEnumerator 고정_시드는_같은_의뢰_순서를_재현한다()
        {
            const int fixedSeed = 20260815;
            DeliveryDirector first = BuildDirector(fixedSeed);
            DeliveryFactory[] firstFactories = BuildNetworkFactories(first);
            yield return null;

            DeliveryDirector second = BuildDirector(fixedSeed);
            DeliveryFactory[] secondFactories = BuildNetworkFactories(second);
            yield return null;

            Assert.AreEqual(fixedSeed, first.ActiveRandomSeed, "0이 아닌 값은 그대로 쓰여야 재현이 된다");
            Assert.AreEqual(fixedSeed, second.ActiveRandomSeed);

            for (int i = 0; i < 6; i++)
            {
                GenerateRandomRequestMethod.Invoke(first, null);
                GenerateRandomRequestMethod.Invoke(second, null);

                DeliveryRequest a = first.Requests[first.Requests.Count - 1];
                DeliveryRequest b = second.Requests[second.Requests.Count - 1];
                Assert.AreEqual(IndexOf(firstFactories, a.Stops[0]), IndexOf(secondFactories, b.Stops[0]),
                                $"{i}번째 의뢰의 출발지가 같은 시드에서 달라졌다");
                Assert.AreEqual(IndexOf(firstFactories, a.Stops[1]), IndexOf(secondFactories, b.Stops[1]),
                                $"{i}번째 의뢰의 도착지가 같은 시드에서 달라졌다");
            }
        }

        private static int IndexOf(DeliveryFactory[] factories, DeliveryFactory target)
        {
            for (int index = 0; index < factories.Length; index++)
                if (factories[index] == target) return index;
            return -1;
        }

        /// <summary>
        /// <c>_randomSeed</c> 는 <c>Awake</c> 에서 읽히고, <c>AddComponent</c> 는 오브젝트가 활성인
        /// 동안 <c>Awake</c> 를 즉시(동기로) 부른다 — 붙인 뒤에 리플렉션으로 값을 넣으면 이미 늦다.
        /// 그래서 **비활성 상태로 만들어 붙이고, 값을 넣은 뒤에 활성화**해서 그 시점에 Awake 가
        /// 원하는 시드로 돌게 한다.
        /// </summary>
        private DeliveryDirector BuildDirector(int seed)
        {
            var gameObject = new GameObject($"__TEST__Director_{seed}_{_root.transform.childCount}");
            gameObject.transform.SetParent(_root.transform);
            gameObject.SetActive(false);

            DeliveryDirector director = gameObject.AddComponent<DeliveryDirector>();
            typeof(DeliveryDirector)
                .GetField("_randomSeed", BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(director, seed);

            gameObject.SetActive(true);
            return director;
        }

        /// <summary>노드 넷을 사슬로 잇고 각 노드에 공장을 하나씩 둔다 — 임의의 두 곳이 항상 서로 갈 수 있다.</summary>
        private DeliveryFactory[] BuildNetworkFactories(DeliveryDirector director)
        {
            string prefix = director.name;
            var nodes = new DeliveryRoadNode[4];
            var factories = new DeliveryFactory[4];
            for (int i = 0; i < nodes.Length; i++)
            {
                DeliveryRoadNode node = Child($"{prefix}_Node{i}").AddComponent<DeliveryRoadNode>();
                node.Configure($"{prefix}_N{i}");
                node.transform.position = new Vector3(i * 10f, 0f, 0f);
                nodes[i] = node;

                DeliveryFactory factory = Child($"{prefix}_Factory{i}").AddComponent<DeliveryFactory>();
                factory.Configure(node);
                factories[i] = factory;
            }

            var segments = new DeliveryRoadSegment[nodes.Length - 1];
            for (int i = 0; i < segments.Length; i++)
            {
                DeliveryRoadSegment segment = Child($"{prefix}_Road{i}").AddComponent<DeliveryRoadSegment>();
                segment.Configure(nodes[i], nodes[i + 1], null, 6f, 0f, 0.25f);
                segments[i] = segment;
            }

            DeliveryRoadNetwork network = Child($"{prefix}_Network").AddComponent<DeliveryRoadNetwork>();
            network.Configure(nodes, segments, factories);

            director.Configure(network, null, null, null);
            return factories;
        }

        private GameObject Child(string name)
        {
            var gameObject = new GameObject("__TEST__" + name);
            gameObject.transform.SetParent(_root.transform);
            return gameObject;
        }
    }
}
