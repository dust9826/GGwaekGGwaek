using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    /// <summary>
    /// 착지 즉시 재점프하던 연타를 막는 쿨타임의 테스트. <b>접지 여부와 분리해서 본다</b> —
    /// 점프 직후에는 어차피 공중이라 접지 조건만으로도 거절되므로, 쿨타임을 길게 잡고
    /// <b>다시 착지한 뒤</b>에 거절되는지를 확인해야 쿨타임을 검증한 것이 된다.
    /// </summary>
    public sealed class PenguinJumpCooldownTests
    {
        private const float LongCooldownSeconds = 10f;

        private GameObject _penguin;
        private GameObject _ground;

        [TearDown]
        public void TearDown()
        {
            if (_penguin != null) Object.DestroyImmediate(_penguin);
            if (_ground != null) Object.DestroyImmediate(_ground);
        }

        [UnityTest]
        public IEnumerator 착지한_뒤에도_쿨타임_동안은_다시_점프하지_않는다()
        {
            CreateGround();
            yield return CreatePenguin();

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();
            SetField(locomotion, "_jumpCooldownSeconds", LongCooldownSeconds);

            int jumps = 0;
            System.Action onJumped = () => jumps++;
            locomotion.Jumped += onJumped;

            try
            {
                yield return WaitUntilGrounded(locomotion);

                yield return PressJump(input);
                Assert.AreEqual(1, jumps, "첫 점프는 나가야 한다");

                yield return WaitUntilGrounded(locomotion);

                yield return PressJump(input);
                Assert.AreEqual(1, jumps, "착지했어도 쿨타임 안이면 거절이다");

                // 쿨타임만 지워 본다. 이것으로 다시 점프가 나가면 막고 있던 것이 접지가 아니라
                // 쿨타임이었음이 확정된다.
                SetField(locomotion, "_jumpCooldownRemaining", 0f);

                yield return PressJump(input);
                Assert.AreEqual(2, jumps, "쿨타임이 끝나면 다시 나가야 한다");
            }
            finally
            {
                locomotion.Jumped -= onJumped;
            }
        }

        // 슬라이딩 점프가 자기 타이머를 따로 가지면 슬라이딩으로 우회해 연타할 수 있다.
        [UnityTest]
        public IEnumerator 슬라이딩_점프도_같은_쿨타임을_공유한다()
        {
            CreateGround();
            yield return CreatePenguin();

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();
            SetField(locomotion, "_jumpCooldownSeconds", LongCooldownSeconds);

            int jumps = 0;
            System.Action onJumped = () => jumps++;
            locomotion.Jumped += onJumped;

            try
            {
                yield return WaitUntilGrounded(locomotion);
                yield return PressJump(input);
                Assert.AreEqual(1, jumps);

                // 달리며 슬라이딩으로 들어간 뒤 다시 눌러 본다.
                SetProperty(input, nameof(PenguinInputReader.MoveInput), Vector2.up);
                SetProperty(input, nameof(PenguinInputReader.SprintHeld), true);
                yield return WaitUntilGrounded(locomotion);
                for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();

                yield return PressJump(input);
                Assert.AreEqual(1, jumps, "슬라이딩으로 우회해도 같은 쿨타임에 걸려야 한다");
            }
            finally
            {
                locomotion.Jumped -= onJumped;
                SetProperty(input, nameof(PenguinInputReader.SprintHeld), false);
                SetProperty(input, nameof(PenguinInputReader.MoveInput), Vector2.zero);
            }
        }

        private static IEnumerator PressJump(PenguinInputReader input)
        {
            SetProperty(input, nameof(PenguinInputReader.JumpPressedThisFrame), true);
            yield return new WaitForFixedUpdate();
            SetProperty(input, nameof(PenguinInputReader.JumpPressedThisFrame), false);
        }

        private static IEnumerator WaitUntilGrounded(PenguinLocomotion locomotion)
        {
            for (int i = 0; i < 300 && !locomotion.IsGrounded; i++) yield return new WaitForFixedUpdate();
            Assert.IsTrue(locomotion.IsGrounded, "지면에 닿기를 기다렸는데 계속 공중이다");
        }

        private void CreateGround()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "__TEST__JumpCooldownGround";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(40f, 1f, 40f);
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
            _penguin.name = "__TEST__JumpCooldownPenguin";
            _penguin.GetComponent<PenguinInputReader>().enabled = false;
            yield return null;
        }

        private static void SetProperty(object target, string name, object value)
            => target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
                .SetValue(target, value);

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
    }
}
