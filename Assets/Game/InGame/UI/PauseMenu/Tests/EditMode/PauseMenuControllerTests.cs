using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 일시정지가 <b>시간을 되돌려 놓는지</b>만 본다. UI 트리와 멀티 경로는 여기서 덮지 않는다 —
    /// <c>UIDocument</c> 는 패널 없이는 트리를 만들지 않고, <c>NetworkRunner</c> 의 상태는 EditMode 에서
    /// 만들어 낼 수 없다(<c>Core/Multiplay/AGENTS.md</c>). 그쪽은 Play 실측으로 확인한다.
    /// </summary>
    public sealed class PauseMenuControllerTests
    {
        private GameObject _root;
        private float _originalTimeScale;

        [SetUp]
        public void SetUp()
        {
            _originalTimeScale = Time.timeScale;
            _root = new GameObject("__TEST__PauseMenu");
        }

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);

            // 테스트가 실패해도 전역 상태를 남기지 않는다. PlayMode 배치는 DisableSceneReload 라
            // timeScale 이 새면 그 뒤 테스트가 통째로 이상해진다.
            Time.timeScale = _originalTimeScale;
        }

        [Test]
        public void 러너가_없으면_열_때_시간이_멈추고_닫으면_돌아온다()
        {
            PauseMenuController menu = _root.AddComponent<PauseMenuController>();

            menu.Open();
            Assert.That(menu.IsOpen, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(0f), "싱글은 완전 정지다");

            menu.Close();
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [Test]
        public void 토글은_열림과_닫힘을_번갈아_한다()
        {
            PauseMenuController menu = _root.AddComponent<PauseMenuController>();

            menu.Toggle();
            Assert.That(menu.IsOpen, Is.True);

            menu.Toggle();
            Assert.That(menu.IsOpen, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [Test]
        public void 두_번_열어도_직전_시간이_0으로_덮이지_않는다()
        {
            PauseMenuController menu = _root.AddComponent<PauseMenuController>();

            menu.Open();
            menu.Open();
            menu.Close();

            Assert.That(Time.timeScale, Is.EqualTo(1f),
                "두 번째 Open 이 0 을 '직전 값' 으로 기억하면 닫아도 멈춘 채로 남는다");
        }

        /// <summary>
        /// <b>EditMode 는 <c>OnDisable</c> 을 부르지 않는다</b>(<c>[ExecuteAlways]</c> 없는
        /// MonoBehaviour). 실측으로 확인했다 — <c>DestroyImmediate</c> 뒤에도 <c>timeScale</c> 이
        /// 0 으로 남아 이 테스트가 처음에 실패했다. 그래서 <b>배선이 아니라 그 메서드의 몸통</b>을
        /// 직접 불러 검증한다. 콜백이 실제로 불리는지는 Play 실측이 확인한다.
        /// </summary>
        [Test]
        public void OnDisable_은_열린_시간을_되돌린다()
        {
            PauseMenuController menu = _root.AddComponent<PauseMenuController>();
            menu.Open();
            Assert.That(Time.timeScale, Is.EqualTo(0f));

            typeof(PauseMenuController)
                .GetMethod("OnDisable", System.Reflection.BindingFlags.NonPublic
                                        | System.Reflection.BindingFlags.Instance)
                .Invoke(menu, null);

            Assert.That(Time.timeScale, Is.EqualTo(1f),
                "메뉴가 열린 채 씬이 바뀌면 다음 씬이 얼어붙는다");
            Assert.That(menu.IsOpen, Is.False);
        }
    }
}
