using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PPack
{
    public sealed class GiftAppearanceTests
    {
        private const string PrefabPath =
            "Assets/Game/InGame/Delivery/Prefabs/PF_GiftBox_Variable.prefab";

        [Test]
        public void 선물_프리팹은_배달_마커와_무작위_외형을_함께_가진다()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<Gift>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<GiftAppearance>(), Is.Not.Null);
            Assert.That(prefab.GetComponent<BoxCollider>(), Is.Not.Null);
            Assert.That(prefab.GetComponentsInChildren<Renderer>(true).Length, Is.EqualTo(7));
        }

        [Test]
        public void 리본_두_줄은_뚜껑_위에서_십자가로_보인다()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = Object.Instantiate(prefab);
            try
            {
                GiftAppearance appearance = instance.GetComponent<GiftAppearance>();
                appearance.Randomize(1225);

                Transform lid = instance.transform.Find("Lid");
                Transform widthRibbon = instance.transform.Find("Ribbon_Width");
                Transform depthRibbon = instance.transform.Find("Ribbon_Depth");
                float lidTop = lid.localPosition.y + lid.localScale.y * 0.5f;

                Assert.That(widthRibbon.localPosition.y + widthRibbon.localScale.y * 0.5f,
                    Is.GreaterThan(lidTop));
                Assert.That(depthRibbon.localPosition.y + depthRibbon.localScale.y * 0.5f,
                    Is.GreaterThan(lidTop));
                Assert.That(widthRibbon.localScale.x, Is.GreaterThan(widthRibbon.localScale.z));
                Assert.That(depthRibbon.localScale.z, Is.GreaterThan(depthRibbon.localScale.x));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void 같은_시드는_같은_크기와_색을_만든다()
        {
            var gameObject = new GameObject("__TEST__GiftAppearance");
            try
            {
                GiftAppearance appearance = gameObject.AddComponent<GiftAppearance>();
                appearance.Randomize(1701);
                Vector3 firstSize = appearance.Size;
                Color firstBox = appearance.BoxColor;
                Color firstRibbon = appearance.RibbonColor;

                appearance.Randomize(1701);

                Assert.That(appearance.Size, Is.EqualTo(firstSize));
                Assert.That(appearance.BoxColor, Is.EqualTo(firstBox));
                Assert.That(appearance.RibbonColor, Is.EqualTo(firstRibbon));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void 무작위_외형은_허용_크기와_대비되는_팔레트_안에_머문다()
        {
            var gameObject = new GameObject("__TEST__GiftAppearanceRange");
            try
            {
                GiftAppearance appearance = gameObject.AddComponent<GiftAppearance>();
                for (int seed = 1; seed <= 64; seed++)
                {
                    appearance.Randomize(seed);
                    Vector3 size = appearance.Size;
                    Assert.That(size.x, Is.InRange(appearance.MinimumSize.x, appearance.MaximumSize.x));
                    Assert.That(size.y, Is.InRange(appearance.MinimumSize.y, appearance.MaximumSize.y));
                    Assert.That(size.z, Is.InRange(appearance.MinimumSize.z, appearance.MaximumSize.z));

                    Color difference = appearance.BoxColor - appearance.RibbonColor;
                    float distanceSquared = difference.r * difference.r
                                          + difference.g * difference.g
                                          + difference.b * difference.b;
                    Assert.That(distanceSquared, Is.GreaterThanOrEqualTo(0.16f));
                }
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
