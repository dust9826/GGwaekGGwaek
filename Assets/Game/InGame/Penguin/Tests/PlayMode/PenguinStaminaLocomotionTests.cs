using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    /// <summary>
    /// 체력이 <b>걷기 분기에 실제로 물려 있는지</b>를 본다. 순수 규칙은 EditMode 의
    /// <c>PenguinStaminaStateTests</c> 가 이미 고정하므로, 여기서 볼 것은 배선이다 —
    /// 로코모션이 소모를 먹이고 게이트를 읽는가.
    ///
    /// <para><b>모든 테스트가 <c>IsSliding</c> 이 거짓임을 함께 단언한다.</b> Shift 는 슬라이딩
    /// 진입도 겸하는 과부하 키라, 슬라이딩으로 새 버린 채 "체력이 안 준다" 를 통과시키면 아무것도
    /// 측정하지 않은 테스트가 된다(2026-08-26 에 실제로 그렇게 오진했다).</para>
    /// </summary>
    public sealed class PenguinStaminaLocomotionTests
    {
        private GameObject _penguin;
        private GameObject _ground;

        [TearDown]
        public void TearDown()
        {
            if (_penguin != null) Object.DestroyImmediate(_penguin);
            if (_ground != null) Object.DestroyImmediate(_ground);
        }

        [UnityTest]
        public IEnumerator 달리면_체력이_준다()
        {
            CreateGround();
            yield return CreatePenguin();
            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();

            SetMove(input, Vector2.up);
            SetSprint(input, true);
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();

            Assert.IsFalse(locomotion.IsSliding, "슬라이딩으로 새면 달리기를 잰 것이 아니다");
            Assert.Greater(locomotion.Speed, locomotion.WalkSpeedMps + 0.5f, "달리기 속도가 나와야 한다");
            Assert.Less(locomotion.Stamina01, 1f, "달렸는데 체력이 그대로다 — 소모가 안 물렸다");
        }

        [UnityTest]
        public IEnumerator 제자리에서_Shift만_쥐면_체력이_줄지_않는다()
        {
            CreateGround();
            yield return CreatePenguin();
            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();

            SetMove(input, Vector2.zero);
            SetSprint(input, true);
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();

            Assert.AreEqual(1f, locomotion.Stamina01, 0.001f);
        }

        [UnityTest]
        public IEnumerator 체력을_다_쓰면_걷기_속도로_내려온다()
        {
            CreateGround();
            yield return CreatePenguin();
            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();

            // 테스트 안에서 6초를 기다리지 않도록 소모를 빠르게 바꾼다. 규칙은 그대로다.
            SetField(locomotion, "_staminaTuning", new PenguinStaminaTuning(
                sprintSeconds: 0.3f, refillSeconds: 4.5f, refillDelaySeconds: 0.6f, exhaustExit01: 0.3f));

            SetMove(input, Vector2.up);
            SetSprint(input, true);
            for (int i = 0; i < 90; i++) yield return new WaitForFixedUpdate();

            Assert.IsFalse(locomotion.IsSliding, "슬라이딩으로 새면 달리기를 잰 것이 아니다");
            Assert.IsTrue(locomotion.StaminaExhausted, "다 썼으면 탈진이어야 한다");
            Assert.Less(locomotion.Speed, locomotion.WalkSpeedMps + 0.5f,
                "탈진했는데 아직 달리기 속도가 나온다 — 게이트가 안 물렸다");
        }

        [UnityTest]
        public IEnumerator 달리기를_놓으면_체력이_다시_찬다()
        {
            CreateGround();
            yield return CreatePenguin();
            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();

            SetMove(input, Vector2.up);
            SetSprint(input, true);
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();
            float drained = locomotion.Stamina01;
            Assert.Less(drained, 1f);

            SetSprint(input, false);
            // 회복 지연(0.6초)이 지나고도 남을 만큼 돈다.
            for (int i = 0; i < 90; i++) yield return new WaitForFixedUpdate();

            Assert.Greater(locomotion.Stamina01, drained, "쉬는데도 안 찬다");
        }

        private void CreateGround()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "__TEST__StaminaGround";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(60f, 1f, 60f);
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
            _penguin = Object.Instantiate(prefab, new Vector3(0f, 0.01f, 0f), Quaternion.identity);
            _penguin.name = "__TEST__StaminaPenguin";
            _penguin.GetComponent<PenguinInputReader>().enabled = false;
            yield return null;
        }

        private static void SetMove(PenguinInputReader input, Vector2 value)
            => SetProperty(input, nameof(PenguinInputReader.MoveInput), value);

        private static void SetSprint(PenguinInputReader input, bool value)
            => SetProperty(input, nameof(PenguinInputReader.SprintHeld), value);

        private static void SetProperty(object target, string name, object value)
            => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                .SetValue(target, value);

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
    }
}
