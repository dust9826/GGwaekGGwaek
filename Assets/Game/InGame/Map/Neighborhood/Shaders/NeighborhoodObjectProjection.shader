Shader "PPack/Map/Neighborhood Object Projection"
{
    Properties
    {
        _BaseMap ("Concept Projection", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _ProjectionRotation ("Projection Rotation", Range(0, 3)) = 0
        _FlipX ("Flip X", Float) = 0
        _FlipY ("Flip Y", Float) = 0
        _Saturation ("Saturation", Range(0, 2)) = 1.05
        _Brightness ("Brightness", Range(0, 2)) = 1.0
        _Roughness ("Roughness", Range(0, 1)) = 0.8
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _ProjectionRotation;
                float _FlipX;
                float _FlipY;
                float _Saturation;
                float _Brightness;
                float _Roughness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float2 RotateProjection(float2 sourceUv, float rotationValue)
            {
                int quarterTurn = (int)round(rotationValue);
                if (quarterTurn == 1) return float2(sourceUv.y, 1.0 - sourceUv.x);
                if (quarterTurn == 2) return 1.0 - sourceUv;
                if (quarterTurn == 3) return float2(1.0 - sourceUv.y, sourceUv.x);
                return sourceUv;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = (input.positionOS.xy + float2(17.99, 20.0)) / float2(35.98, 40.0);
                uv = RotateProjection(uv, _ProjectionRotation);
                if (_FlipX > 0.5) uv.x = 1.0 - uv.x;
                if (_FlipY > 0.5) uv.y = 1.0 - uv.y;
                uv.y = 1.0 - uv.y;

                half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, saturate(uv)).rgb * _BaseColor.rgb;
                half luminance = dot(albedo, half3(0.2126, 0.7152, 0.0722));
                albedo = lerp(luminance.xxx, albedo, _Saturation) * _Brightness;

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half normalLight = saturate(dot(normalize(input.normalWS), mainLight.direction));
                half lighting = 0.58h + normalLight * 0.42h * mainLight.shadowAttenuation;
                half3 color = albedo * lighting * mainLight.color;

                half3 viewDirection = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                half3 halfDirection = SafeNormalize(viewDirection + mainLight.direction);
                half highlight = pow(saturate(dot(normalize(input.normalWS), halfDirection)), lerp(48.0h, 6.0h, _Roughness));
                color += highlight * (1.0h - _Roughness) * 0.18h;
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
}
