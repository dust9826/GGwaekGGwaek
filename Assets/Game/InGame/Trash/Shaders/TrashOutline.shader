Shader "PPack/TrashOutline"
{
    Properties
    {
        _OutlineColor("Outline Color", Color) = (0.9451, 0.3569, 0.2588, 1)
        _OutlinePixels("Outline Width (Pixels)", Range(1, 8)) = 3
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry+10"
        }

        Pass
        {
            Name "TrashOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Off
            ZWrite Off
            ZTest LEqual

            Stencil
            {
                Ref 1
                ReadMask 1
                WriteMask 0
                Comp NotEqual
                Pass Keep
            }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlinePixels;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 outlineDirectionOS : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                float3 directionWS = TransformObjectToWorldDir(input.outlineDirectionOS, true);
                float2 projectedDirection = TransformWorldToHClipDir(directionWS).xy;
                float lengthSquared = max(dot(projectedDirection, projectedDirection), 1e-6);
                float2 outlineDirection = projectedDirection * rsqrt(lengthSquared);
                float2 pixelToClip = 2.0 / _ScreenParams.xy;

                output.positionCS.xy += outlineDirection
                    * _OutlinePixels
                    * pixelToClip
                    * output.positionCS.w;

                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
