// 눈 표면 — 바닥 위 패널의 정점 변위 셰이더.
//
// 설계: docs/specs/2026-08-14-snow-surface.md
//
// 패스 셋(ForwardLit / ShadowCaster / DepthOnly)이 **모두 같은 변위와 같은 clip** 을 한다.
// ForwardLit 만 밀면 그림자와 깊이가 변위 안 된 평면에 남아 그림자가 눈 표면에서 뜬다(스펙 §2 규칙 3).
//
// 하드웨어 테셀레이션으로 올릴 때: 정점 함수는 Attributes -> Varyings 모양을 유지하고 있으므로
// domain 셰이더가 패치 안에서 Attributes 를 보간해 그대로 호출하면 된다. 변위는 include 의
// 순수 함수(SnowDisplaceWS)라 옮길 것이 없다.
Shader "PPack/SnowSurface"
{
    Properties
    {
        _SnowColor("Snow Color", Color) = (0.93, 0.95, 1.0, 1)
        _SnowSmoothness("Snow Smoothness", Range(0, 1)) = 0.35
        _SnowAmbientFloor("Snow Ambient Floor", Range(0, 0.75)) = 0.34
        [Toggle] _SlopeSafeLighting("Slope Safe Lighting", Float) = 0

        // 방금 밀린 자국 — 다져지고 젖은 톤. 필드 G 채널이 가중치다.
        _PackedColor("Packed Track Color", Color) = (0.72, 0.78, 0.86, 1)
        _PackedSmoothness("Packed Track Smoothness", Range(0, 1)) = 0.75

        [Normal] _GrainMap("Snow Grain Normal", 2D) = "bump" {}
        // 월드 공간 타일링이라 단위는 "미터당 반복 수"다. 패널을 가로질러 이어진다.
        _GrainTiling("Grain Tiling (per meter)", Float) = 1.5
        _GrainStrength("Grain Strength", Range(0, 4)) = 1.2

        _SnowMaxDepth("Max Depth (m)", Float) = 0.3
        // 변위가 읽는 밉. 12.5cm 필드 + 25cm 정점이면 1 이 맞다(나이퀴스트).
        _DisplaceMip("Displace Mip", Range(0, 4)) = 1

        // 벽 프로파일 — 이 창 안에서만 높이가 떨어진다. 좁을수록 벽이 서고, 그래야
        // 보이는 실루엣을 clip 이 소유한다(게이트 1차 실측: 그대로 밀면 25cm 계단이 된다).
        _WallKnee("Wall Knee", Range(0, 0.9)) = 0.12
        // 정점 높이 지터 — 셀 경계의 축 정렬을 깬다. 주파수는 정점 간격으로 표현 가능해야 한다.
        _HeightJitter("Height Jitter (m)", Range(0, 0.1)) = 0.02
        // 필드 샘플 좌표를 흔든다. 대략 셀 한 칸(0.125m)이 기준.
        _FieldWarp("Field Warp (m)", Range(0, 0.4)) = 0.1
        _FieldWarpScale("Field Warp Scale (per meter)", Float) = 2
        _HeightJitterScale("Height Jitter Scale (per meter)", Float) = 1.5
        _WallTop("Wall Top", Range(0.05, 1)) = 0.55
        // 이 값 미만의 깊이는 눈이 없는 것으로 보고 잘라낸다. 실루엣이 여기서 나온다.
        // 벽 프로파일 창 안에 두어야 clip 이 벽의 중간을 자른다.
        _SnowCutoff("Snow Cutoff", Range(0.001, 0.9)) = 0.22
        // 경계를 흩뜨리는 양(depth01 단위)과 결의 크기(미터당 반복 수).
        _EdgeNoiseAmount("Edge Noise Amount", Range(0, 0.8)) = 0.34
        _EdgeNoiseScale("Edge Noise Scale (per meter)", Float) = 6

        // 미세 요철 — 절차적. 눈이 매끈한 판으로 읽히는 것을 막는다.
        // 단위는 **요철 높이(m)**. 0.45 로 두면 45cm 짜리 요철이라 표면이 무늬로 뒤덮인다.
        _MicroRelief("Micro Relief Height (m)", Range(0, 0.08)) = 0.015
        _MicroReliefScale("Micro Relief Scale (per meter)", Float) = 9
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4  _SnowColor;
            half   _SnowSmoothness;
            half   _SnowAmbientFloor;
            half   _SlopeSafeLighting;
            half4  _PackedColor;
            half   _PackedSmoothness;
            float4 _GrainMap_ST;
            float  _GrainTiling;
            half   _GrainStrength;
            float  _SnowMaxDepth;
            float  _DisplaceMip;
            float  _HeightJitter;
            float  _FieldWarp;
            float  _FieldWarpScale;
            float  _HeightJitterScale;
            float  _WallKnee;
            float  _WallTop;
            float  _SnowCutoff;
            float  _EdgeNoiseAmount;
            float  _EdgeNoiseScale;
            float  _MicroRelief;
            float  _MicroReliefScale;
        CBUFFER_END

        TEXTURE2D(_GrainMap);        SAMPLER(sampler_GrainMap);

        #include "SnowSurface.hlsl"

        // 경계 판정 — 세 패스가 같은 식을 써야 그림자와 깊이가 표면과 어긋나지 않는다.
        void SnowClip(float3 positionWS, float depth01)
        {
            float n = SnowEdgeNoise(positionWS.xz, _EdgeNoiseScale) - 0.5;
            clip(depth01 - _SnowCutoff - n * _EdgeNoiseAmount);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            // 정점 단계 텍스처 페치가 필요하다 — 먼지의 2.0 으로는 안 된다.
            #pragma target 3.5
            #pragma vertex SnowForwardVertex
            #pragma fragment SnowForwardFragment

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct ForwardAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ForwardVaryings
            {
                float3 positionWS : TEXCOORD0;
                half3  normalWS : TEXCOORD1;
                half4  fogFactorAndVertexLight : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ForwardVaryings SnowForwardVertex(ForwardAttributes input)
            {
                ForwardVaryings output = (ForwardVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                positionWS = SnowDisplaceWS(positionWS, normalWS, _SnowMaxDepth, _DisplaceMip, _WallKnee, _WallTop,
                                          _HeightJitter, _HeightJitterScale,
                                          _FieldWarp, _FieldWarpScale);

                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactorAndVertexLight = half4(
                    ComputeFogFactor(output.positionCS.z),
                    VertexLighting(positionWS, normalWS));
                output.shadowCoord = TransformWorldToShadowCoord(positionWS);
                return output;
            }

            half4 SnowForwardFragment(ForwardVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = SnowWarpedFieldUV(input.positionWS, _FieldWarp, _FieldWarpScale);
                float2 field = SampleSnowField(uv, 0);
                SnowClip(input.positionWS, field.r);

                // 지오메트리는 25cm 인데 눈 깊이의 미세 기울기는 필드 해상도로 간다.
                // SnowFieldNormalWS 는 수평 패널을 기준으로 한 교란값이므로, 경사 지형에서
                // 그대로 쓰면 실제 지형 노멀을 (0,1,0) 으로 덮어써 완만한 경계가 검은
                // 한 줄짜리 능선처럼 보인다. 메시의 연속 노멀을 기준으로 XZ 교란만 더한다.
                float3 fieldNormalWS = SnowFieldNormalWS(uv, _SnowMaxDepth);
                float3 normalWS = SafeNormalize(
                    SafeNormalize(input.normalWS) + float3(fieldNormalWS.x, 0.0, fieldNormalWS.z));
                normalWS = SnowMicroRelief(normalWS, input.positionWS.xz,
                                           _MicroReliefScale, _MicroRelief);

                float3 tangentWS, bitangentWS;
                SnowBuildFrame(normalWS, tangentWS, bitangentWS);
                half3 grainTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_GrainMap, sampler_GrainMap, input.positionWS.xz * _GrainTiling),
                    _GrainStrength);
                normalWS = SafeNormalize(
                    grainTS.x * tangentWS + grainTS.y * bitangentWS + grainTS.z * normalWS);

                half fresh = saturate(field.g);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = lerp(_SnowColor.rgb, _PackedColor.rgb, fresh);
                surface.smoothness = lerp(_SnowSmoothness, _PackedSmoothness, fresh);
                surface.occlusion = 1;
                surface.alpha = 1;

                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.normalWS = normalWS;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = input.shadowCoord;
                lighting.fogCoord = input.fogFactorAndVertexLight.x;
                lighting.vertexLighting = input.fogFactorAndVertexLight.yzw;
                lighting.bakedGI = SampleSH(normalWS);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(lighting, surface);
                // 눈은 다중 산란과 주변광 반사가 강하므로 지형 굴곡도 완전한 검정으로
                // 떨어지지 않는다. 저각도에서 완만한 경계가 검은 선으로 보이는 현상만
                // 눌러 주고, 직접광·그림자·안개 변화는 그대로 유지한다.
                color.rgb = max(color.rgb, surface.albedo * _SnowAmbientFloor);

                // Hillside처럼 여러 높이의 메시를 한 눈 표면으로 잇는 맵은 저각도 PBR
                // 응답이 한 픽셀짜리 등고선으로 보일 수 있다. 이 모드는 제설 clip/변위는
                // 그대로 두고 눈의 높은 산란 특성에 맞춘 안정적인 저주파 톤만 사용한다.
                half slopeNoise = SnowValueNoise(input.positionWS.xz * 0.35);
                half3 slopeSafeColor = surface.albedo * lerp(0.58h, 0.66h, slopeNoise);
                color.rgb = lerp(color.rgb, slopeSafeColor, saturate(_SlopeSafeLighting));
                color.rgb = MixFog(color.rgb, lighting.fogCoord);
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
            #pragma target 3.5
            #pragma vertex SnowShadowVertex
            #pragma fragment SnowShadowFragment
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
                float3 positionWS : TEXCOORD0;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            ShadowVaryings SnowShadowVertex(ShadowAttributes input)
            {
                ShadowVaryings output = (ShadowVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                positionWS = SnowDisplaceWS(positionWS, normalWS, _SnowMaxDepth, _DisplaceMip, _WallKnee, _WallTop,
                                          _HeightJitter, _HeightJitterScale,
                                          _FieldWarp, _FieldWarpScale);
                output.positionWS = positionWS;

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

            half4 SnowShadowFragment(ShadowVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // 치워진 자리가 눈 그림자를 드리우면 안 된다 — 경계 판정을 여기서도 한다.
                SnowClip(input.positionWS,
                         SampleSnowField(SnowWarpedFieldUV(input.positionWS, _FieldWarp, _FieldWarpScale), 0).r);
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
            #pragma target 3.5
            #pragma vertex SnowDepthVertex
            #pragma fragment SnowDepthFragment
            #pragma multi_compile_instancing

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float3 positionWS : TEXCOORD0;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings SnowDepthVertex(DepthAttributes input)
            {
                DepthVaryings output = (DepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                positionWS = SnowDisplaceWS(positionWS, normalWS, _SnowMaxDepth, _DisplaceMip, _WallKnee, _WallTop,
                                          _HeightJitter, _HeightJitterScale,
                                          _FieldWarp, _FieldWarpScale);

                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half SnowDepthFragment(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                SnowClip(input.positionWS,
                         SampleSnowField(SnowWarpedFieldUV(input.positionWS, _FieldWarp, _FieldWarpScale), 0).r);
                return 0;
            }
            ENDHLSL
        }
    }
}
