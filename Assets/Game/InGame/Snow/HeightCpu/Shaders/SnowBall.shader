// 눈덩이. 표면과 달리 <b>메시</b>다.
//
// 왜 마칭이 아닌가
// ----------------
// 이 씬의 다른 눈은 전부 높이필드를 마칭한 것이다. 공은 아니고, 이유는 취향이 아니라 구조다 -
// 높이필드는 Y 에 대해 <b>단일값</b>이다. 구에는 아랫면이 있어서 같은 XZ 위에 표면이 둘이고,
// 그것은 필드로 표현할 수가 없다. 마처에 두 번째 프리미티브를 넣으면 화면의 모든 광선의 모든
// 스텝마다 그것을 평가해야 하고, coarse-max 빈공간 상한도 공 높이만큼 전부 넓혀야 한다 -
// 이 프로젝트가 가장 신경 써서 지킨 그 상한을, 삼각형 1,280개짜리 드로 하나가 정확히 맞히는
// 형태와 바꾸는 셈이다.
//
// 잃는 것도 없다. 마칭한 눈이 SV_Depth 를 쓰므로 공과 눈은 픽셀마다 <b>평범한 깊이 테스트</b>로
// 교차한다 - 공이 자기가 파낸 골 <b>안에</b> 실제로 앉고, 그게 공짜다.
//
// 굴러가는 것처럼 읽히게 하는 것이 일의 대부분이다
// ------------------------------------------------
// 완벽하게 매끈한 흰 구는 회전해도 <b>멈춰 있는 것으로 보인다.</b> 따라갈 것이 표면에 없다.
// 그래서 공은 오브젝트 공간 매크로 형태를 지닌다 - 단위구당 2.2 와 4.6 주기의 3D 값노이즈 두
// 옥타브, 즉 공을 가로질러 로브 서너 개다. 그것을 <b>실제 정점 변위</b>로 넣어 실루엣에 남기고,
// 노멀 교란으로 음영에도 남긴다. 오브젝트 공간이라 메시와 함께 돌아서 회전이 읽히고, 월드
// 미터가 아니라 <b>단위구당</b> 주기라서 로브가 공과 함께 커져 0.5 m 에서나 4 m 에서나 똑같이 읽힌다.
//
// 이것은 <b>매크로 형태이지 그레인이 아니다</b>. 반지름의 4.5%, 공을 가로질러 로브 세 개면
// "손으로 뭉친 눈덩이" 이고, 같은 에너지를 열 배 주파수에 넣으면 이 프로젝트가 룩 예산을 통째로
// 써서 없앤 "부순 얼음" 이 된다. _BallLumpAmp 0 이 A/B 이고, 그것이 바로 굴러가는 것으로
// 읽히지 않는 특징 없는 구다.
//
// 음영은 표면과 <b>같은</b> SnowCasualApply 를 통과한다. 그래야 공이 자기가 만들어진 눈에서
// 색이 떨어져 나가지 않는다.
//
// 출처: AnyTest/Assets/SnowGrainFakeV6/Shaders/SnowBallV6.shader
//
// V6 와 다른 점 하나: 접지 그림자의 기준 높이를 유니폼으로 받지 않고 <b>변환에서 직접 구한다.</b>
// V6 는 공이 화면에 하나라 C# 이 매 프레임 _BallContactY 를 밀 수 있었지만, 이 게임은 공이
// 여럿이고 그러면 공마다 다른 값이 필요해 MaterialPropertyBlock 이 되고 SRP 배쳐가 깨진다.
// 원점과 스케일은 unity_ObjectToWorld 에 이미 들어 있으므로 유니폼이 필요 없다.
Shader "PPack/SnowBall"
{
    Properties
    {
        _BaseColor    ("Base Color", Color) = (0.94, 0.96, 1.00, 1)
        _DeepColor    ("Deep Color", Color) = (0.55, 0.63, 0.80, 1)
        _AmbientColor ("Ambient",    Color) = (0.34, 0.40, 0.54, 1)

        _BallLumpAmp   ("Lump amp (fraction of radius)", Range(0, 0.25)) = 0.045
        _BallLumpFreq  ("Lump cycles per unit sphere", Range(0.5, 8)) = 2.2
        _BallLumpOct2  ("Second octave weight", Range(0, 1)) = 0.45
        _BallAlbedoVary ("Albedo variation", Range(0, 1)) = 0.35

        _BallContactAo ("Contact AO", Range(0, 1)) = 0.45
        _BallContactFadeM ("Contact fade (m)", Range(0.02, 2)) = 0.35

        _Wrap ("Realistic diffuse wrap", Range(0, 1)) = 0.45
        _Fill ("Realistic fill", Range(0, 0.5)) = 0.10
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // 표면과 같은 팔레트. 전역으로 오고 미는 주체는 SnowLookStyle 하나다 - 공이 자기가
        // 만들어진 눈과 다른 조명 아래 있는 것처럼 보이면 안 된다.
        #include "../../Shaders/SnowCasualStyle.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float4 _BaseColor;
        float4 _DeepColor;
        float4 _AmbientColor;
        float _BallLumpAmp;
        float _BallLumpFreq;
        float _BallLumpOct2;
        float _BallAlbedoVary;
        float _BallContactAo;
        float _BallContactFadeM;
        float _Wrap;
        float _Fill;
        CBUFFER_END

        // 정수 격자 해시. sin() 을 쓰지 않는다 - sin 트릭은 정밀도를 고르지 않게 잃고, 이것은
        // 비트 단위로 안정해야 한다. 정점 단계와 프래그먼트 단계가 <b>같은</b> 표면에 동의하지
        // 않으면 음영이 변위 위에서 미끄러진다.
        float BallHash(float3 pi)
        {
            uint3 u = (uint3)(int3(pi) + 512);
            uint  h = u.x * 1597334677u ^ u.y * 3812015801u ^ u.z * 2654435761u;
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return (float)(h & 0x00ffffffu) * (1.0 / 16777216.0);
        }

        float BallVNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = p - i;
            f = f * f * (3.0 - 2.0 * f);

            float a = lerp(lerp(BallHash(i + float3(0, 0, 0)), BallHash(i + float3(1, 0, 0)), f.x),
                           lerp(BallHash(i + float3(0, 1, 0)), BallHash(i + float3(1, 1, 0)), f.x), f.y);
            float b = lerp(lerp(BallHash(i + float3(0, 0, 1)), BallHash(i + float3(1, 0, 1)), f.x),
                           lerp(BallHash(i + float3(0, 1, 1)), BallHash(i + float3(1, 1, 1)), f.x), f.y);

            return lerp(a, b, f.z) * 2.0 - 1.0;
        }

        // 두 옥타브를 -1..1 로 정규화한다. 그래야 두 번째 옥타브 가중치가 얼마든 _BallLumpAmp 가
        // 반지름의 진짜 비율로 남는다.
        float BallForm(float3 dirOS)
        {
            float w = saturate(_BallLumpOct2);
            float n = BallVNoise(dirOS * _BallLumpFreq)
                    + BallVNoise(dirOS * (_BallLumpFreq * 2.13) + 19.7) * w;
            return n / (1.0 + w);
        }

        // 밀어낸 반지름, 단위구 기준. 메시는 <b>지름 1</b> 아이코스피어이고 변환이 공의 지름을
        // 균일 스케일로 나른다. 그래서 여기의 비율은 어느 크기에서나 월드 반지름의 같은 비율이고,
        // 그것이 공이 커져도 로브 비례가 유지되는 이유다.
        float BallRadiusAt(float3 dirOS)
        {
            return 1.0 + BallForm(dirOS) * _BallLumpAmp;
        }

        // 단위구 반지름을 이 메시의 오브젝트 공간으로. 메시가 <b>지름 1</b> 이므로 절반이고,
        // 이 환산은 정점을 놓는 자리에서 <b>한 번만</b> 한다 - BallRadiusAt 안에 넣으면 위의
        // 중앙차분 기울기가 같이 절반이 되어 노멀 교란이 V6 의 절반 세기로 약해진다.

        // 월드 반지름. 변환의 첫 열 길이가 균일 스케일이고 메시가 지름 1 이므로 그 절반이다.
        float BallWorldRadius()
        {
            return 0.5 * length(float3(unity_ObjectToWorld._m00, unity_ObjectToWorld._m10,
                                       unity_ObjectToWorld._m20));
        }

        struct Attributes
        {
            float3 positionOS : POSITION;
            float3 normalOS   : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 dirOS      : TEXCOORD1;   // 밀기 <b>전</b>의 단위 방향 = 매크로 형태
            float3 normalWS   : TEXCOORD2;   // <b>매끄러운</b> 구 노멀. 밴드 항이 이것을 읽는다
        };

        Varyings Vert(Attributes IN)
        {
            Varyings OUT;

            // 메시가 용접된 아이코스피어라서 정규화한 위치가 곧 단위 방향이고 정확한 매끄러운
            // 노멀이다. 그래도 정규화하는 것은 비용이 0 이고, 셰이더가 메시의 노멀에 의존하지
            // 않게 만든다.
            float3 dir = normalize(IN.positionOS);

            float3 dispOS = dir * BallRadiusAt(dir) * 0.5;

            OUT.dirOS      = dir;
            OUT.positionWS = TransformObjectToWorld(dispOS);
            OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
            OUT.normalWS   = TransformObjectToWorldNormal(dir);
            return OUT;
        }

        // 교란된 노멀. 정점 단계가 밀어낸 <b>같은</b> 형태의 중앙차분을 구의 접평면에서 취한다.
        // 화면의 공 몇 개에 노이즈 평가 네 번이므로 아낄 비용이 아니고, 이것을 정점이 아니라
        // 여기서 하는 것이 642 정점 구가 642 개 평면으로 읽히지 않게 하는 이유다.
        float3 BallNormal(float3 dirOS, float3 smoothNormalWS)
        {
            // dir 에 수직인 아무 두 벡터. 분기 없는 선택이라 극에서 기준축이 dir 과 평행해지는
            // 퇴화를 피한다.
            float3 ref = (abs(dirOS.y) < 0.9) ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
            float3 t = normalize(cross(ref, dirOS));
            float3 b = cross(dirOS, t);

            const float e = 0.045;   // 단위구 기준, 출하 주파수에서 로브의 십분의 일쯤

            float ht = BallRadiusAt(normalize(dirOS + t * e)) - BallRadiusAt(normalize(dirOS - t * e));
            float hb = BallRadiusAt(normalize(dirOS + b * e)) - BallRadiusAt(normalize(dirOS - b * e));

            // 접평면에서의 기울기. 두 축 모두 단위구 기준이라 2e 분모가 같고, 결과가 스케일에서
            // 자유롭다 - 공이 커져도 교란이 변하지 않고, 형태도 변하지 않으니 그게 맞다.
            float3 nOS = normalize(dirOS - (t * ht + b * hb) / (2.0 * e));

            float3 nWS = TransformObjectToWorldNormal(nOS);

            // 형태를 끄면 매끄러운 노멀로 정확히 돌아간다 - _BallLumpAmp 0 이 근사가 아니라
            // 정확한 A/B 이어야 한다.
            return (_BallLumpAmp > 1e-5) ? normalize(nWS) : normalize(smoothNormalWS);
        }

        half4 ShadeBall(Varyings IN)
        {
            float3 nBand = normalize(IN.normalWS);          // 매크로 형태 = 구 자체
            float3 n     = BallNormal(IN.dirOS, nBand);     // 그 위에 얹힌 뭉친 눈의 형태

            float3 L = _MainLightPosition.xyz;
            L = (dot(L, L) > 1e-4) ? normalize(L) : normalize(float3(0.35, 0.85, 0.40));

            float3 V = normalize(_WorldSpaceCameraPos - IN.positionWS);

            // 접지. 공의 아랫면은 자기가 파낸 골에 묻혀 있고, 눈과 만나는 자리가 어둡지 않은 공은
            // 실루엣이 얼마나 정확하든 <b>떠 보인다.</b> 기준은 오브젝트 공간의 아래축이 아니라
            // 공의 최저점 <b>월드 Y</b> 다 - 오브젝트 공간은 돌고 있고, 공과 함께 도는 그림자는
            // 그려 붙인 그림이다.
            float contactY = unity_ObjectToWorld._m13
                           - BallWorldRadius() * (1.0 + _BallLumpAmp);
            float below = saturate((contactY + _BallContactFadeM - IN.positionWS.y)
                                   / max(1e-3, _BallContactFadeM));
            float ao = 1.0 - saturate(_BallContactAo) * below;

            // 표면을 밀어낸 것과 같은 형태를 알베도 변화로도 쓴다. 이것이 <b>빛을 받는 정수리</b>
            // 에서 회전을 읽히게 하는 항이다 - 그쪽은 노멀이 광원과 거의 평행해서 음영 항이 할
            // 말이 거의 없다.
            float vary = BallForm(IN.dirOS) * saturate(_BallAlbedoVary);
            float3 albedo = lerp(_BaseColor.rgb, _DeepColor.rgb, saturate(0.5 - vary * 0.5) * 0.35) * ao;

            // 사실적 기준. _SnowCasual = 0 이 공을 흰 대리석으로 남기지 않고 눈과 같은 키에
            // 두도록, 마처가 계산하는 것과 같은 모양으로 만든다.
            float wrap = saturate((dot(n, L) + _Wrap) / (1.0 + _Wrap));
            float3 amb = _AmbientColor.rgb * (0.55 + 0.45 * saturate(n.y * 0.5 + 0.5));
            float3 realistic = albedo * (_MainLightColor.rgb * (_Fill + (1.0 - _Fill) * wrap) + amb);

            // shadow = 1: 여기 셀프섀도 마칭이 없고 있을 필요도 없다. <b>구</b>에서는 감싼
            // 명암 경계가 그림자 항이 할 일을 이미 다 한다 - 구에서는 노멀이 곧 형태이므로
            // 밴드 경계가 형태 위에 앉는다.
            float3 col = SnowCasualApply(realistic, albedo, n, nBand, L, V,
                                         1.0, ao, IN.positionWS, _MainLightColor.rgb, amb);

            return half4(col, 1.0);
        }

        ENDHLSL

        Pass
        {
            Name "SnowBallForward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment FragForward
            // 3.5 다, 4.5 가 아니다. 모바일이 대상이고 이 셰이더가 쓰는 것은 uint 비트연산뿐이라
            // GLES3.0 에서 돈다. 표면 마처는 SV_Depth 때문에 4.5 를 요구하지만 이쪽은 아니다.
            #pragma target 3.5

            half4 FragForward(Varyings IN) : SV_Target
            {
                return ShadeBall(IN);
            }
            ENDHLSL
        }

        // 깊이 텍스처에 공이 존재하도록, 그리고 URP 가 깊이 프라이밍을 켜면 포워드가 그릴 것과
        // <b>같은</b> 지오메트리로 프라임하도록 남긴다. 정점 단계가 같아서 둘이 어긋날 수 없다.
        Pass
        {
            Name "SnowBallDepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask R

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment FragDepthOnly
            #pragma target 3.5

            half4 FragDepthOnly(Varyings IN) : SV_Target
            {
                return half4(0.0, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }

        // 그림자. 공이 4 m 까지 자라므로 자기 그림자가 없으면 지면에서 떠 보인다.
        Pass
        {
            Name "SnowBallShadow"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull Back
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   VertShadow
            #pragma fragment FragShadow
            #pragma target 3.5

            // ApplyShadowBias 가 여기 있다. 포워드 패스는 이것이 필요 없으므로 이 패스에서만 넣는다.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            float4 VertShadow(Attributes IN) : SV_POSITION
            {
                float3 dir = normalize(IN.positionOS);
                float3 dispOS = dir * BallRadiusAt(dir) * 0.5;
                float3 positionWS = TransformObjectToWorld(dispOS);
                float3 normalWS = TransformObjectToWorldNormal(dir);

                float3 lightDir = (dot(_LightDirection, _LightDirection) > 1e-4)
                    ? normalize(_LightDirection) : normalize(_MainLightPosition.xyz);

                positionWS = ApplyShadowBias(positionWS, normalWS, lightDir);
                return TransformWorldToHClip(positionWS);
            }

            half4 FragShadow() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
