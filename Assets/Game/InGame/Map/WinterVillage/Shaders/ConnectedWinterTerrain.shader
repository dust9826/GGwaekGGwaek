Shader "PPack/Connected Winter Terrain"
{
    Properties
    {
        _SnowColor ("Snow Color", Color) = (0.47, 0.59, 0.76, 1)
        _RoadEdgeColor ("Packed Snow Edge", Color) = (0.36, 0.43, 0.51, 1)
        _RoadColor ("Road Color", Color) = (0.16, 0.135, 0.115, 1)
        [NoScaleOffset] _RoadMask ("Road Paint Mask", 2D) = "black" {}
        _Smoothness ("Smoothness", Range(0, 1)) = 0.16
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SnowColor;
                half4 _RoadEdgeColor;
                half4 _RoadColor;
                half _Smoothness;
            CBUFFER_END

            TEXTURE2D(_RoadMask);
            SAMPLER(sampler_RoadMask);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 paintMask : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                float2 uv : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half2 paintMask = SAMPLE_TEXTURE2D(_RoadMask, sampler_RoadMask, input.uv).rg;
                half roadMask = saturate(paintMask.r);
                half edgeMask = saturate(paintMask.g);
                half3 baseColor = lerp(_SnowColor.rgb, _RoadEdgeColor.rgb, edgeMask);
                baseColor = lerp(baseColor, _RoadColor.rgb, roadMask);

                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half3 blueHourFill = half3(0.66h, 0.72h, 0.88h);
                half3 lighting = blueHourFill + mainLight.color * diffuse * shadow * 0.52h;
                half3 color = baseColor * lighting;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
