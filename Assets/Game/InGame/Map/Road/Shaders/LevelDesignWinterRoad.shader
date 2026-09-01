Shader "PPack/Level Design Winter Road"
{
    Properties
    {
        _RoadColor ("Frozen Dirt", Color) = (0.25, 0.21, 0.18, 1)
        _EdgeColor ("Snowy Edge", Color) = (0.70, 0.82, 0.92, 1)
        _EdgeWidth ("Snowy Edge Width", Range(0, 0.45)) = 0.16
        _RadialEdge ("Radial Edge", Range(0, 1)) = 0
        _NoiseScale ("Variation Scale", Float) = 1.4
        _Smoothness ("Smoothness", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+5" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _RoadColor;
                half4 _EdgeColor;
                half _EdgeWidth;
                half _RadialEdge;
                float _NoiseScale;
                half _Smoothness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fog : TEXCOORD3;
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.fog = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float ribbonAcross = abs(input.uv.x - 0.5) * 2.0;
                float radialAcross = length(input.uv - float2(0.5, 0.5)) * 2.0;
                float across = lerp(ribbonAcross, radialAcross, saturate(_RadialEdge));
                float edgeNoise = (Hash21(floor(input.positionWS.xz * _NoiseScale)) - 0.5) * 0.10;
                // EasyRoads can tile either UV axis along the road.  A disabled
                // edge must therefore be an explicit zero instead of a nearly
                // zero smoothstep, otherwise tiled UVs produce white checks.
                half edgeWidth = max(_EdgeWidth, 0.001h);
                half edge = _EdgeWidth > 0.0005h
                    ? smoothstep(1.0 - edgeWidth + edgeNoise, 1.0, across)
                    : 0.0h;
                half variation = lerp(0.88, 1.10, Hash21(floor(input.positionWS.xz * (_NoiseScale * 2.7))));
                half3 albedo = lerp(_RoadColor.rgb * variation, _EdgeColor.rgb, edge);

                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = SampleSH(normalWS);
                half3 lighting = ambient + mainLight.color * diffuse * mainLight.shadowAttenuation * 0.65;
                half3 color = albedo * max(lighting, 0.28h);
                color = MixFog(color, input.fog);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
