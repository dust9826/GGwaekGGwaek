using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class ThiefGiftClaimTests
    {
        private GameObject _giftObject;
        private GameObject _firstOwner;
        private GameObject _secondOwner;

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_giftObject);
            Object.DestroyImmediate(_firstOwner);
            Object.DestroyImmediate(_secondOwner);
        }

        [Test]
        public void 한_선물은_동시에_한_운반자만_선점한다()
        {
            Gift gift = CreateObjects();

            Assert.That(gift.TryClaim(_firstOwner), Is.True);
            Assert.That(gift.TryClaim(_secondOwner), Is.False);
            Assert.That(gift.IsCarried, Is.True);
            Assert.That(gift.ReleaseClaim(_secondOwner), Is.False);
            Assert.That(gift.ReleaseClaim(_firstOwner), Is.True);
            Assert.That(gift.TryClaim(_secondOwner), Is.True);
        }

        [Test]
        public void 파괴된_소유자의_선점은_자동으로_비어진다()
        {
            Gift gift = CreateObjects();
            Assert.That(gift.TryClaim(_firstOwner), Is.True);

            Object.DestroyImmediate(_firstOwner);

            Assert.That(gift.IsCarried, Is.False);
            Assert.That(gift.TryClaim(_secondOwner), Is.True);
        }

        private Gift CreateObjects()
        {
            _giftObject = new GameObject("__TEST__Gift");
            _firstOwner = new GameObject("__TEST__OwnerA");
            _secondOwner = new GameObject("__TEST__OwnerB");
            return _giftObject.AddComponent<Gift>();
        }
    }
}
