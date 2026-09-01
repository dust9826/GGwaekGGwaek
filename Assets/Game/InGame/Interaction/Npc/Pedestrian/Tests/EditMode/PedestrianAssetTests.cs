using System;
using System.Linq;
using NUnit.Framework;
using Opsive.BehaviorDesigner.Runtime;
using UnityEditor;
using UnityEngine;

namespace PPack
{
    public sealed class PedestrianAssetTests
    {
        private const string CatalogPath =
            "Assets/Game/InGame/Interaction/Npc/Pedestrian/Appearance/NpcAppearanceCatalog.asset";
        private const string PrefabPath =
            "Assets/Game/InGame/Interaction/Npc/Pedestrian/Prefabs/PF_Pedestrian.prefab";
        private const string CharacterFbxPath =
            "Assets/Game/InGame/creature asset/Creative_Characters/Meshes/Base_Mesh.fbx";

        [Test]
        public void 외형_카탈로그는_모든_슬롯과_고유한_ID를_가진다()
        {
            NpcAppearanceCatalog catalog = AssetDatabase.LoadAssetAtPath<NpcAppearanceCatalog>(CatalogPath);

            Assert.That(catalog, Is.Not.Null);
            foreach (ENpcAppearanceSlot slot in Enum.GetValues(typeof(ENpcAppearanceSlot))) {
                Assert.That(catalog.Entries.Count(entry => entry.Slot == slot), Is.GreaterThan(0), slot.ToString());
            }
            Assert.That(catalog.Entries.Select(entry => entry.Id).Distinct().Count(),
                Is.EqualTo(catalog.Entries.Count));
            Assert.That(catalog.Entries.All(entry => entry.Id != 0 && entry.Mesh != null), Is.True);
        }

        [Test]
        public void 보행자_프리팹은_Creative_외형과_프로필_행동_구성요소를_가진다()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<PedestrianAppearance>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<PedestrianNetworkHub>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<PedestrianContext>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<PedestrianBehaviorExecutor>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<PedestrianImpactReceiver>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BehaviorTree>(), Is.Not.Null);
            Assert.That(prefab.GetComponents<Component>().Any(component =>
                component.GetType() == typeof(NpcMaleLocomotionAnimator)), Is.False);
        }

        [Test]
        public void 보행자_애니메이터는_Creative_기본_동작만_참조한다()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Animator animator = prefab.GetComponentInChildren<Animator>(true);
            string[] clipNames = animator.runtimeAnimatorController.animationClips
                .Select(clip => clip.name)
                .ToArray();

            Assert.That(prefab.GetComponent<Animator>(), Is.Null);
            Assert.That(animator.transform, Is.Not.EqualTo(prefab.transform));
            Assert.That(animator.GetBoneTransform(HumanBodyBones.Hips), Is.Not.Null);
            Assert.That(animator.avatar, Is.Not.Null);
            Assert.That(animator.avatar.isValid, Is.True);
            Assert.That(animator.avatar.isHuman, Is.True);
            Assert.That(AssetDatabase.GetAssetPath(animator.avatar), Is.EqualTo(CharacterFbxPath));
            Assert.That(clipNames, Is.EquivalentTo(new[] {
                "Idle_Relaxed", "Walk_Forward", "Run_Forward",
                "Hit_Reaction_Heavy", "Attack_Punch",
            }));

            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips) {
                bool expectedLoop = clip.name == "Idle_Relaxed" || clip.name == "Walk_Forward" ||
                    clip.name == "Run_Forward";
                Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime,
                    Is.EqualTo(expectedLoop), clip.name);
                Assert.That(AssetDatabase.GetAssetPath(clip),
                    Does.StartWith("Assets/Game/InGame/Interaction/Npc/Pedestrian/Animations/RuntimeClips/"));
            }
        }

        [Test]
        public void 외형을_교체하면_렌더러_경계가_새_메시에_맞춰진다()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try {
                PedestrianAppearance appearance = root.GetComponent<PedestrianAppearance>();
                NpcAppearanceCatalog catalog = AssetDatabase.LoadAssetAtPath<NpcAppearanceCatalog>(CatalogPath);

                foreach (NpcAppearanceCatalog.Entry entry in catalog.Entries) {
                    NpcAppearanceData data = default;
                    data.SetId(entry.Slot, entry.Id);
                    appearance.Apply(data);

                    SkinnedMeshRenderer renderer = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .Single(candidate => candidate.gameObject.activeSelf &&
                            candidate.sharedMesh == entry.Mesh);
                    Assert.That(renderer.gameObject.activeSelf, Is.True, entry.ClueLabel);
                    Assert.That(renderer.localBounds.center, Is.EqualTo(entry.Mesh.bounds.center), entry.ClueLabel);
                    Assert.That(renderer.localBounds.size, Is.EqualTo(entry.Mesh.bounds.size), entry.ClueLabel);
                }
            }
            finally {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }
}
