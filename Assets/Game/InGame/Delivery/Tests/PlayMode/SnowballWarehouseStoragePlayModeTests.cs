using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class SnowballWarehouseStoragePlayModeTests
    {
        private GameObject _root;
        private SnowballWarehouseStorage _storage;
        private Transform _leftPivot;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("__TEST__SnowballWarehouse");
            _leftPivot = DoorPivot("LeftDoor");
            Transform rightPivot = DoorPivot("RightDoor");
            Transform[] slots = new Transform[8];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new GameObject("Slot_" + i).transform;
                slots[i].SetParent(_root.transform);
                int lane = i % 4;
                bool reserve = i >= 4;
                slots[i].localPosition = new Vector3(lane * 1.6f, reserve ? 1.2f : 0f,
                    reserve ? 0.8f : 0f);
            }

            _storage = _root.AddComponent<SnowballWarehouseStorage>();
            _storage.Configure(
                _leftPivot, _leftPivot.GetComponent<Rigidbody>(),
                rightPivot, rightPivot.GetComponent<Rigidbody>(),
                slots);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 접근한_Rigidbody가_있으면_문이_연속적으로_열리고_나가면_닫힌다()
        {
            GameObject actor = new GameObject("Actor", typeof(Rigidbody), typeof(BoxCollider));
            actor.transform.SetParent(_root.transform);
            Rigidbody actorBody = actor.GetComponent<Rigidbody>();
            actorBody.isKinematic = true;
            Collider actorCollider = actor.GetComponent<Collider>();

            _storage.NotifyTrigger(EWarehouseTriggerKind.Approach, actorCollider, true);
            Quaternion closed = _leftPivot.localRotation;
            for (int i = 0; i < 35; i++) yield return new WaitForFixedUpdate();
            Assert.That(Quaternion.Angle(closed, _leftPivot.localRotation), Is.GreaterThan(80f));

            _storage.NotifyTrigger(EWarehouseTriggerKind.Approach, actorCollider, false);
            for (int i = 0; i < 45; i++) yield return new WaitForFixedUpdate();
            Assert.That(Quaternion.Angle(closed, _leftPivot.localRotation), Is.LessThan(2f));
        }

        [UnityTest]
        public IEnumerator 내려놓은_선물은_빈_선반에_저장되고_다시_들면_슬롯이_비워진다()
        {
            Gift gift = CreateGift("Gift");

            Assert.IsTrue(_storage.TryStoreGift(gift));
            Assert.That(_storage.StoredCount, Is.EqualTo(1));

            gift.SetCarried(true);
            yield return null;
            Assert.That(_storage.StoredCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator 아래_선물을_꺼내면_위_예비_선물이_픽업_칸으로_내려온다()
        {
            Gift pickupGift = CreateGift("PickupGift", EGiftBoxKind.Blue);
            Gift reserveGift = CreateGift("ReserveGift", EGiftBoxKind.Blue);

            Assert.IsTrue(_storage.TryStoreGift(pickupGift));
            Assert.IsTrue(_storage.TryStoreGift(reserveGift));
            Assert.That(reserveGift.GetComponent<Rigidbody>().isKinematic, Is.True);

            pickupGift.SetCarried(true);
            yield return new WaitForSeconds(0.65f);

            Transform pickupSlot = _storage.StorageSlots[0];
            Assert.That(_storage.StoredCount, Is.EqualTo(1));
            Assert.That(reserveGift.transform.position.x, Is.EqualTo(pickupSlot.position.x).Within(0.02f));
            Assert.That(reserveGift.transform.position.z, Is.EqualTo(pickupSlot.position.z).Within(0.02f));
            Assert.That(reserveGift.GetComponent<Rigidbody>().isKinematic, Is.False);
        }

        [UnityTest]
        public IEnumerator 네가지_색상은_각자의_전용_레인에_분류된다()
        {
            Gift red = CreateGift("Red", EGiftBoxKind.Red);
            Gift blue = CreateGift("Blue", EGiftBoxKind.Blue);
            Gift yellow = CreateGift("Yellow", EGiftBoxKind.Yellow);
            Gift green = CreateGift("Green", EGiftBoxKind.Green);

            Assert.IsTrue(_storage.TryStoreGift(red));
            Assert.IsTrue(_storage.TryStoreGift(blue));
            Assert.IsTrue(_storage.TryStoreGift(yellow));
            Assert.IsTrue(_storage.TryStoreGift(green));
            yield return null;

            Assert.That(blue.transform.position.x, Is.EqualTo(_storage.StorageSlots[0].position.x).Within(0.02f));
            Assert.That(green.transform.position.x, Is.EqualTo(_storage.StorageSlots[1].position.x).Within(0.02f));
            Assert.That(yellow.transform.position.x, Is.EqualTo(_storage.StorageSlots[2].position.x).Within(0.02f));
            Assert.That(red.transform.position.x, Is.EqualTo(_storage.StorageSlots[3].position.x).Within(0.02f));
        }

        [UnityTest]
        public IEnumerator 같은_색_레인이_가득차면_다른_색_레인을_침범하지_않는다()
        {
            Assert.IsTrue(_storage.TryStoreGift(CreateGift("BluePickup", EGiftBoxKind.Blue)));
            Assert.IsTrue(_storage.TryStoreGift(CreateGift("BlueReserve", EGiftBoxKind.Blue)));
            Assert.IsFalse(_storage.TryStoreGift(CreateGift("BlueOverflow", EGiftBoxKind.Blue)));
            Assert.That(_storage.StoredCount, Is.EqualTo(2));
            yield return null;
        }

        private Gift CreateGift(string name, EGiftBoxKind kind = EGiftBoxKind.Blue)
        {
            GameObject giftObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            giftObject.name = name;
            giftObject.transform.SetParent(_root.transform);
            Gift gift = giftObject.AddComponent<Gift>();
            giftObject.AddComponent<Rigidbody>();
            gift.SetKind(kind);
            return gift;
        }

        private Transform DoorPivot(string name)
        {
            GameObject door = new GameObject(name, typeof(Rigidbody));
            door.transform.SetParent(_root.transform);
            Rigidbody body = door.GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            return door.transform;
        }
    }
}
