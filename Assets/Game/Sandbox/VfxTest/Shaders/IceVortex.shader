Shader "PPack/VfxTest/IceVortex"
{
    Properties
    {
        _NoiseMap("Noise", 2D) = "gray" {}
        _NoiseTiling("Sheet Tiling", Vector) = (2, 1, 0, 0)
        _PanSpeed("Sheet Pan", Vector) = (0.06, -0.05, 0, 0)
        _StreakTiling("Streak Tiling", Vector) = (1, 6, 0, 0)
        _StreakPan("Streak Pan", Vector) = (-0.10, 0.14, 0, 0)
        [HDR] _ColorDeep("Deep Color", Color) = (0.16, 0.35, 0.62, 1)
        [HDR] _ColorBright("Bright Color", Color) = (0.75, 0.94, 1.15, 1)
        _Swirl("Swirl Shear", Range(-6, 6)) = 1.6
        _SpinSpeed("Spin Speed", Range(-2, 2)) = 0.10
        _SpinTipAmp("Tip Breathe Amount", Range(0, 2)) = 0.30
        _SpinTipFreq("Tip Breathe Speed", Range(0, 4)) = 0.55
        _StreakSharp("Streak Sharpness", Range(1, 24)) = 8
        _StreakGain("Streak Gain", Range(0, 4)) = 1.4
        _FresnelPower("Fresnel Power", Range(0.5, 8)) = 2.5
        _FresnelGain("Fresnel Gain", Range(0, 3)) = 0.8
        _TipBoost("Tip Boost", Range(0, 4)) = 1.2
        _TopFade("Top Fade", Range(0, 1)) = 0.18
        _Cutoff("Cutoff", Range(0, 1)) = 0.35
        _Alpha("Alpha", Range(0, 2)) = 1
        _Displace("Vertex Displace", Range(-1, 1)) = 0.08
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

        // 뒷면 → 앞면 2 패스. 한 패스에서 Cull Off 로 양면을 그리면 깊이가 아니라 삼각형
        // 순서로 나가 원뿔의 먼 쪽이 가까운 쪽을 덮는다. 껍데기 **사이**의 순서는 셰이더가
        // 못 잡으므로 층마다 renderQueue 를 다르게 준다.
        Pass
        {
            Name "IceVortexBack"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha One
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma vertex IceVortexVertex
            #pragma fragment IceVortexFragment
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "IceVortexCommon.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "IceVortexFront"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend SrcAlpha One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex IceVortexVertex
            #pragma fragment IceVortexFragment
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "IceVortexCommon.hlsl"
            ENDHLSL
        }
    }

    Fallback Off
}
