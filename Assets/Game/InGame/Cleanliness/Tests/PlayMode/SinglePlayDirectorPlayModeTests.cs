using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    /// <summary>
    /// 씬(SinglePlay.unity)을 로드하지 않고 최소 리그로 SinglePlayDirector의 상태 전이만 검증한다.
    /// _introController는 일부러 null로 둔다 — StageIntroController는 실제 UXML/PanelSettings
    /// 배선이 있어야 크래시 없이 동작하므로, 그 배선 자체는 실제 SinglePlay 씬의 Play Mode 실측으로
    /// 이미 확인했다(2026-08-18). 여기서는 OnEnable의 null 안전 분기(실제 프로덕션 코드 경로)를 타고,
    /// 인트로 완료는 SinglePlayDirector.OnIntroFinished()를 직접 불러 흉내낸다 — UnityEvent가
    /// 정확히 이 메서드를 부르는 배선은 옛 SinglePlayDirector 기반 씬에서 검증됐다.
    /// </summary>
    public sealed class SinglePlayDirectorPlayModeTests
    {
        private GameObject _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("__TEST__SinglePlayDirectorPlayMode");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Intro_동안_입력이_꺼진다()
        {
            PenguinInputReader playerInput = BuildPlayerInput();

            SinglePlayDirector director = BuildDirector(playerInput);
            yield return null;

            Assert.That(director.Phase, Is.EqualTo(EStagePhase.Intro));
            Assert.IsFalse(playerInput.enabled);
        }

        [UnityTest]
        public IEnumerator OnIntroFinished_이후_Playing으로_전이하고_입력을_켠다()
        {
            PenguinInputReader playerInput = BuildPlayerInput();

            SinglePlayDirector director = BuildDirector(playerInput);
            yield return null;

            director.OnIntroFinished();

            Assert.That(director.Phase, Is.EqualTo(EStagePhase.Playing));
            Assert.IsTrue(playerInput.enabled);
        }

        [UnityTest]
        public IEnumerator 시간이_지나도_주문이_실패하기_전에는_Playing을_유지한다()
        {
            SinglePlayDirector director = BuildDirector(null);
            yield return null;
            director.OnIntroFinished();

            yield return new WaitForSeconds(0.3f);

            Assert.That(director.Phase, Is.EqualTo(EStagePhase.Playing));
            Assert.That(director.ElapsedSeconds, Is.GreaterThan(0.1f));
        }

        [UnityTest]
        public IEnumerator 주문_실패로_종료하면_입력이_비워지고_배달_루프가_멈춘다()
        {
            PenguinInputReader playerInput = BuildPlayerInput();

            GiftDeliveryDirector giftDirector = BuildGiftDirector();
            SinglePlayDirector director = BuildDirector(playerInput, giftDirector);
            yield return null;
            director.OnIntroFinished();

            SetPlayerInputHeld(playerInput, move: new Vector2(1f, 0f), pushHeld: true);

            RaiseGiftGameOver(giftDirector);
            yield return null;

            Assert.That(director.Phase, Is.EqualTo(EStagePhase.Ended));
            Assert.IsFalse(playerInput.enabled);
            Assert.IsFalse(playerInput.PackSnowHeld);
            Assert.That(playerInput.MoveInput, Is.EqualTo(Vector2.zero));
            Assert.IsFalse(giftDirector.enabled);
        }

        private SinglePlayDirector BuildDirector(PenguinInputReader playerInput,
            GiftDeliveryDirector giftDirector = null)
        {
            var gameObject = new GameObject("__TEST__SinglePlayDirector");
            gameObject.transform.SetParent(_root.transform);
            gameObject.SetActive(false);

            SinglePlayDirector director = gameObject.AddComponent<SinglePlayDirector>();

            var type = typeof(SinglePlayDirector);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            type.GetField("_introController", flags).SetValue(director, null);
            type.GetField("_giftDeliveryDirector", flags).SetValue(director, giftDirector);
            type.GetField("_snowStage", flags).SetValue(director, null);
            type.GetField("_playerInput", flags).SetValue(director, playerInput);
            type.GetField("_autoReturnToMenuSeconds", flags).SetValue(director, 10f); // 테스트 시간보다 길게

            gameObject.SetActive(true);
            return director;
        }

        private GiftDeliveryDirector BuildGiftDirector()
        {
            var gameObject = new GameObject("__TEST__GiftDeliveryDirector");
            gameObject.transform.SetParent(_root.transform);
            return gameObject.AddComponent<GiftDeliveryDirector>();
        }

        private static void RaiseGiftGameOver(GiftDeliveryDirector director)
        {
            typeof(GiftDeliveryDirector)
                .GetMethod("RaiseGameOver", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(director, null);
        }

        private PenguinInputReader BuildPlayerInput()
        {
            var gameObject = new GameObject("__TEST__PenguinInput");
            gameObject.transform.SetParent(_root.transform);
            LogAssert.Expect(LogType.Error, "PenguinInputReader: 입력 자산이 비어 있다.");
            return gameObject.AddComponent<PenguinInputReader>();
        }

        private static void SetPlayerInputHeld(PenguinInputReader input, Vector2 move, bool pushHeld)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(PenguinInputReader).GetProperty(nameof(PenguinInputReader.MoveInput),
                    flags | BindingFlags.Public).SetValue(input, move);
            typeof(PenguinInputReader).GetProperty(nameof(PenguinInputReader.PackSnowHeld),
                    flags | BindingFlags.Public).SetValue(input, pushHeld);
        }
    }
}
