// DeliveryTruckBeacon 전용. 실제 씬 조명(블루아워 야간+안개)과 무관하게 항상 같은 대비로 보여야
// 해서 진짜 Lit 은 안 쓰고, 고정된 가짜 광원 방향으로 fake diffuse + specular 만 계산한다 —
// 조명이 없거나 어두워도 반사 하이라이트는 항상 그대로 보인다.
//
// DeliveryRouteRibbonUnlit 과 렌더 스테이트를 통일한다 — 그쪽도 같은 저폴리 단면 문제를 겪었고,
// 불투명 + 상시 스펙큘러로는 면(facet)마다 정적으로 반짝이는 각짐이 보인다는 걸 이미 확인했다.
// 리본은 "흐르는 스트라이프" 구간에서만 스펙큘러를 켜 해결했지만, 비콘은 경로 방향이라는 개념이
// 없는 대신 Y축으로 계속 스핀하고 있어 그 자체가 이미 "하이라이트가 표면을 쓸고 지나가는" 역할을
// 한다 — 그래서 스트라이프 게이팅은 가져오지 않고, 반투명 렌더 스테이트만 맞춘다.
Shader "PPack/DeliveryBeaconUnlit"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _BaseAlpha("Base Alpha", Range(0, 1)) = 0.8
        _BaseMap("Base Map", 2D) = "white" {}
        _UseBaseMap("Use Base Map", Float) = 0
        _AccentColor("Accent Color", Color) = (1, 1, 1, 1)
        _UseAccentRemap("Use Accent Remap", Float) = 0
        _AmbientFloor("Ambient Floor", Range(0, 1)) = 0.55
        _Shininess("Shininess", Range(1, 128)) = 18
        _SpecularIntensity("Specular Intensity", Range(0, 2)) = 0.35
        _SoftShininess("Soft Shininess", Range(1, 32)) = 4
        _SoftSpecularIntensity("Soft Specular Intensity", Range(0, 2)) = 0.18
        _RimPower("Rim Power", Range(0.5, 8)) = 2.5
        _RimIntensity("Rim Intensity", Range(0, 2)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        // 닫힌 입체라 Cull Off 면 앞뒤 면이 두 번 블렌딩되어 _BaseAlpha 보다 훨씬 진하게 나온다
        // (리본 셰이더가 먼저 겪은 문제). 뒷면을 잘라 한 겹만 남긴다.
        Cull Back
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float _BaseAlpha;
                float _UseBaseMap;
                float4 _AccentColor;
                float _UseAccentRemap;
                float _AmbientFloor;
                float _Shininess;
                float _SpecularIntensity;
                float _SoftShininess;
                float _SoftSpecularIntensity;
                float _RimPower;
                float _RimIntensity;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.uv = IN.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return OUT;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetCameraPositionWS() - IN.positionWS);
                // 실제 씬 조명과 완전히 분리된 고정 방향 — 회전하면서 하이라이트가 표면을 쓸고
                // 지나가는 "반짝임"만 만든다. 시간/날씨에 따라 사라지지 않아야 하므로 상수로 둔다.
                float3 L = normalize(float3(0.4, 0.85, 0.35));
                float3 H = normalize(V + L);

                float diffuse = lerp(_AmbientFloor, 1.0, saturate(dot(N, L)));

                // 좁은 로브 하나만 쓰면 인접한 면(facet)마다 하이라이트 세기가 뚝뚝 끊겨 옆에서 볼 때
                // 저폴리 각짐이 도드라진다. 넓고 흐린 보조 로브를 섞어 면 사이 전환을 완만하게 만든다.
                float NdotH = saturate(dot(N, H));
                float specular = pow(NdotH, _Shininess) * _SpecularIntensity
                                + pow(NdotH, _SoftShininess) * _SoftSpecularIntensity;

                // 실루엣(그레이징 앵글)을 은은하게 밝혀 각진 면 경계가 시선에 덜 걸리게 한다.
                float rim = pow(1.0 - saturate(dot(N, V)), _RimPower) * _RimIntensity;

                float4 sampled = _UseBaseMap > 0.5
                    ? SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv)
                    : float4(1.0, 1.0, 1.0, 1.0);
                // 도장 원본에서 빨강/코랄 계열을 찾아 선물색으로 치환한다. 안티앨리어싱과 밉맵에서
                // 맞닿는 작은 따뜻한 포인트도 은은하게 물들 수 있으며, 도장 전체의 색 통일감으로 허용한다.
                float redDominance = sampled.r - max(sampled.g, sampled.b);
                float redHueMask = smoothstep(0.18, 0.34, redDominance);
                // 실측 원본 도장 잉크(G=0.23~0.27)를 중심으로 잡되 가장자리는 부드럽게 블렌딩한다.
                float lowGreenMask = 1.0 - smoothstep(0.29, 0.36, sampled.g);
                float accentMask = redHueMask * lowGreenMask * sampled.a * _UseAccentRemap;
                float accentInkValue = lerp(0.82, 1.0, sampled.r);
                float3 remappedTexture = lerp(
                    sampled.rgb,
                    saturate(_AccentColor.rgb * accentInkValue),
                    saturate(accentMask));

                float3 color = _UseBaseMap > 0.5
                    ? remappedTexture * _BaseColor.rgb
                    : _BaseColor.rgb * diffuse + specular.xxx + rim.xxx;
                return float4(color, _BaseAlpha * _BaseColor.a * sampled.a);
            }
            ENDHLSL
        }
    }
}
