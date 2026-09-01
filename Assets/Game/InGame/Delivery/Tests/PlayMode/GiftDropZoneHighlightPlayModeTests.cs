using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class GiftDropZoneHighlightPlayModeTests
    {
        [UnityTest]
        public IEnumerator 배달_지점은_입체_메시와_펭귄_칭찬_도장_효과를_만든다()
        {
            var zoneObject = new GameObject("__TEST__GiftZone");
            var effectObject = new GameObject("__TEST__GiftDropZoneEffect");
            effectObject.SetActive(false);

            try
            {
                Color giftColor = Gift.ColorForKind(EGiftBoxKind.Blue);
                GiftDropZoneHighlight effect = effectObject.AddComponent<GiftDropZoneHighlight>();
                effect.Configure(zoneObject.transform, new Vector2(2.5f, 2.5f), giftColor);
                effectObject.SetActive(true);
                yield return null;

                Transform marker = effectObject.transform.Find("GroundMarker");
                Assert.That(marker, Is.Not.Null);
                Assert.That(marker.Find("GiftGlyph"), Is.Null, "선물 상자 글리프가 남으면 펭귄 장판으로 읽히지 않는다.");
                Transform mat = marker.Find("PenguinMatMesh");
                Assert.That(mat, Is.Not.Null, "눈과 비스듬한 카메라에서도 보이는 얇은 입체 받침이 필요하다.");
                Assert.That(mat.GetComponent<MeshFilter>().sharedMesh.bounds.size.y, Is.GreaterThanOrEqualTo(0.07f));
                Assert.That(marker.Find("PenguinBody"), Is.Null, "조각난 기하학 펭귄이 PNG 도장과 겹치면 안 된다.");
                Transform stamp = marker.Find("PenguinPraiseStampDecal");
                Assert.That(stamp, Is.Not.Null);
                Assert.That(stamp.GetComponent<MeshFilter>().sharedMesh.uv, Is.Not.Empty);
                Assert.That(stamp.GetComponent<MeshFilter>().sharedMesh.bounds.size.x, Is.GreaterThanOrEqualTo(2.0f),
                    "참 잘했어요 도장은 2.5m 배달 지점에서 멀리서도 읽히는 크기여야 한다.");
                var stampProperties = new MaterialPropertyBlock();
                stamp.GetComponent<MeshRenderer>().GetPropertyBlock(stampProperties);
                Assert.That(stampProperties.GetTexture(Shader.PropertyToID("_BaseMap")), Is.Not.Null);
                Assert.That(stampProperties.GetFloat(Shader.PropertyToID("_UseBaseMap")), Is.EqualTo(1f));
                Assert.That(stampProperties.GetFloat(Shader.PropertyToID("_UseAccentRemap")), Is.EqualTo(1f));
                Color stampAccent = stampProperties.GetColor(Shader.PropertyToID("_AccentColor"));
                Assert.That(stampAccent.r, Is.EqualTo(giftColor.r).Within(0.001f));
                Assert.That(stampAccent.g, Is.EqualTo(giftColor.g).Within(0.001f));
                Assert.That(stampAccent.b, Is.EqualTo(giftColor.b).Within(0.001f));
                Assert.That(stamp.GetComponent<MeshRenderer>().sortingOrder, Is.GreaterThan(0));
                Assert.That(marker.Find("RisingGiftColorSteam"), Is.Not.Null);
                Assert.That(effectObject.GetComponent<MeshRenderer>(), Is.Null,
                    "예전 공중 직육면체 렌더러가 루트에 남으면 안 된다.");

                MeshRenderer innerRing = marker.Find("GiftColorInnerRing").GetComponent<MeshRenderer>();
                var properties = new MaterialPropertyBlock();
                innerRing.GetPropertyBlock(properties);
                Color applied = properties.GetColor(Shader.PropertyToID("_BaseColor"));
                Assert.That(applied.r, Is.EqualTo(giftColor.r).Within(0.001f));
                Assert.That(applied.g, Is.EqualTo(giftColor.g).Within(0.001f));
                Assert.That(applied.b, Is.EqualTo(giftColor.b).Within(0.001f));

                ParticleSystem steam = marker.Find("RisingGiftColorSteam").GetComponent<ParticleSystem>();
                Assert.That(steam.main.startLifetime.constantMax, Is.GreaterThanOrEqualTo(3f));
                Assert.That(steam.main.startSizeY.constantMax, Is.GreaterThanOrEqualTo(0.6f));
                Assert.That(steam.velocityOverLifetime.y.constantMax, Is.GreaterThanOrEqualTo(0.5f));
                Assert.That(steam.isPlaying, Is.True);

                effect.PlayCompletion();
                yield return null;
                ParticleSystem burst = effectObject.transform.Find("GiftAcceptedBurst").GetComponent<ParticleSystem>();
                Assert.That(steam.isEmitting, Is.False);
                Assert.That(burst.isPlaying, Is.True);
            }
            finally
            {
                Object.Destroy(effectObject);
                Object.Destroy(zoneObject);
            }
        }
    }
}
