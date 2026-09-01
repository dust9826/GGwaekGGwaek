// DeliveryRouteDisplay 전용. DeliveryBeaconUnlit 과 같은 이유로 실제 씬 조명과 무관한 고정
// 가짜 광원으로 fake diffuse + specular 를 계산하고, 추가로 경로를 따라간 거리(UV.y)를 이용해
// 목적지 쪽으로 흐르는 반사 하이라이트를 얹어 진행 방향을 보여준다. 흰색을 그냥 얹는 대신
// _BaseColor 로 틴트된 specular 강도를 변조해서, 경로선과 같은 색조를 유지한 채 밝기만
// 도드라지게 한다 — 흰 점이 산만하게 지나가는 느낌을 없애기 위함.
// 경로선 자체는 반투명/저채도로 옅게 깔고, 흐르는 구간만 불투명에 가깝게 진해져서 시선이
// 그쪽으로만 모이도록 alpha 를 stripe 로 변조한다.
Shader "PPack/DeliveryRouteRibbonUnlit"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _BaseAlpha("Base Line Alpha", Range(0, 1)) = 0.35
        _AmbientFloor("Ambient Floor", Range(0, 1)) = 0.55
        _Shininess("Shininess", Range(1, 128)) = 18
        _SpecularIntensity("Specular Intensity", Range(0, 2)) = 0.45
        _StripeSpacing("Stripe Spacing (m)", Range(1, 40)) = 14
        _StripeWidth("Stripe Width (m)", Range(0.05, 4)) = 0.3
        _FlowSpeed("Flow Speed (m/s)", Range(0, 20)) = 6
        _StripeBrightness("Stripe Brightness", Range(0, 3)) = 1.4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        // 닫힌 관이라 Cull Off 면 모든 픽셀에서 뒷면과 앞면이 두 번 블렌딩되어 _BaseAlpha 로 지정한
        // 것보다 훨씬 진하게 나온다("전반적으로 연하게" 가 안 먹던 이유). 뒷면을 잘라 한 겹만 남긴다.
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
                float _BaseAlpha;
                float _AmbientFloor;
                float _Shininess;
                float _SpecularIntensity;
                float _StripeSpacing;
                float _StripeWidth;
                float _FlowSpeed;
                float _StripeBrightness;
            CBUFFER_END

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
                OUT.uv = IN.uv;
                return OUT;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetCameraPositionWS() - IN.positionWS);
                float3 L = normalize(float3(0.4, 0.85, 0.35));
                float3 H = normalize(V + L);

                float diffuse = lerp(_AmbientFloor, 1.0, saturate(dot(N, L)));
                float specularBase = pow(saturate(dot(N, H)), _Shininess);

                // 목적지 쪽(uv.y 증가 방향)으로 흐르는 반사 하이라이트. uv.y 는 경로를 따라간 실제
                // 거리(월드 미터)라 경로 길이와 무관하게 항상 같은 간격으로 보인다.
                //
                // cos^N 펄스는 밴드 폭이 간격에 비례해서 커지는 구조라, 간격을 넓히면 밴드도 같이
                // 두꺼워져 리본 폭(0.5m)과 비슷한 정사각형 덩어리로 찍혔다. 대신 "가장 가까운 밴드
                // 중심까지의 거리"를 미터로 구해서 폭을 직접 지정한다 — 간격(_StripeSpacing)과
                // 폭(_StripeWidth)이 완전히 분리되어 넓은 간격 + 가는 선을 동시에 낼 수 있다.
                // frac 의 불연속은 d 가 최대(밴드에서 가장 먼 지점, stripe=0)인 곳에서만 생겨 안 보인다.
                float spacing = max(_StripeSpacing, 1e-4);
                float travel = IN.uv.y - _Time.y * _FlowSpeed;
                float d = abs(frac(travel / spacing + 0.5) - 0.5) * spacing;
                float stripe = 1.0 - smoothstep(0.0, _StripeWidth, d);

                // 흐르는 구간(stripe)에서만 반사 강도(specular)를 켠다 — 평소에 꺼둬야 각진 단면의
                // 면(facet)마다 법선이 달라 생기는 정적인 반짝임(희미한 점들)이 남지 않는다. 색은
                // 항상 _BaseColor 로 틴트되므로 경로선과 같은 색조의 반짝임으로 보인다.
                float specular = specularBase * _SpecularIntensity * stripe * _StripeBrightness;
                float3 color = _BaseColor.rgb * (diffuse + specular);

                // 평소엔 옅은 반투명 선으로만 깔고, 흐르는 구간에서만 진하게(불투명에 가깝게)
                // 올려서 그쪽으로 시선이 모이게 한다.
                float alpha = lerp(_BaseAlpha, 1.0, stripe) * _BaseColor.a;
                return float4(color, alpha);
            }
            ENDHLSL
        }
    }
}
