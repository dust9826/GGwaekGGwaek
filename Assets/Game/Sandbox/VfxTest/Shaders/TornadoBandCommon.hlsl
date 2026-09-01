#ifndef PPACK_TORNADO_BAND_COMMON_INCLUDED
#define PPACK_TORNADO_BAND_COMMON_INCLUDED

// 토네이도 밴딩 셰이더의 본문. **두 패스가 공유한다** — 뒷면(Cull Front)과 앞면(Cull Back)을
// 따로 그려야 양면 반투명의 깊이 순서가 맞기 때문이다. 한 곳에 두지 않으면 두 패스가 갈라진다.

TEXTURE2D(_NoiseMap);
SAMPLER(sampler_NoiseMap);

CBUFFER_START(UnityPerMaterial)
    float4 _NoiseTiling;
    float4 _PanSpeed;
    float4 _ColorA;
    float4 _ColorB;
    float _Bands;
    float _Cutoff;
    float _Displace;
    float _Alpha;
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
    half fogFactor : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

float2 BuildSamplingUV(float2 meshUV)
{
    float2 samplingUV = meshUV;

    if (_PolarUV >= 0.5)
    {
        float2 delta = meshUV - 0.5;
        float radiusSquared = dot(delta, delta);

        // 중심점에서는 방향이 없으므로 각도를 고정해 atan2의 플랫폼별 미정 값을 피한다.
        float angle = radiusSquared > 1e-8 ? atan2(delta.y, delta.x) : 0.0;
        samplingUV = float2(angle / (2.0 * PI) + 0.5, sqrt(radiusSquared) * 2.0);
    }

    return samplingUV * _NoiseTiling.xy + _Time.y * _PanSpeed.xy;
}

Varyings TornadoBandVertex(Attributes input)
{
    Varyings output = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float2 noiseUV = BuildSamplingUV(input.uv);
    // 버텍스 단계에는 화면 미분값이 없으므로 명시적 LOD로 같은 노이즈 흐름을 안정적으로 공유한다.
    float noise = SAMPLE_TEXTURE2D_LOD(_NoiseMap, sampler_NoiseMap, noiseUV, 0).r;
    float3 positionOS = input.positionOS.xyz
        + input.normalOS * ((noise - 0.5) * _Displace);

    output.positionCS = TransformObjectToHClip(positionOS);
    output.uv = input.uv;
    output.fogFactor = ComputeFogFactor(output.positionCS.z);
    return output;
}

half4 TornadoBandFragment(Varyings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float2 noiseUV = BuildSamplingUV(input.uv);
    float noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, noiseUV).r;
    float banded = floor(noise * _Bands) / _Bands;

    // 경계값을 외부에서 움직일 때 띠 단위로 사라져야 하므로 보간 이후 값이 아니라 양자화 값을 자른다.
    clip(banded - _Cutoff);

    half4 color = lerp(_ColorA, _ColorB, banded);
    color.a *= _Alpha;

    // 극좌표 모드에서는 **원형으로 사라져야 한다.** 평면 메시가 사각형이라 그냥 두면
    // 소용돌이 무늬가 네 모서리에서 칼로 자른 듯 끊긴다 (실측). 중심에서의 거리로
    // 페이드하면 메시 모양과 무관하게 원반으로 읽힌다.
    if (_PolarUV >= 0.5)
    {
        float edge = length(input.uv - 0.5) * 2.0;
        color.a *= 1.0 - smoothstep(0.62, 1.0, edge);
    }
    color.rgb = MixFog(color.rgb, input.fogFactor);
    return color;
}

#endif
