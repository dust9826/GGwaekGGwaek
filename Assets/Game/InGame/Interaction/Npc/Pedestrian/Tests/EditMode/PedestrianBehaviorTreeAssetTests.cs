using System.Linq;
using NUnit.Framework;
using Opsive.BehaviorDesigner.Runtime;
using Opsive.BehaviorDesigner.Runtime.Tasks;
using UnityEditor;

namespace PPack
{
    public sealed class PedestrianBehaviorTreeAssetTests
    {
        private const string AssetPath =
            "Assets/Game/InGame/Interaction/Npc/Pedestrian/Behavior/PedestrianBehaviorTree.asset";

        [Test]
        public void 행동_트리_에셋은_모든_노드를_역직렬화한다()
        {
            Subtree subtree = AssetDatabase.LoadAssetAtPath<Subtree>(AssetPath);

            Assert.That(subtree, Is.Not.Null);
            Assert.That(subtree.Deserialize(true), Is.True);
            Assert.That(subtree.EventNodes, Has.Length.EqualTo(1));
            Assert.That(subtree.TreeLogicNodes, Has.Length.EqualTo(13));
            Assert.That(subtree.LogicNodeProperties, Has.Length.EqualTo(subtree.TreeLogicNodes.Length));
        }

        [Test]
        public void 행동_트리는_상태별_조건과_실행_Task를_포함한다()
        {
            Subtree subtree = AssetDatabase.LoadAssetAtPath<Subtree>(AssetPath);
            subtree.Deserialize(true);

            Task[] tasks = subtree.TreeLogicNodes
                .OfType<StackedTask>()
                .SelectMany(node => node.Tasks)
                .ToArray();

            Assert.That(tasks.OfType<HasPendingPedestrianImpact>().Count(), Is.EqualTo(1));
            Assert.That(tasks.OfType<RunPedestrianHitReaction>().Count(), Is.EqualTo(1));
            RunPedestrianReaction[] reactions = tasks.OfType<RunPedestrianReaction>().ToArray();
            Assert.That(reactions, Has.Length.EqualTo(2));
            Assert.That(reactions.Select(task => task.Reaction),
                Is.EquivalentTo(new[] { EPedestrianAction.Attack, EPedestrianAction.Flee }));
            Assert.That(tasks.OfType<RunPedestrianNormalBehavior>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void 모든_트리_노드의_인덱스는_배열_순서와_일치한다()
        {
            Subtree subtree = AssetDatabase.LoadAssetAtPath<Subtree>(AssetPath);
            subtree.Deserialize(true);

            for (ushort index = 0; index < subtree.TreeLogicNodes.Length; index++) {
                Assert.That(subtree.TreeLogicNodes[index].Index, Is.EqualTo(index));
            }
        }

        [Test]
        public void 루트_선택자는_피격_사건_일상_순서로_분기한다()
        {
            Subtree subtree = AssetDatabase.LoadAssetAtPath<Subtree>(AssetPath);
            subtree.Deserialize(true);

            Assert.That(subtree.TreeLogicNodes[1].ParentIndex, Is.EqualTo(0));
            Assert.That(subtree.TreeLogicNodes[2].ParentIndex, Is.EqualTo(1));
            Assert.That(subtree.TreeLogicNodes[5].ParentIndex, Is.EqualTo(1));
            Assert.That(subtree.TreeLogicNodes[12].ParentIndex, Is.EqualTo(1));
            Assert.That(subtree.TreeLogicNodes[2].SiblingIndex, Is.EqualTo(5));
            Assert.That(subtree.TreeLogicNodes[5].SiblingIndex, Is.EqualTo(12));
            Assert.That(subtree.TreeLogicNodes[12].SiblingIndex, Is.EqualTo(ushort.MaxValue));
        }
    }
}
