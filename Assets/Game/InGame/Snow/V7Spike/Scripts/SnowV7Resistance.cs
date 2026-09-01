using UnityEngine;

namespace SnowSpike.PileV7
{
    /// <summary>
    /// v7 이 "눈이 차를 늦춘다"를 계산하는 <b>순수 함수</b>들. 상태가 없고 컴포넌트가 아니다.
    ///
    /// <para>여기로 뽑은 이유는 <b>소비자가 둘이 됐기</b> 때문이다 — 싱글 경로의
    /// <see cref="SnowV7VehicleFeel"/>(Rigidbody + <c>VehicleController</c>)과 멀티 경로의
    /// 네트워크 제설차(kinematic, 서버 권위)가 같은 수식을 써야 한다. 두 곳에 같은 식을 적어 두면
    /// 한쪽만 고쳐져 <b>싱글과 멀티의 조작감이 조용히 갈린다.</b></para>
    ///
    /// <para>세 항으로 쪼개고 <b>따로 반환한다</b>는 v7 규약을 그대로 지킨다 — 느린 차를 보고 짐 때문인지
    /// 깊이 때문인지 날이 물려서인지 구분할 수 있어야 한다.</para>
    /// </summary>
    public static class SnowV7Resistance
    {
        /// <summary>포화 깊이. 이 프로젝트 눈이 30 cm 라 v7 기본값이 그것이다.</summary>
        public const float DefaultDepthSatM = 0.30f;

        /// <summary>포화 깊이에서 남는 최고속 비율.</summary>
        public const float DefaultDepthFloor01 = 0.35f;

        /// <summary>짐 계수의 기준 질량 (kg).</summary>
        public const float DefaultMassReferenceKg = 1200f;

        /// <summary>기준 질량에서 남는 최고속 비율.</summary>
        public const float DefaultMassSpeedFactorAtRef = 0.45f;

        /// <summary>기준 질량에서 남는 코스트 비율. 저항에 곱해져 "질량으로 나눈다"를 만든다.</summary>
        public const float DefaultMassCoastFactorAtRef = 0.12f;

        /// <summary>물린 깊이에서의 기본 감속 (m/s²).</summary>
        public const float DefaultDragBaseMps2 = 3.0f;

        /// <summary>속도에 비례해 더해지는 감속 (m/s² per m/s).</summary>
        public const float DefaultDragPerSpeed = 0.45f;

        /// <summary>저항이 100% 로 물리는 깊이 (m).</summary>
        public const float DefaultBiteDepthM = 0.30f;

        /// <summary>
        /// 깊이가 만드는 최고속 배수. <b>바퀴 밑</b> 깊이를 넣는다 — 앞이 아니라 밑을 보는 것이 핵심이고,
        /// 그래서 블레이드가 이미 치운 자리에 올라서면 <b>즉시</b> 풀린다.
        /// </summary>
        public static float DepthSpeedFactor(float depthUnderM, float satDepthM = DefaultDepthSatM,
                                             float floor01 = DefaultDepthFloor01)
        {
            return Mathf.Lerp(1f, Mathf.Clamp(floor01, 0.02f, 1f),
                              Mathf.Clamp01(depthUnderM / Mathf.Max(0.01f, satDepthM)));
        }

        /// <summary>
        /// 짐이 만드는 배수 <c>1/(1+k·m)</c>. 선형 감소가 아니라 이 꼴인 이유는 <b>절대 0 이 되지 않는다</b>는
        /// 것이다 — 아무리 실어도 차가 멈추지는 않고 굼떠지기만 한다.
        /// </summary>
        public static float MassFactor(float massKg, float refKg = DefaultMassReferenceKg,
                                       float atRef = DefaultMassSpeedFactorAtRef)
        {
            atRef = Mathf.Clamp(atRef, 0.01f, 1f);
            float k = (1f / atRef - 1f) / Mathf.Max(1f, refKg);
            return 1f / (1f + k * Mathf.Max(0f, massKg));
        }

        /// <summary>
        /// 제설 저항 (m/s²). 천장이 아니라 <b>감속</b>이고, 호출자가 <b>블레이드가 내려가 있을 때만</b>
        /// 불러야 한다(v7 규약 — 날을 올리면 사라진다).
        ///
        /// <para><paramref name="depthUnderM"/> 와 <paramref name="depthAtBladeM"/> 중 <b>깊은 쪽</b>을 쓴다.
        /// 깊은 눈에 들어갈 때는 차가 들어가기 전에 물리고, 나올 때는 다 나온 뒤에 풀리게 하려는 것이다.</para>
        ///
        /// <para>결과에 코스트 질량 계수가 곱해진다 — 눈 저항은 힘이고 감속은 F/m 이라, 나누지 않으면
        /// <b>2톤을 실은 차가 빈 날보다 눈에 더 빨리 멈춘다.</b></para>
        /// </summary>
        public static float DragMps2(float depthUnderM, float depthAtBladeM, float forwardSpeedMps,
                                     float pileMassKg,
                                     float biteDepthM = DefaultBiteDepthM,
                                     float dragBaseMps2 = DefaultDragBaseMps2,
                                     float dragPerSpeed = DefaultDragPerSpeed,
                                     float massReferenceKg = DefaultMassReferenceKg,
                                     float massCoastFactorAtRef = DefaultMassCoastFactorAtRef)
        {
            float depth = Mathf.Max(depthUnderM, depthAtBladeM);
            float bite = Mathf.Clamp01(depth / Mathf.Max(0.01f, biteDepthM));
            if (bite <= 0f) return 0f;

            float coast = MassFactor(pileMassKg, massReferenceKg, massCoastFactorAtRef);
            return (dragBaseMps2 + dragPerSpeed * Mathf.Abs(forwardSpeedMps)) * bite * coast;
        }
    }
}
