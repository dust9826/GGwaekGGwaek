using System;
using System.Collections.Generic;
using System.Linq;
using Fusion;
using Opsive.BehaviorDesigner.Runtime;
using Opsive.BehaviorDesigner.Runtime.Tasks;
using Opsive.BehaviorDesigner.Runtime.Tasks.Actions;
using Opsive.BehaviorDesigner.Runtime.Tasks.Composites;
using Opsive.BehaviorDesigner.Runtime.Tasks.Conditionals;
using Opsive.BehaviorDesigner.Runtime.Tasks.Decorators;
using Opsive.GraphDesigner.Runtime;
using Opsive.GraphDesigner.Runtime.Variables;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace PPack
{
    public static class CreativePedestrianAssetBuilder
    {
        private const string CreativeRoot =
            "Assets/Game/InGame/creature asset/Creative_Characters";
        private const string FeatureRoot =
            "Assets/Game/InGame/Interaction/Npc/Pedestrian";
        private const string CatalogPath = FeatureRoot + "/Appearance/NpcAppearanceCatalog.asset";
        private const string ControllerPath = FeatureRoot + "/Animations/AC_Pedestrian.controller";
        private const string RuntimeClipFolder = FeatureRoot + "/Animations/RuntimeClips";
        private const string BehaviorTreePath = FeatureRoot + "/Behavior/PedestrianBehaviorTree.asset";
        private const string PrefabPath = FeatureRoot + "/Prefabs/PF_Pedestrian.prefab";
        private const string CharacterFbxPath = CreativeRoot + "/Meshes/Base_Mesh.fbx";

        private static readonly SlotSource[] SlotSources = {
            new(ENpcAppearanceSlot.Body, "Body", "Body"),
            new(ENpcAppearanceSlot.Face, "Faces", "Faces"),
            new(ENpcAppearanceSlot.Hair, "Hairstyle", "Hairstyle"),
            new(ENpcAppearanceSlot.Top, "Outfit", "T_Shirt"),
            new(ENpcAppearanceSlot.Coat, "Outwear", "Outerwear"),
            new(ENpcAppearanceSlot.Pants, "Pants", "Pants"),
            new(ENpcAppearanceSlot.Shoes, "Shoes", "Shoes"),
            new(ENpcAppearanceSlot.Hat, "Hat", "Hat"),
        };

        [MenuItem("PPack/NPC/Rebuild Creative Pedestrian Assets")]
        public static void Rebuild()
        {
            EnsureFolder(FeatureRoot, "Appearance");
            EnsureFolder(FeatureRoot, "Prefabs");
            EnsureFolder(FeatureRoot + "/Animations", "RuntimeClips");

            NpcAppearanceCatalog catalog = BuildCatalog();
            AnimatorController controller = BuildAnimatorController();
            Subtree behaviorTree = BuildBehaviorTree();
            BuildPrefab(catalog, controller, behaviorTree);
            AssetDatabase.DeleteAsset(FeatureRoot + "/Animations/Getting Up.fbx");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Creative Pedestrian assets rebuilt: {PrefabPath}");
        }

        private static NpcAppearanceCatalog BuildCatalog()
        {
            NpcAppearanceCatalog catalog = AssetDatabase.LoadAssetAtPath<NpcAppearanceCatalog>(CatalogPath);
            if (catalog == null) {
                catalog = ScriptableObject.CreateInstance<NpcAppearanceCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            List<NpcAppearanceCatalog.Entry> entries = new List<NpcAppearanceCatalog.Entry>();
            HashSet<int> usedIds = new HashSet<int>();
            foreach (SlotSource source in SlotSources) {
                string folder = CreativeRoot + "/Prefabs/" + source.Folder;
                string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
                Array.Sort(guids, StringComparer.Ordinal);
                foreach (string guid in guids) {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    Mesh mesh = FindMesh(prefab, source.RendererName);
                    if (mesh == null) continue;

                    int id = StableId(guid);
                    if (!usedIds.Add(id)) {
                        throw new InvalidOperationException($"NPC appearance ID collision: {guid} -> {id}");
                    }
                    entries.Add(new NpcAppearanceCatalog.Entry(source.Slot, id, prefab.name, mesh));
                }
            }

            catalog.SetEntriesForEditor(entries.ToArray());
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static AnimatorController BuildAnimatorController()
        {
            AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AddState(stateMachine, "Idle", "Idle_Relaxed", true, true, new Vector3(100f, 80f));
            AddState(stateMachine, "Walk", "Walk_Forward", false, true, new Vector3(320f, 30f));
            AddState(stateMachine, "Run", "Run_Forward", false, true, new Vector3(320f, 110f));
            AddState(stateMachine, "HitReaction", "Hit_Reaction_Heavy", false, false, new Vector3(550f, 30f));
            AddState(stateMachine, "Attack", "Attack_Punch", false, false, new Vector3(550f, 110f));
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static Subtree BuildBehaviorTree()
        {
            Subtree asset = AssetDatabase.LoadAssetAtPath<Subtree>(BehaviorTreePath);
            if (asset == null) {
                asset = ScriptableObject.CreateInstance<Subtree>();
                AssetDatabase.CreateAsset(asset, BehaviorTreePath);
            }
            else {
                asset.Deserialize(true);
            }

            IEventNode[] eventNodes = asset.EventNodes ?? Array.Empty<IEventNode>();
            NodeProperties[] eventProperties = asset.EventNodeProperties ?? Array.Empty<NodeProperties>();
            ITreeLogicNode[] nodes = {
                Node(new Repeater {
                    RepeatForever = new SharedVariable<bool> { Value = true },
                    EndOnFailure = new SharedVariable<bool> { Value = false },
                }, 0, ushort.MaxValue, ushort.MaxValue),
                Node(new Selector { AbortType = ConditionalAbortType.LowerPriority },
                    1, 0, ushort.MaxValue),
                Node(new Sequence(), 2, 1, 5),
                Stack(new StackedConditional(), new HasPendingPedestrianImpact(), 3, 2, 4),
                Stack(new StackedAction(), new RunPedestrianHitReaction(), 4, 2, ushort.MaxValue),
                Node(new Sequence(), 5, 1, 12),
                Stack(new StackedConditional(), new HasPedestrianIncident(), 6, 5, 7),
                Node(new Selector(), 7, 5, ushort.MaxValue),
                Node(new Sequence(), 8, 7, 11),
                Stack(new StackedConditional(), new IsAggressivePedestrian(), 9, 8, 10),
                Stack(new StackedAction(), new RunPedestrianReaction {
                    Reaction = EPedestrianAction.Attack,
                }, 10, 8, ushort.MaxValue),
                Stack(new StackedAction(), new RunPedestrianReaction {
                    Reaction = EPedestrianAction.Flee,
                }, 11, 7, ushort.MaxValue),
                Stack(new StackedAction(), new RunPedestrianNormalBehavior(),
                    12, 1, ushort.MaxValue),
            };

            asset.Name = "Pedestrian";
            asset.TreeLogicNodes = nodes;
            asset.LogicNodeProperties = nodes.Select(CreateNodeProperties).ToArray();
            asset.EventNodes = eventNodes;
            asset.EventNodeProperties = eventProperties;
            asset.Serialize();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void BuildPrefab(NpcAppearanceCatalog catalog, AnimatorController controller,
            Subtree behaviorTree)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(
                CreativeRoot + "/Prefabs/Base_Mesh.prefab");
            if (source == null) throw new InvalidOperationException("Creative Characters Base_Mesh prefab missing.");

            GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try {
                root.name = "PF_Pedestrian";
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
                agent.speed = 1.5f;
                agent.angularSpeed = 360f;
                agent.acceleration = 8f;
                agent.stoppingDistance = 0.8f;

                GetOrAdd<NetworkObject>(root);
                PedestrianContext context = GetOrAdd<PedestrianContext>(root);
                PedestrianAppearance appearance = GetOrAdd<PedestrianAppearance>(root);
                PedestrianNetworkHub hub = GetOrAdd<PedestrianNetworkHub>(root);
                GetOrAdd<PedestrianImpactReceiver>(root);
                GetOrAdd<PedestrianBehaviorExecutor>(root);
                PedestrianAnimator pedestrianAnimator = GetOrAdd<PedestrianAnimator>(root);
                GetOrAdd<NpcGroupMember>(root);

                BehaviorTree tree = GetOrAdd<BehaviorTree>(root);
                tree.Subgraph = behaviorTree;
                tree.StartWhenEnabled = false;
                GetOrAdd<NpcBehaviorTreeAuthority>(root);

                SetObjectReference(appearance, "_catalog", catalog);
                SetObjectReference(hub, "_appearanceCatalog", catalog);
                SetObjectReference(hub, "_appearance", appearance);
                SetObjectReference(hub, "_context", context);
                SetObjectReference(pedestrianAnimator, "_animator", animator);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally {
                UnityEngine.Object.DestroyImmediate(root);
            }
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

        private static T Stack<T>(T node, Task task, ushort index, ushort parentIndex,
            ushort siblingIndex) where T : StackedTask
        {
            task.Enabled = true;
            node.Tasks = new[] { task };
            return Node(node, index, parentIndex, siblingIndex);
        }

        private static LogicNodeProperties CreateNodeProperties(ITreeLogicNode node)
        {
            LogicNodeProperties properties = new LogicNodeProperties();
            properties.Data = new LogicNodeProperties.NodeData {
                ParentIndex = node.ParentIndex,
                SiblingIndex = node.SiblingIndex,
                IsParent = node is not StackedTask,
                ContainedNodeTypes = Array.Empty<string>(),
                ContainedNodeNames = Array.Empty<string>(),
            };
            return properties;
        }

        private static void AddState(AnimatorStateMachine stateMachine, string stateName,
            string clipName, bool isDefault, bool loop, Vector3 position)
        {
            AnimationClip clip = BuildRuntimeClip(clipName, loop);
            AnimatorState state = stateMachine.AddState(stateName, position);
            state.motion = clip;
            if (isDefault) stateMachine.defaultState = state;
        }

        private static AnimationClip BuildRuntimeClip(string clipName, bool loop)
        {
            AnimationClip source = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                CreativeRoot + "/Animations/Other_Animations/" + clipName + ".anim");
            if (source == null) throw new InvalidOperationException($"Animation clip missing: {clipName}");

            string path = RuntimeClipFolder + "/" + clipName + ".anim";
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null) {
                clip = new AnimationClip();
                AssetDatabase.CreateAsset(clip, path);
            }

            EditorUtility.CopySerialized(source, clip);
            clip.name = clipName;
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        private static Avatar LoadCharacterAvatar()
        {
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(CharacterFbxPath)
                .OfType<Avatar>()
                .FirstOrDefault();
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
            while (characterRoot.parent != root.transform) {
                characterRoot = characterRoot.parent;
                if (characterRoot == null)
                    throw new InvalidOperationException("Creative Characters root is outside the prefab.");
            }
            return characterRoot;
        }

        private static Mesh FindMesh(GameObject prefab, string rendererName)
        {
            if (prefab == null) return null;
            SkinnedMeshRenderer exact = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(renderer => renderer.name == rendererName);
            SkinnedMeshRenderer renderer = exact ??
                prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
            return renderer != null ? renderer.sharedMesh : null;
        }

        private static int StableId(string guid)
        {
            int id = (int)(Convert.ToUInt32(guid.Substring(0, 8), 16) & 0x7fffffff);
            return id == 0 ? 1 : id;
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static void SetObjectReference(UnityEngine.Object target, string propertyName,
            UnityEngine.Object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        }

        private readonly struct SlotSource
        {
            public ENpcAppearanceSlot Slot { get; }
            public string Folder { get; }
            public string RendererName { get; }

            public SlotSource(ENpcAppearanceSlot slot, string folder, string rendererName)
            {
                Slot = slot;
                Folder = folder;
                RendererName = rendererName;
            }
        }
    }
}
