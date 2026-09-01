#ifndef SNOW_RAYMARCH_CPU_INCLUDED
#define SNOW_RAYMARCH_CPU_INCLUDED

// -----------------------------------------------------------------------------------------------
// 비용의 법칙 (v7 이 세 번 틀리고 세 번 같은 이유로 설명된 것)
//
//   표면 함수는 픽셀당 여러 번 호출된다 - 마칭 스텝마다 + 노멀 탭 4회 + 소프트섀도 스텝마다.
//   그래서 표면 함수 안에 무엇을 넣으면 그 전부에 곱해진다.
//
// 반대로 meanSteps 는 비용의 좋은 예측자가 아니다. v7 실측에서 디테일 옥타브를 2 에서 1 로
// 줄이자 스텝이 30.2 에서 33.0 으로 늘었는데 프레임은 6.74 에서 4.48 로 줄었다.
//
// 그래서 이 파일의 SurfaceY 는 텍스처 탭 하나다. 무엇을 더하고 싶으면 여기가 아니라
// 텍스처에 구워서 탭 하나로 만드는 것이 옳다 - v7 이 로브를 스텝마다 평가했다가
// +9.31ms 를 냈고, 구워서 +1.70ms 로 5.5배 줄인 것이 그 사례다.
// -----------------------------------------------------------------------------------------------
// 높이필드 레이마칭의 코어. v7 의 SnowMarchCoreV7 을 참조하되 표면에 아무것도 더하지 않는
// 최소 형태다 - 디테일 노이즈도, fillet 도, 구운 로브도 없다.
//
// 그것이 이 파일의 유일한 장점이자 한계다. 표면에 더하는 것이 없으므로 coarse-max 상한이
// EXACT 하고, _CoarseMaxBiasM 이 0 이어도 표면을 뚫는 것이 구조적으로 불가능하다. v7 이
// fillet 으로 상한을 깨서 표면에 구멍이 뚫린 사고를 여기서는 낼 수가 없다.
//
// 나중에 무엇이든 표면에 더하면, 그 최대 들림값을 반드시 _CoarseMaxBiasM 에 포함시켜야 한다.
// 그것이 이 기법의 유일한 치명적 실수다.
// -----------------------------------------------------------------------------------------------

TEXTURE2D(_HeightTex);        // R16 UNorm, 1.0 == 65.535 m. CPU 시뮬의 ushort[] 가 그대로 올라온다
TEXTURE2D(_CoarseMaxTex);     // R16 UNorm, 블록 최대값을 다일레이트한 상한

// 구운 로브 리프트. 필드의 2배 해상도(6.25 cm), 단채널 8비트.
// BILINEAR 다 - 이 파일에서 보간해도 되는 유일한 텍스처다. 상한이 아니라 표면 기여라서,
// 보간이 값을 낮추는 쪽으로만 움직이고 +r 상한을 깨지 않는다.
TEXTURE2D(_LumpTex);

// 구운 둥근 어깨. 부호가 있어서 0.5 가 변화 없음이고 ±_FilletRangeM 로 펼쳐진다.
// 이것도 바이리니어다 - 표면 기여이지 상한이 아니다.
TEXTURE2D(_FilletTex);
SAMPLER(sampler_linear_clamp);
SAMPLER(sampler_point_clamp);

float4 _PatchMin;             // .xy = 필드 원점 (월드 XZ)
float4 _InvPatchSize;         // .xy = 1 / 필드 크기. 축마다 다를 수 있다
float4 _BoxMin;
float4 _BoxMax;

float  _GroundY;
float  _MarchTopY;            // 마칭 시작 높이. 필드 최고점 + 여유
float  _MarchFloorY;          // 마칭 종료 높이. 지면 바로 위
float  _MinSnowHeightM;       // 이 아래는 맨바닥이고 표면이 없다
float  _CoarseSafeRadiusM;    // coarse 텍셀 하나의 상한이 유효한 월드 반경
float  _CoarseMaxBiasM;       // 표면에 더해지는 것의 최대 들림 = 로브 반지름
float  _LumpRadiusM;          // 구운 리프트의 스케일. 텍셀 1.0 이 이 높이다
float  _LumpAmount;           // 0 이면 로브 끄기
float  _FilletRangeM;         // 인코딩 반범위
float  _FilletAmount;         // 0 이면 둥근 어깨 끄기
float  _CellSizeM;            // 권위 높이 한 셀. 0↔눈 경계 커버리지의 탭 간격

float  _MaxSteps;
float  _StepM;
float  _RefineSteps;

float4 _SnowColor;
float4 _DeepColor;
float4 _GroundColor;
float4 _AmbientColor;
float3 _SunDir;
float  _AoStrength;
float  _ShadowStrength;
float  _DebugMode;
// 매크로 노멀의 유한차분 반폭. 밴딩이 읽는 것은 큰 형태이지 로브 하나하나가 아니다.
float  _BandNormalWide;

// 사실적 기준의 확산광 wrap. 양자화는 SnowCasualApply 가 하므로 여기 밴딩 노브는 없다 -
// 두 곳에서 양자화하면 계단이 두 번 생긴다.
float  _StepGrowPerM;   // 거리에 따라 스텝이 자란다. 먼 곳은 정밀도가 필요 없다

// R16 UNorm 이라 1.0 이 65,535 mm = 65.535 m. 저장 단위가 표시 단위로 그대로 넘어온다.
static const float kMmScale = 65.535;

float2 FieldUv(float2 xz)
{
    return (xz - _PatchMin.xy) * _InvPatchSize.xy;
}

// 구운 로브 한 탭. 이것이 눈을 각진 슬래브에서 둥근 눈으로 바꾸는 전부다.
float LumpLift(float2 xz)
{
    return SAMPLE_TEXTURE2D_LOD(_LumpTex, sampler_linear_clamp, FieldUv(xz), 0).r
         * _LumpRadiusM * _LumpAmount;
}

// 구운 둥근 어깨 한 탭. relax 가 아직 못 푼 날카로운 능선을 렌더에서만 둥글린다 -
// 권위 필드는 날것 그대로 남는다.
float FilletLift(float2 xz)
{
    float e = SAMPLE_TEXTURE2D_LOD(_FilletTex, sampler_linear_clamp, FieldUv(xz), 0).r;
    return (e * 2.0 - 1.0) * _FilletRangeM * _FilletAmount;
}

// 표면 높이. 필드는 바이리니어(상한이 아니라 실제 높이라 안전) + 구운 두 층 각 한 탭.
//
// 비용의 법칙: 이 함수는 픽셀당 마칭 스텝마다 + 노멀 4탭 + 섀도 스텝마다 불린다. 그래서
// 여기 들어가는 것은 탭 하나여야 하고, 형태는 텍스처에 구워서 가져와야 한다.
float SurfaceY(float2 xz)
{
    float d = SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_linear_clamp, FieldUv(xz), 0).r * kMmScale;
    // 구운 로브와 필렛은 0 셀 밖까지 보간될 수 있다. 그대로 더하면 닦인 경계에 눈 송곳이 남는다.
    // 실제 높이 0→5 cm 구간에서만 형태 기여를 감쇠해 바닥으로 부드럽게 접속한다. 추가 탭은 없다.
    float edge = smoothstep(0.0, 0.05, d);
    return _GroundY + max(d + FilletLift(xz) * edge, 0.0) + LumpLift(xz) * edge;
}

float SurfaceDepth(float2 xz)
{
    return SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_linear_clamp, FieldUv(xz), 0).r * kMmScale;
}

// 얕은 눈 전체를 지우지 않고 실제 0 셀과 맞닿은 경계만 보간한다. 마칭 루프 안이 아니라
// 최종 적중점에서 한 번만 호출하므로 추가 탭 넷이 스텝 수에 곱해지지 않는다.
float SurfaceEdgeCoverage(float2 xz)
{
    float centerM = SurfaceDepth(xz);
    float leftM  = SurfaceDepth(xz + float2(-_CellSizeM, 0));
    float rightM = SurfaceDepth(xz + float2( _CellSizeM, 0));
    float downM  = SurfaceDepth(xz + float2(0, -_CellSizeM));
    float upM    = SurfaceDepth(xz + float2(0,  _CellSizeM));
    float minM = min(centerM, min(min(leftM, rightM), min(downM, upM)));
    if (minM >= _MinSnowHeightM) return 1.0;

    float maxM = max(centerM, max(max(leftM, rightM), max(downM, upM)));
    return maxM >= _MinSnowHeightM ? saturate(centerM / maxM) : 0.0;
}

// 상한. POINT 필터여야 한다 - 바이리니어 탭은 다일레이트된 최대값을 도로 아래로 보간해서,
// 이 텍스처가 제거하려던 문턱 아래 골을 다시 만들어낸다. 그러면 상한이 깨진다.
float CoarseMaxY(float2 xz)
{
    float d = SAMPLE_TEXTURE2D_LOD(_CoarseMaxTex, sampler_point_clamp, FieldUv(xz), 0).r * kMmScale;
    return _GroundY + d + _CoarseMaxBiasM;
}

bool SlabIntersect(float3 ro, float3 rd, float3 bmin, float3 bmax, out float t0, out float t1)
{
    float3 inv = 1.0 / (abs(rd) < 1e-6 ? 1e-6 : rd);
    float3 a = (bmin - ro) * inv;
    float3 b = (bmax - ro) * inv;
    float3 lo = min(a, b);
    float3 hi = max(a, b);
    t0 = max(max(lo.x, lo.y), lo.z);
    t1 = min(min(hi.x, hi.y), hi.z);
    return t1 > max(t0, 0.0);
}

// 필드 기울기에서 노멀. 저장하지 않고 여기서 만든다 - 저장하면 권위 필드가 렌더링을 알게 된다.
float3 SurfaceNormal(float2 xz, float e)
{
    float hx0 = SurfaceY(xz + float2(-e, 0));
    float hx1 = SurfaceY(xz + float2( e, 0));
    float hz0 = SurfaceY(xz + float2(0, -e));
    float hz1 = SurfaceY(xz + float2(0,  e));
    return normalize(float3(hx0 - hx1, 2.0 * e, hz0 - hz1));
}

// 광선이 표면을 만나는 지점. 못 만나면 false.
//
// 왜 안전한가: 건너뛴 구간 전체에 대해 coarseMax 상한이 성립하고 광선의 y 는 단조 감소한다.
// 그래서 표면을 지나쳐 뛰는 것이 확률적으로 드문 게 아니라 구조적으로 불가능하다.
bool MarchSurface(float3 ro, float3 rd, float tStart, float tEnd, out float3 hit, out int steps)
{
    steps = 0;
    hit = 0;
    if (rd.y > -1e-5) return false;              // 위를 보는 광선은 표면을 만나지 않는다

    float t = tStart;
    float tPrev = tStart;                        // 직전 표본. 이분법의 하한이 된다
    float invRdY = 1.0 / abs(rd.y);
    int maxSteps = (int)_MaxSteps;

    for (int i = 0; i < maxSteps; i++)
    {
        steps++;
        float3 p = ro + rd * t;
        if (t > tEnd) return false;

        float bound = CoarseMaxY(p.xz);
        if (p.y <= bound)
        {
            // 상한 아래로 들어왔다. 여기서부터는 실제 표면을 본다.
            float surf = SurfaceY(p.xz);
            if (p.y <= surf)
            {
                // 이분법. 하한은 반드시 <b>직전에 실제로 표본한 t</b> 여야 한다.
                //
                // 여기에 t - _StepM 을 쓰면 계단이 생긴다. 직전 전진은 빈 공간 스킵이라
                // 최대 _CoarseSafeRadiusM(1.5 m)까지 갈 수 있는데, 하한을 0.1 m 앞으로 잡으면
                // 구간이 교차점을 감싸지 못하고 이분법이 하한으로 수렴해버린다. 결과는 표면보다
                // 최대 1.4 m 앞에서 멈춘 적중점이고, 카메라에 가까울수록 같은 world 오차가
                // 화면을 더 많이 차지해서 눈에 띄게 증폭된다.
                float lo = tPrev;
                float hi = t;
                int refine = (int)_RefineSteps;
                for (int r = 0; r < refine; r++)
                {
                    float mid = 0.5 * (lo + hi);
                    float3 pm = ro + rd * mid;
                    if (pm.y <= SurfaceY(pm.xz)) hi = mid; else lo = mid;
                }
                hit = ro + rd * hi;
                // 권위 높이가 0 인 셀은 눈 표면이 아니다. 여기서 적중으로 인정하면 실제 바닥과
                // 정확히 같은 깊이를 쓰는 눈 픽셀이 한 장 더 생겨 겹침이 난다. 아래의 바닥 지오메트리가
                // 치운 자리를 소유하도록 버린다.
                if (SurfaceEdgeCoverage(hit.xz) < 0.5) return false;
                return true;
            }
            tPrev = t;
            t += _StepM * (1.0 + t * _StepGrowPerM);
        }
        else
        {
            // 빈 공간을 건너뛴다. 안전반경과 상한까지의 거리 중 작은 쪽.
            float skip = (p.y - bound) * invRdY;
            tPrev = t;
            t += max(min(skip, _CoarseSafeRadiusM), _StepM * (1.0 + t * _StepGrowPerM));
        }

        // 바닥 이탈 검사는 <b>적중 검사 뒤</b>여야 한다.
        //
        // 앞에 두면 평평한 긁힌 바닥에서 통째로 무너진다. 최소 스텝 때문에 광선이 y=+0.05 에서
        // y=-0.03 으로 건너뛰는 일이 생기는데, 그 자리에서 먼저 바닥을 검사하면 적중을 보지도
        // 않고 버린다 - 치운 차선에 검은 줄무늬가 생기는 것이 정확히 이것이었다.
        //
        // 뒤에 두면 도달할 수 없는 검사가 된다. 표면은 항상 지면 이상이고 바닥은 그 아래이므로,
        // 바닥 밑에 있으면서 표면 위일 수는 없다. 안전망으로만 남긴다.
        if (p.y < _MarchFloorY) return false;
    }
    return false;
}

// 짧은 2차 마칭으로 만든 소프트 섀도. 둔덕이 부피로 읽히게 하는 것이 이것이다.
float SunShadow(float3 p, float3 sunDir)
{
    float shade = 1.0;
    float t = 0.15;
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        float3 q = p + sunDir * t;
        if (q.y > _MarchTopY) break;
        float d = q.y - SurfaceY(q.xz);
        shade = min(shade, saturate(d * 6.0 / t));
        t += 0.28;
    }
    return lerp(1.0, shade, _ShadowStrength);
}

// 주변 높이가 나보다 높을수록 어둡다. 골이 진짜 골로 읽히게 한다.
// 주변 높이가 나보다 높을수록 어둡다. 골이 진짜 골로 읽히게 한다.
//
// 상한 텍스처를 쓴다. 여기서 알고 싶은 것은 "주변에 나보다 높은 것이 있나" 이고 그것이 곧
// coarse-max 가 담고 있는 값이라, 필드를 네 번 더 탭할 이유가 없다. 위의 비용의 법칙이
// 말하는 그대로다 - 표면 함수에서 뺄 수 있는 것은 뺀다.
float FieldAo(float2 xz, float y)
{
    float m = CoarseMaxY(xz);
    return saturate(1.0 - saturate((m - y - 0.12) * 0.7) * _AoStrength);
}

#endif
