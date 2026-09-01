using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class ThiefImpactTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("__TEST__ThiefImpactRoot");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void 약한_충돌은_무시하고_직접_공격은_한번만_넘어진_뒤_도망친다()
        {
            ThiefActor actor = CreateActor(out ThiefImpactReceiver receiver);

            receiver.ReceiveImpact(new ImpactHit(EImpactCause.PhysicalCollision,
                Vector3.forward * 139f, actor.transform.position));
            Assert.That(actor.IsImpactReacting, Is.False);

            receiver.ReceiveImpact(new ImpactHit(EImpactCause.DirectAttack,
                Vector3.forward, actor.transform.position));
            Assert.That(actor.ImpactPhase, Is.EqualTo(EThiefImpactPhase.Falling));
            Assert.That(actor.TryBeginImpact(new ImpactHit(EImpactCause.DirectAttack,
                Vector3.right, actor.transform.position)), Is.False);

            Assert.That(actor.TickImpactReaction(10f), Is.EqualTo(EThiefTaskResult.Success));
            Assert.That(actor.IsImpactReacting, Is.False);
            Assert.That(actor.IsEscaping, Is.True);
            Assert.That(actor.CurrentAction, Is.EqualTo(EThiefAction.Escaping));
        }

        [Test]
        public void 운반_중_공격은_선물의_선점과_물리를_복구한다()
        {
            ThiefActor actor = CreateActor(out ThiefImpactReceiver receiver);
            GameObject giftObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            giftObject.name = "__TEST__DroppedGift";
            giftObject.transform.SetParent(_root.transform);
            Rigidbody giftBody = giftObject.AddComponent<Rigidbody>();
            Gift gift = giftObject.AddComponent<Gift>();
            Collider giftCollider = giftObject.GetComponent<Collider>();
            Assert.That(gift.TryClaim(actor), Is.True);

            giftBody.isKinematic = true;
            giftBody.useGravity = false;
            giftCollider.enabled = false;
            SetPrivate(actor, "_claimedGift", gift);
            SetPrivate(actor, "_giftBody", giftBody);
            SetPrivate(actor, "_giftColliders", new[] { giftCollider });
            SetPrivate(actor, "_giftColliderStates", new[] { true });
            SetPrivate(actor, "_giftWasKinematic", false);
            SetPrivate(actor, "_giftUsedGravity", true);
            SetPrivate(actor, "_giftAttached", true);
            SetPrivate(actor, "_hasCargo", true);

            receiver.ReceiveImpact(new ImpactHit(EImpactCause.DirectAttack,
                Vector3.forward * 105f, actor.transform.position));

            Assert.That(actor.HasCargo, Is.False);
            Assert.That(actor.HasClaimedGift, Is.False);
            Assert.That(gift.ClaimOwner, Is.Null);
            Assert.That(giftCollider.enabled, Is.True);
            Assert.That(giftBody.isKinematic, Is.False);
            Assert.That(giftBody.useGravity, Is.True);
        }

        private ThiefActor CreateActor(out ThiefImpactReceiver receiver)
        {
            GameObject thiefObject = new GameObject("__TEST__Thief");
            thiefObject.transform.SetParent(_root.transform);
            Rigidbody body = thiefObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            ThiefActor actor = thiefObject.AddComponent<ThiefActor>();
            ThiefMovement movement = thiefObject.GetComponent<ThiefMovement>();
            receiver = thiefObject.AddComponent<ThiefImpactReceiver>();
            SetPrivate(actor, "_movement", movement);
            SetPrivate(receiver, "_actor", actor);

            GameObject siteObject = new GameObject("__TEST__RaidSite");
            siteObject.transform.SetParent(_root.transform);
            BoxCollider volume = siteObject.AddComponent<BoxCollider>();
            volume.isTrigger = true;
            volume.size = new Vector3(10f, 3f, 10f);
            ThiefRaidSite site = siteObject.AddComponent<ThiefRaidSite>();
            Transform approach = new GameObject("__TEST__Approach").transform;
            approach.SetParent(siteObject.transform, false);
            site.Configure(volume, new[] { approach });
            actor.Initialize(site, EGiftBoxKind.Red, new Vector3(0f, 0f, -6f));
            return actor;
        }

        private static void SetPrivate(Object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }
    }
}
