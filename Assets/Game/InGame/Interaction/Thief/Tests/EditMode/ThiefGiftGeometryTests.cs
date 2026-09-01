using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class ThiefGiftGeometryTests
    {
        private GameObject _giftObject;

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_giftObject);
        }

        [Test]
        public void 집기_위치는_선물_표면에서_몸_반경과_여유만큼_떨어진다()
        {
            ThiefGiftGeometry geometry = CreateGeometry(Vector3.zero,
                new Vector3(0.8f, 0.7f, 0.6f));

            Vector3 position = geometry.GrabStandPosition(
                new Vector3(0f, 0f, -3f), 0.3f, 0.25f);

            Assert.That(position.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(position.z, Is.EqualTo(-0.85f).Within(0.001f));
        }

        [Test]
        public void 회전한_선물도_회전된_반폭으로_집기_거리를_계산한다()
        {
            ThiefGiftGeometry geometry = CreateGeometry(Vector3.zero,
                new Vector3(1f, 0.7f, 0.4f));
            _giftObject.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            Vector3 position = geometry.GrabStandPosition(
                new Vector3(0f, 0f, -3f), 0.3f, 0.25f);

            Assert.That(position.z, Is.EqualTo(-1.05f).Within(0.001f));
        }

        [Test]
        public void 목표_중심을_루트_위치로_바꿔도_선물_중심이_유지된다()
        {
            ThiefGiftGeometry geometry = CreateGeometry(new Vector3(0f, 0.4f, 0f),
                new Vector3(0.8f, 0.8f, 0.6f));
            Quaternion rotation = Quaternion.Euler(0f, 35f, 0f);
            Vector3 targetCenter = new Vector3(2f, 1.5f, -3f);

            Vector3 rootPosition = geometry.RootPositionForCenter(targetCenter, rotation);

            Assert.That(Vector3.Distance(geometry.CenterAt(rootPosition, rotation), targetCenter),
                Is.LessThan(0.001f));
        }

        private ThiefGiftGeometry CreateGeometry(Vector3 center, Vector3 size)
        {
            _giftObject = new GameObject("__TEST__GiftGeometry");
            BoxCollider box = _giftObject.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
            Gift gift = _giftObject.AddComponent<Gift>();
            Assert.That(ThiefGiftGeometry.TryCreate(gift, out ThiefGiftGeometry geometry), Is.True);
            return geometry;
        }
    }
}
