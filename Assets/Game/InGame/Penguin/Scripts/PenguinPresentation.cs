using UnityEngine;

namespace PPack
{
    /// <summary>
    /// <see cref="PenguinLocomotion.Step"/> 이 <b>실제로 만들어 낸</b> 연출용 상태. 애니메이터·몸통
    /// 기울기·카메라가 읽는 값들이며, 이동 규칙 자체는 여기 없다.
    ///
    /// <para><b>왜 이 struct 가 필요한가.</b> 예측을 켜지 않았으므로 <c>Step</c> 은 서버에서만 돈다.
    /// 그러면 클라이언트의 <see cref="PenguinLocomotion"/> 은 <c>Speed</c> 도 <c>IsGrounded</c> 도
    /// <c>IsSliding</c> 도 <b>영원히 기본값</b>이다 — 몸은 <c>NetworkRigidbody</c> 보정으로 움직이는데
    /// 애니메이터는 서 있는 자세로 미끄러지고, 몸통은 안 기울고, 카메라의 속도 반응은 죽는다.
    /// 남의 펭귄만이 아니라 <b>클라이언트에서는 자기 펭귄도</b> 그렇다.</para>
    ///
    /// <para>그래서 입력과 같은 모양을 쓴다 — <c>Step(dt, in PenguinMoveInput)</c> 이 입력을 인자로
    /// 받듯, 연출 상태도 인자로 넣을 수 있게 한다. <b>본문 하나 · 채우는 길 둘</b>이다. 권위 피어는
    /// <c>Step</c> 이 채우고, 나머지는 복제된 이 값을
    /// <see cref="PenguinLocomotion.ApplyPresentation"/> 로 채운다.</para>
    ///
    /// <para><b>여기 없는 것.</b> <c>CurrentSpeed01</c> 은 <c>Speed</c> 와 슬라이딩 여부에서 프리팹의
    /// 커브로 나오므로 각 피어가 스스로 구한다 — 보내면 같은 규칙이 두 곳에 산다.
    /// <c>IsSnowballMounted</c>·<c>IsSnowballSideAttached</c> 는 연출이 아니라 이동 규칙이 읽는
    /// 값이라 복제하지 않는다(비권위 피어는 이동을 계산하지 않는다).</para>
    /// </summary>
    public struct PenguinPresentation
    {
        /// <summary>수평 이동 속력(m/s). 애니메이터의 <c>Speed</c> 파라미터가 읽는다.</summary>
        public float Speed;

        /// <summary>수평 이동 방향. 정지 중이면 <see cref="Vector3.zero"/>.</summary>
        public Vector3 HorizontalVelocityDirection;

        /// <summary>마지막으로 측정한 지면 법선. 몸통이 설면 기울기를 여기서 얻는다.</summary>
        public Vector3 GroundNormal;

        /// <summary>실제로 적용된 그립력에서 나온 횡가속도(m/s²). 카빙 린이 읽는다.</summary>
        public float LateralGripAccel;

        /// <summary>발밑에 지면이 있는가.</summary>
        public bool Grounded;

        /// <summary>지상 슬라이딩 중인가.</summary>
        public bool Sliding;

        /// <summary>
        /// 슬라이딩 <b>자세</b>를 써야 하는가. 물리 상태(<see cref="Sliding"/>)와 따로 보내는 이유는
        /// 착지 예약 구간에서 둘이 갈리기 때문이다 — <c>PenguinLocomotion.IsSlidePose</c> 참고.
        /// </summary>
        public bool SlidePose;

        /// <summary>
        /// 점프가 발동한 <b>횟수</b>. 이벤트가 아니라 계수기인 이유는 복제되는 것이 상태이기
        /// 때문이다 — 한 틱짜리 펄스를 보내면 그 틱을 못 본 피어는 점프를 통째로 놓친다.
        /// 받는 쪽이 값이 바뀐 것을 보고 트리거를 한 번 쏜다.
        /// </summary>
        public byte JumpCount;

        /// <summary>
        /// 달리기 체력(0~1). <b>이동 규칙이 만들고 UI 가 읽는 값이라 <see cref="Speed"/> 와 같은
        /// 자리다</b> — 위의 "여기 없는 것" 이 배제한 <c>IsSnowballMounted</c> 류와는 다르다.
        /// 저쪽은 비권위 피어가 <b>쓸 일이 없는</b> 이동 입력이고, 이쪽은 비권위 피어가 반드시
        /// <b>그려야 하는</b> 값이다.
        ///
        /// <para>로컬 플레이어의 바가 RTT 만큼 늦는 것은 의도된 결과다. 예측을 켜지 않았으므로
        /// 자기 펭귄의 속도도 접지도 이미 서버가 정한 값을 그리고 있다 — 체력만 로컬로 앞서
        /// 그리면 그 하나만 화면의 나머지와 어긋난다.</para>
        /// </summary>
        public float Stamina01;

        /// <summary>다 써서 잠긴 상태인가. 바의 색이 이것으로 갈린다. <see cref="Stamina01"/> 과
        /// 따로 보내는 이유는 문턱이 조정값이라, 받는 쪽이 다시 판정하면 규칙이 두 곳에 살기
        /// 때문이다.</summary>
        public bool StaminaExhausted;

    }
}
