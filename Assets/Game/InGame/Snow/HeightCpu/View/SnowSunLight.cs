using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 눈 셰이더의 <c>_SunDir</c> 을 정할 <b>방향광</b>을 고른다.
    ///
    /// <para><b>왜 이게 필요한가.</b> 두 뷰가 모두 <c>FindAnyObjectByType&lt;Light&gt;()</c> 로
    /// <b>아무 라이트나</b> 집고 있었다. 눈 테스트 씬은 라이트가 <c>Sun</c> 하나뿐이라 우연히
    /// 맞았지만, <c>SinglePlay</c> 는 가로등 18 + 창문광 11 + 태양 1 = 30 개라 포인트 라이트를
    /// 집는다. 포인트 라이트의 <c>forward</c> 는 조명 방향으로서 아무 의미가 없다.</para>
    ///
    /// <para><b>증상이 눈에 안 띄는 이유.</b> 값이 틀려도 화면은 그냥 "눈이 좀 밋밋하네" 로
    /// 보인다. <c>_SunDir</c> 은 <c>SnowCasualApply</c> 의 밴딩이 읽는 입력이라, 방향이 어긋나면
    /// 표면 전체가 한 밴드에 몰려 계조가 사라진다. 게다가 URP 섀도맵은 <b>진짜</b> 메인 라이트
    /// 기준으로 그려지므로, 그림자와 음영이 서로 다른 방향을 보게 된다 — 그림자가 "이상한 자리에
    /// 있다" 가 아니라 "없는 것처럼" 보이는 원인이다.</para>
    ///
    /// <para>두 호출부(<c>SnowCpuStageView</c> · <c>SnowDisplaceView</c>)가 같은 규칙을 써야
    /// 마처와 저사양 경로가 같은 방향으로 음영을 낸다. 그것이 이 헬퍼가 존재하는 이유이고,
    /// 프로젝트 규칙이 말하는 "두 번째 호출부가 확인된 뒤의 추상화" 다.</para>
    /// </summary>
    public static class SnowSunLight
    {
        /// <summary>
        /// 씬의 태양. 없으면 <c>null</c> — 호출자는 그때 <c>_SunDir</c> 을 건드리지 않는다
        /// (마지막 유효값이 남는 편이 0 벡터로 뭉개는 것보다 낫다).
        /// </summary>
        public static Light Resolve()
        {
            // Lighting 창에서 지정한 Sun Source 가 있으면 그것이 의도된 답이다.
            Light designated = RenderSettings.sun;
            if (designated != null && designated.type == LightType.Directional
                                   && designated.isActiveAndEnabled) return designated;

            // 없으면 가장 밝은 방향광. URP 가 메인 라이트를 고르는 기준과 같아서, 섀도맵을 그리는
            // 라이트와 우리가 음영에 쓰는 라이트가 일치한다.
            Light best = null;
            Light[] all = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Light l = all[i];
                if (l.type != LightType.Directional || !l.isActiveAndEnabled) continue;
                if (best == null || l.intensity > best.intensity) best = l;
            }
            return best;
        }
    }
}
