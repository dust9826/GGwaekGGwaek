using System.Linq;
using NUnit.Framework;
using Opsive.GraphDesigner.Runtime;
using Opsive.BehaviorDesigner.Runtime;
using Opsive.BehaviorDesigner.Runtime.Tasks;
using Opsive.BehaviorDesigner.Runtime.Tasks.Events;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace PPack
{
    public sealed class ThiefAssetTests
    {
        private const string Root = "Assets/Game/InGame/Interaction/Thief";

        [Test]
        public void 행동_트리는_충격_귀가_발각_절도_접근_우선순위를_가진다()
        {
            Subtree tree = AssetDatabase.LoadAssetAtPath<Subtree>(Root + "/Behavior/ThiefBehaviorTree.asset");
            Assert.That(tree, Is.Not.Null);
            Assert.That(tree.Deserialize(true), Is.True);
            Assert.That(tree.TreeLogicNodes, Has.Length.EqualTo(15));

            Task[] tasks = tree.TreeLogicNodes.OfType<StackedTask>()
                .SelectMany(node => node.Tasks).ToArray();
            Assert.That(tasks.OfType<IsThiefImpactReacting>().Count(), Is.EqualTo(1));
            Assert.That(tasks.OfType<RunThiefImpactReaction>().Count(), Is.EqualTo(1));
            Assert.That(tasks.OfType<IsThiefEscaping>().Count(), Is.EqualTo(1));
            Assert.That(tasks.OfType<RunThiefEscape>().Count(), Is.EqualTo(1));
            Assert.That(tasks.OfType<IsThiefSpotted>().Count(), Is.EqualTo(1));
            Assert.That(tasks.OfType<RunThiefBeginSpottedRetreat>().Count(), Is.EqualTo(1));
            Assert.That(tasks.OfType<HasThiefGiftClaim>().Count(), Is.EqualTo(1));
            Assert.That(tasks.OfType<RunThiefSteal>().Count(), Is.EqualTo(1));
            Assert.That(tasks.OfType<RunThiefAcquireOrApproach>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void 발각_가지는_귀가와_선물_선점_사이_우선순위에_있다()
        {
            Subtree tree = AssetDatabase.LoadAssetAtPath<Subtree>(Root + "/Behavior/ThiefBehaviorTree.asset");
            Assert.That(tree, Is.Not.Null);
            Assert.That(tree.Deserialize(true), Is.True);

            ITreeLogicNode[] nodes = tree.TreeLogicNodes;
            bool HasTask<T>(ushort sequenceIndex) => nodes.OfType<StackedTask>()
                .Any(stacked => stacked.ParentIndex == sequenceIndex && stacked.Tasks.OfType<T>().Any());

            ITreeLogicNode escapeSequence = nodes.Single(node => HasTask<IsThiefEscaping>(node.Index));
            ITreeLogicNode spottedSequence = nodes.Single(node => node.Index == escapeSequence.SiblingIndex);
            Assert.That(HasTask<IsThiefSpotted>(spottedSequence.Index), Is.True,
                "귀가 가지 바로 다음 형제는 발각 가지여야 한다");

            ITreeLogicNode giftClaimSequence = nodes.Single(node => node.Index == spottedSequence.SiblingIndex);
            Assert.That(HasTask<HasThiefGiftClaim>(giftClaimSequence.Index), Is.True,
                "발각 가지 바로 다음 형제는 선물 선점 가지여야 한다");
        }

        [Test]
        public void 행동_트리는_Start_이벤트에서_루트로_연결된다()
        {
            Subtree tree = AssetDatabase.LoadAssetAtPath<Subtree>(Root + "/Behavior/ThiefBehaviorTree.asset");
            Assert.That(tree, Is.Not.Null);
            Assert.That(tree.Deserialize(true), Is.True);
            Assert.That(tree.EventNodes, Has.Length.EqualTo(1));
            Assert.That(tree.EventNodes[0], Is.TypeOf<Start>());
            Assert.That(tree.EventNodes[0].ConnectedIndex, Is.EqualTo(0));
        }

        [Test]
        public void 도둑_프리팹은_Npc_구현_없이_독립_실행_구성을_가진다()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/PF_Thief.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<ThiefActor>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<ThiefMovement>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<ThiefPlayerSensor>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<ThiefNetworkHub>(), Is.Not.Null);
            ThiefImpactReceiver impactReceiver = prefab.GetComponent<ThiefImpactReceiver>();
            Assert.That(impactReceiver, Is.Not.Null);
            Assert.That(impactReceiver.KnockdownMomentumKgMps, Is.EqualTo(140f));
            ThiefExitCountdownView countdown = prefab.GetComponent<ThiefExitCountdownView>();
            Assert.That(countdown, Is.Not.Null);
            Transform countdownRoot = prefab.transform.Find("ExitCountdown");
            Assert.That(countdownRoot, Is.Not.Null);
            Assert.That(countdownRoot.gameObject.activeSelf, Is.False);
            Assert.That(countdownRoot.GetComponentsInChildren<TextMesh>(true), Has.Length.EqualTo(2));
            Assert.That(prefab.GetComponent<ThiefBehaviorTreeAuthority>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<NpcGroupMember>(), Is.Null);
            Assert.That(prefab.GetComponent<PedestrianContext>(), Is.Null);
            Assert.That(prefab.GetComponentsInChildren<Component>(true)
                .Any(component => component != null && component.GetType().Name == "FullBodyBipedIK"), Is.True);
        }

        [Test]
        public void 도둑_애니메이터는_Synty_복사본과_Lift_상태를_가진다()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                Root + "/Animations/AC_Thief.controller");
            AnimatorController source = AssetDatabase.LoadAssetAtPath<AnimatorController>(
                "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/AC_Polygon_Masculine.controller");
            Assert.That(controller, Is.Not.Null);
            Assert.That(source, Is.Not.Null);
            Assert.That(controller, Is.Not.SameAs(source), "Synty 원본이 아니라 Thief 소유 복사본이어야 한다");
            ModelImporter pickingImporter = AssetImporter.GetAtPath(
                Root + "/Animations/Picking Up.fbx") as ModelImporter;
            Assert.That(pickingImporter, Is.Not.Null);
            Assert.That(pickingImporter.animationType, Is.EqualTo(ModelImporterAnimationType.Human));
            Assert.That(controller.parameters.Any(parameter => parameter.name == "MoveSpeed"), Is.True);
            Assert.That(controller.parameters.Any(parameter => parameter.name == "LiftPhase"), Is.True);
            Assert.That(controller.parameters.Any(parameter => parameter.name == "HasCargo"), Is.True);
            Assert.That(controller.parameters.Any(parameter => parameter.name == "ImpactPhase"), Is.True);

            AnimatorState[] states = AllStates(controller.layers[0].stateMachine).ToArray();
            Assert.That(states.Single(state => state.name == "GiftPrepareCrouch").motion.name,
                Is.EqualTo("A_Idle_Standing_Masc"));
            Assert.That(states.Single(state => state.name == "GiftReachFloor").motion.name,
                Is.EqualTo("A_Idle_Standing_Masc"));
            Assert.That(states.Single(state => state.name == "GiftStandAndOverhead").motion.name,
                Is.EqualTo("A_Idle_Standing_Masc"));
            Assert.That(states.Single(state => state.name == "GiftCarryReady").motion.name,
                Is.EqualTo("A_Idle_Standing_Masc"));
            Assert.That(states.Single(state => state.name == "ImpactFalling").motion.name,
                Is.EqualTo("Death_Backward"));
            Assert.That(states.Single(state => state.name == "ImpactDown").speed, Is.Zero);
            Assert.That(states.Single(state => state.name == "ImpactGettingUp").speed, Is.LessThan(0f));

            AnimatorControllerLayer pickingLayer = controller.layers.Single(layer =>
                layer.name == "GiftPickingArms");
            Assert.That(pickingLayer.blendingMode, Is.EqualTo(AnimatorLayerBlendingMode.Override));
            Assert.That(pickingLayer.defaultWeight, Is.EqualTo(1f));
            Assert.That(AssetDatabase.GetAssetPath(pickingLayer.avatarMask),
                Is.EqualTo(Root + "/Animations/AM_ThiefPickingArms.mask"));
            Assert.That(pickingLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Head), Is.False);
            Assert.That(pickingLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.Body), Is.False);
            Assert.That(pickingLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.LeftArm), Is.True);
            Assert.That(pickingLayer.avatarMask.GetHumanoidBodyPartActive(AvatarMaskBodyPart.RightArm), Is.True);
            AnimatorState picking = AllStates(pickingLayer.stateMachine)
                .Single(state => state.name == "GiftPickingUpArms");
            Assert.That(picking.motion.name, Is.EqualTo("mixamo.com"));
            Assert.That(picking.speed, Is.EqualTo(3.5f));

            Assert.That(controller.layers.Any(layer => layer.name is "GiftLiftHead" or "GiftLiftUpperBody"),
                Is.False, "중립 자세를 다시 덮는 레이어 대신 기본 자세 자체가 중립이어야 한다");
        }

        [Test]
        public void 도둑_프리팹은_RootMotion을_끄고_전신_IK_타깃을_소유한다()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/PF_Thief.prefab");
            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.applyRootMotion, Is.False);
            Assert.That(AssetDatabase.GetAssetPath(animator.runtimeAnimatorController),
                Is.EqualTo(Root + "/Animations/AC_Thief.controller"));

            string[] requiredTargets =
            {
                "CarryAnchor", "LeftHandTarget", "RightHandTarget", "BodyTarget",
                "LeftElbowTarget", "RightElbowTarget", "LeftFootTarget", "RightFootTarget",
            };
            Transform[] transforms = prefab.GetComponentsInChildren<Transform>(true);
            foreach (string target in requiredTargets)
                Assert.That(transforms.Any(transform => transform.name == target), Is.True, target);
        }

        [Test]
        public void 도둑은_선물_표면에서_떨어져_서고_상자_외부_IK_여유를_사용한다()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/PF_Thief.prefab");
            var sensor = new SerializedObject(prefab.GetComponent<ThiefPlayerSensor>());
            var movement = new SerializedObject(prefab.GetComponent<ThiefMovement>());
            var actor = new SerializedObject(prefab.GetComponent<ThiefActor>());
            var ik = new SerializedObject(prefab.GetComponent<ThiefFinalIkAdapter>());

            Assert.That(sensor.FindProperty("_visualRangeM").floatValue, Is.EqualTo(10f));
            Assert.That(movement.FindProperty("_walkSpeedMps"), Is.Null,
                "서서 걷기 속도가 남아 있으면 실수로 Walk가 다시 활성화될 수 있다");
            Assert.That(actor.FindProperty("_giftReachM"), Is.Null,
                "선물 중심 거리만으로 집기를 시작하면 큰 선물에서 몸이 겹친다");
            Assert.That(actor.FindProperty("_grabSurfaceClearanceM").floatValue,
                Is.GreaterThanOrEqualTo(0.2f));
            Assert.That(ik.FindProperty("_bodyWeight").floatValue, Is.Zero,
                "Picking Up 팔 동작이 주동작이고 Final IK가 몸통을 끌면 안 된다");
            Assert.That(ik.FindProperty("_footPinWeight").floatValue, Is.Zero,
                "팔 보정이 발 고정을 통해 전신 자세를 비틀면 안 된다");
            Assert.That(ik.FindProperty("_palmClearanceM").floatValue, Is.GreaterThan(0f));
            Assert.That(ik.FindProperty("_forearmClearanceM").floatValue, Is.GreaterThan(0f));
            Assert.That(ik.FindProperty("_supportInset01").floatValue, Is.InRange(0.1f, 0.4f));
            Assert.That(ik.FindProperty("_carryHandWeight"), Is.Null,
                "운반 중 손 위치 가중치를 낮추면 손이 선물에서 고정적으로 떨어진다");
        }

        [Test]
        public void 배치_리그는_별도_습격_영역과_디렉터를_소유한다()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Prefabs/PF_ThiefRaidRig.prefab");
            Assert.That(prefab, Is.Not.Null);
            ThiefRaidSite site = prefab.GetComponentInChildren<ThiefRaidSite>(true);
            ThiefDirector director = prefab.GetComponent<ThiefDirector>();
            Assert.That(site, Is.Not.Null);
            Assert.That(site.LootVolume, Is.Not.Null);
            Assert.That(director, Is.Not.Null);
        }

        private static System.Collections.Generic.IEnumerable<AnimatorState> AllStates(
            AnimatorStateMachine stateMachine)
        {
            foreach (ChildAnimatorState child in stateMachine.states) yield return child.state;
            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
                foreach (AnimatorState state in AllStates(child.stateMachine)) yield return state;
        }
    }
}
