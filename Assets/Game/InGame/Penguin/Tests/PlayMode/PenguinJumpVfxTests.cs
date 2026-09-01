using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class PenguinJumpVfxTests
    {
        private GameObject _penguin;

        [TearDown]
        public void TearDown()
        {
            if (_penguin != null) Object.DestroyImmediate(_penguin);
            DestroyEffects();
        }

        private static void DestroyEffects()
        {
            foreach (ParticleSystem particle in Object.FindObjectsByType<ParticleSystem>(
                         FindObjectsInactive.Include))
            {
                if (particle.transform.root.name == "FX_PenguinJump")
                    Object.DestroyImmediate(particle.transform.root.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator 점프_VFX는_지면과_무관하게_연한_하늘색을_쓴다()
        {
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab");
#else
            GameObject prefab = null;
#endif
            Assert.IsNotNull(prefab);
            _penguin = Object.Instantiate(prefab);
            _penguin.name = "__TEST__JumpVfxPenguin";
            _penguin.transform.rotation = Quaternion.Euler(0f, 37f, 0f);

            PenguinInputReader input = _penguin.GetComponent<PenguinInputReader>();
            input.enabled = false;
            PenguinLocomotion locomotion = _penguin.GetComponent<PenguinLocomotion>();
            Assert.IsNotNull(_penguin.GetComponent<PenguinJumpVfx>());

            DestroyEffects();
            float expectedY = _penguin.GetComponent<CapsuleCollider>().bounds.min.y + 0.01f;
            Vector3 footForward = _penguin.transform.forward;
            locomotion.RaisePresentationJump();
            yield return null;
            AssertEffectColor(new Color(0.62f, 0.88f, 1f, 1f));
            AssertEffectRotation(footForward);
            Assert.That(FindEffect().transform.position.y, Is.EqualTo(expectedY).Within(0.001f),
                "VFX는 캡슐 바닥에서 1cm만 위로 띄워 생성돼야 한다");
        }

        private static void AssertEffectColor(Color expected)
        {
            GameObject effect = FindEffect();
            ParticleSystem[] particles = effect.GetComponentsInChildren<ParticleSystem>(true);
            Assert.Greater(particles.Length, 0);
            foreach (ParticleSystem particle in particles)
            {
                Color actual = particle.main.startColor.color;
                Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f));
                Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f));
                Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f));
                Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.001f));

                ParticleSystemRenderer renderer = particle.GetComponent<ParticleSystemRenderer>();
                Assert.IsNotNull(renderer.sharedMaterial);
                Assert.AreEqual("Universal Render Pipeline/Particles/Unlit",
                    renderer.sharedMaterial.shader.name);
                Assert.AreEqual(Color.white, renderer.sharedMaterial.GetColor("_BaseColor"));

                ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particle.colorOverLifetime;
                if (!colorOverLifetime.enabled) continue;
                foreach (GradientColorKey key in colorOverLifetime.color.gradient.colorKeys)
                    Assert.AreEqual(Color.white, key.color,
                        "Color over Lifetime은 알파만 바꾸고 RGB를 탁하게 만들면 안 된다");
            }
        }

        private static void AssertEffectRotation(Vector3 footForward)
        {
            Transform effect = FindEffect().transform;
            Assert.That(Vector3.Angle(effect.forward, Vector3.up), Is.LessThan(0.1f),
                "VFX의 진행축이 점프 힘의 방향을 향해야 한다");
            Assert.That(Vector3.Angle(effect.up, footForward), Is.LessThan(0.1f),
                "VFX의 롤 방향이 펭귄 발의 앞방향을 따라야 한다");
        }

        private static GameObject FindEffect()
        {
            GameObject effect = null;
            foreach (ParticleSystem particle in Object.FindObjectsByType<ParticleSystem>(
                         FindObjectsInactive.Include))
            {
                GameObject root = particle.transform.root.gameObject;
                if (root.name != "FX_PenguinJump") continue;
                if (effect == null) effect = root;
                else Assert.AreSame(effect, root, "이번 점프에서 VFX 인스턴스가 하나만 생성돼야 한다");
            }

            Assert.IsNotNull(effect, "점프 이벤트가 Synty VFX 인스턴스를 만들어야 한다");
            return effect;
        }
    }
}
