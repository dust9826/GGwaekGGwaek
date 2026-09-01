#ifndef PPACK_ICE_VORTEX_COMMON_INCLUDED
#define PPACK_ICE_VORTEX_COMMON_INCLUDED

// 얼음 소용돌이 본문. `TornadoBand` 와 같은 이유로 **두 패스가 공유한다** — 뒷면(Cull Front)과
// 앞면(Cull Back)을 따로 그려야 양면 반투명의 깊이 순서가 맞는다. 한 곳에 두지 않으면 갈라진다.
//
// `TornadoBand` 와 갈라진 지점은 셋이다.
//  1. 노이즈를 **반올림하지 않는다.** 층 무늬 대신 매끄러운 유리판을 원하기 때문이다.
//     띠는 대신 가늘고 밝은 **줄무늬(streak)** 가 만든다 — 같은 노이즈를 세로로 길게 늘여
//     거듭제곱으로 조이면 실 같은 선이 남는다.
//  2. **프레넬 림.** 레퍼런스의 껍데기는 가장자리에서 밝다. 시선과 나란한 면이 밝아야
//     원뿔이 유리처럼 읽힌다.
//  3. **가산 합성.** 어두운 배경 위에서 빛나야 하므로 알파 블렌딩이 아니다.

TEXTURE2D(_NoiseMap);
SAMPLER(sampler_NoiseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _NoiseTiling;
    float4 _PanSpeed;
    float4 _StreakTiling;
    float4 _StreakPan;
    float4 _ColorDeep;
    float4 _ColorBright;
    float _Swirl;
    float _SpinSpeed;
    float _SpinTipAmp;
    float _SpinTipFreq;
    float _StreakSharp;
    float _StreakGain;
    float _FresnelPower;
    float _FresnelGain;
    float _TipBoost;
    float _TopFade;
    float _Cutoff;
    float _Alpha;
    float _Displace;
    float _PolarUV;
CBUFFER_END

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float3 viewWS : TEXCOORD2;
    half fogFactor : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

// 원반 모드에서는 극좌표로 갈아탄다. 나선이 중심으로 빨려드는 것처럼 읽힌다.
float2 BaseUV(float2 meshUV)
{
    if (_PolarUV < 0.5)
    {
        // 무늬를 감는 것은 **정적인 `_Swirl`** 이다. 수렴하는 긴 결이 여기서 나온다.
        //
        // ⚠ 처음에는 꼭짓점 가속을 `_Time.y * _SpinTipGain * (1-v)` 로 넣었고 **80 초 뒤
        // 촘촘한 가로 고리 더미로 무너졌다** (실측). 이유는 이 항의 v 기울기
        // `d/dv = -_Time.y * _SpinTipGain` 가 시간에 비례해 무한히 커지기 때문이다 —
        // 비틀림이 영원히 감긴다. **모양을 내는 전단은 시간에 의존하면 안 된다.**
        //
        // 시간에 맡길 수 있는 것은 두 가지뿐이다.
        //   · 균일 회전: v 와 무관하므로 아무리 커져도 그냥 흐를 뿐 전단이 안 생긴다
        //   · 유한하게 진동하는 차등 회전: 안쪽이 바깥쪽에 대해 앞뒤로 흔들려 휘젓는 느낌만 준다
        float uniformSpin = _Time.y * _SpinSpeed;
        float breathe = _SpinTipAmp * sin(_Time.y * _SpinTipFreq) * saturate(1.0 - meshUV.y);
        return float2(meshUV.x + meshUV.y * _Swirl + uniformSpin + breathe, meshUV.y);
    }

    float2 delta = meshUV - 0.5;
    float radiusSquared = dot(delta, delta);
    float angle = radiusSquared > 1e-8 ? atan2(delta.y, delta.x) : 0.0;
    float radius = sqrt(radiusSquared) * 2.0;
    float discBreathe = _SpinTipAmp * sin(_Time.y * _SpinTipFreq) * saturate(1.0 - radius);
    return float2(angle / (2.0 * PI) + 0.5 + radius * _Swirl + _Time.y * _SpinSpeed + discBreathe, radius);
}

float SampleSheet(float2 baseUV)
{
    float2 uv = baseUV * _NoiseTiling.xy + _Time.y * _PanSpeed.xy;
    return SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, uv).r;
}

float SampleSheetLOD(float2 baseUV)
{
    float2 uv = baseUV * _NoiseTiling.xy + _Time.y * _PanSpeed.xy;
    return SAMPLE_TEXTURE2D_LOD(_NoiseMap, sampler_NoiseMap, uv, 0).r;
}

// 줄무늬는 같은 노이즈를 **다른 타일링**으로 한 번 더 읽어 만든다. 세로 주기를 크게,
// 가로 주기를 작게 주면 결이 길게 늘어나고, 거듭제곱이 그중 밝은 마루만 남긴다.
float SampleStreak(float2 baseUV)
{
    float2 uv = baseUV * _StreakTiling.xy + _Time.y * _StreakPan.xy;
    float n = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, uv).r;
    return pow(saturate(n), _StreakSharp);
}

Varyings IceVortexVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    // 버텍스 단계에는 화면 미분값이 없으므로 명시적 LOD 로 프래그먼트와 같은 흐름을 공유한다.
    float noise = SampleSheetLOD(BaseUV(input.uv));
    float3 positionOS = input.positionOS.xyz + input.normalOS * ((noise - 0.5) * _Displace);

    float3 positionWS = TransformObjectToWorld(positionOS);
    output.positionCS = TransformWorldToHClip(positionWS);
    output.uv = input.uv;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    output.viewWS = GetWorldSpaceViewDir(positionWS);
    output.fogFactor = ComputeFogFactor(output.positionCS.z);
    return output;
}

half4 IceVortexFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 baseUV = BaseUV(input.uv);
    float sheet = SampleSheet(baseUV);
    float streak = SampleStreak(baseUV);

    // 컷오프가 층을 가른다. 약하면 껍데기끼리 평균으로 뭉개지고, 세게 주면 구멍이 뚫려
    // 그 사이로 뒷층이 보인다. 자르는 대상은 줄무늬가 아니라 **판** 이다 — 줄무늬까지
    // 자르면 실이 점선으로 끊긴다.
    clip(sheet - _Cutoff);

    // 시선과 나란한 면일수록 밝다. 원뿔의 실루엣 가장자리가 여기서 살아난다.
    float3 n = normalize(input.normalWS);
    float3 v = normalize(input.viewWS);
    float fresnel = pow(1.0 - saturate(abs(dot(n, v))), _FresnelPower) * _FresnelGain;

    float body = saturate(sheet + streak * _StreakGain + fresnel);
    half4 color = lerp(_ColorDeep, _ColorBright, saturate(streak * _StreakGain + fresnel));

    float profile = 1.0;
    if (_PolarUV < 0.5)
    {
        // 아래(꼭짓점)로 갈수록 밝다 — 레퍼런스에서 가장 밝은 곳은 바닥에 닿는 지점이다.
        profile *= 1.0 + _TipBoost * saturate(1.0 - input.uv.y * 2.5);
        // 위쪽 입구는 열려 있어야 한다. 안 그러면 원뿔이 잘린 파이프처럼 끝난다.
        //
        // ⚠ `_TopFade = 0` 이면 `smoothstep(1, 1, v)` 가 되어 **경계 두 개가 같아진다.** 0 으로
        // 나누는 꼴이라 결과가 1 로 포화되고, `profile` 이 통째로 0 이 되어 메시가 완전히
        // 사라진다. "페이드를 끈다"는 뜻으로 0 을 넣으면 정반대로 전부 지워진다 (실측).
        float fadeStart = min(1.0 - _TopFade, 0.999);
        profile *= 1.0 - smoothstep(fadeStart, 1.0, input.uv.y);
    }
    else
    {
        // 평면 메시는 사각형이라 그냥 두면 무늬가 네 모서리에서 칼로 자른 듯 끊긴다.
        float edge = length(input.uv - 0.5) * 2.0;
        profile *= 1.0 - smoothstep(0.55, 1.0, edge);
    }

    color.rgb *= body * profile;
    color.a = saturate(color.a * body * profile * _Alpha);
    color.rgb = MixFog(color.rgb, input.fogFactor);
    return color;
}

#endif
