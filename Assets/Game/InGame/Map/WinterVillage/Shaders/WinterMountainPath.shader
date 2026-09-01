Shader "PPack/WinterMountainPath"
{
    Properties
    {
        _PathDark("Damp Earth", Color) = (0.20, 0.18, 0.15, 1)
        _PathLight("Dry Gravel", Color) = (0.39, 0.34, 0.27, 1)
        _StoneColor("Embedded Stone", Color) = (0.47, 0.47, 0.43, 1)
        _FrostColor("Packed Edge Snow", Color) = (0.66, 0.73, 0.78, 1)
        _NoiseScale("Broad Variation", Float) = 0.22
        _DetailScale("Fine Gravel", Float) = 2.8
        _EdgeWidth("Frost Edge Width", Range(0, 0.45)) = 0.18
        _EdgeIrregularity("Frost Edge Irregularity", Range(0, 0.2)) = 0.07
        _FrostStrength("Frost Edge Strength", Range(0, 1)) = 0.55
        _TrackCenter("Wheel Track Center", Range(0, 0.45)) = 0.22
        _TrackWidth("Wheel Track Width", Range(0.01, 0.15)) = 0.05
        _TrackStrength("Wheel Track Strength", Range(0, 0.6)) = 0.24
        _Smoothness("Surface Smoothness", Range(0, 1)) = 0.10
        _AmbientFloor("Night Readability", Range(0, 0.5)) = 0.38
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _PathDark;
                half4 _PathLight;
                half4 _StoneColor;
                half4 _FrostColor;
                float _NoiseScale;
                float _DetailScale;
                float _EdgeWidth;
                float _EdgeIrregularity;
                float _FrostStrength;
                float _TrackCenter;
                float _TrackWidth;
                float _TrackStrength;
                half _Smoothness;
                half _AmbientFloor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                half3 normalWS : TEXCOORD2;
                half4 fogAndVertexLight : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                half radialJoint : TEXCOORD5;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionWS = positionInputs.positionWS;
                output.positionOS = input.positionOS.xyz;
                output.positionCS = positionInputs.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.shadowCoord = GetShadowCoord(positionInputs);
                float scaleX = length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10, unity_ObjectToWorld._m20));
                float scaleZ = length(float3(unity_ObjectToWorld._m02, unity_ObjectToWorld._m12, unity_ObjectToWorld._m22));
                output.radialJoint = 1.0 - smoothstep(0.02, 0.16, abs(scaleX - scaleZ) / max(max(scaleX, scaleZ), 0.001));
                output.fogAndVertexLight = half4(
                    ComputeFogFactor(positionInputs.positionCS.z),
                    VertexLighting(positionInputs.positionWS, output.normalWS));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 worldXZ = input.positionWS.xz;
                float broad = ValueNoise(worldXZ * max(_NoiseScale, 0.001));
                float detail = ValueNoise(worldXZ * max(_DetailScale, 0.001));
                float pebbles = smoothstep(0.78, 0.95, ValueNoise(worldXZ * (_DetailScale * 3.7) + 19.4));

                half3 albedo = lerp(_PathDark.rgb, _PathLight.rgb, saturate(broad * 0.72 + detail * 0.28));
                albedo = lerp(albedo, _StoneColor.rgb, pebbles * 0.24);

                // All authored road pieces are scaled Unity cubes whose local X is the path width.
                // Keeping this mask in object space makes the snowy shoulder stay equally wide on
                // straight and rotated pieces without changing their colliders.
                float across = abs(input.positionOS.x) * 2.0;
                float radial = length(input.positionOS.xz) * 2.0;
                float pathEdge = lerp(across, radial, input.radialJoint);
                float edgeNoise = (ValueNoise(worldXZ * 0.65 + 7.2) - 0.5) * _EdgeIrregularity;
                float frostStart = 1.0 - _EdgeWidth + edgeNoise;
                float frost = smoothstep(frostStart - 0.035, frostStart + 0.055, pathEdge);
                float frostPatch = smoothstep(0.18, 0.78, ValueNoise(worldXZ * 0.31 + 13.6));
                frost *= lerp(0.58, 1.0, detail) * lerp(0.48, 1.0, frostPatch);
                albedo = lerp(albedo, _FrostColor.rgb, frost * _FrostStrength);

                // The visible path edge is slightly narrower and noisier than the unchanged driving
                // collider. Equal X/Z scale pieces are authored junction caps, so they receive a
                // circular edge while long pieces keep open ends for seamless overlaps.
                clip(1.015 - pathEdge + edgeNoise * 0.42);

                float trackDistance = abs(abs(input.positionOS.x) - _TrackCenter);
                float track = 1.0 - smoothstep(_TrackWidth, _TrackWidth * 1.9, trackDistance);
                track *= lerp(0.7, 1.0, ValueNoise(worldXZ * 0.9 + 31.7));
                albedo = lerp(albedo, _PathDark.rgb * 0.70, track * _TrackStrength * (1.0 - frost));

                half3 normalWS = normalize(input.normalWS);
                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.metallic = 0;
                surface.smoothness = lerp(_Smoothness, _Smoothness * 0.45, frost);
                surface.occlusion = 1;
                surface.alpha = 1;

                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.normalWS = normalWS;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = input.shadowCoord;
                lighting.fogCoord = input.fogAndVertexLight.x;
                lighting.vertexLighting = input.fogAndVertexLight.yzw;
                lighting.bakedGI = SampleSH(normalWS);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = max(color.rgb, albedo * _AmbientFloor);
                color.rgb = MixFog(color.rgb, lighting.fogCoord);
                return color;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
