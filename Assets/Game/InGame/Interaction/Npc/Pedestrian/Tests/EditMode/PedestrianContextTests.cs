using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class PedestrianContextTests
    {
        private GameObject _gameObject;
        private PedestrianContext _context;

        [SetUp]
        public void SetUp()
        {
            _gameObject = new GameObject("__TEST__ PedestrianContext");
            _context = _gameObject.AddComponent<PedestrianContext>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_gameObject);
        }

        [Test]
        public void 약한_충격은_대기열에_쌓이지_않는다()
        {
            Assert.That(_context.ReportImpact(9.99f), Is.False);
            Assert.That(_context.HasPendingStrongImpact, Is.False);
            Assert.That(_context.CurrentAction, Is.EqualTo(EPedestrianAction.Normal));
        }

        [Test]
        public void 강한_충격을_소비하면_피격_반응_상태가_된다()
        {
            Assert.That(_context.ReportImpact(10f, Vector3.right), Is.True);
            Assert.That(_context.ConsumeImpactAndBeginHitReaction(), Is.True);

            Assert.That(_context.HasPendingStrongImpact, Is.False);
            Assert.That(_context.IsHitReacting, Is.True);
            Assert.That(_context.HasPendingIncident, Is.True);
            Assert.That(_context.IncidentPosition, Is.EqualTo(Vector3.right));
        }

        [Test]
        public void 피격_반응은_외부_완료_통지를_기다린다()
        {
            BeginHitReaction();
            Assert.That(_context.FinishHitReaction(), Is.False);

            _context.NotifyHitReactionComplete();
            Assert.That(_context.FinishHitReaction(), Is.True);
            Assert.That(_context.CurrentAction, Is.EqualTo(EPedestrianAction.Normal));
            Assert.That(_context.HasPendingIncident, Is.True);
        }

        [TestCase(ENpcTemperament.Timid)]
        [TestCase(ENpcTemperament.Aggressive)]
        public void 성격은_생성된_프로필에서_적용된다(ENpcTemperament temperament)
        {
            _context.ApplyProfile(new NpcProfileData(12, 34, temperament, default));
            Assert.That(_context.Temperament, Is.EqualTo(temperament));
        }

        [TestCase(EPedestrianAction.Flee)]
        [TestCase(EPedestrianAction.Attack)]
        public void 반응은_외부_완료_통지까지_실행_상태를_유지한다(EPedestrianAction reaction)
        {
            _context.ReportIncident(Vector3.forward);
            Assert.That(_context.BeginReaction(reaction), Is.True);
            Assert.That(_context.FinishReaction(), Is.False);

            _context.NotifyReactionComplete();
            Assert.That(_context.FinishReaction(), Is.True);
            Assert.That(_context.HasPendingIncident, Is.False);
            Assert.That(_context.CurrentAction, Is.EqualTo(EPedestrianAction.Normal));
        }

        [Test]
        public void 충격을_본_주변_NPC는_사건을_기억한다()
        {
            GameObject witnessObject = new GameObject("__TEST__ PedestrianWitness");
            PedestrianContext witness = null;
            try {
                witnessObject.transform.position = Vector3.right;
                witness = witnessObject.AddComponent<PedestrianContext>();
                PedestrianIncidentSystem.Register(_context);
                PedestrianIncidentSystem.Register(witness);

                _context.ReportImpact(10f, Vector3.zero);

                Assert.That(witness.HasPendingIncident, Is.True);
                Assert.That(witness.IncidentPosition, Is.EqualTo(Vector3.zero));
            }
            finally {
                PedestrianIncidentSystem.Unregister(_context);
                PedestrianIncidentSystem.Unregister(witness);
                Object.DestroyImmediate(witnessObject);
            }
        }

        private void BeginHitReaction()
        {
            _context.ReportImpact(10f);
            _context.ConsumeImpactAndBeginHitReaction();
        }
    }
}
