using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class GiftSpawnerPlayModeTests
    {
        private GameObject _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("__TEST__GiftSpawner");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Gift[] gifts = Object.FindObjectsByType<Gift>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (Gift gift in gifts)
                if (gift != null && !gift.transform.IsChildOf(_root.transform)) Object.Destroy(gift.gameObject);

            Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 생성한_선물은_플레이어가_밀수있는_동적_Rigidbody를_갖는다()
        {
            var templateObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            templateObject.name = "GiftTemplate";
            templateObject.transform.SetParent(_root.transform);
            Gift template = templateObject.AddComponent<Gift>();
            templateObject.SetActive(false);

            GiftSpawner spawner = _root.AddComponent<GiftSpawner>();
            SetPrivate(spawner, "_giftPrefab", template);
            SetPrivate(spawner, "_radius", 0f);
            SetPrivate(spawner, "_maxAlive", 1);

            yield return null;

            Gift spawned = null;
            foreach (Gift gift in Gift.All)
                if (gift != template) spawned = gift;

            Assert.That(spawned, Is.Not.Null);
            Assert.That(spawned.TryGetComponent(out Rigidbody body), Is.True);
            Assert.IsFalse(body.isKinematic);
            Assert.IsTrue(body.useGravity);
            Assert.That(body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.ContinuousDynamic));
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(target, value);
        }
    }
}
