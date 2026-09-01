using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class NpcGroupContextTests
    {
        private GameObject _groupObject;
        private GameObject _memberObject;

        [TearDown]
        public void TearDown()
        {
            if (_memberObject != null) Object.DestroyImmediate(_memberObject);
            if (_groupObject != null) Object.DestroyImmediate(_groupObject);
        }

        [Test]
        public void SetGroup_멤버를_등록하고_해제한다()
        {
            NpcGroupContext group = CreateGroup();
            NpcGroupMember member = CreateMember();

            member.SetGroup(group);
            Assert.That(group.MemberCount, Is.EqualTo(1));

            member.SetGroup(null);
            Assert.That(group.MemberCount, Is.Zero);
        }

        [Test]
        public void 영역_밖이면_경계_안쪽의_복귀점을_준다()
        {
            NpcGroupContext group = CreateGroup();
            NpcGroupMember member = CreateMember();
            member.SetGroup(group);
            member.transform.position = new Vector3(20f, 3f, 0f);

            Assert.That(member.IsOutsideGroupTerritory, Is.True);

            Vector3 returnPoint = member.GetGroupReturnPoint(0.5f);
            Assert.That(returnPoint.x, Is.EqualTo(7.5f).Within(0.001f));
            Assert.That(returnPoint.y, Is.EqualTo(3f).Within(0.001f));
            Assert.That(group.IsInsideTerritory(returnPoint), Is.True);
        }

        [Test]
        public void 신호는_종류별_최신값과_발신자를_보존한다()
        {
            NpcGroupContext group = CreateGroup();
            NpcGroupMember member = CreateMember();
            member.SetGroup(group);

            NpcGroupSignal first = member.BroadcastGroupSignal(ENpcGroupSignal.Alert, Vector3.one);
            NpcGroupSignal second = member.BroadcastGroupSignal(ENpcGroupSignal.Flee, Vector3.right * 2f);

            Assert.That(second.Sequence, Is.GreaterThan(first.Sequence));
            Assert.That(group.TryGetSignal(ENpcGroupSignal.Alert, out NpcGroupSignal alert), Is.True);
            Assert.That(alert.Source, Is.SameAs(member));
            Assert.That(alert.Position, Is.EqualTo(Vector3.one));
            Assert.That(group.TryGetSignal(ENpcGroupSignal.Flee, out NpcGroupSignal flee), Is.True);
            Assert.That(flee.Position, Is.EqualTo(Vector3.right * 2f));
        }

        private NpcGroupContext CreateGroup()
        {
            _groupObject = new GameObject("__TEST__NpcGroup");
            return _groupObject.AddComponent<NpcGroupContext>();
        }

        private NpcGroupMember CreateMember()
        {
            _memberObject = new GameObject("__TEST__NpcMember");
            return _memberObject.AddComponent<NpcGroupMember>();
        }
    }
}

