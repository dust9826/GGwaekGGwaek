#ifndef PPACK_DUST_BRUSH_INCLUDED
#define PPACK_DUST_BRUSH_INCLUDED

// 표면에 붙어 움직이는 **사각 패드**의 브러시 판정.
//
// 두 셰이더가 이 파일을 공유한다 — 마스크에서 빼는 DustPaint 와, 실제로 지워질 양을
// 기록하는 DustErased. 한 곳에서 계산해야 "지운 양"과 "지웠다고 기록한 양"이 갈라지지 않는다.
//
// 붓이 구가 아니라 사각형인 이유는 도구가 스팀청소기이기 때문이다. 자세한 근거는
// docs/specs/2026-08-11-dust-clean-vfx.md 0절과 2절.

// 두 셰이더가 같은 레이아웃으로 선언해야 하므로 CBUFFER 를 여기 둔다.
CBUFFER_START(UnityPerMaterial)
    float4x4 _BrushWorldToPad;
    float4   _BrushHalfExtents;   // xy = 패드 로컬 XZ 반쪽 크기
    float    _BrushThickness;     // 패드 로컬 Y 허용 범위
    float    _BrushFeather;       // 경계 폭 (월드 단위)
    float    _BrushStrength;
    float    _BrushNoiseAmount;
    float    _BrushNoiseScale;
CBUFFER_END

float BrushHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float BrushValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = BrushHash(i);
    float b = BrushHash(i + float2(1.0, 0.0));
    float c = BrushHash(i + float2(0.0, 1.0));
    float d = BrushHash(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// 두 옥타브 — 큰 얼룩과 잔 결이 같이 있어야 붓질로 읽힌다.
float BrushFbm(float2 p)
{
    return BrushValueNoise(p) * 0.65 + BrushValueNoise(p * 2.7 + 13.1) * 0.35;
}

float3 BrushToPadLocal(float3 positionWS)
{
    return mul(_BrushWorldToPad, float4(positionWS, 1.0)).xyz;
}

// 이 텍셀에서 지울 양. 패드 밖이거나 두께 밖이면 0.
float BrushAmount(float3 positionWS, float3 padLocal)
{
    // 사각 프리즘은 무한히 길다. 이걸 안 자르면 바닥을 닦을 때 아래층 천장까지 지워진다.
    // 구 브러시에는 없던 실패 모드다.
    if (abs(padLocal.y) > _BrushThickness) return 0.0;

    // 노이즈는 **월드 좌표**로 뽑는다. UV 로 뽑으면 무늬가 붓을 따라 움직여서
    // 얼룩이 바닥이 아니라 도구에 붙어 있는 것처럼 보인다.
    float2 noiseUV = positionWS.xz * _BrushNoiseScale;
    float edgeNoise = BrushFbm(noiseUV);
    float patchNoise = BrushFbm(noiseUV * 1.9 + 41.0);

    // 2D 박스 SDF — 음수면 패드 안.
    float2 q = abs(padLocal.xz) - _BrushHalfExtents.xy;
    float outside = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);

    // 외곽선을 흔든다 — 거리 자체에 노이즈를 더해 너덜너덜하게 만든다.
    // 완벽한 직사각형은 도구가 아니라 스탬프로 읽힌다.
    outside += (edgeNoise - 0.5) * _BrushFeather * _BrushNoiseAmount * 2.0;

    float falloff = 1.0 - smoothstep(-_BrushFeather, 0.0, outside);

    // 안쪽도 균일하지 않게 — 한 번 지나가면 얼룩이 남고, 겹쳐 문지르면 고르게 지워진다.
    float patch = lerp(1.0 - _BrushNoiseAmount, 1.0, patchNoise);

    return falloff * _BrushStrength * patch;
}

#endif
