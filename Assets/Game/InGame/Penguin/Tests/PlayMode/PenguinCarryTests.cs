using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class PenguinCarryTests
    {
        private GameObject _ground;
        private GameObject _penguin;
        private GameObject _giftObject;
        private GameObject _stageObject;
        private GameObject _snowballObject;

        [SetUp]
        public void SetUp() => Time.captureDeltaTime = 1f / 60f;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            if (_giftObject != null) Object.DestroyImmediate(_giftObject);
            if (_snowballObject != null) Object.DestroyImmediate(_snowballObject);
            if (_stageObject != null) Object.DestroyImmediate(_stageObject);
            if (_penguin != null) Object.DestroyImmediate(_penguin);
            if (_ground != null) Object.DestroyImmediate(_ground);
        }

        [UnityTest]
        public IEnumerator F를_누르면_선물_앞으로_걸어가_등을_보인_뒤_운반한다()
        {
            CreateGround();
            CreatePenguin();
            Gift gift = CreateGift(new Vector3(0f, 0.25f, 1.7f), 6f);

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinCarry carry = _penguin.GetComponent<PenguinCarry>();
            PenguinControlState state = _penguin.GetComponent<PenguinControlState>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();
            Rigidbody playerBody = _penguin.GetComponent<Rigidbody>();
            float baseMass = playerBody.mass;

            Assert.IsNotNull(carry, "PF_Penguin에 PenguinCarry가 연결돼야 한다");
            Physics.SyncTransforms();
            yield return null;

            SetCarry(input, true);
            yield return new WaitForFixedUpdate();
            SetCarry(input, false);

            Assert.AreEqual(EPenguinControlState.CarryApproach, state.Current);
            Assert.AreSame(gift, carry.Cargo);
            Assert.IsFalse(locomotion.UsesSlidingLocomotion,
                "자동 접근 중에는 걷기 자세를 유지해야 한다");

            for (int i = 0; i < 220 && !carry.IsHolding; i++)
                yield return new WaitForFixedUpdate();

            Assert.IsTrue(carry.IsHolding, "적재 동작 뒤 선물이 등에 안착해야 한다");
            Assert.IsTrue(locomotion.UsesSlidingLocomotion,
                "운반이 시작된 뒤에는 슬라이딩 물리를 사용해야 한다");
            Assert.IsTrue(gift.IsCarried);
            Assert.That(playerBody.mass, Is.EqualTo(baseMass + 6f).Within(0.001f));
            Assert.Less(HorizontalSpeed(playerBody), 0.15f,
                "적재 완료 자체가 플레이어를 밀어 움직이면 안 된다");
            Collider carryProxy = null;
            foreach (Collider candidate in _penguin.transform.Find("CarryCollisionProxy")
                         .GetComponents<Collider>())
            {
                if (candidate.enabled) carryProxy = candidate;
            }
            Assert.IsNotNull(carryProxy);
            Assert.AreSame(_penguin.GetComponent<CapsuleCollider>().sharedMaterial,
                carryProxy.sharedMaterial,
                "운반 충돌 프록시도 슬라이딩 캡슐과 같은 무마찰 재질을 써야 한다");
            Vector3 away = _penguin.transform.position - gift.transform.position;
            away.y = 0f;
            Assert.Greater(Vector3.Dot(_penguin.transform.forward, away.normalized), 0.95f,
                "장착 직전에는 선물이 등 뒤에 오도록 바깥을 바라봐야 한다");

            playerBody.linearVelocity = Vector3.zero;
            SetSprintHeld(input, true);
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
            SetSprintHeld(input, false);
            Assert.Greater(HorizontalSpeed(playerBody), 0.5f,
                "가벼운 선물 운반 중에는 정지 상태에서 Shift 발차기로 출발해야 한다");

            SetCarry(input, true);
            yield return new WaitForFixedUpdate();
            SetCarry(input, false);
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();

            Assert.IsFalse(carry.IsCarrying);
            Assert.IsFalse(gift.IsCarried);
            Assert.AreEqual(EPenguinControlState.Normal, state.Current);
            Assert.That(playerBody.mass, Is.EqualTo(baseMass).Within(0.001f));
            Assert.Less(Vector3.Dot(_penguin.transform.forward,
                    gift.transform.position - _penguin.transform.position), 0f,
                "선물은 플레이어 후방 바닥에 놓여야 한다");
        }

        [UnityTest]
        public IEnumerator 기울어진_선물도_F를_누르면_바닥높이를_유지하며_직립해_운반한다()
        {
            CreateGround();
            CreatePenguin();
            Gift gift = CreateGift(new Vector3(0f, 0.5f, 1.4f), 2f);
            gift.transform.localScale = new Vector3(0.8f, 0.4f, 0.6f);
            gift.transform.rotation = Quaternion.Euler(72f, 28f, 17f);
            Rigidbody giftBody = gift.GetComponent<Rigidbody>();
            giftBody.useGravity = false;
            giftBody.constraints = RigidbodyConstraints.FreezeRotation;
            Physics.SyncTransforms();
            Collider giftCollider = gift.GetComponent<Collider>();
            gift.transform.position += Vector3.up * (0.01f - giftCollider.bounds.min.y);
            Physics.SyncTransforms();

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinCarry carry = _penguin.GetComponent<PenguinCarry>();
            yield return null;
            Physics.SyncTransforms();
            float supportHeight = giftCollider.bounds.min.y;

            SetCarry(input, true);
            yield return new WaitForFixedUpdate();
            SetCarry(input, false);

            Assert.IsTrue(carry.IsApproaching);
            Assert.Greater(Vector3.Dot(gift.transform.up, Vector3.up), 0.99f,
                "기울어진 선물은 접근을 시작할 때 직립해야 한다");
            Assert.That(giftCollider.bounds.min.y, Is.EqualTo(supportHeight).Within(0.02f),
                "직립시키면서 선물이 지면 위로 뜨거나 파고들면 안 된다");

            for (int i = 0; i < 220 && !carry.IsHolding; i++)
                yield return new WaitForFixedUpdate();

            Assert.IsTrue(carry.IsHolding, "기울어진 상태로 생성된 선물도 등에 안착해야 한다");
            Assert.Greater(Vector3.Dot(gift.transform.up, Vector3.up), 0.99f);
        }

        [UnityTest]
        public IEnumerator E는_선물을_운반하지_않고_F_접근은_다시_F로_취소한다()
        {
            CreateGround();
            CreatePenguin();
            Gift gift = CreateGift(new Vector3(0f, 0.25f, 0.7f), 2f);
            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinCarry carry = _penguin.GetComponent<PenguinCarry>();

            Physics.SyncTransforms();
            yield return null;
            SetInteract(input, true);
            yield return new WaitForFixedUpdate();
            SetInteract(input, false);

            Assert.IsFalse(carry.IsCarrying);
            Assert.IsFalse(gift.IsCarried);
            Assert.AreEqual(EPenguinControlState.Normal,
                _penguin.GetComponent<PenguinControlState>().Current);

            SetCarry(input, true);
            yield return new WaitForFixedUpdate();
            SetCarry(input, false);
            Assert.IsTrue(carry.IsApproaching);
            Assert.IsTrue(gift.IsCarried, "접근 중에는 배달 시스템이 선물을 소비하면 안 된다");

            SetCarry(input, true);
            yield return new WaitForFixedUpdate();
            SetCarry(input, false);
            Assert.IsFalse(carry.IsApproaching);
            Assert.IsFalse(gift.IsCarried);
            Assert.IsFalse(gift.GetComponent<Rigidbody>().isKinematic);
            Assert.AreEqual(EPenguinControlState.Normal,
                _penguin.GetComponent<PenguinControlState>().Current);
        }

        [UnityTest]
        public IEnumerator 맨바닥_운반_S_Space는_운반을_유지하며_뒤로_작게_점프한다()
        {
            CreateGround();
            CreatePenguin();

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinControlState state = _penguin.GetComponent<PenguinControlState>();
            Rigidbody body = _penguin.GetComponent<Rigidbody>();
            Assert.IsTrue(state.TryTransitionTo(EPenguinControlState.Carrying));
            Vector3 start = _penguin.transform.position;

            SetMoveInput(input, Vector2.down);
            SetJumpPressed(input, true);
            yield return new WaitForFixedUpdate();
            SetJumpPressed(input, false);
            SetMoveInput(input, Vector2.zero);

            Assert.AreEqual(EPenguinControlState.Carrying, state.Current);
            Assert.Less(body.linearVelocity.z, -0.35f);
            Assert.That(body.linearVelocity.y, Is.InRange(1.5f, 2.2f));

            // 순간 속도가 아니라 한 번의 점프가 만든 실제 이동거리를 검증한다.
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();

            float distance = _penguin.transform.position.z - start.z;
            Assert.That(distance, Is.InRange(-0.8f, -0.4f),
                "운반 점프도 맨바닥 마찰에 즉시 지워지지 않고 실제로 뒤로 이동해야 한다");
        }

        [UnityTest]
        public IEnumerator 운반을_해제하면_루트와_몸통이_즉시_직립으로_복구된다()
        {
            CreateGround();
            CreatePenguin();
            CreateGift(new Vector3(0f, 0.25f, 1.2f), 2f);

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinCarry carry = _penguin.GetComponent<PenguinCarry>();
            Rigidbody body = _penguin.GetComponent<Rigidbody>();
            PenguinBodyMotion bodyMotion = _penguin.GetComponentInChildren<PenguinBodyMotion>(true);
            Physics.SyncTransforms();
            yield return null;

            SetCarry(input, true);
            yield return new WaitForFixedUpdate();
            SetCarry(input, false);
            for (int i = 0; i < 220 && !carry.IsHolding; i++)
                yield return new WaitForFixedUpdate();
            Assert.IsTrue(carry.IsHolding);

            body.rotation = Quaternion.Euler(18f, 35f, -12f);
            body.angularVelocity = new Vector3(2f, 1f, -3f);
            bodyMotion.transform.localPosition = new Vector3(0.1f, 0f, 0.1f);
            bodyMotion.transform.localRotation = Quaternion.Euler(9f, 0f, 15f);

            carry.ForceRelease();

            Assert.Greater(Vector3.Dot(_penguin.transform.up, Vector3.up), 0.999f,
                "운반 해제 뒤 루트의 X/Z 기울기가 남으면 안 된다");
            Assert.That(body.angularVelocity.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(body.angularVelocity.z, Is.EqualTo(0f).Within(0.001f));
            Assert.That(bodyMotion.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.Less(Quaternion.Angle(bodyMotion.transform.localRotation, Quaternion.identity), 0.01f,
                "운반 해제 뒤 BodyPivot 기울기가 남으면 안 된다");
        }

        [UnityTest]
        public IEnumerator 운반중_E는_새_눈덩이를_만들지_않는다()
        {
            CreatePenguin();
            _stageObject = new GameObject("__TEST__CarrySnowStage");
            _stageObject.AddComponent<SnowCpuStage>();
            yield return null;

            PenguinControlState state = _penguin.GetComponent<PenguinControlState>();
            PenguinSnowball snowball = _penguin.GetComponent<PenguinSnowball>();
            Assert.IsTrue(state.TryTransitionTo(EPenguinControlState.Carrying));
            int before = Object.FindObjectsByType<SnowBallCarrier>().Length;

            snowball.Step(Time.fixedDeltaTime, new PenguinMoveInput
            {
                CreateSnowballPressed = true
            });

            _snowballObject = snowball.Held != null ? snowball.Held.gameObject : null;
            int after = Object.FindObjectsByType<SnowBallCarrier>().Length;
            Assert.AreEqual(before, after, "운반 중 E가 눈을 소비해 새 공을 만들면 안 된다");
            Assert.IsNull(snowball.Held);
            Assert.AreEqual(EPenguinControlState.Carrying, state.Current);
        }

        private void CreateGround()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "__TEST__CarryGround";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(12f, 1f, 12f);
        }

        private void CreatePenguin()
        {
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab");
#else
            GameObject prefab = null;
#endif
            Assert.IsNotNull(prefab, "PF_Penguin이 있어야 한다");
            _penguin = Object.Instantiate(prefab, new Vector3(0f, 0.01f, 0f),
                Quaternion.identity);
            _penguin.name = "__TEST__CarryPenguin";
            _penguin.GetComponent<PenguinInputReader>().enabled = false;
        }

        private Gift CreateGift(Vector3 position, float mass)
        {
            _giftObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _giftObject.name = "__TEST__CarryGift";
            _giftObject.transform.position = position;
            _giftObject.transform.localScale = Vector3.one * 0.4f;
            Rigidbody body = _giftObject.AddComponent<Rigidbody>();
            body.mass = mass;
            return _giftObject.AddComponent<Gift>();
        }

        private static void SetInteract(PenguinInputReader input, bool value)
        {
            typeof(PenguinInputReader).GetProperty(
                    nameof(PenguinInputReader.CreateSnowballPressedThisFrame),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(input, value);
        }

        private static void SetCarry(PenguinInputReader input, bool value)
        {
            typeof(PenguinInputReader).GetProperty(
                    nameof(PenguinInputReader.PickupPressedThisFrame),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(input, value);
        }

        private static void SetSprintHeld(PenguinInputReader input, bool value)
        {
            typeof(PenguinInputReader).GetProperty(
                    nameof(PenguinInputReader.SprintHeld),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(input, value);
        }

        private static void SetMoveInput(PenguinInputReader input, Vector2 value)
        {
            typeof(PenguinInputReader).GetProperty(
                    nameof(PenguinInputReader.MoveInput),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(input, value);
        }

        private static void SetJumpPressed(PenguinInputReader input, bool value)
        {
            typeof(PenguinInputReader).GetProperty(
                    nameof(PenguinInputReader.JumpPressedThisFrame),
                    BindingFlags.Instance | BindingFlags.Public)
                .SetValue(input, value);
        }

        private static float HorizontalSpeed(Rigidbody body)
            => new Vector2(body.linearVelocity.x, body.linearVelocity.z).magnitude;
    }
}
