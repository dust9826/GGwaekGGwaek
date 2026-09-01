Shader "PPack/VfxTest/TornadoBand"
{
    Properties
    {
        _NoiseMap("Noise", 2D) = "gray" {}
        _NoiseTiling("Noise Tiling", Vector) = (1, 1, 0, 0)
        _PanSpeed("Pan Speed", Vector) = (0, 0, 0, 0)
        [HDR] _ColorA("Color A", Color) = (0, 0, 0, 1)
        [HDR] _ColorB("Color B", Color) = (1, 1, 1, 1)
        _Bands("Band Count", Range(1, 32)) = 8
        _Cutoff("Cutoff", Range(0, 1)) = 0
        _Displace("Vertex Displace", Range(-1, 1)) = 0
        _Alpha("Alpha", Range(0, 1)) = 1
        _PolarUV("Polar UV", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        // **뒷면을 먼저, 앞면을 나중에 그리는 2 패스다.** 한 패스에서 Cull Off 로 양면을 그리면
        // 깊이 순서가 아니라 삼각형 순서대로 나가므로, 원뿔의 먼 쪽이 가까운 쪽을 덮는다 (실측).
        // ZWrite 가 꺼져 있어 깊이로 걸러지지도 않는다. 패스를 갈라 원뿔 안쪽 → 바깥쪽 순서를
        // 강제하면 껍데기 하나 안에서는 정렬이 맞는다.
        //
        // 껍데기 **사이**의 정렬은 셰이더가 못 한다 — 4 겹의 중심점이 같은 자리라 Unity 의 거리
        // 정렬이 임의로 뒤바뀐다. 그건 머티리얼의 renderQueue 를 층마다 다르게 줘서 잡는다.
        Pass
        {
            Name "TornadoBandBack"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex TornadoBandVertex
            #pragma fragment TornadoBandFragment
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "TornadoBandCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "TornadoBand"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex TornadoBandVertex
            #pragma fragment TornadoBandFragment
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #include "TornadoBandCommon.hlsl"
            ENDHLSL
        }
    }
}
