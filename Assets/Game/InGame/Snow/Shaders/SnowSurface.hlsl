#ifndef PPACK_SNOW_SURFACE_INCLUDED
#define PPACK_SNOW_SURFACE_INCLUDED

// ---------------------------------------------------------------------------
// 눈 높이 필드
//
// 규약(docs/specs/2026-08-14-snow-surface.md §5):
//   R = depth01 (1 = 눈 가득)  ·  G = freshness01 (방금 밀린 자국, 클라 로컬 연출)
//   변위는 저역 통과(밉)를 읽고, 경계 clip 과 노멀은 풀 해상도를 읽는다.
//   셰이더는 깊이 "단계 수"를 모른다 — depth01 * _SnowMaxDepth 뿐이다.
//
// 필드는 세계 좌표 격자라서 UV0 을 쓰지 않는다. 그래서 패널 여러 장이 메시 한 장과
// 머티리얼 한 장을 공유할 수 있다.
// ---------------------------------------------------------------------------

TEXTURE2D(_SnowField);
SAMPLER(sampler_SnowField);

// 전역 — 머티리얼이 아니라 SnowSurfaceRenderer 가 Shader.SetGlobal* 로 싣는다.
// UnityPerMaterial 밖에 있어야 패널들이 머티리얼 하나를 공유해도 SRP Batcher 가 묶는다.
float4 _SnowFieldOrigin;     // xy = 격자 원점의 월드 XZ
float4 _SnowFieldInvSize;    // xy = 1 / (격자 전체 크기 m)
float4 _SnowFieldTexelSize;  // xy = 1 / 텍스처 해상도, zw = 해상도
float  _SnowFieldCellSize;   // 셀 한 변의 미터 (권위 셀 = 0.125)

float2 SnowFieldUV(float3 positionWS)
{
    return (positionWS.xz - _SnowFieldOrigin.xy) * _SnowFieldInvSize.xy;
}

// mip 지정 샘플. 정점 단계와 domain 단계 모두 그래디언트가 없으므로 양쪽에서 이 함수를 쓴다.
float2 SampleSnowField(float2 uv, float mip)
{
    return SAMPLE_TEXTURE2D_LOD(_SnowField, sampler_SnowField, uv, mip).rg;
}

// ---------------------------------------------------------------------------
// 변위 — 순수 함수
//
// 인터폴레이터에 의존하지 않는다. 지금은 정점 셰이더가 부르고, 하드웨어 테셀레이션으로
// 올릴 때는 domain 셰이더가 같은 것을 부른다(스펙 §2 규칙 1·2).
//
// 오브젝트 공간이 아니라 월드 공간에서 밀어낸다 — 스케일이 걸린 트랜스폼에서도
// 깊이가 미터로 유지된다.
//
// mip: 저역 통과 단계. 12.5cm 필드를 25cm 정점에서 그냥 읽으면 절반을 건너뛰어
// 지오메트리가 앨리어싱·팝핑한다(나이퀴스트).
// ---------------------------------------------------------------------------
// 경계 교란 노이즈
//
// 월드 공간이어야 한다. UV 로 하면 자국이 도구에 붙어 따라다닌다 — 먼지에서 측정된 결론.
// 격자는 12.5cm 인데 이 교란은 프래그먼트 해상도이므로, 보이는 경계는 픽셀 단위로 누더기가 된다.
// ---------------------------------------------------------------------------
float SnowHash21(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float SnowValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);      // smoothstep
    float a = SnowHash21(i);
    float b = SnowHash21(i + float2(1, 0));
    float c = SnowHash21(i + float2(0, 1));
    float d = SnowHash21(i + float2(1, 1));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// 두 옥타브면 충분하다. 큰 결이 덩어리를, 작은 결이 부스러기를 만든다.
float SnowEdgeNoise(float2 worldXZ, float scale)
{
    return SnowValueNoise(worldXZ * scale) * 0.65
         + SnowValueNoise(worldXZ * scale * 2.7) * 0.35;
}

// ---------------------------------------------------------------------------
// 도메인 워프 — 셀 경계의 축 정렬을 깨는 유일한 방법 (2026-08-14 게이트 4차)
//
// 높이 지터로는 안 된다. 높이를 흔들면 단의 양쪽이 같이 올라가서 **단 자체는 남는다**.
// 깨야 하는 것은 높이가 아니라 **셀 경계의 위치**이므로 샘플 좌표를 흔든다.
//
// 정점 단계와 프래그먼트 단계가 **같은 함수**를 써야 한다. 월드 XZ 만의 함수이므로
// 두 단계가 같은 값을 얻고, 지오메트리와 실루엣이 어긋나지 않는다.
// 워프 주파수도 정점 간격으로 표현 가능해야 한다(25cm 정점에 2/m = 50cm 결).
// ---------------------------------------------------------------------------
float2 SnowWarpedFieldUV(float3 positionWS, float warpMeters, float warpScale)
{
    if (warpMeters <= 0.0) return SnowFieldUV(positionWS);

    float2 p = positionWS.xz * warpScale;
    float2 w = float2(SnowValueNoise(p), SnowValueNoise(p + 37.3)) - 0.5;
    return SnowFieldUV(positionWS + float3(w.x, 0.0, w.y) * (2.0 * warpMeters));
}

// ---------------------------------------------------------------------------
// 벽 세우기 — 측정으로 들어온 항 (2026-08-14 게이트 1차)
//
// 깊이를 그대로 밀면 25cm 쿼드 안에서 선형 보간돼 벽이 완만한 경사로가 되고, 그 경사로가
// 격자축에 정렬돼 **계단**으로 읽힌다. 프래그먼트 노이즈로는 못 가린다 — 계단이 셰이딩이
// 아니라 지오메트리에 있기 때문이다.
//
// knee~top 창 안에서만 높이가 떨어지게 remap 하면 전이가 한 셀 안으로 몰려 벽이 서고,
// 그러면 **보이는 실루엣을 clip 이 소유한다** — clip 은 프래그먼트 해상도이므로 그때부터
// 경계 노이즈(밴드 B)가 실제로 외곽선을 흩뜨린다.
float SnowWallProfile(float depth01, float knee, float top)
{
    return smoothstep(knee, max(top, knee + 1e-3), depth01);
}

// 높이 지터 — 정점 단계의 밴드 B (2026-08-14 게이트 3차)
//
// 벽을 세워도 **내부 단**(레벨 사이 terrace)에는 계단이 남는다. 거기서는 깊이가 cutoff 를
// 넘으므로 clip 이 개입하지 않고, 프래그먼트 노이즈는 외곽선만 흩뜨린다. 셀 경계가 격자축에
// 정렬된 것이 원인이므로 정점 높이를 월드 공간으로 흔들어 정렬을 깬다.
//
// ⚠ 지터의 주파수는 정점 간격으로 표현 가능해야 한다. 25cm 정점에서 1.5/m(≈65cm 결)면
// 충분히 표현되고, 그보다 촘촘하게 잡으면 정점이 못 따라가 다시 앨리어싱이 된다.
//
// wall 을 곱해서 눈이 없는 자리는 흔들지 않는다 — 안 그러면 치워진 바닥이 울렁거린다.
float3 SnowDisplaceWS(float3 positionWS, float3 normalWS, float maxDepth, float mip,
                      float wallKnee, float wallTop, float jitterHeight, float jitterScale,
                      float warpMeters, float warpScale)
{
    float depth01 = SampleSnowField(SnowWarpedFieldUV(positionWS, warpMeters, warpScale), mip).r;
    float wall = SnowWallProfile(depth01, wallKnee, wallTop);

    float jitter = (SnowValueNoise(positionWS.xz * jitterScale) - 0.5) * 2.0 * jitterHeight;
    float height = wall * maxDepth + jitter * wall;

    return positionWS + normalWS * height;
}

// ---------------------------------------------------------------------------
// 미세 요철 — 절차적, 노멀맵 없이
//
// 눈 표면이 매끈한 판으로 읽히는 것을 막는다. 노멀맵 에셋이 아직 없어도 게이트 판정이
// 텍스처 유무에 흔들리지 않게 하기 위해 셰이더 안에서 만든다. 밴드 B(클라 로컬, 무한 해상도).
// 수평 패널 전제(up = +Y) — SnowFieldNormalWS 와 같은 제약이다.
// ---------------------------------------------------------------------------
// heightMeters 는 요철의 **높이(미터)** 다. 기울기가 아니다 —
// 처음에 strength/e 로 썼더니 e = 0.5/scale 이라 실제로는 strength·2·scale 이 되어
// scale 9 에서 18 배로 증폭됐고, 표면이 눈이 아니라 고주파 지렁이 무늬가 됐다(게이트 2차 실측).
float3 SnowMicroRelief(float3 normalWS, float2 worldXZ, float scale, float heightMeters)
{
    if (heightMeters <= 0.0) return normalWS;

    float e = 0.5 / max(scale, 1e-3);            // 노이즈 반 칸에 해당하는 월드 거리(m)
    float n0 = SnowValueNoise(worldXZ * scale);
    float nx = SnowValueNoise((worldXZ + float2(e, 0)) * scale);
    float nz = SnowValueNoise((worldXZ + float2(0, e)) * scale);

    // (높이차 m) / (수평거리 m) = 기울기
    float2 slope = float2(nx - n0, nz - n0) * heightMeters / e;
    return SafeNormalize(normalWS + float3(-slope.x, 0, -slope.y));
}

// ---------------------------------------------------------------------------
// 필드 그래디언트에서 노멀
//
// 25cm 정점이 12.5cm 데이터처럼 보이게 만드는 장치. 지오메트리는 조대하고 셰이딩은 세밀하다.
// 수평 패널을 전제한다(up = +Y). 경사면에 눈을 올리게 되면 여기서 탄젠트 프레임이 필요해진다.
// ---------------------------------------------------------------------------
float3 SnowFieldNormalWS(float2 uv, float maxDepth)
{
    float2 t = _SnowFieldTexelSize.xy;
    float hL = SampleSnowField(uv - float2(t.x, 0), 0).r;
    float hR = SampleSnowField(uv + float2(t.x, 0), 0).r;
    float hD = SampleSnowField(uv - float2(0, t.y), 0).r;
    float hU = SampleSnowField(uv + float2(0, t.y), 0).r;

    // 텍셀 두 칸 = 셀 두 칸의 거리
    float2 slope = float2(hR - hL, hU - hD) * maxDepth / max(2.0 * _SnowFieldCellSize, 1e-4);
    return SafeNormalize(float3(-slope.x, 1.0, -slope.y));
}

// 월드 노멀에서 탄젠트 프레임을 세운다. 패널은 UV0 이 없으므로 메시 탄젠트도 쓰지 않는다.
void SnowBuildFrame(float3 normalWS, out float3 tangentWS, out float3 bitangentWS)
{
    float3 up = float3(1, 0, 0);
    tangentWS = SafeNormalize(up - normalWS * dot(normalWS, up));
    bitangentWS = cross(normalWS, tangentWS);
}

#endif
