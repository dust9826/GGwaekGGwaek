// 저사양 눈. 마칭이 아니라 <b>미리 잘게 쪼갠 패널의 정점을 밀어올린다.</b>
//
// 왜 이것이 따로 있어야 하는가
// ---------------------------
// 마처(`SnowRaymarchCpu`)는 모바일에서 <b>컴파일되지 않는다</b>. `#pragma target 4.5` 에 프래그먼트가
// `SV_Depth` 를 쓰고, GLES3 는 둘 다 못 한다. 그리고 그 위에 CPU 비용이 남는다 - 마처를 먹이기
// 위한 굽기(로브·둥근 어깨·coarse-max)가 M5 Pro 에서 1.79 ms 다(844,800 셀). 그 굽기는 마칭을 위해서만
// 존재하므로, 정점 변위로 바꾸면 <b>같이 사라진다</b>.
//
// 그래서 이쪽이 안 하는 것 셋: 마칭 안 함, `SV_Depth` 안 씀, 구운 텍스처 안 읽음. 읽는 것은 높이
// 텍스처 하나이고 그것은 권위 격자의 `ushort[]` 를 그대로 올린 것이다.
//
// 무엇을 잃는가 - 정직하게
// ------------------------
// 1. <b>실루엣이 패널 격자에 묶인다.</b> 마처는 픽셀마다 광선을 쏘므로 자국의 벽이 셀 해상도(12.5 cm)
//    까지 선명하지만, 이쪽은 패널의 정점 간격까지만 표현한다. 간격을 좁히면 정점이 제곱으로 늘어난다.
// 2. <b>차량·공과의 교차가 깊이에서 나오지 않는다.</b> 마처는 `SV_Depth` 로 픽셀 단위 교차를 공짜로
//    얻지만, 이쪽은 평범한 메시라 눈에 파묻히는 표현이 지오메트리 정확도만큼만 맞는다.
// 3. <b>둥근 어깨(fillet)와 로브가 없다.</b> 그것들은 구운 텍스처였고 그 굽기를 없애는 것이 목적이다.
//    대신 정점 노이즈로 표면을 조금 흔들어 각진 슬래브로 보이지 않게 한다.
//
// 얻는 것: GLES3 에서 돌고, CPU 굽기가 0 이고, 드로가 하나다.
//
// 음영은 마처와 <b>같은</b> `SnowCasualApply` 를 통과한다. 룩이 갈리면 옵션을 바꾼 것이 조명을 바꾼
// 것처럼 보이고, 그러면 A/B 가 불가능해진다.
Shader "PPack/SnowDisplace"
{
    Properties
    {
        _HeightTex ("Height (R16)", 2D) = "black" {}
        _FloorTex ("Floor (R16)", 2D) = "black" {}
        _MaskTex ("Coverage (R8)", 2D) = "white" {}
        _SnowColor ("Snow", Color) = (0.93, 0.95, 1.0, 1)
        _DeepColor ("Deep", Color) = (0.62, 0.70, 0.88, 1)
        _GroundColor ("Scraped ground", Color) = (0.52, 0.58, 0.68, 1)
        _AmbientColor ("Ambient", Color) = (0.20, 0.25, 0.40, 1)
        _Wrap ("Realistic diffuse wrap", Range(0,1)) = 0.45
        _AoStrength ("Curvature AO", Range(0,2)) = 0.8
        _GrainAmpM ("Vertex grain (m)", Range(0,0.15)) = 0.03
        _GrainFreq ("Vertex grain frequency (1/m)", Range(0.2,4)) = 1.3

        // <b>뷰가 코드로 넣는 값들도 반드시 여기 선언한다.</b> 선언하지 않으면 평소에는 동작하는데
        // (머티리얼이 미선언 프로퍼티도 런타임에 들고 있다) <b>셰이더를 재임포트하는 순간 전부 0 이
        // 된다</b> — 그러면 필드 UV 가 (0,0) 으로 굳어 격자 전체가 셀 하나의 값으로 그려진다.
        // 초기 적설이 균일하면 그 화면이 정상과 구별되지 않아서, 경사가 들어오고 나서야 드러났다.
        _PatchMin ("Field origin (object XZ)", Vector) = (0,0,0,0)
        _InvPatchSize ("1 / field size", Vector) = (0,0,0,0)
        _SunDir ("Sun direction", Vector) = (0,1,0,0)
        _CellSizeM ("Cell size (m)", Float) = 0.125
        _FloorOriginY ("Floor origin Y (m)", Float) = 0
        _EdgeProfile ("Edge profile (0 rounded shoulder, 1 flared skirt)", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "../../Shaders/SnowCasualStyle.hlsl"

        TEXTURE2D(_HeightTex);
        // <b>인라인 샘플러 이름은 규약이다.</b> 접미사를 붙여 `sampler_linear_clamp` 로
        // 두면 유니티가 인라인 샘플러로 알아보지 못하고 바인딩이 어긋난다. 마처와 같은 이름을 쓴다.
        SAMPLER(sampler_linear_clamp);

        // 바닥과 마스크. <b>둘 다 불변이다</b> - 구운 데이터라 프레임마다 올릴 것이 없고,
        // 바닥 맵이 없는 씬에서는 뷰가 1x1 짜리(바닥 0 · 마스크 1)를 물려 준다. 그래서 이 셰이더에
        // 분기도 키워드도 없다.
        TEXTURE2D(_FloorTex);
        TEXTURE2D(_MaskTex);
        // 커버리지는 <b>bilinear</b> 로 읽는다. 이 값이 경계 마감이기도 하므로 점 샘플이면
        // 12.5 cm 톱니가 그대로 실루엣에 나온다.

        // 마처와 같은 방식으로 <b>전역 스코프</b>에 둔다 - UnityPerMaterial 에 넣으면서 Properties 에
        // 선언하지 않으면 SRP 배쳐가 재질 상수 버퍼를 잘못 읽을 수 있다.
        float4 _PatchMin;         // .xy = 필드 원점(패널 오브젝트 XZ)
        float4 _InvPatchSize;     // .xy = 1 / 필드 크기
        float4 _SnowColor;
        float4 _DeepColor;
        float4 _GroundColor;

        float4 _AmbientColor;
        // Vector 프로퍼티는 4성분이다. float3 으로 받으면 크기가 어긋나 값이 밀린다.
        float4 _SunDir;
        float  _Wrap;
        float  _AoStrength;
        float  _GrainAmpM;
        float  _GrainFreq;
        float  _EdgeProfile;      // 가장자리 단면. 0 둥근 어깨 · 1 흘러내린 치마
        float  _CellSizeM;        // 노멀 유한차분 폭. 셀 하나가 기준이다
        float  _FloorOriginY;     // 바닥 mm 의 기준(월드 Y). SnowFieldGeometry.OriginYM 과 같다

        // R16 UNorm 이라 1.0 이 65,535 mm = 65.535 m. 마처와 같은 규약이다.
        static const float kMmScale = 65.535;
        /// <summary>
        /// 눈 픽셀을 버리는 문턱. <b>커버리지가 아니라 렌더된 깊이로 자른다</b> — 그래야 잘리는 자리와
        /// 눈이 바닥에 닿는 자리가 <b>같은 지점</b>이 된다.
        ///
        /// <para>커버리지로 자르면 둥근 어깨 단면에서 <b>구멍이 뚫린다</b>: 어깨는 사분원이라
        /// 커버리지 0.004 에서도 높이 계수가 0.089 이고, 눈 60 cm 면 <b>5.3 cm 두께로 서 있는 채</b>
        /// 픽셀이 사라진다. 그 틈으로 아래가 비쳐 보인다. 깊이로 자르면 마지막으로 그려지는 픽셀이
        /// 바닥면(정확히는 그 1 cm 아래)이라 틈이 생길 자리가 없다.</para>
        /// </summary>
        static const float kMinVisibleSnowM = 0.0005;
        // 0 높이 패널을 실제 바닥 아래로 숨긴다. 0→35 cm 구간에서 서서히 원래 높이로 돌아오므로
        // clip 윤곽 없이 bilinear 표면과 Ground 의 깊이 교차가 자연스러운 경계를 만든다.
        static const float kGroundSinkM = 0.01;
        static const float kGroundBlendM = 0.35;

        float2 FieldUv(float2 xz)
        {
            return (xz - _PatchMin.xy) * _InvPatchSize.xy;
        }

        // 높이 한 탭. <b>정점 단계에서 부르므로 LOD 를 명시해야 한다</b> - 정점에는 밉 미분이 없다.
        float SnowHeight(float2 xz)
        {
            return SAMPLE_TEXTURE2D_LOD(_HeightTex, sampler_linear_clamp,
                                        FieldUv(xz), 0).r * kMmScale;
        }

        // 바닥의 월드 Y. 높이와 같은 R16 규약이고, 기준 Y 를 더해 절대 좌표로 만든다.
        float SnowFloorY(float2 xz)
        {
            return _FloorOriginY + SAMPLE_TEXTURE2D_LOD(_FloorTex, sampler_linear_clamp,
                                                        FieldUv(xz), 0).r * kMmScale;
        }

        /// <summary>
        /// 커버리지 0~1. <b>0 이면 눈이 없고, 1 이면 가득, 그 사이가 가장자리 마감</b>이다.
        /// 권위 격자의 용량과 <b>같은 값</b>이라(<c>SnowGroundFieldCpu.Coverage</c>) 보이는 경계와
        /// 팔 수 있는 경계가 갈리지 않는다.
        ///
        /// <para><b>bilinear 다.</b> 점 샘플이면 12.5 cm 톱니가 그대로 실루엣에 나온다.</para>
        /// </summary>
        float SnowMask(float2 xz)
        {
            return SAMPLE_TEXTURE2D_LOD(_MaskTex, sampler_linear_clamp, FieldUv(xz), 0).r;
        }

        float Curve01(float t)
        {
            t = saturate(t);
            return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
        }

        /// <summary>
        /// <b>경계 마감.</b> 커버리지를 가장자리 단면 곡선에 태워 깊이에 곱할 계수로 만든다.
        ///
        /// <para>단면은 둘 중 하나다. <b>둥근 어깨</b>(기본, <c>_EdgeProfile = 0</c>)는 사분원이라
        /// 두께를 끝까지 들고 가다가 굴러 떨어진다 — 쌓인 눈 덩어리의 실루엣이다. <b>흘러내린 치마</b>
        /// (<c>1</c>)는 5차 곡선이라 가장자리로 갈수록 얇아지며 퍼진다 — 바람에 쓸린 눈이다.</para>
        ///
        /// <para><b>진짜 오버행은 못 만든다.</b> 높이장은 XZ 당 값이 하나라 "중간이 가장 넓은" 단면이
        /// 표현되지 않는다. 사분원은 끝에서 접선이 수직이 되므로 거기까지가 한계이고, 그 이상은
        /// 메시를 따로 세우는 일이다.</para>
        ///
        /// <para><b>폭과 경계 모양은 커버리지가 들고 있다.</b> 굽는 쪽
        /// (<c>SnowGroundFieldCpu.FromRect</c>)이 페이드 폭과 경계 노이즈를 이미 구워 넣었으므로
        /// 여기 남은 노브는 단면 하나뿐이다.</para>
        /// </summary>
        float SnowEdgeFade(float2 xz)
        {
            float c = saturate(SnowMask(xz));

            // 사분원: c = 1 에서 기울기 0(윗면과 매끈하게 이어짐), c → 0 에서 수직.
            float shoulder = sqrt(saturate(1.0 - (1.0 - c) * (1.0 - c)));
            return lerp(shoulder, Curve01(c), _EdgeProfile);
        }

        // 주변 셀을 십자형으로 가중 평균한다. bilinear 한 번만 쓰면 삼각형 경계에서
        // 기울기가 갑자기 바뀌지만, 이 작은 필터는 marching-cubes의 스칼라장 보간처럼
        // 눈 높이가 바닥으로 이어지는 기울기를 한 셀 폭에 걸쳐 둥글게 만든다.
        float SnowHeightSmooth(float2 xz)
        {
            float2 r = float2(max(_CellSizeM, 0.02), 0.0);
            float h = SnowHeight(xz) * 0.5;
            h += SnowHeight(xz + r) * 0.125;
            h += SnowHeight(xz - r) * 0.125;
            r = r.yx;
            h += SnowHeight(xz + r) * 0.125;
            h += SnowHeight(xz - r) * 0.125;
            // 경계 마감을 여기서 곱한다 — 이 함수를 부르는 곳이 변위·노멀·그림자로 여섯이고,
            // 한 곳에서 곱해야 그 셋이 같은 표면을 본다.
            return h * SnowEdgeFade(xz);
        }

        /// <summary>
        /// <b>접합 곡선.</b> 0 높이를 바닥 1 cm 아래로 담그고 0→35 cm 에서 서서히 실제 높이로 돌아온다.
        ///
        /// <para>여기에 <b>노이즈를 얹지 않는다</b> — 2026-08-24 에 접촉선의 1.2 cm 저주파 노이즈를
        /// 뺐다. 얹는 자리가 깊이 8 cm 아래로 감쇠하는 구간이라 그 진폭이 곧 <b>경계선의 굵기</b>가
        /// 되고, 눈이 바닥에 닿는 선이 하나가 아니라 두께를 가진 띠(잔물결)로 읽혔다. 표면의 요철은
        /// 정점 그레인(<c>SnowGrain</c>)이 이미 담당한다.</para>
        /// </summary>
        float SnowRenderHeight(float depthM)
        {
            float rise = Curve01(depthM / kGroundBlendM);
            return depthM * rise - (1.0 - rise) * kGroundSinkM;
        }

        // 값 노이즈 한 옥타브. 각진 슬래브로 보이지 않을 만큼만 흔든다 - 구운 로브의 대체가 아니라
        // <b>가장 싼 대신</b>이다. sin 을 쓰지 않는 이유는 마처 쪽과 같다(정밀도).
        float SnowGrainHash(float2 pi)
        {
            uint2 u = (uint2)(int2(pi) + 4096);
            uint h = u.x * 1597334677u ^ u.y * 3812015801u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (float)(h & 0x00ffffffu) * (1.0 / 16777216.0) * 2.0 - 1.0;
        }

        float SnowGrain(float2 p)
        {
            float2 i = floor(p);
            float2 f = p - i;
            f = f * f * (3.0 - 2.0 * f);
            float a = lerp(SnowGrainHash(i), SnowGrainHash(i + float2(1, 0)), f.x);
            float b = lerp(SnowGrainHash(i + float2(0, 1)), SnowGrainHash(i + float2(1, 1)), f.x);
            return lerp(a, b, f.y);
        }

        struct Attributes { float4 positionOS : POSITION; };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;   // 바닥 + 눈. 조명이 읽는 실제 표면 노멀이다
            // .x = 눈 깊이 · .y = AO. <b>AO 는 필드 프레임(오브젝트 공간)에서 잰다</b> — 월드 노멀의
            // y 로 재면 기울어진 상자 전체가 "골" 로 읽혀 45° 램프의 눈이 23% 어두워진다(실측:
            // ao = 1 - (1 - cos45) * 0.8 = 0.766). 자기 평면 기준으로 평평한 눈은 AO 가 1 이어야 한다.
            float2 depthAo : TEXCOORD2;
            // 필드 좌표(오브젝트 XZ). 프래그먼트의 마스크 clip 이 이것을 읽는다 —
            // positionWS 에서 되돌리면 <b>기울어진 상자에서 틀린다</b>.
            float2 fieldXZ : TEXCOORD4;
        };

        /// <summary>
        /// <b>모든 필드 참조는 오브젝트 공간 XZ 다.</b> 지면 패널은 원점에 축 정렬로 놓이므로 예전과
        /// 같은 값이 나오고, 눈 상자(<c>SnowZone</c>)는 회전한 트랜스폼의 자식이라 <b>같은 코드가</b>
        /// 기울어진 램프 위의 격자를 읽는다. 월드 XZ 로 두면 상자를 돌린 순간 격자가 미끄러진다.
        /// </summary>
        Varyings Vert(Attributes IN)
        {
            Varyings OUT;

            float3 posOS = IN.positionOS.xyz;
            float2 fxz = posOS.xz;

            float rawDepthM = SnowHeightSmooth(fxz);
            float d = SnowRenderHeight(rawDepthM);

            // 눈 그레인은 눈 깊이에만, 바닥 노이즈는 별도의 작은 진폭으로 경계에만 적용한다.
            float grain = SnowGrain(fxz * _GrainFreq) * _GrainAmpM * saturate(rawDepthM / 0.05);
            float y = d + grain;

            // 노멀도 여기서 만든다. 프래그먼트에서 만들면 픽셀마다 탭 넷이고, 이 경로의 존재 이유가
            // 그 비용을 안 내는 것이다.
            float e = max(_CellSizeM, 0.02);
            float hx = SnowRenderHeight(SnowHeightSmooth(fxz + float2(e, 0)))
                     - SnowRenderHeight(SnowHeightSmooth(fxz - float2(e, 0)));
            float hz = SnowRenderHeight(SnowHeightSmooth(fxz + float2(0, e)))
                     - SnowRenderHeight(SnowHeightSmooth(fxz - float2(0, e)));

            // 바닥의 기울기. 이것이 없으면 경사 위의 눈이 <b>평지처럼</b> 음영되어 램프가 평면으로 보인다.
            float fx = SnowFloorY(fxz + float2(e, 0)) - SnowFloorY(fxz - float2(e, 0));
            float fz = SnowFloorY(fxz + float2(0, e)) - SnowFloorY(fxz - float2(0, e));

            posOS.y += SnowFloorY(fxz) + y;

            float3 posWS = TransformObjectToWorld(posOS);

            OUT.positionWS = posWS;
            OUT.positionCS = TransformWorldToHClip(posWS);
            OUT.normalWS = TransformObjectToWorldNormal(normalize(float3(-(hx + fx), 2.0 * e, -(hz + fz))));

            // AO 는 <b>필드 프레임</b>의 눈 노멀에서 나온다 — 회전을 곱하지 않는 것이 요점이다.
            float3 snowNormalOS = normalize(float3(-hx, 2.0 * e, -hz));
            OUT.depthAo = float2(max(d, 0.0),
                                 saturate(1.0 - (1.0 - snowNormalOS.y) * _AoStrength));
            OUT.fieldXZ = fxz;
            return OUT;
        }

        // shadow 는 호출자가 넘긴다. 섀도맵 샘플링에 필요한 include 와 키워드를 이 공용 블록이
        // 아니라 UniversalForward 패스 안에만 두기 위해서다 - DepthOnly / ShadowCaster 는 그림자를
        // 읽을 이유가 없다.
        //
        // <b>faceSign 은 뒷면이면 -1 이다.</b> 높이장은 한 겹짜리 시트라 밑면에는 자기 노멀이 없다 —
        // 뒤집지 않으면 아래에서 올려다본 처마가 <b>위에서 본 것과 같은 밝기</b>로 빛나 종이처럼 읽힌다.
        half4 Shade(Varyings IN, float shadow, float faceSign)
        {
            float3 n = normalize(IN.normalWS) * faceSign;
            float3 sun = normalize(_SunDir.xyz);
            float3 V = normalize(_WorldSpaceCameraPos - IN.positionWS);

            // 긁힌 바닥 -> 얕은 눈 -> 두꺼운 눈. 마처와 <b>같은 규칙</b>이어야 치운 차선이 같은 색으로
            // 읽힌다.
            float3 albedo = lerp(_GroundColor.rgb, _SnowColor.rgb, saturate(IN.depthAo.x / 0.06));
            albedo = lerp(albedo, _DeepColor.rgb, saturate((IN.depthAo.x - 0.45) * 0.9) * 0.35);

            // 곡률 AO 대신 기울기로 판단한다 - coarse-max 텍스처가 없으므로 "주변이 나보다 높은가" 를
            // 물을 수 없고, 대신 "여기가 경사인가" 로 골을 어둡게 한다. 정점에서 이미 계산했다.
            float ao = IN.depthAo.y;

            float wrap = saturate((dot(n, sun) + _Wrap) / (1.0 + _Wrap));
            float3 amb = _AmbientColor.rgb * albedo * ao;

            // 그림자는 wrap 만 깎고 ambient 는 안 깎는다. 마처(SnowRaymarchCpu.shader)의
            // `albedo * (wrap * shadow * 0.95 + 0.05) + amb` 와 <b>같은 식</b>이어야 경로를 바꾼 것이
            // 조명을 바꾼 것처럼 보이지 않는다.
            float3 realistic = albedo * (wrap * shadow * 0.95 + 0.05) + amb;

            return half4(SnowCasualApply(realistic, albedo, n, n, sun, V,
                                         shadow, ao, IN.positionWS, _MainLightColor.rgb, amb), 1.0);
        }

        ENDHLSL

        Pass
        {
            Name "SnowDisplaceForward"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest LEqual
            // <b>양면으로 그린다.</b> 상자 경계 밖으로 흘러넘친 눈(Edge Spill 0.3 m)은 램프·슬래브의
            // 실제 모서리를 지나 <b>허공에 처마로 남는다</b>. 뒷면을 버리면 그 처마를 아래에서 올려다볼
            // 때 아무것도 안 그려져 눈을 통과해 하늘이 보인다.
            //
            // <b>비용은 두 배가 아니다.</b> 삼각형은 앞면이거나 뒷면이지 둘 다가 아니므로, 위에서
            // 내려다보는 높이장은 거의 전부 앞면이고 이 설정이 더 그리는 것은 <b>범프의 뒷사면</b>과
            // 처마 밑면뿐이다. 그것도 대개 윗면이 이미 채운 자리라 깊이 테스트에서 떨어진다.
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            // 3.5 다. 이 경로의 존재 이유가 GLES3 에서 도는 것이므로 이 값이 올라가면 의미가 없어진다.
            #pragma target 3.5

            // <b>그림자 수신.</b> URP 는 이 키워드가 켜져 있고 셰이더가 <b>직접</b> 섀도맵을 샘플할
            // 때만 방향광 그림자를 적용한다. 이 자리에 있던 "방향광 그림자는 URP 가 알아서 한다" 는
            // 주석이 틀렸고, 그래서 눈이 그림자를 <b>던지기만 하고 받지 않았다</b>(ShadowCaster 패스는
            // 아래에 멀쩡히 있다). 키워드가 꺼지면 MainLightRealtimeShadow 가 1 을 돌려주므로
            // 그림자를 끈 프로젝트에서도 이전과 같은 그림이 나온다.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            half4 Frag(Varyings IN, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                // 좌표를 <b>프래그먼트에서</b> 만든다. 정점에서 만들어 보간하면 캐스케이드 경계를
                // 가로지르는 삼각형에서 어긋난다 - 이 경로의 정점 간격이 25 cm 라 그 삼각형이 크다.
                float shadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(IN.positionWS));

                // 눈이 없는 자리는 그리지 않는다 — 커버리지가 0 이면 깊이도 0 이므로 이 한 줄이
                // "특정 지역에만 눈" 과 "가장자리 마감" 을 동시에 끝낸다.
                clip(IN.depthAo.x - kMinVisibleSnowM);
                return Shade(IN, shadow, IS_FRONT_VFACE(face, 1.0, -1.0));
            }
            ENDHLSL
        }

        Pass
        {
            Name "SnowDisplaceDepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            // <b>Forward 와 같아야 한다.</b> 여기만 뒷면을 버리면 처마 밑면이 깊이 버퍼에 없어
            // 그 픽셀의 SSAO·안개·깊이 기반 효과가 뒤 배경의 깊이로 계산된다.
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth
            #pragma target 3.5

            half4 FragDepth(Varyings IN) : SV_Target
            {
                clip(IN.depthAo.x - kMinVisibleSnowM);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "SnowDisplaceShadow"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ColorMask 0
            // <b>여기는 뒷면을 버린다.</b> 그림자는 빛 쪽에서 본 실루엣만 필요하고, 한 겹 시트의
            // 밑면은 언제나 윗면과 같은 자리에 있다 — 양면으로 그리면 섀도맵에 같은 깊이를 두 번
            // 쓰면서 자기 그림자(acne)만 늘어난다.
            Cull Back

            HLSLPROGRAM
            #pragma vertex VertShadow
            #pragma fragment FragShadow
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float depthM : TEXCOORD0;
                float2 xz : TEXCOORD1;
            };

            ShadowVaryings VertShadow(Attributes IN)
            {
                ShadowVaryings OUT;
                float3 posOS = IN.positionOS.xyz;
                float2 fxz = posOS.xz;
                float rawDepthM = SnowHeightSmooth(fxz);
                posOS.y += SnowFloorY(fxz) + SnowRenderHeight(rawDepthM);
                float3 posWS = TransformObjectToWorld(posOS);

                float3 lightDir = (dot(_LightDirection, _LightDirection) > 1e-4)
                    ? normalize(_LightDirection) : normalize(_MainLightPosition.xyz);

                OUT.xz = fxz;
                posWS = ApplyShadowBias(posWS, float3(0, 1, 0), lightDir);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.depthM = rawDepthM;
                return OUT;
            }

            half4 FragShadow(ShadowVaryings IN) : SV_Target
            {
                clip(IN.depthM - kMinVisibleSnowM);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
