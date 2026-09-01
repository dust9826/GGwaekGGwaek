Shader "PPack/SnowRaymarchCpu"
{
    // CPU 높이필드를 3D 로 그린다. 바닥에 패널을 깔고 정점을 밀어올리는 대신,
    // 프록시 박스를 하나 그리고 픽셀마다 광선을 쏜다 - 삼각형 12개다.
    //
    // 세 패스 전부 SV_Depth 를 쓴다. 그래서 실루엣과 차량 교차가 지오메트리가 아니라 깊이에서
    // 나오고, 눈이 블레이드 아랫부분을 가리고 실린 짐이 윗변 위로 얹힌다.
    Properties
    {
        _HeightTex ("Height (R16)", 2D) = "black" {}
        _CoarseMaxTex ("Coarse max (R16)", 2D) = "black" {}
        _LumpTex ("Baked lump lift (R8)", 2D) = "black" {}
        _FilletTex ("Baked fillet (R8, 0.5 = zero)", 2D) = "grey" {}
        _SnowColor ("Snow", Color) = (0.93, 0.95, 1.0, 1)
        _DeepColor ("Deep", Color) = (0.62, 0.70, 0.88, 1)
        _GroundColor ("Scraped ground", Color) = (0.52, 0.58, 0.68, 1)
        _AmbientColor ("Ambient", Color) = (0.20, 0.25, 0.40, 1)
        _DebugMode ("Debug (0 off, 1 coverage, 2 steps)", Float) = 0
        _BandNormalWide ("Macro normal epsilon (m)", Float) = 0.55
        _Wrap ("Realistic diffuse wrap", Range(0,1)) = 0.45
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "SnowRaymarchCpu.hlsl"

        // 눈의 생김새. 표면과 눈덩이가 같은 팔레트를 봐야 하므로 전역으로 오고, 미는 주체는
        // SnowLookStyle 하나다. 출처는 AnyTest/SnowGrainFakeV6.
        #include "../../Shaders/SnowCasualStyle.hlsl"

        float _Wrap;

        struct Attributes { float4 positionOS : POSITION; };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
        };

        Varyings Vert(Attributes IN)
        {
            Varyings OUT;
            OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
            OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
            return OUT;
        }

        // 월드 좌표를 클립 깊이로. 실루엣이 지오메트리가 아니라 깊이에서 나오게 하는 지점이다.
        float DepthFromWorld(float3 wp)
        {
            float4 clip = TransformWorldToHClip(wp);
            return clip.z / clip.w;
        }

        bool TraceFromPixel(float3 positionWS, out float3 hit, out int steps)
        {
            float3 ro = GetCameraPositionWS();
            float3 rd = normalize(positionWS - ro);

            float3 bmin = float3(_BoxMin.x, _MarchFloorY, _BoxMin.z);
            float3 bmax = float3(_BoxMax.x, _MarchTopY, _BoxMax.z);

            float t0, t1;
            steps = 0;
            hit = 0;
            if (!SlabIntersect(ro, rd, bmin, bmax, t0, t1)) return false;
            return MarchSurface(ro, rd, max(t0, 0.0), t1, hit, steps);
        }

        half3 ShadeSurface(float3 hit)
        {
            float depth = SurfaceDepth(hit.xz);

            // 노멀 둘. 좁은 것은 음영용, 넓은 것은 밴딩용이다.
            //
            // v7 이 못박은 것: 밴딩이 읽는 것은 매크로 노멀이다. 좁은 노멀로 밴드를 만들면
            // 밴드가 로브 하나하나를 따라가면서 매끄러운 곡면에 동심원 줄무늬가 생긴다.
            // 넓게 잡으면 밴드가 큰 형태를 따라가고, 그것이 계단식 파스텔의 정체다.
            float3 n = SurfaceNormal(hit.xz, 0.09);
            float3 nMacro = SurfaceNormal(hit.xz, _BandNormalWide);

            float3 sun = normalize(_SunDir);
            float shadow = SunShadow(hit + float3(0, 0.02, 0), sun);
            float ao = FieldAo(hit.xz, hit.y);

            // 긁힌 바닥 -> 얕은 눈 -> 두꺼운 눈. 치운 차선이 한눈에 구별되는 지점이고,
            // v7 이 "치운 바닥이 깔끔해야 한다" 로 정리한 것이 이 전이다. 눈을 남겨서
            // 팔레트를 살리려는 것은 오진이었고, 바닥 색을 긁힌 얼음색으로 바꾸는 것이 답이었다.
            float3 albedo = lerp(_GroundColor.rgb, _SnowColor.rgb, saturate(depth / 0.06));
            albedo = lerp(albedo, _DeepColor.rgb, saturate((depth - 0.45) * 0.9) * 0.35);

            // 사실적 기준. _SnowCasual = 0 이 비트 단위로 돌아갈 자리이고, 여기에는
            // 양자화가 없다 - 밴딩은 SnowCasualApply 가 하고, 두 곳에서 하면 계단이 두 번 생긴다.
            float wrap = saturate((dot(n, sun) + _Wrap) / (1.0 + _Wrap));
            float3 amb = _AmbientColor.rgb * albedo * ao;
            float3 realistic = albedo * (wrap * shadow * 0.95 + 0.05) + amb;

            // 그리고 토이 처리. 밴드 항은 <b>매크로</b> 노멀에서 나오고 디테일 노멀은 림과
            // 스파클에만 쓰인다 - 울퉁불퉁한 노멀로 계산한 광량을 양자화하면 모든 요철에
            // 등고선이 둘러진다.
            float3 V = normalize(_WorldSpaceCameraPos - hit);
            return SnowCasualApply(realistic, albedo, n, nMacro, sun, V,
                                   shadow, ao, hit, _MainLightColor.rgb, amb);
        }
        ENDHLSL

        Pass
        {
            Name "SnowRaymarchForward"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest LEqual
            // <b>Cull Off 여야 한다 - 앞면만도 뒷면만도 둘 다 틀린다.</b>
            //
            // Cull Back(앞면만)이면 카메라가 박스 <b>안</b>에 있을 때 프래그먼트가 하나도 안 생긴다.
            // 이 게임의 카메라는 펭귄 눈높이라 40x40x2 박스 안에 들어와 있는 것이 정상이다.
            //
            // Cull Front(뒷면만)이면 카메라가 박스 <b>위</b>로 올라갈 때 눈이 통째로 사라진다.
            // 실측(2026-08-19, 카메라 y=7): 뒷면 중 광선이 만나는 것은 바닥면 y=_MarchFloorY(-0.75)
            // 인데 그것은 지면 평면(y=0)보다 <b>멀다</b>. 그래서 ZTest LEqual 에서 지면에 가려 전부
            // 버려지고, 화면에는 맨바닥만 남는다 - 공은 눈을 먹으며 자라는데 눈이 안 보이는
            // 상태였다. 내려다보는 시점(제설 확인·미니맵·컷신)이 전부 여기 걸린다.
            //
            // 대가는 두 면이 다 살아남는 픽셀에서 마칭이 두 번 도는 것이다. 실제로는 앞면이 먼저
            // 처리되며 SV_Depth 로 표면 깊이를 쓰고, 뒷면은 그 깊이에 걸려 대부분 탈락한다.
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragForward
            #pragma target 4.5

            half4 FragForward(Varyings IN, out float outDepth : SV_Depth) : SV_Target
            {
                float3 hit; int steps;
                bool ok = TraceFromPixel(IN.positionWS, hit, steps);

                if (_DebugMode > 0.5)
                {
                    // 1 = 커버리지: 프래그먼트가 생겼으면 무엇이든 칠한다.
                    //     초록 = 표면 적중, 빨강 = 마칭 실패, 파랑 = 슬랩 미스.
                    // 2 = 스텝 히트맵.
                    outDepth = ok ? DepthFromWorld(hit) : 0.5;
                    if (_DebugMode > 1.5)
                        return half4(saturate(steps / 48.0), saturate(steps / 96.0), 0, 1);
                    return ok ? half4(0, 0.8, 0.2, 1) : half4(0.9, 0.1, 0.1, 1);
                }

                if (!ok) discard;
                outDepth = DepthFromWorld(hit);
                return half4(ShadeSurface(hit), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "SnowRaymarchDepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ZTest LEqual
            Cull Off            // 포워드와 같은 이유. 프라임과 포워드가 다른 면을 보면 눈이 지워진다
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepthOnly
            #pragma target 4.5

            half4 FragDepthOnly(Varyings IN, out float outDepth : SV_Depth) : SV_Target
            {
                // 깊이 프라이밍이 켜지면 이 패스로 프라임하고 포워드를 ZTest Equal 로 그린다.
                // 둘이 다른 표면을 보고하면 눈이 통째로 지워진다 - 같은 함수를 써야 하는 이유다.
                float3 hit; int steps;
                if (!TraceFromPixel(IN.positionWS, hit, steps)) discard;
                outDepth = DepthFromWorld(hit);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "SnowRaymarchShadow"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            Cull Off            // 포워드와 같은 이유. 프라임과 포워드가 다른 면을 보면 눈이 지워진다
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragShadow
            #pragma target 4.5

            half4 FragShadow(Varyings IN, out float outDepth : SV_Depth) : SV_Target
            {
                float3 hit; int steps;
                if (!TraceFromPixel(IN.positionWS, hit, steps)) discard;
                outDepth = DepthFromWorld(hit);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
