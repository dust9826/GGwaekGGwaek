// 오염 마스크에 붓질하는 패스. 화면이 아니라 **메시의 UV 공간**에 렌더한다.
//
// 버텍스 셰이더가 UV 를 클립 좌표로 내보내면 래스터라이저가 UV 아일랜드를 그대로 재현하고,
// 프래그먼트에서 그 텍셀의 월드 위치로 붓 판정을 한다.
//
// 레이캐스트의 textureCoord 를 UV 로 쓰는 방식보다 나은 점:
//   - 붓 크기가 UV 왜곡과 무관하게 월드 기준으로 일정하다
//   - UV 이음새가 자연스럽게 처리된다
//   - Read/Write 가능한 MeshCollider 가 필요 없다
//
// 붓 판정 자체는 DustBrush.hlsl 에 있다 — DustErased 와 공유해야 지운 양과 기록한 양이
// 갈라지지 않기 때문이다.
Shader "PPack/DustPaint"
{
    Properties
    {
        _BrushHalfExtents("Brush Half Extents (XZ)", Vector) = (0.5, 0.15, 0, 0)
        _BrushThickness("Brush Thickness", Float) = 0.25
        _BrushFeather("Brush Feather", Float) = 0.06
        // 하한이 0.002 인 이유: 마스크가 R8 이라 매 스탬프 round(strength * 255) 단계씩 빠지고,
        // 그 아래는 반올림이 0 이라 영원히 아무 일도 일어나지 않는다. Dust/AGENTS.md 참조.
        _BrushStrength("Brush Strength", Range(0.002, 1)) = 0.35
        _BrushNoiseAmount("Brush Unevenness", Range(0, 1)) = 0.55
        _BrushNoiseScale("Brush Unevenness Scale", Float) = 6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "DustBrush"
            Cull Off
            ZTest Always
            ZWrite Off
            // dst - src. 마스크는 지우기 전용이라 빼기만 한다.
            BlendOp RevSub
            Blend One One

            HLSLPROGRAM
            #pragma vertex BrushVertex
            #pragma fragment BrushFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "DustBrush.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 padLocal   : TEXCOORD1;
            };

            Varyings BrushVertex(Attributes input)
            {
                Varyings output;

                // UV(0..1) -> 클립(-1..1). 메시를 UV 공간에 펼쳐 그린다.
                float2 clipXY = input.uv * 2.0 - 1.0;
                // 렌더 타깃에 그릴 때 그래픽스 API 에 따라 Y 가 뒤집힌다.
                clipXY.y *= _ProjectionParams.x;

                output.positionCS = float4(clipXY, 0.0, 1.0);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.padLocal   = BrushToPadLocal(output.positionWS);
                return output;
            }

            half4 BrushFragment(Varyings input) : SV_Target
            {
                return half4(BrushAmount(input.positionWS, input.padLocal), 0, 0, 0);
            }
            ENDHLSL
        }
    }
}
