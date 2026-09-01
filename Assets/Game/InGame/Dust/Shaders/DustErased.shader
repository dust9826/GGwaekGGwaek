// 이번 프레임에 **실제로 지워질 양**과 그 월드 좌표를 기록하는 패스.
//
// DustPaint 가 마스크를 UV 공간에 그리는 것과 달리, 이쪽은 메시를 **패드 공간**에 그린다 —
// 버텍스 셰이더가 패드 로컬 XZ 를 클립 좌표로 내보내므로 패드의 발자국이 렌더 타깃 전체를
// 채운다. 두 가지가 따라온다:
//
//   - 텍셀이 낭비되지 않는다. UV 공간에 구우면 넓은 바닥에서 패드가 차지하는 면적이 UV 의
//     1% 도 안 되고, VFX Graph 가 랜덤 UV 로 샘플하면 대부분이 빈 텍셀에 떨어져 죽는다.
//   - 렌더 타깃이 표면당이 아니라 **도구당** 하나가 된다. 패드 아래 표면이 여럿이어도
//     모두 같은 타깃에 그려 넣는다.
//
// 반드시 DustPaint 보다 **먼저** 그려야 한다. 빼고 나면 지워진 양을 알 수 없다.
Shader "PPack/DustErased"
{
    Properties
    {
        _BrushHalfExtents("Brush Half Extents (XZ)", Vector) = (0.5, 0.15, 0, 0)
        _BrushThickness("Brush Thickness", Float) = 0.25
        _BrushFeather("Brush Feather", Float) = 0.06
        _BrushStrength("Brush Strength", Range(0.002, 1)) = 0.35
        _BrushNoiseAmount("Brush Unevenness", Range(0, 1)) = 0.55
        _BrushNoiseScale("Brush Unevenness Scale", Float) = 6
        // 기록하는 좌표를 표면에서 살짝 띄운다. 표면 위 정확히 0 이면 파티클이 불투명한 바닥과
        // 같은 평면에 생겨 보이지 않는다. 노멀을 따라 띄우므로 벽에서도 옳게 동작한다.
        _ErasedLift("Erased Lift (along normal)", Float) = 0.15
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "DustErased"
            Cull Off
            ZTest Always
            ZWrite Off
            // 한 프레임에 스탬프가 겹치면 마지막 것이 이긴다. 위치가 거의 같으므로 무해하다.
            Blend Off

            HLSLPROGRAM
            #pragma vertex ErasedVertex
            #pragma fragment ErasedFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "DustBrush.hlsl"

            TEXTURE2D(_DirtMask);
            SAMPLER(sampler_DirtMask);

            // _BrushXxx 는 DustBrush.hlsl 의 CBUFFER 에 있다. 이건 이 셰이더만 쓰므로 밖에 둔다.
            float _ErasedLift;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 padLocal   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float3 liftedWS   : TEXCOORD3;
            };

            Varyings ErasedVertex(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.liftedWS = positionWS + normalWS * _ErasedLift;

                float3 padLocal = BrushToPadLocal(positionWS);

                // 패드 발자국을 -1..1 로 정규화해 렌더 타깃 전체에 펼친다.
                float2 clipXY = padLocal.xz / max(_BrushHalfExtents.xy, 1e-4);
                clipXY.y *= _ProjectionParams.x;

                output.positionCS = float4(clipXY, 0.0, 1.0);
                output.positionWS = positionWS;
                output.padLocal   = padLocal;
                output.uv         = input.uv;
                return output;
            }

            float4 ErasedFragment(Varyings input) : SV_Target
            {
                // 남아 있던 양보다 많이 지울 수는 없다. 이 min 이 "이미 깨끗한 자리에서는
                // 퍼프가 나지 않는다"는 동작의 전부다.
                float previous = SAMPLE_TEXTURE2D(_DirtMask, sampler_DirtMask, input.uv).r;
                float erased = min(previous, BrushAmount(input.positionWS, input.padLocal));
                // 붓 판정은 표면 좌표로, 기록은 띄운 좌표로. 판정을 띄운 좌표로 하면 두께가 어긋난다.
                return float4(input.liftedWS, erased);
            }
            ENDHLSL
        }
    }
}