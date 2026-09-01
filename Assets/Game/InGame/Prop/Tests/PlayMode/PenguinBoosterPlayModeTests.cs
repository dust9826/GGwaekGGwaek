using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class PenguinBoosterPlayModeTests
    {
        [UnityTest]
        public IEnumerator BoostReceiver_AppliesAndRestoresLocomotionMultiplier()
        {
            GameObject penguin = new GameObject("TestPenguin");
            penguin.AddComponent<Rigidbody>().useGravity = false;
            PenguinLocomotion locomotion = penguin.AddComponent<PenguinLocomotion>();
            PenguinBoostReceiver receiver = penguin.AddComponent<PenguinBoostReceiver>();

            yield return null;
            receiver.Activate(null, 1.6f, 0.1f);

            Assert.That(receiver.IsBoosted, Is.True);
            Assert.That(locomotion.SpeedBoostMultiplier, Is.EqualTo(1.6f).Within(0.001f));

            yield return new WaitForSeconds(0.15f);

            Assert.That(receiver.IsBoosted, Is.False);
            Assert.That(locomotion.SpeedBoostMultiplier, Is.EqualTo(1f).Within(0.001f));
            Object.Destroy(penguin);
        }

        [UnityTest]
        public IEnumerator Pickup_CollectsOnlyOnceWhileUnavailable()
        {
            GameObject penguin = new GameObject("TestPenguin");
            penguin.AddComponent<Rigidbody>().useGravity = false;
            PenguinLocomotion locomotion = penguin.AddComponent<PenguinLocomotion>();

            GameObject pickupObject = new GameObject("TestBooster");
            pickupObject.AddComponent<SphereCollider>().isTrigger = true;
            PenguinBoosterPickup pickup = pickupObject.AddComponent<PenguinBoosterPickup>();

            yield return null;

            Assert.That(pickup.TryCollect(locomotion), Is.True);
            Assert.That(pickup.TryCollect(locomotion), Is.False);
            Assert.That(pickup.IsAvailable, Is.False);
            Assert.That(locomotion.SpeedBoostMultiplier, Is.EqualTo(1.6f).Within(0.001f));

            Object.Destroy(pickupObject);
            Object.Destroy(penguin);
        }
    }
}
