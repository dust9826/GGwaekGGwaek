Shader "PPack/SnowHeightRamp"
{
    // 높이맵을 색으로만 읽는다. 지오메트리 변위도, 조명도, 그림자도 없다 -
    // 조명이 섞이면 색에서 높이를 되읽을 수 없다.
    Properties
    {
        _MainTex  ("Height (R16, mm / 65535)", 2D) = "black" {}
        _RampMaxM ("Ramp max (m)", Float) = 1.5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite On
        Cull Off

        Pass
        {
            Name "HeightRamp"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _RampMaxM;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            // 다섯 정거장. 낮은 곳이 파랑, 높은 곳이 빨강.
            float3 Ramp(float t)
            {
                const float3 c0 = float3(0.184, 0.310, 0.659);
                const float3 c1 = float3(0.353, 0.627, 0.839);
                const float3 c2 = float3(0.788, 0.847, 0.753);
                const float3 c3 = float3(0.878, 0.659, 0.376);
                const float3 c4 = float3(0.722, 0.227, 0.227);
                float s = saturate(t) * 4.0;
                float3 a = lerp(c0, c1, saturate(s));
                a = lerp(a, c2, saturate(s - 1.0));
                a = lerp(a, c3, saturate(s - 2.0));
                a = lerp(a, c4, saturate(s - 3.0));
                return a;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // R16 UNorm 이라 1.0 이 곧 65,535 mm = 65.535 m 다. 저장 단위가 표시 단위로
                // 그대로 넘어오는 것이 ushort 를 고른 이유 중 하나다.
                float heightM = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).r * 65.535;
                return half4(Ramp(heightM / max(_RampMaxM, 1e-4)), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
