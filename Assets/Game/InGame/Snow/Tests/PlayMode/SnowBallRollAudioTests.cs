using System.Collections;
using MoreMountains.Feedbacks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    /// <summary>
    /// 굴리기 마찰음. <b>프리팹 배선 자체가 검증 대상이다</b> — 여기서 <c>AddComponent</c> 로
    /// 세우면 <c>PF_SnowBall</c> 에서 배선이 빠져도 통과해 버린다
    /// (<c>PenguinSnowballPushTests</c> 의 협동 연출 테스트와 같은 이유).
    /// </summary>
    public sealed class SnowBallRollAudioTests
    {
        private GameObject _ground;
        private GameObject _ballObject;

        [TearDown]
        public void TearDown()
        {
            if (_ballObject != null) Object.DestroyImmediate(_ballObject);
            if (_ground != null) Object.DestroyImmediate(_ground);
        }

        [UnityTest]
        public IEnumerator 프리팹에_굴리기_전용_소스가_배선돼_있다()
        {
            yield return CreateBall();
            var roll = _ballObject.GetComponent<SnowBallRollAudio>();
            Assert.IsNotNull(roll, "PF_SnowBall 에 SnowBallRollAudio 가 없다");

            AudioSource rollSource = ReadRollSource(roll);
            Assert.IsNotNull(rollSource, "굴리기용 AudioSource 가 배선되지 않았다");
            Assert.IsNotNull(rollSource.clip, "굴리기용 AudioSource 에 클립이 없다");
            Assert.IsTrue(rollSource.loop, "지속 마찰음은 루프여야 한다");
            Assert.IsFalse(rollSource.playOnAwake, "공이 생기자마자 소리가 난다");

            // 협동 성공음과 소스를 공유하면 성공이 날 때마다 굴리기 루프가 끊긴다.
            var player = _ballObject.GetComponent<MMF_Player>();
            Assert.IsNotNull(player, "PF_SnowBall 에 MMF_Player 가 없다");
            MMF_AudioSource thump = player.GetFeedbackOfType<MMF_AudioSource>();
            Assert.IsNotNull(thump, "협동 성공음(MMF_AudioSource)이 없다");
            Assert.AreNotSame(thump.TargetAudioSource, rollSource,
                "굴리기와 협동 성공음이 같은 AudioSource 를 쓴다");
        }

        [UnityTest]
        public IEnumerator 지면_위에서_굴리면_소리가_난다()
        {
            yield return CreateBall();
            var roll = _ballObject.GetComponent<SnowBallRollAudio>();
            var ball = _ballObject.GetComponent<SnowBallCarrier>();
            var body = _ballObject.GetComponent<Rigidbody>();

            yield return Settle(ball);

            // 정지 판정이 속도를 지우므로 매 스텝 다시 실어 준다.
            for (int i = 0; i < 40; i++)
            {
                body.linearVelocity = new Vector3(0f, body.linearVelocity.y, 3f);
                yield return new WaitForFixedUpdate();
            }

            Assert.IsTrue(roll.IsRolling, "지면 위에서 굴리는데 구르는 것으로 안 친다");
            Assert.Greater(roll.CurrentVolume, 0f, "굴리는데 소리가 0 이다");
        }

        [UnityTest]
        public IEnumerator 등에_메면_조용하다()
        {
            yield return CreateBall();
            var roll = _ballObject.GetComponent<SnowBallRollAudio>();
            var ball = _ballObject.GetComponent<SnowBallCarrier>();
            var body = _ballObject.GetComponent<Rigidbody>();

            yield return Settle(ball);

            // 운반은 공을 kinematic 으로 만든다 — HasSupport 가 그것으로 갈린다.
            body.isKinematic = true;
            for (int i = 0; i < 30; i++)
            {
                _ballObject.transform.position += new Vector3(0f, 0f, 0.06f);
                yield return new WaitForFixedUpdate();
            }

            Assert.IsFalse(roll.IsRolling, "등에 멘 공이 구르는 것으로 잡힌다");
            Assert.LessOrEqual(roll.CurrentVolume, 0.01f, "운반 중인데 소리가 난다");
        }

        private static AudioSource ReadRollSource(SnowBallRollAudio roll)
            => (AudioSource)typeof(SnowBallRollAudio)
                .GetField("_source", System.Reflection.BindingFlags.NonPublic
                                     | System.Reflection.BindingFlags.Instance)
                .GetValue(roll);

        private static IEnumerator Settle(SnowBallCarrier ball)
        {
            for (int i = 0; i < 200 && !ball.HasSupport; i++) yield return new WaitForFixedUpdate();
            Assert.IsTrue(ball.HasSupport, "공이 지면에 앉기를 기다렸는데 계속 떠 있다");
        }

        private IEnumerator CreateBall()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "__TEST__RollAudioGround";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(60f, 1f, 60f);

            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab, "PF_SnowBall 이 Resources 에 있어야 한다");
            _ballObject = Object.Instantiate(prefab, new Vector3(0f, 1f, 0f), Quaternion.identity);
            _ballObject.name = "__TEST__RollAudioBall";
            yield return null;
        }
    }
}
