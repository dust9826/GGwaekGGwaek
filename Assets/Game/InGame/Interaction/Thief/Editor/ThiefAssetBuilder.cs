using System;
using System.Linq;
using System.Reflection;
using Fusion;
using Opsive.BehaviorDesigner.Runtime;
using Opsive.BehaviorDesigner.Runtime.Tasks;
using Opsive.BehaviorDesigner.Runtime.Tasks.Actions;
using Opsive.BehaviorDesigner.Runtime.Tasks.Composites;
using Opsive.BehaviorDesigner.Runtime.Tasks.Conditionals;
using Opsive.BehaviorDesigner.Runtime.Tasks.Decorators;
using Opsive.BehaviorDesigner.Runtime.Tasks.Events;
using Opsive.GraphDesigner.Runtime;
using Opsive.GraphDesigner.Runtime.Variables;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace PPack
{
    public static class ThiefAssetBuilder
    {
        private const string FeatureRoot = "Assets/Game/InGame/Interaction/Thief";
        private const string CreativeRoot = "Assets/Game/InGame/creature asset/Creative_Characters";
        private const string BasePrefabPath = CreativeRoot + "/Prefabs/Base_Mesh.prefab";
        private const string CharacterFbxPath = CreativeRoot + "/Meshes/Base_Mesh.fbx";
        private const string RuntimeClipRoot = FeatureRoot + "/Animations/RuntimeClips";
        private const string ControllerPath = FeatureRoot + "/Animations/AC_Thief.controller";
        private const string PickingAnimationPath = FeatureRoot + "/Animations/Picking Up.fbx";
        private const string PickingMaskPath = FeatureRoot + "/Animations/AM_ThiefPickingArms.mask";
        private const string ImpactAnimationPath =
            CreativeRoot + "/Animations/Other_Animations/Death_Backward.anim";
        private const string PickingLayerName = "GiftPickingArms";
        private const string SyntyControllerPath =
            "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/AC_Polygon_Masculine.controller";
        private const string SyntyAnimationRoot =
            "Assets/Synty/AnimationBaseLocomotion/Animations/Polygon/Masculine";
        private const string TreePath = FeatureRoot + "/Behavior/ThiefBehaviorTree.asset";
        private const string ThiefPrefabPath = FeatureRoot + "/Prefabs/PF_Thief.prefab";
        private const string RigPrefabPath = FeatureRoot + "/Prefabs/PF_ThiefRaidRig.prefab";
        private const string GiftPrefabPath = "Assets/Game/InGame/Delivery/Prefabs/PF_GiftBox_Variable.prefab";
        private const string PenguinPrefabPath = "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab";
        private const string TestScenePath = FeatureRoot + "/Tests/Thief_GiftRaid_Test.unity";
        private const string TestNavMeshPath = FeatureRoot + "/Tests/NavMesh-ThiefGiftRaid.asset";

        [MenuItem("PPack/Thief/Rebuild Thief Assets")]
        public static void Rebuild()
        {
            AnimatorController controller = BuildAnimatorController();
            Subtree behaviorTree = BuildBehaviorTree();
            GameObject thiefPrefab = BuildThiefPrefab(controller, behaviorTree);
            BuildRaidRig(thiefPrefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Thief assets rebuilt: {ThiefPrefabPath}, {RigPrefabPath}");
        }

        [MenuItem("PPack/Thief/Rebuild Gift Raid Test Scene")]
        public static void RebuildTestScene()
        {
            Scene originalActiveScene = SceneManager.GetActiveScene();
            Scene testScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(testScene);
                BuildTestSceneContents(testScene, out NavMeshSurface surface);
                EditorSceneManager.SaveScene(testScene, TestScenePath);
                surface.BuildNavMesh();
                AssetDatabase.DeleteAsset(TestNavMeshPath);
                AssetDatabase.CreateAsset(surface.navMeshData, TestNavMeshPath);
                EditorUtility.SetDirty(surface);
                EditorSceneManager.MarkSceneDirty(testScene);
                EditorSceneManager.SaveScene(testScene, TestScenePath);
                AssetDatabase.SaveAssets();
                Debug.Log($"Thief gift raid test scene rebuilt: {TestScenePath}");
            }
            finally
            {
                if (testScene.IsValid() && testScene.isLoaded)
                    EditorSceneManager.CloseScene(testScene, true);
                if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                    SceneManager.SetActiveScene(originalActiveScene);
            }
        }

        private static void BuildTestSceneContents(Scene scene, out NavMeshSurface surface)
        {
            GameObject environment = new GameObject("TestEnvironment");
            surface = environment.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            CreatePrimitive(environment.transform, "NavMeshFloor", PrimitiveType.Cube,
                new Vector3(0f, -0.1f, 0f), new Vector3(60f, 0.2f, 60f));
            CreatePrimitive(environment.transform, "StorageBackWall", PrimitiveType.Cube,
                new Vector3(0f, 1f, 4.2f), new Vector3(9f, 2f, 0.3f));
            CreatePrimitive(environment.transform, "StorageLeftWall", PrimitiveType.Cube,
                new Vector3(-4.2f, 1f, 0f), new Vector3(0.3f, 2f, 8f));
            CreatePrimitive(environment.transform, "StorageRightWall", PrimitiveType.Cube,
                new Vector3(4.2f, 1f, 0f), new Vector3(0.3f, 2f, 8f));

            GameObject lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            GameObject rigPrefab = LoadRequiredPrefab(RigPrefabPath);
            GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
            rig.name = "ThiefRaidRig_Test";
            ThiefRaidSite site = rig.GetComponentInChildren<ThiefRaidSite>(true);
            ThiefDirector director = rig.GetComponent<ThiefDirector>();
            SetVector2(site, "_spawnRadiusMRange", new Vector2(10f, 14f));
            SetInt(site, "_navMeshSampleAttempts", 32);
            SetVector2(director, "_spawnDelaySecondsRange", new Vector2(2f, 5f));
            SetFloat(director, "_failedSpawnRetrySeconds", 0.5f);
            SetInt(director, "_randomSeed", 20260828);

            GameObject giftPrefab = LoadRequiredPrefab(GiftPrefabPath);
            GameObject spawnRoot = new GameObject("GiftSpawnPoints");
            Vector3[] giftPositions =
            {
                new Vector3(-2.4f, 0f, 1.2f),
                new Vector3(-0.8f, 0f, 1.2f),
                new Vector3(0.8f, 0f, 1.2f),
                new Vector3(2.4f, 0f, 1.2f),
            };
            EGiftBoxKind[] kinds =
            {
                EGiftBoxKind.Red,
                EGiftBoxKind.Yellow,
                EGiftBoxKind.Green,
                EGiftBoxKind.Blue,
            };
            Transform[] spawnPoints = new Transform[giftPositions.Length];
            for (int index = 0; index < giftPositions.Length; index++)
            {
                Transform spawnPoint = Child(spawnRoot.transform, $"{kinds[index]}Spawn", giftPositions[index]);
                spawnPoints[index] = spawnPoint;
                GameObject giftObject = (GameObject)PrefabUtility.InstantiatePrefab(giftPrefab, scene);
                giftObject.name = $"TestGift_{kinds[index]}";
                giftObject.transform.SetPositionAndRotation(giftPositions[index], Quaternion.identity);
                giftObject.GetComponent<Gift>().SetKind(kinds[index]);
            }

            GameObject penguinPrefab = LoadRequiredPrefab(PenguinPrefabPath);
            GameObject penguin = (GameObject)PrefabUtility.InstantiatePrefab(penguinPrefab, scene);
            penguin.name = "TestPlayer_Penguin";
            penguin.transform.SetPositionAndRotation(new Vector3(0f, 0.5f, -8f), Quaternion.identity);

            GameObject harnessObject = new GameObject("ThiefGiftRaidTestHarness");
            ThiefGiftRaidTestHarness harness = harnessObject.AddComponent<ThiefGiftRaidTestHarness>();
            harness.Configure(director, site, giftPrefab, spawnPoints);
        }

        private static GameObject LoadRequiredPrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException($"Required prefab missing: {path}");
            return prefab;
        }

        private static GameObject CreatePrimitive(Transform parent, string name, PrimitiveType type,
            Vector3 position, Vector3 scale)
        {
            GameObject instance = GameObject.CreatePrimitive(type);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.position = position;
            instance.transform.localScale = scale;
            return instance;
        }

        private static AnimatorController BuildAnimatorController()
        {
            AnimatorController source = AssetDatabase.LoadAssetAtPath<AnimatorController>(SyntyControllerPath);
            if (source == null)
                throw new InvalidOperationException($"Synty sample controller missing: {SyntyControllerPath}");

            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            bool needsMigration = controller == null || !HasParameter(controller, "MoveSpeed");
            if (needsMigration)
            {
                if (controller != null && !AssetDatabase.DeleteAsset(ControllerPath))
                    throw new InvalidOperationException($"Could not replace legacy thief controller: {ControllerPath}");
                if (!AssetDatabase.CopyAsset(SyntyControllerPath, ControllerPath))
                    throw new InvalidOperationException($"Could not copy Synty controller to: {ControllerPath}");
                AssetDatabase.ImportAsset(ControllerPath, ImportAssetOptions.ForceUpdate);
                controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
                if (controller == null)
                    throw new InvalidOperationException($"Copied thief controller could not be loaded: {ControllerPath}");
                if (AssetDatabase.IsValidFolder(RuntimeClipRoot)) AssetDatabase.DeleteAsset(RuntimeClipRoot);
            }

            ConfigureGiftLiftStates(controller);
            ConfigureImpactStates(controller);
            ConfigurePickingArmLayer(controller);
            RemoveLayerIfPresent(controller, "GiftLiftHead");
            RemoveLayerIfPresent(controller, "GiftLiftUpperBody");
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void ConfigureGiftLiftStates(AnimatorController controller)
        {
            EnsureParameter(controller, "LiftPhase", AnimatorControllerParameterType.Int);
            EnsureParameter(controller, "HasCargo", AnimatorControllerParameterType.Bool);

            AnimatorStateMachine root = controller.layers[0].stateMachine;
            AnimatorState prepare = EnsureState(root, "GiftPrepareCrouch",
                LoadSyntyClip("A_Idle_Standing_Masc"), new Vector3(600f, -160f));
            AnimatorState reach = EnsureState(root, "GiftReachFloor",
                LoadSyntyClip("A_Idle_Standing_Masc"), new Vector3(800f, -160f));
            AnimatorState rise = EnsureState(root, "GiftStandAndOverhead",
                LoadSyntyClip("A_Idle_Standing_Masc"), new Vector3(1000f, -160f));
            AnimatorState carryReady = EnsureState(root, "GiftCarryReady",
                LoadSyntyClip("A_Idle_Standing_Masc"), new Vector3(1200f, -160f));
            AnimatorState idleStanding = FindState(root, "Idle_Standing");
            AnimatorState locomotion = FindState(root, "LocomotionBlendTree");
            if (idleStanding == null || locomotion == null)
                throw new InvalidOperationException("Synty controller is missing Idle_Standing or LocomotionBlendTree.");

            ReplaceAnyTransition(root, prepare, 0.08f,
                (AnimatorConditionMode.Equals, (float)EThiefLiftPhase.PrepareCrouch, "LiftPhase"));
            ReplaceAnyTransition(root, reach, 0.08f,
                (AnimatorConditionMode.Greater, (float)EThiefLiftPhase.PrepareCrouch, "LiftPhase"),
                (AnimatorConditionMode.Less, (float)EThiefLiftPhase.StandAndOverhead, "LiftPhase"));
            ReplaceAnyTransition(root, rise, 0.08f,
                (AnimatorConditionMode.Equals, (float)EThiefLiftPhase.StandAndOverhead, "LiftPhase"));

            ReplaceTransition(prepare, reach, 0.08f,
                (AnimatorConditionMode.Greater, (float)EThiefLiftPhase.PrepareCrouch, "LiftPhase"));
            ReplaceTransition(reach, rise, 0.08f,
                (AnimatorConditionMode.Greater, (float)EThiefLiftPhase.LiftToChest, "LiftPhase"));
            ReplaceTransition(rise, carryReady, 0.1f,
                (AnimatorConditionMode.Greater, (float)EThiefLiftPhase.StandAndOverhead, "LiftPhase"));
            ReplaceTransition(carryReady, locomotion, 0.12f,
                (AnimatorConditionMode.If, 0f, "HasCargo"),
                (AnimatorConditionMode.IfNot, 0f, "IsStopped"));

            ReplaceTransition(prepare, idleStanding, 0.1f,
                (AnimatorConditionMode.Equals, (float)EThiefLiftPhase.None, "LiftPhase"));
            ReplaceTransition(reach, idleStanding, 0.1f,
                (AnimatorConditionMode.Equals, (float)EThiefLiftPhase.None, "LiftPhase"));
            ReplaceTransition(rise, idleStanding, 0.1f,
                (AnimatorConditionMode.Equals, (float)EThiefLiftPhase.None, "LiftPhase"));
            ReplaceTransition(carryReady, idleStanding, 0.1f,
                (AnimatorConditionMode.IfNot, 0f, "HasCargo"));
        }

        private static void ConfigurePickingArmLayer(AnimatorController controller)
        {
            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(PickingAnimationPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(candidate => !candidate.name.StartsWith("__preview__"));
            if (clip == null)
                throw new InvalidOperationException($"Picking animation clip missing: {PickingAnimationPath}");

            AvatarMask mask = BuildHumanoidMask(PickingMaskPath, "AM_ThiefPickingArms",
                AvatarMaskBodyPart.LeftArm, AvatarMaskBodyPart.RightArm,
                AvatarMaskBodyPart.LeftFingers, AvatarMaskBodyPart.RightFingers);
            AnimatorControllerLayer layer = controller.layers
                .FirstOrDefault(candidate => candidate.name == PickingLayerName);
            if (layer == null)
            {
                controller.AddLayer(PickingLayerName);
            }

            AnimatorControllerLayer[] layers = controller.layers;
            int layerIndex = Array.FindIndex(layers, candidate => candidate.name == PickingLayerName);
            layer = layers[layerIndex];
            layer.avatarMask = mask;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layer.defaultWeight = 1f;
            layer.iKPass = false;
            layers[layerIndex] = layer;
            controller.layers = layers;

            AnimatorStateMachine stateMachine = layer.stateMachine;
            AnimatorState empty = EnsureState(stateMachine, "Empty", null, new Vector3(300f, 80f));
            AnimatorState picking = EnsureState(stateMachine, "GiftPickingUpArms", clip,
                new Vector3(560f, 80f));
            picking.speed = 3.5f;
            stateMachine.defaultState = empty;

            ConfigureLiftWindowTransitions(stateMachine, picking, empty);
        }

        private static void ConfigureImpactStates(AnimatorController controller)
        {
            EnsureParameter(controller, "ImpactPhase", AnimatorControllerParameterType.Int);
            AnimationClip fallClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ImpactAnimationPath);
            if (fallClip == null)
                throw new InvalidOperationException($"Thief impact animation missing: {ImpactAnimationPath}");

            AnimatorStateMachine root = controller.layers[0].stateMachine;
            AnimatorState falling = EnsureState(root, "ImpactFalling", fallClip,
                new Vector3(600f, -340f));
            AnimatorState down = EnsureState(root, "ImpactDown", fallClip,
                new Vector3(800f, -340f));
            AnimatorState gettingUp = EnsureState(root, "ImpactGettingUp", fallClip,
                new Vector3(1000f, -340f));
            AnimatorState idleStanding = FindState(root, "Idle_Standing");
            if (idleStanding == null)
                throw new InvalidOperationException("Synty controller is missing Idle_Standing.");

            falling.speed = 1.25f;
            falling.cycleOffset = 0f;
            down.speed = 0f;
            down.cycleOffset = 1f;
            gettingUp.speed = -1.25f;
            gettingUp.cycleOffset = 1f;

            ReplaceAnyTransition(root, falling, 0.06f,
                (AnimatorConditionMode.Equals, (float)EThiefImpactPhase.Falling, "ImpactPhase"));
            ReplaceAnyTransition(root, down, 0.04f,
                (AnimatorConditionMode.Equals, (float)EThiefImpactPhase.Down, "ImpactPhase"));
            ReplaceAnyTransition(root, gettingUp, 0.08f,
                (AnimatorConditionMode.Equals, (float)EThiefImpactPhase.GettingUp, "ImpactPhase"));
            ReplaceTransition(falling, down, 0.04f,
                (AnimatorConditionMode.Equals, (float)EThiefImpactPhase.Down, "ImpactPhase"));
            ReplaceTransition(down, gettingUp, 0.08f,
                (AnimatorConditionMode.Equals, (float)EThiefImpactPhase.GettingUp, "ImpactPhase"));
            ReplaceTransition(gettingUp, idleStanding, 0.1f,
                (AnimatorConditionMode.Equals, (float)EThiefImpactPhase.None, "ImpactPhase"));
        }

        private static void RemoveLayerIfPresent(AnimatorController controller, string layerName)
        {
            int layerIndex = Array.FindIndex(controller.layers, candidate => candidate.name == layerName);
            if (layerIndex >= 0) controller.RemoveLayer(layerIndex);
        }

        private static void ConfigureLiftWindowTransitions(AnimatorStateMachine stateMachine,
            AnimatorState active, AnimatorState empty)
        {
            ReplaceAnyTransition(stateMachine, active, 0.06f,
                (AnimatorConditionMode.Greater, (float)EThiefLiftPhase.None, "LiftPhase"),
                (AnimatorConditionMode.Less, (float)EThiefLiftPhase.Carrying, "LiftPhase"));
            foreach (AnimatorStateTransition transition in active.transitions
                         .Where(candidate => candidate.destinationState == empty).ToArray())
                active.RemoveTransition(transition);
            AnimatorStateTransition cancelled = active.AddTransition(empty);
            ConfigureTransition(cancelled, 0.08f,
                new[] { (AnimatorConditionMode.Equals, (float)EThiefLiftPhase.None, "LiftPhase") });
            AnimatorStateTransition completed = active.AddTransition(empty);
            ConfigureTransition(completed, 0.08f,
                new[] { (AnimatorConditionMode.Greater, (float)EThiefLiftPhase.StandAndOverhead, "LiftPhase") });
        }

        private static AvatarMask BuildHumanoidMask(string path, string name,
            params AvatarMaskBodyPart[] activeParts)
        {
            AvatarMask mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
            if (mask == null)
            {
                mask = new AvatarMask { name = name };
                AssetDatabase.CreateAsset(mask, path);
            }

            for (int index = 0; index < (int)AvatarMaskBodyPart.LastBodyPart; index++)
                mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)index, false);
            foreach (AvatarMaskBodyPart part in activeParts)
                mask.SetHumanoidBodyPartActive(part, true);
            EditorUtility.SetDirty(mask);
            return mask;
        }

        private static bool HasParameter(AnimatorController controller, string name)
        {
            return controller.parameters.Any(parameter => parameter.name == name);
        }

        private static void EnsureParameter(AnimatorController controller, string name,
            AnimatorControllerParameterType type)
        {
            AnimatorControllerParameter existing = controller.parameters
                .FirstOrDefault(parameter => parameter.name == name);
            if (existing != null)
            {
                if (existing.type != type)
                    throw new InvalidOperationException($"Animator parameter {name} has type {existing.type}, expected {type}.");
                return;
            }
            controller.AddParameter(name, type);
        }

        private static AnimatorState EnsureState(AnimatorStateMachine root, string name,
            Motion motion, Vector3 position)
        {
            AnimatorState state = root.states.Select(child => child.state)
                .FirstOrDefault(candidate => candidate.name == name);
            if (state == null) state = root.AddState(name, position);
            state.motion = motion;
            state.writeDefaultValues = true;
            return state;
        }

        private static AnimatorState FindState(AnimatorStateMachine stateMachine, string name)
        {
            AnimatorState state = stateMachine.states.Select(child => child.state)
                .FirstOrDefault(candidate => candidate.name == name);
            if (state != null) return state;
            foreach (ChildAnimatorStateMachine child in stateMachine.stateMachines)
            {
                state = FindState(child.stateMachine, name);
                if (state != null) return state;
            }
            return null;
        }

        private static AnimationClip LoadSyntyClip(string name)
        {
            string[] guids = AssetDatabase.FindAssets($"{name} t:AnimationClip", new[] { SyntyAnimationRoot });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                    .FirstOrDefault(candidate => candidate.name == name);
                if (clip != null) return clip;
            }
            throw new InvalidOperationException($"Synty animation clip missing: {name}");
        }

        private static void ReplaceAnyTransition(AnimatorStateMachine root, AnimatorState destination,
            float duration, params (AnimatorConditionMode mode, float threshold, string parameter)[] conditions)
        {
            foreach (AnimatorStateTransition transition in root.anyStateTransitions
                         .Where(candidate => candidate.destinationState == destination).ToArray())
                root.RemoveAnyStateTransition(transition);
            AnimatorStateTransition created = root.AddAnyStateTransition(destination);
            ConfigureTransition(created, duration, conditions);
            created.canTransitionToSelf = false;
        }

        private static void ReplaceTransition(AnimatorState source, AnimatorState destination,
            float duration, params (AnimatorConditionMode mode, float threshold, string parameter)[] conditions)
        {
            foreach (AnimatorStateTransition transition in source.transitions
                         .Where(candidate => candidate.destinationState == destination).ToArray())
                source.RemoveTransition(transition);
            AnimatorStateTransition created = source.AddTransition(destination);
            ConfigureTransition(created, duration, conditions);
        }

        private static void ConfigureTransition(AnimatorStateTransition transition, float duration,
            (AnimatorConditionMode mode, float threshold, string parameter)[] conditions)
        {
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.interruptionSource = TransitionInterruptionSource.SourceThenDestination;
            foreach ((AnimatorConditionMode mode, float threshold, string parameter) in conditions)
                transition.AddCondition(mode, threshold, parameter);
        }

        private static Subtree BuildBehaviorTree()
        {
            Subtree asset = AssetDatabase.LoadAssetAtPath<Subtree>(TreePath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<Subtree>();
                AssetDatabase.CreateAsset(asset, TreePath);
            }
            else
            {
                asset.Deserialize(true);
            }

            ITreeLogicNode[] nodes =
            {
                Node(new Repeater
                {
                    RepeatForever = new SharedVariable<bool> { Value = true },
                    EndOnFailure = new SharedVariable<bool> { Value = false },
                }, 0, ushort.MaxValue, ushort.MaxValue),
                Node(new Selector { AbortType = ConditionalAbortType.LowerPriority }, 1, 0, ushort.MaxValue),
                Node(new Sequence(), 2, 1, 5),
                Stack(new StackedConditional(), new IsThiefImpactReacting(), 3, 2, 4),
                Stack(new StackedAction(), new RunThiefImpactReaction(), 4, 2, ushort.MaxValue),
                Node(new Sequence(), 5, 1, 12),
                Stack(new StackedConditional(), new IsThiefEscaping(), 6, 5, 7),
                Stack(new StackedAction(), new RunThiefEscape(), 7, 5, ushort.MaxValue),
                Node(new Sequence(), 8, 1, 11),
                Stack(new StackedConditional(), new HasThiefGiftClaim(), 9, 8, 10),
                Stack(new StackedAction(), new RunThiefSteal(), 10, 8, ushort.MaxValue),
                Stack(new StackedAction(), new RunThiefAcquireOrApproach(), 11, 1, ushort.MaxValue),
                Node(new Sequence(), 12, 1, 8),
                Stack(new StackedConditional(), new IsThiefSpotted(), 13, 12, 14),
                Stack(new StackedAction(), new RunThiefBeginSpottedRetreat(), 14, 12, ushort.MaxValue),
            };

            asset.Name = "Thief";
            asset.TreeLogicNodes = nodes;
            asset.LogicNodeProperties = nodes.Select(CreateNodeProperties).ToArray();
            asset.EventNodes = new IEventNode[]
            {
                new Start { Index = 0, Enabled = true, ConnectedIndex = 0 },
            };
            asset.EventNodeProperties = new NodeProperties[] { CreateStartEventProperties() };
            asset.Serialize();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static GameObject BuildThiefPrefab(AnimatorController controller, Subtree behaviorTree)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(BasePrefabPath);
            if (source == null) throw new InvalidOperationException($"Thief source prefab missing: {BasePrefabPath}");

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                root.name = "PF_Thief";
                Transform characterRoot = FindCharacterRoot(root);
                Animator animator = GetOrAdd<Animator>(characterRoot.gameObject);
                animator.runtimeAnimatorController = controller;
                animator.avatar = LoadCharacterAvatar();
                animator.applyRootMotion = false;

                CapsuleCollider collider = GetOrAdd<CapsuleCollider>(root);
                collider.center = new Vector3(0f, 0.9f, 0f);
                collider.height = 1.8f;
                collider.radius = 0.3f;
                Rigidbody body = GetOrAdd<Rigidbody>(root);
                body.isKinematic = true;
                body.useGravity = false;

                NavMeshAgent agent = GetOrAdd<NavMeshAgent>(root);
                agent.radius = 0.3f;
                agent.height = 1.8f;
                agent.angularSpeed = 360f;
                agent.acceleration = 10f;
                agent.stoppingDistance = 0.65f;

                GetOrAdd<NetworkObject>(root);
                GetOrAdd<NetworkTransform>(root);
                ThiefPlayerSensor sensor = GetOrAdd<ThiefPlayerSensor>(root);
                SetFloat(sensor, "_visualRangeM", 10f);
                ThiefMovement movement = GetOrAdd<ThiefMovement>(root);
                ThiefActor actor = GetOrAdd<ThiefActor>(root);
                ThiefImpactReceiver impactReceiver = GetOrAdd<ThiefImpactReceiver>(root);
                ThiefNetworkHub hub = GetOrAdd<ThiefNetworkHub>(root);
                ThiefAnimator thiefAnimator = GetOrAdd<ThiefAnimator>(root);
                ThiefFinalIkAdapter ikAdapter = GetOrAdd<ThiefFinalIkAdapter>(root);
                ThiefExitCountdownView countdownView = GetOrAdd<ThiefExitCountdownView>(root);

                Transform carryAnchor = Child(root.transform, "CarryAnchor", new Vector3(0f, 1.9f, 0.05f));
                Transform leftTarget = Child(carryAnchor, "LeftHandTarget", new Vector3(-0.28f, -0.1f, 0f));
                Transform rightTarget = Child(carryAnchor, "RightHandTarget", new Vector3(0.28f, -0.1f, 0f));
                Transform ikTargets = Child(root.transform, "IkTargets", Vector3.zero);
                Transform bodyTarget = Child(ikTargets, "BodyTarget", new Vector3(0f, 0.8f, 0.15f));
                Transform leftElbowTarget = Child(ikTargets, "LeftElbowTarget", new Vector3(-0.55f, 1.1f, -0.1f));
                Transform rightElbowTarget = Child(ikTargets, "RightElbowTarget", new Vector3(0.55f, 1.1f, -0.1f));
                Transform leftFootTarget = Child(ikTargets, "LeftFootTarget", new Vector3(-0.16f, 0.08f, 0f));
                Transform rightFootTarget = Child(ikTargets, "RightFootTarget", new Vector3(0.16f, 0.08f, 0f));
                Transform countdownRoot = Child(root.transform, "ExitCountdown", new Vector3(0f, 2.8f, 0f));
                Font countdownFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                TextMesh countdownShadow = CreateCountdownText(countdownRoot, "Shadow", countdownFont,
                    new Vector3(0.025f, -0.025f, 0.01f), new Color(0.05f, 0.04f, 0.08f, 0.9f), 99);
                TextMesh countdownLabel = CreateCountdownText(countdownRoot, "Label", countdownFont,
                    Vector3.zero, Color.white, 100);
                countdownRoot.gameObject.SetActive(false);
                Component finalIk = AddFinalIk(characterRoot.gameObject);

                BehaviorTree tree = GetOrAdd<BehaviorTree>(root);
                tree.Subgraph = behaviorTree;
                tree.StartWhenEnabled = false;
                GetOrAdd<ThiefBehaviorTreeAuthority>(root);

                SetReference(movement, "_agent", agent);
                SetReference(movement, "_sensor", sensor);
                SetReference(actor, "_movement", movement);
                SetReference(actor, "_bodyCollider", collider);
                SetReference(actor, "_carryAnchor", carryAnchor);
                SetFloat(actor, "_grabSurfaceClearanceM", 0.25f);
                SetFloat(actor, "_chestBodyClearanceM", 0.15f);
                SetFloat(actor, "_overheadClearanceM", 0.08f);
                SetFloat(actor, "_fallingSeconds", 0.8f);
                SetFloat(actor, "_downSeconds", 1f);
                SetFloat(actor, "_gettingUpSeconds", 1f);
                SetFloat(actor, "_dropGiftMaxDeltaVMps", 3f);
                SetReference(impactReceiver, "_actor", actor);
                SetFloat(impactReceiver, "_knockdownMomentumKgMps", 140f);
                SetReference(hub, "_actor", actor);
                SetReference(thiefAnimator, "_animator", animator);
                SetReference(thiefAnimator, "_networkHub", hub);
                SetReference(thiefAnimator, "_actor", actor);
                SetReference(ikAdapter, "_fullBodyBipedIk", finalIk);
                SetReference(ikAdapter, "_animator", animator);
                SetReference(ikAdapter, "_leftHandTarget", leftTarget);
                SetReference(ikAdapter, "_rightHandTarget", rightTarget);
                SetReference(ikAdapter, "_bodyTarget", bodyTarget);
                SetReference(ikAdapter, "_leftElbowTarget", leftElbowTarget);
                SetReference(ikAdapter, "_rightElbowTarget", rightElbowTarget);
                SetReference(ikAdapter, "_leftFootTarget", leftFootTarget);
                SetReference(ikAdapter, "_rightFootTarget", rightFootTarget);
                SetReference(ikAdapter, "_networkHub", hub);
                SetReference(ikAdapter, "_actor", actor);
                SetReference(countdownView, "_actor", actor);
                SetReference(countdownView, "_networkHub", hub);
                SetReference(countdownView, "_visualRoot", countdownRoot);
                SetReference(countdownView, "_label", countdownLabel);
                SetReference(countdownView, "_shadow", countdownShadow);
                SetFloat(ikAdapter, "_bodyWeight", 0f);
                SetFloat(ikAdapter, "_footPinWeight", 0f);
                SetFloat(ikAdapter, "_palmClearanceM", 0.045f);
                SetFloat(ikAdapter, "_forearmClearanceM", 0.16f);
                SetFloat(ikAdapter, "_supportInset01", 0.22f);

                return PrefabUtility.SaveAsPrefabAsset(root, ThiefPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void BuildRaidRig(GameObject thiefPrefab)
        {
            GameObject root = new GameObject("PF_ThiefRaidRig");
            try
            {
                GameObject volumeObject = new GameObject("LootVolume");
                volumeObject.transform.SetParent(root.transform, false);
                BoxCollider volume = volumeObject.AddComponent<BoxCollider>();
                volume.isTrigger = true;
                volume.size = new Vector3(8f, 4f, 8f);
                ThiefRaidSite site = volumeObject.AddComponent<ThiefRaidSite>();

                Transform approach = Child(root.transform, "ApproachPoint", new Vector3(0f, 0f, -3.2f));
                ThiefDirector director = root.AddComponent<ThiefDirector>();
                SetReference(site, "_lootVolume", volume);
                SetReferenceArray(site, "_approachPoints", new UnityEngine.Object[] { approach });
                SetReference(director, "_raidSite", site);
                SetReference(director, "_thiefPrefab", thiefPrefab);
                PrefabUtility.SaveAsPrefabAsset(root, RigPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Component AddFinalIk(GameObject target)
        {
            Type type = Type.GetType("RootMotion.FinalIK.FullBodyBipedIK, Assembly-CSharp-firstpass");
            if (type == null)
            {
                Debug.LogWarning("Final IK FullBodyBipedIK type was not found. Thief logic remains functional without IK.");
                return null;
            }
            Component component = target.GetComponent(type) ?? target.AddComponent(type);
            MethodInfo autoDetect = type.GetMethod("AutoDetectReferences",
                BindingFlags.Instance | BindingFlags.NonPublic);
            autoDetect?.Invoke(component, null);
            return component;
        }

        private static Avatar LoadCharacterAvatar()
        {
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(CharacterFbxPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
                throw new InvalidOperationException($"Valid humanoid Avatar missing: {CharacterFbxPath}");
            return avatar;
        }

        private static Transform FindCharacterRoot(GameObject root)
        {
            Transform hips = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => candidate.name == "Hips");
            if (hips == null) throw new InvalidOperationException("Creative Characters Hips bone missing.");
            Transform characterRoot = hips;
            while (characterRoot.parent != root.transform)
            {
                characterRoot = characterRoot.parent;
                if (characterRoot == null) throw new InvalidOperationException("Character root is outside prefab.");
            }
            return characterRoot;
        }

        private static T Node<T>(T node, ushort index, ushort parentIndex, ushort siblingIndex)
            where T : class, ITreeLogicNode
        {
            node.Index = index;
            node.ParentIndex = parentIndex;
            node.SiblingIndex = siblingIndex;
            node.Enabled = true;
            return node;
        }

        private static T Stack<T>(T node, Task task, ushort index, ushort parentIndex, ushort siblingIndex)
            where T : StackedTask
        {
            task.Enabled = true;
            node.Tasks = new[] { task };
            return Node(node, index, parentIndex, siblingIndex);
        }

        private static LogicNodeProperties CreateNodeProperties(ITreeLogicNode node)
        {
            return new LogicNodeProperties
            {
                Data = new LogicNodeProperties.NodeData
                {
                    ParentIndex = node.ParentIndex,
                    SiblingIndex = node.SiblingIndex,
                    IsParent = node is not StackedTask,
                    ContainedNodeTypes = Array.Empty<string>(),
                    ContainedNodeNames = Array.Empty<string>(),
                },
            };
        }

        private static LogicNodeProperties CreateStartEventProperties()
        {
            return new LogicNodeProperties
            {
                GuidString = "93fc152b-4cd5-4e91-aa14-f612d1e9ba5d",
                Position = new Vector2(80f, -100f),
                Width = 160f,
                DisplayName = "Start",
                Data = new LogicNodeProperties.NodeData
                {
                    ParentIndex = 0,
                    SiblingIndex = 0,
                    IsParent = false,
                    ContainedNodeTypes = Array.Empty<string>(),
                    ContainedNodeNames = Array.Empty<string>(),
                },
            };
        }

        private static Transform Child(Transform parent, string name, Vector3 localPosition)
        {
            Transform existing = parent.Find(name);
            if (existing != null) return existing;
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            child.transform.localPosition = localPosition;
            return child.transform;
        }

        private static TextMesh CreateCountdownText(Transform parent, string name, Font font,
            Vector3 localPosition, Color color, int sortingOrder)
        {
            Transform textTransform = Child(parent, name, localPosition);
            TextMesh text = GetOrAdd<TextMesh>(textTransform.gameObject);
            text.font = font;
            text.text = "5";
            text.color = color;
            text.fontSize = 64;
            text.characterSize = 0.12f;
            text.fontStyle = FontStyle.Bold;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.richText = false;

            MeshRenderer renderer = text.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = font != null ? font.material : null;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sortingOrder = sortingOrder;
            }
            return text;
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static void SetReference(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetReferenceArray(UnityEngine.Object target, string propertyName,
            UnityEngine.Object[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector2(UnityEngine.Object target, string propertyName, Vector2 value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).vector2Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(UnityEngine.Object target, string propertyName, float value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(UnityEngine.Object target, string propertyName, int value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
