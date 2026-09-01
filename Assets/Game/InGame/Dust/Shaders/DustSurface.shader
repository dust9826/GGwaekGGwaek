Shader "PPack/DustSurface"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _BaseMapTiling("Base Map Tiling", Float) = 1
        [Normal] _NormalMap("Normal Map", 2D) = "bump" {}
        _Smoothness("Smoothness", Range(0, 1)) = 0.7

        _DirtMask("Dirt Mask", 2D) = "white" {}
        _Layer0_Amount("Dirt Amount", Range(0, 1)) = 1
        // 알베도 맵이 없으면 흰색이라 _Layer0_ColorA 가 그대로 단색으로 쓰인다.
        // 맵을 물리면 _Layer0_ColorA 는 그 위의 틴트가 된다 — 재수출 없이 밝기·색조를 잡을 수 있다.
        _Layer0_AlbedoMap("Dirt Albedo", 2D) = "white" {}
        // 저주파 노이즈로 두 틴트 사이를 오간다. 한 가지 갈색으로 깔리는 걸 막는 색감 레버.
        // 둘을 같은 색으로 두면 단색이 된다.
        _Layer0_ColorA("Dirt Tint A", Color) = (1, 1, 1, 1)
        _Layer0_ColorB("Dirt Tint B", Color) = (0.82, 0.74, 0.62, 1)
        _MacroTiling("Macro Variation Tiling", Float) = 0.6
        // 러프니스 맵이 없으면 회색(0.5)이라 영향이 0이 된다. 기본 광택은 _Layer0_Smoothness 가 정하고
        // 맵은 그 값을 텍셀마다 위아래로 흔들기만 한다.
        _Layer0_RoughMap("Dirt Roughness", 2D) = "gray" {}
        _Layer0_RoughInfluence("Dirt Roughness Influence", Range(0, 1)) = 0.35
        _Layer0_Smoothness("Dirt Smoothness", Range(0, 1)) = 0.05

        [Normal] _Layer0_NormalMap("Dirt Grain Normal", 2D) = "bump" {}
        _Layer0_GrainTiling("Grain Tiling", Float) = 12
        _Layer0_GrainStrength("Grain Strength", Range(0, 4)) = 1.8

        // 확률적 타일링 — 켜면 먼지 3종(알베도·노멀·러프)을 육각 격자로 무작위 오프셋 샘플한다.
        // 텍스처 샘플이 3배가 되므로 필요할 때만 켠다.
        [Toggle(_STOCHASTIC_TILING)] _UseStochasticTiling("Stochastic Tiling", Float) = 1
        // 가중치 지수. 낮으면 셋이 뭉개져 대비가 죽고, 높으면 칸 경계가 드러난다.
        _StochasticContrast("Stochastic Contrast", Range(1, 16)) = 7

        // 먼지의 룩(알갱이·매크로·디졸브·바닥 타일)을 UV0 대신 월드 XZ 로 샘플한다.
        // UV0 은 메시마다 0~1 이라, 바닥을 패널 격자로 깔면 **모든 패널이 픽셀 단위로 똑같이**
        // 렌더돼 이음매가 격자로 드러난다. 월드로 두면 패널을 가로질러 이어진다.
        // 켜면 타일링 값의 단위가 "메시당 반복 수"에서 "미터당 반복 수"로 바뀐다.
        // 오염 마스크는 규약대로 UV0 그대로다.
        [Toggle(_WORLD_PROJECTED_DIRT)] _Layer0_WorldProjection("Dirt In World Space", Float) = 0

        _DissolveNoise("Dissolve Noise", 2D) = "white" {}
        _Layer0_DissolveTiling("Dissolve Noise Tiling", Float) = 6
        _Layer0_EdgeSoftness("Edge Softness", Range(0.001, 0.5)) = 0.04

        _Layer0_ThinOpacity("Thin Opacity", Range(0, 1)) = 0.35
        _Layer0_FullDirtAt("Full Dirt At", Range(0.01, 1)) = 0.6

        _Layer0_EdgeRim("Edge Rim", Range(0, 2)) = 0.3
        _Layer0_EdgeRimColor("Edge Rim Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap);
        SAMPLER(sampler_NormalMap);
        TEXTURE2D(_DirtMask);
        SAMPLER(sampler_DirtMask);
        TEXTURE2D(_Layer0_NormalMap);
        SAMPLER(sampler_Layer0_NormalMap);
        TEXTURE2D(_Layer0_AlbedoMap);
        SAMPLER(sampler_Layer0_AlbedoMap);
        TEXTURE2D(_Layer0_RoughMap);
        SAMPLER(sampler_Layer0_RoughMap);
        TEXTURE2D(_DissolveNoise);
        SAMPLER(sampler_DissolveNoise);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _Layer0_ColorA;
            float4 _Layer0_ColorB;
            float4 _Layer0_EdgeRimColor;
            float _MacroTiling;
            float _BaseMapTiling;
            float _Smoothness;
            float _Layer0_Amount;
            float _Layer0_Smoothness;
            float _Layer0_GrainTiling;
            float _Layer0_GrainStrength;
            float _Layer0_RoughInfluence;
            float _UseStochasticTiling;
            float _StochasticContrast;
            float _Layer0_DissolveTiling;
            float _Layer0_EdgeSoftness;
            float _Layer0_ThinOpacity;
            float _Layer0_FullDirtAt;
            float _Layer0_EdgeRim;
        CBUFFER_END

        #include "DustSurface.hlsl"
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DustForwardVertex
            #pragma fragment DustForwardFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _STOCHASTIC_TILING
            #pragma shader_feature_local_fragment _WORLD_PROJECTED_DIRT

            struct ForwardAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv0 : TEXCOORD0;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ForwardVaryings
            {
                float2 uv0 : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3;
                half4 fogFactorAndVertexLight : TEXCOORD4;
                float4 shadowCoord : TEXCOORD5;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 6);
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ForwardVaryings DustForwardVertex(ForwardAttributes input)
            {
                ForwardVaryings output = (ForwardVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                half tangentSign = input.tangentOS.w * GetOddNegativeScale();

                output.uv0 = input.uv0;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = half4(normalInputs.tangentWS, tangentSign);
                output.fogFactorAndVertexLight = half4(
                    ComputeFogFactor(positionInputs.positionCS.z),
                    VertexLighting(positionInputs.positionWS, normalInputs.normalWS));
                output.shadowCoord = GetShadowCoord(positionInputs);
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                output.positionCS = positionInputs.positionCS;
                return output;
            }

            half4 DustForwardFragment(ForwardVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 룩 텍스처의 샘플 좌표. 오염 마스크만은 규약대로 UV0 을 쓴다 — 마스크는 이
                // 메시의 것이고, 룩은 세상의 것이다.
            #ifdef _WORLD_PROJECTED_DIRT
                float2 lookUV = input.positionWS.xz;
            #else
                float2 lookUV = input.uv0;
            #endif

                float2 cleanUV = lookUV * _BaseMapTiling;
                half3 cleanAlbedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, cleanUV).rgb * _BaseColor.rgb;
                half3 cleanNormalTS = UnpackNormal(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, cleanUV));

                float d = SampleDirt(TEXTURE2D_ARGS(_DirtMask, sampler_DirtMask), input.uv0) * _Layer0_Amount;
                float dissolveN = SAMPLE_TEXTURE2D(
                    _DissolveNoise, sampler_DissolveNoise, lookUV * _Layer0_DissolveTiling).r;
                // 먼지 3종은 같은 UV·같은 타일링을 쓴다. 셋이 어긋나면 알갱이와 색이 따로 논다.
                float2 grainUV = lookUV * _Layer0_GrainTiling;
            #ifdef _STOCHASTIC_TILING
                half4 grainPacked   = DustStochasticSample(
                    TEXTURE2D_ARGS(_Layer0_NormalMap, sampler_Layer0_NormalMap), grainUV, _StochasticContrast);
                half3 dirtAlbedoTex = DustStochasticSample(
                    TEXTURE2D_ARGS(_Layer0_AlbedoMap, sampler_Layer0_AlbedoMap), grainUV, _StochasticContrast).rgb;
                half  dirtRough     = DustStochasticSample(
                    TEXTURE2D_ARGS(_Layer0_RoughMap, sampler_Layer0_RoughMap), grainUV, _StochasticContrast).r;
            #else
                half4 grainPacked   = SAMPLE_TEXTURE2D(_Layer0_NormalMap, sampler_Layer0_NormalMap, grainUV);
                half3 dirtAlbedoTex = SAMPLE_TEXTURE2D(_Layer0_AlbedoMap, sampler_Layer0_AlbedoMap, grainUV).rgb;
                half  dirtRough     = SAMPLE_TEXTURE2D(_Layer0_RoughMap, sampler_Layer0_RoughMap, grainUV).r;
            #endif
                // 인코딩된 노멀을 섞고 나서 푼다. 엄밀히는 풀고 섞어야 맞지만, 가중치가 날카로워
                // 대부분의 픽셀이 한 샘플에 지배되므로 차이가 보이지 않는다. 언팩 3회를 아낀다.
                half3 grainNormalTS = UnpackNormal(grainPacked);

                // 매크로 변화 — 그레인보다 훨씬 큰 스케일에서 색을 흔든다.
                // 이게 없으면 아무리 디테일이 좋아도 "한 가지 갈색"으로 깔린다.
                half macro = SAMPLE_TEXTURE2D(
                    _DissolveNoise, sampler_DissolveNoise, lookUV * _MacroTiling).r;
                half3 dirtTint = lerp(_Layer0_ColorA.rgb, _Layer0_ColorB.rgb, macro);
                half3 dirtAlbedo = dirtAlbedoTex * dirtTint;
                // 러프니스 맵은 기본 광택을 텍셀마다 흔들기만 한다. 맵이 회색(기본)이면 순효과 0.
                half dirtSmooth = saturate(_Layer0_Smoothness + (0.5 - dirtRough) * _Layer0_RoughInfluence);

                DustSurfaceResult dust = ComposeDust(
                    cleanAlbedo, _Smoothness, cleanNormalTS,
                    d, dissolveN, grainNormalTS,
                    dirtAlbedo, dirtSmooth,
                    _Layer0_EdgeSoftness, _Layer0_ThinOpacity, _Layer0_FullDirtAt,
                    _Layer0_GrainStrength, _Layer0_EdgeRim, _Layer0_EdgeRimColor.rgb);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo = dust.albedo;
                surfaceData.specular = 0;
                surfaceData.metallic = 0;
                surfaceData.smoothness = dust.smoothness;
                surfaceData.normalTS = dust.normalTS;
                surfaceData.emission = 0;
                surfaceData.occlusion = 1;
                surfaceData.alpha = 1;
                surfaceData.clearCoatMask = 0;
                surfaceData.clearCoatSmoothness = 0;

                half3 geometricNormalWS = NormalizeNormalPerPixel(input.normalWS);
                half3 tangentWS = SafeNormalize(input.tangentWS.xyz);
                half3 bitangentWS = input.tangentWS.w * cross(geometricNormalWS, tangentWS);
                half3x3 tangentToWorld = half3x3(tangentWS, bitangentWS, geometricNormalWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.positionCS = input.positionCS;
                inputData.tangentToWorld = tangentToWorld;
                inputData.normalWS = NormalizeNormalPerPixel(
                    TransformTangentToWorld(surfaceData.normalTS, tangentToWorld));
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = input.shadowCoord;
                inputData.fogCoord = input.fogFactorAndVertexLight.x;
                inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
                inputData.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask = SAMPLE_SHADOWMASK(input.lightmapUV);

                half4 color = UniversalFragmentPBR(inputData, surfaceData);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
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
            #pragma target 2.0
            #pragma vertex DustShadowVertex
            #pragma fragment DustShadowFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings DustShadowVertex(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                output.positionCS = TransformWorldToHClip(
                    ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(output.positionCS);
                return output;
            }

            half4 DustShadowFragment(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DustDepthVertex
            #pragma fragment DustDepthFragment
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DustDepthVertex(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DustDepthFragment(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return input.positionCS.z;
            }
            ENDHLSL
        }
    }
}
