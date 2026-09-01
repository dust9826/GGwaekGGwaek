using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class PenguinActionTests
    {
        private GameObject _penguin;
        private GameObject _target;

        [TearDown]
        public void TearDown()
        {
            if (_penguin != null) Object.DestroyImmediate(_penguin);
            if (_target != null) Object.DestroyImmediate(_target);
        }

        [UnityTest]
        public IEnumerator 눈덩이가_없으면_좌클릭은_공격이다()
        {
            yield return CreatePenguin();
            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinActions actions = _penguin.GetComponent<PenguinActions>();
            SetProperty(input, nameof(PenguinInputReader.PrimaryActionPressedThisFrame), true);

            yield return new WaitForFixedUpdate();

            Assert.AreEqual(1, actions.AttackCount);
        }

        [UnityTest]
        public IEnumerator 선물을_좌클릭해도_속도증가는_초당_3미터를_넘지_않는다()
        {
            yield return CreatePenguin();
            PenguinActions actions = _penguin.GetComponent<PenguinActions>();

            _target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _target.name = "__TEST__AttackGift";
            Vector3 attackCenter = _penguin.transform.position + Vector3.up * 0.85f
                                                        + _penguin.transform.forward * 0.85f;
            _target.transform.SetPositionAndRotation(attackCenter, Quaternion.identity);
            _target.transform.localScale = Vector3.one * 0.3f;
            Rigidbody giftBody = _target.AddComponent<Rigidbody>();
            giftBody.mass = 2f;
            giftBody.useGravity = false;
            giftBody.constraints = RigidbodyConstraints.FreezeRotation;
            _target.AddComponent<Gift>();
            Physics.SyncTransforms();

            InvokePrivate(actions, "ApplyAttackHit");
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(1, actions.AttackHitCount);
            Assert.That(giftBody.linearVelocity.magnitude, Is.LessThanOrEqualTo(3.05f),
                "가벼운 선물에 일반 공격 충격량을 그대로 주면 너무 멀리 날아간다");
        }

        [UnityTest]
        public IEnumerator 눈덩이_조작_상태에서는_좌클릭_공격을_소비하지_않는다()
        {
            yield return CreatePenguin();
            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinActions actions = _penguin.GetComponent<PenguinActions>();
            _penguin.GetComponent<PenguinSnowball>().enabled = false;
            PenguinControlState state = _penguin.GetComponent<PenguinControlState>();
            Assert.IsTrue(state.TryTransitionTo(EPenguinControlState.SnowballSide));
            SetProperty(input, nameof(PenguinInputReader.PrimaryActionPressedThisFrame), true);

            yield return new WaitForFixedUpdate();

            Assert.AreEqual(0, actions.AttackCount);
        }

        [UnityTest]
        public IEnumerator 큰_외부충격은_회전제약과_조작권을_풀고_착지할_때_Dead를_재생한다()
        {
            yield return CreatePenguin();
            PenguinImpactRelay impact = _penguin.GetComponent<PenguinImpactRelay>();
            PenguinActions actions = _penguin.GetComponent<PenguinActions>();
            Rigidbody body = _penguin.GetComponent<Rigidbody>();

            impact.ReceiveExternalImpulse(Vector3.right * 240f, _penguin.transform.position + Vector3.up);

            Assert.IsTrue(impact.IsHeavyImpactActive);
            Assert.IsFalse(impact.HasLandedFromHeavyImpact);
            Assert.AreEqual(0, actions.HeavyImpactCount);
            Assert.AreEqual(0, actions.DamageCount);
            Assert.AreEqual(RigidbodyConstraints.None, body.constraints);
            Assert.IsFalse(_penguin.GetComponent<PenguinLocomotion>().enabled);
            Assert.IsTrue(_penguin.GetComponent<CapsuleCollider>().enabled);
            Assert.AreEqual(1, _penguin.GetComponentsInChildren<Rigidbody>(true).Length);
            Assert.AreEqual(0, _penguin.GetComponentsInChildren<CharacterJoint>(true).Length);

            yield return null;
        }

        [UnityTest]
        public IEnumerator 큰_충격은_착지_후_Dead_마지막자세를_유지했다가_조작권을_복구한다()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "__TEST__ImpactFloor";
            floor.transform.position = new Vector3(0f, -0.5f, 0f);
            floor.transform.localScale = new Vector3(8f, 1f, 8f);
            _target = floor;

            yield return CreatePenguin();
            PenguinImpactRelay impact = _penguin.GetComponent<PenguinImpactRelay>();
            PenguinActions actions = _penguin.GetComponent<PenguinActions>();
            Rigidbody body = _penguin.GetComponent<Rigidbody>();
            Animator animator = _penguin.GetComponentInChildren<Animator>(true);
            PenguinBodyMotion bodyMotion = _penguin.GetComponentInChildren<PenguinBodyMotion>(true);
            SetField(impact, "_landingBlendSeconds", 0.08f);
            SetField(impact, "_deadPoseHoldSeconds", 0.05f);
            SetField(impact, "_getUpBlendSeconds", 0.05f);

            Vector3 impulse = (Vector3.right + Vector3.up * 0.35f).normalized * 240f;
            impact.ReceiveExternalImpulse(impulse, body.worldCenterOfMass + Vector3.up * 0.5f);
            body.AddForceAtPosition(impulse, body.worldCenterOfMass + Vector3.up * 0.5f,
                ForceMode.Impulse);

            float landingTimeout = Time.time + 3f;
            while (!impact.HasLandedFromHeavyImpact && Time.time < landingTimeout)
                yield return new WaitForFixedUpdate();

            Assert.IsTrue(impact.HasLandedFromHeavyImpact);
            Assert.AreEqual(1, actions.HeavyImpactCount);
            Assert.AreEqual(RigidbodyConstraints.FreezeAll, body.constraints);
            Assert.IsTrue(impact.IsBlendingToDeadPose);
            Assert.AreEqual(1f, animator.speed, 0.001f,
                "착지 블렌딩이 끝나기 전에 Animator를 멈추면 다시 하드 컷이 된다");

            float blendTimeout = Time.time + 1f;
            while (impact.IsBlendingToDeadPose && Time.time < blendTimeout) yield return null;

            AnimatorStateInfo deadPose = animator.GetCurrentAnimatorStateInfo(1);
            Assert.AreEqual(Animator.StringToHash("DeadPose"), deadPose.shortNameHash);
            Assert.AreEqual(1f, animator.speed, 0.001f,
                "DeadPose 상태 자체가 멈춰야 하며 전역 Animator는 Idle을 계속 준비해야 한다");
            Assert.AreEqual(1f, animator.GetLayerWeight(1), 0.001f);
            Assert.That(bodyMotion.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(Quaternion.Angle(bodyMotion.transform.localRotation, Quaternion.identity),
                Is.LessThan(0.1f));

            float recoveryTimeout = Time.time + 3f;
            while (impact.IsHeavyImpactActive && Time.time < recoveryTimeout) yield return null;

            Assert.IsFalse(impact.IsHeavyImpactActive);
            Assert.AreEqual(RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ,
                body.constraints);
            Assert.IsTrue(_penguin.GetComponent<PenguinLocomotion>().enabled);
            Assert.IsTrue(actions.CanAct);
            Assert.AreEqual(1f, animator.speed, 0.001f);
            Assert.AreEqual(0f, animator.GetLayerWeight(1), 0.001f,
                "복구가 끝나면 DeadPose를 가진 Actions 레이어가 완전히 빠져야 한다");
            Assert.AreEqual(Animator.StringToHash("Empty"),
                animator.GetCurrentAnimatorStateInfo(1).shortNameHash);

            actions.PlayDamage();
            Assert.AreEqual(1f, animator.GetLayerWeight(1), 0.001f,
                "복구 후 다음 일반 액션은 Actions 레이어를 다시 켜야 한다");
            Assert.AreEqual(1, actions.DamageCount);
        }

        [UnityTest]
        public IEnumerator 큰_충격의_착지보정은_BodyPivot의_월드자세를_갑자기_바꾸지_않는다()
        {
            yield return CreatePenguin();
            PenguinImpactRelay impact = _penguin.GetComponent<PenguinImpactRelay>();
            PenguinBodyMotion bodyMotion = _penguin.GetComponentInChildren<PenguinBodyMotion>(true);
            Rigidbody body = _penguin.GetComponent<Rigidbody>();

            impact.ReceiveExternalImpulse(Vector3.right * 240f,
                body.worldCenterOfMass + Vector3.up * 0.5f);
            body.position = new Vector3(1f, 2f, -1f);
            body.rotation = Quaternion.Euler(42f, 31f, 18f);
            bodyMotion.transform.localPosition = new Vector3(0.08f, -0.04f, 0.03f);
            bodyMotion.transform.localRotation = Quaternion.Euler(7f, -4f, 13f);
            Vector3 pivotWorldPosition = bodyMotion.transform.position;
            Quaternion pivotWorldRotation = bodyMotion.transform.rotation;

            InvokePrivate(impact, "LandFromHeavyImpact", 0f);

            Assert.That(Vector3.Distance(bodyMotion.transform.position, pivotWorldPosition),
                Is.LessThan(0.001f));
            Assert.That(Quaternion.Angle(bodyMotion.transform.rotation, pivotWorldRotation),
                Is.LessThan(0.1f));
            Assert.IsTrue(impact.IsBlendingToDeadPose);
        }

        [UnityTest]
        public IEnumerator 중간_외부충격은_Damage를_재생한다()
        {
            yield return CreatePenguin();
            PenguinImpactRelay impact = _penguin.GetComponent<PenguinImpactRelay>();
            PenguinActions actions = _penguin.GetComponent<PenguinActions>();

            impact.ReceiveExternalImpulse(Vector3.right * 150f, _penguin.transform.position + Vector3.up);

            Assert.AreEqual(1, actions.DamageCount);
            Assert.AreEqual(0, actions.HeavyImpactCount);

            yield return null;
        }

        private IEnumerator CreatePenguin()
        {
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab");
#else
            GameObject prefab = null;
#endif
            Assert.IsNotNull(prefab);
            _penguin = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            _penguin.name = "__TEST__ActionPenguin";
            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            input.enabled = false;
            yield return null;
        }

        private static void SetProperty(object target, string name, object value)
            => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                .SetValue(target, value);

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);

        private static void InvokePrivate(object target, string name, params object[] arguments)
            => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(target, arguments);
    }
}
