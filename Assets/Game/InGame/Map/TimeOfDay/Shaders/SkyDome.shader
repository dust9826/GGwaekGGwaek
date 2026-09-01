// 하늘 겹침 레이어(별·오로라) 전용. URP Unlit을 쓰지 않는 이유는 딱 하나 - 안개다.
//
// 돔은 반지름 450m의 진짜 메시라, 지수제곱 안개에서 가시도가 0.0003까지 떨어져 별이 통째로
// 지워진다(측정값). URP의 안개 키워드는 렌더러가 전역으로 켜므로 머티리얼 단위로 끌 수 없다.
// 그래서 안개 항이 아예 없는 셰이더를 따로 둔다.
//
// 텍스처의 검은 배경은 그대로 투명해진다. ZWrite는 끄고 ZTest는 LEqual이라
// 산·집 같은 실제 지오메트리가 별을 정상적으로 가린다. Queue는 Transparent - URP는 불투명
// 다음에 스카이박스를 그리므로, 하늘 위에 더해지려면 그보다 뒤여야 한다.
//
// 합성은 프리멀티플라이드 알파(Blend One OneMinusSrcAlpha)다. 순수 가산(One One)이 아닌
// 이유는 오로라 때문이다 - 가산은 교환법칙이 성립해서 그리는 순서를 바꿔도 결과가 같고,
// 그래서 밝은 오로라 커튼 뒤의 별이 그대로 뜼 보인다. 실제 오로라는 발광하는 반투명
// 매질이라 자기 빛을 더하면서 뒤쪽을 가린다. 그게 정확히 이 블렌드 식이다.
//
// _Occlusion 이 0 이면 알파가 항상 0 이라 One One 과 수식적으로 완전히 같아진다.
// 별은 0(서로 가리지 않는다), 오로라는 1을 쓴다. 두 돔이 같은 셀이더를 계속 공유한다.
Shader "PPack/SkyDome"
{
    Properties
    {
        [MainTexture] _BaseMap ("Sky Layer", 2D) = "black" {}
        [MainColor][HDR] _BaseColor ("Intensity", Color) = (1,1,1,1)
        _Occlusion ("Occlusion", Range(0,1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SkyDome"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Occlusion;
            CBUFFER_END

            Varyings vert (Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                return output;
            }

            half4 frag (Varyings input) : SV_Target
            {
                half3 layer = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;

                // 가리는 정도는 텍스처가 얼마나 진한가로 정한다. 밝기 상한(_BaseColor.rgb)을
                // 쓰면 안 된다 - 그걸 HDR 로 1을 넘기면 알파가 포화되어 흰 판이 된다.
                // _BaseColor.a 는 페이드(0~1)라 오로라가 약해질 때 가림도 같이 풀린다.
                half density = max(layer.r, max(layer.g, layer.b));
                half alpha = saturate(density * _Occlusion * _BaseColor.a);

                return half4(layer * _BaseColor.rgb, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
