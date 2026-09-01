using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class PenguinControlStateTests
    {
        private GameObject _root;
        private PenguinControlState _state;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("__TEST__PenguinControlState");
            _state = _root.AddComponent<PenguinControlState>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void 일반_슬라이드_눈덩이_전환은_허용된다()
        {
            Assert.IsTrue(_state.TryTransitionTo(EPenguinControlState.Sliding));
            Assert.IsTrue(_state.TryTransitionTo(EPenguinControlState.SnowballSide));
            Assert.IsTrue(_state.TryTransitionTo(EPenguinControlState.SnowballTop));
            Assert.IsTrue(_state.TryTransitionTo(EPenguinControlState.Normal));
            Assert.AreEqual(EPenguinControlState.Normal, _state.Current);
        }

        [Test]
        public void 눈덩이_위로_직접_건너뛸_수_없다()
        {
            Assert.IsFalse(_state.TryTransitionTo(EPenguinControlState.SnowballTop));
            Assert.AreEqual(EPenguinControlState.Normal, _state.Current);
        }

        [Test]
        public void 눈덩이_두_상태만_눈덩이_조작으로_분류된다()
        {
            Assert.IsFalse(_state.IsSnowballState);
            _state.TryTransitionTo(EPenguinControlState.SnowballSide);
            Assert.IsTrue(_state.IsSnowballState);
            _state.TryTransitionTo(EPenguinControlState.SnowballTop);
            Assert.IsTrue(_state.IsSnowballState);
        }

        [Test]
        public void 운반은_일반에서만_시작하고_일반으로만_끝난다()
        {
            Assert.IsTrue(_state.TryTransitionTo(EPenguinControlState.CarryApproach));
            Assert.IsTrue(_state.TryTransitionTo(EPenguinControlState.Carrying));
            Assert.IsFalse(_state.IsSnowballState);
            Assert.IsFalse(_state.TryTransitionTo(EPenguinControlState.Sliding));
            Assert.IsFalse(_state.TryTransitionTo(EPenguinControlState.SnowballSide));
            Assert.IsTrue(_state.TryTransitionTo(EPenguinControlState.Normal));
            Assert.AreEqual(EPenguinControlState.Normal, _state.Current);
        }
    }
}
