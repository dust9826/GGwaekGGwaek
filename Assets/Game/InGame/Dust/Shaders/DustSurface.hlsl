#ifndef PPACK_DUST_SURFACE_INCLUDED
#define PPACK_DUST_SURFACE_INCLUDED

// 오염 마스크 샘플 — RGBA 확장 시 고치는 유일한 지점.
// 규약: R 채널, 1 = 더러움.
float SampleDirt(TEXTURE2D_PARAM(dirtMask, dirtSampler), float2 uv)
{
    return SAMPLE_TEXTURE2D(dirtMask, dirtSampler, uv).r;
}

// ---------------------------------------------------------------------------
// 확률적 타일링
//
// uv * 16 으로 같은 텍스처를 16x16 번 똑같이 찍으면 격자가 눈에 보인다.
// UV 를 정삼각 격자로 나누고 칸마다 텍스처를 무작위 오프셋으로 읽어 이웃 셋을 섞는다.
// 근거: Heitz & Neyret 2018, "High-Performance By-Example Noise using a
// Histogram-Preserving Blending Operator".
//
// 셋을 그냥 가중평균하면 평균으로 몰려 대비가 죽는다. 논문은 히스토그램 보존 블렌딩을
// 쓰지만 룩업 텍스처가 필요하다. 여기서는 가중치를 지수로 날카롭게 만들어 대부분의
// 픽셀이 한 샘플에 지배되게 한다 — 훨씬 싸고, 좁은 전이 구간에서만 섞인다.
// ---------------------------------------------------------------------------

// UV -> 정삼각 격자. 세 꼭짓점과 무게중심 가중치를 돌려준다.
void DustTriangleGrid(float2 uv, out float3 w, out int2 v0, out int2 v1, out int2 v2)
{
    // 정삼각 격자로 기울인다
    float2 skewed = float2(uv.x - uv.y * 0.57735027, uv.y * 1.15470054);
    int2 baseCell = int2(floor(skewed));
    float2 f = frac(skewed);
    float3 t = float3(f.x, f.y, 1.0 - f.x - f.y);

    if (t.z > 0.0)
    {
        w  = float3(t.z, t.y, t.x);
        v0 = baseCell;
        v1 = baseCell + int2(0, 1);
        v2 = baseCell + int2(1, 0);
    }
    else
    {
        w  = float3(-t.z, 1.0 - t.y, 1.0 - t.x);
        v0 = baseCell + int2(1, 1);
        v1 = baseCell + int2(1, 0);
        v2 = baseCell + int2(0, 1);
    }
}

float2 DustCellHash(int2 cell)
{
    float2 p = float2(cell);
    return frac(sin(float2(dot(p, float2(127.1, 311.7)),
                           dot(p, float2(269.5, 183.3)))) * 43758.5453);
}

// 오프셋이 셀마다 튀므로 자동 미분이 망가진다. 반드시 명시적 그래디언트로 샘플한다.
half4 DustStochasticSample(TEXTURE2D_PARAM(tex, samp), float2 uv, float contrast)
{
    float3 w; int2 v0, v1, v2;
    DustTriangleGrid(uv * 3.464, w, v0, v1, v2);   // sqrt(12) — 셀 하나가 텍스처 한 장쯤

    float2 dx = ddx(uv);
    float2 dy = ddy(uv);

    half4 s0 = SAMPLE_TEXTURE2D_GRAD(tex, samp, uv + DustCellHash(v0), dx, dy);
    half4 s1 = SAMPLE_TEXTURE2D_GRAD(tex, samp, uv + DustCellHash(v1), dx, dy);
    half4 s2 = SAMPLE_TEXTURE2D_GRAD(tex, samp, uv + DustCellHash(v2), dx, dy);

    float3 ww = pow(abs(w), contrast);
    ww /= max(dot(ww, 1.0.xxx), 1e-6);
    return s0 * ww.x + s1 * ww.y + s2 * ww.z;
}

// 탄젠트 공간 노멀 블렌드 (whiteout)
half3 BlendDustNormal(half3 baseN, half3 detailN)
{
    return SafeNormalize(half3(baseN.xy + detailN.xy, baseN.z * detailN.z));
}

// 노멀 세기 조절. xy 를 키우면 경사가 가팔라진다.
//
// lerp(float3(0,0,1), n, s) 로 하면 s 가 1 을 넘어도 saturate 에 잘려 원본보다
// 세게 만들 수 없다 — 노멀맵이 "덜 먹는" 것처럼 보이는 원인이 이것이었다.
half3 ScaleDustNormal(half3 n, float s)
{
    return SafeNormalize(half3(n.xy * s, n.z));
}

struct DustSurfaceResult
{
    half3 albedo;
    half  smoothness;
    half3 normalTS;
};

// d            : 먼지 양 0..1 (마스크 * amount)
// dissolveN    : 디졸브 노이즈 0..1
DustSurfaceResult ComposeDust(
    half3 cleanAlbedo, half cleanSmoothness, half3 cleanNormalTS,
    float d, float dissolveN, half3 grainNormalTS,
    half3 dirtColor, half dirtSmoothness,
    float edgeSoftness, float thinOpacity, float fullDirtAt,
    float grainStrength, float edgeRim, half3 edgeRimColor)
{
    // 핵심: d를 알파로 쓰지 않고 노이즈와 비교한다.
    // 알파로 쓰면 원이 투명해지고, 비교하면 얼룩덜룩 걷힌다.
    //
    // d를 [-s, 1+s]로 넓혀서 비교한다. 이게 없으면 d=1인데도 노이즈가 1에 가까운 텍셀이
    // 안 덮여 흰 점으로 남고, d=0인데도 노이즈가 0에 가까운 텍셀에 먼지가 남는다.
    // 즉 "완전히 더러움"과 "완전히 깨끗함"이 도달 불가능해진다.
    float threshold = d * (1.0 + 2.0 * edgeSoftness) - edgeSoftness;
    float cover = smoothstep(dissolveN - edgeSoftness, dissolveN + edgeSoftness, threshold);

    // 옅어짐: cover가 1이어도 d가 낮으면 먼지색 자체가 옅다.
    // 이게 없으면 "얇아진다"가 아니라 "구멍이 뚫린다"로 읽힌다.
    float opacity = lerp(thinOpacity, 1.0, saturate(d / max(fullDirtAt, 1e-4)));
    half3 dirtA   = lerp(cleanAlbedo, dirtColor, opacity);

    // 갓 드러난 경계가 밝게 튄다 (cover 0.5에서 최대)
    float rim = edgeRim * (1.0 - abs(cover * 2.0 - 1.0));

    DustSurfaceResult o;
    o.albedo     = lerp(cleanAlbedo, dirtA, cover) + edgeRimColor * rim;
    o.smoothness = lerp(cleanSmoothness, dirtSmoothness, cover);
    o.normalTS   = BlendDustNormal(cleanNormalTS,
                       ScaleDustNormal(grainNormalTS, cover * grainStrength));
    return o;
}
#endif
