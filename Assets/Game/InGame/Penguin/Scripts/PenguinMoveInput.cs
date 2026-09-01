using UnityEngine;

namespace PPack
{
    /// <summary>
    /// <see cref="PenguinLocomotion"/> 이 한 물리 스텝에 쓰는 입력. <b>싱글과 멀티가 같은 것을
    /// 넘긴다</b> — 싱글은 <see cref="PenguinInputReader"/> 에서, 멀티는 <c>NetworkInputData</c> 에서
    /// 채운다.
    ///
    /// <para><b>왜 struct 하나로 묶는가.</b> 로코모션이 입력 컴포넌트를 직접 들고 있으면 서버가
    /// 그것을 대신 채울 방법이 없다 — 데디 서버에는 키보드도 마우스도 없다. 입력을 인자로 받으면
    /// 진입점이 둘이어도 <b>본문은 하나</b>로 남는다. 같은 이유로 <c>dt</c> 도 인자다.</para>
    ///
    /// <para><b>카메라 요가 여기 있는 이유.</b> 이동 방향이 카메라 기준인데 데디 서버에는 원격
    /// 플레이어의 카메라가 없다. 방향 벡터가 아니라 각도를 넘겨야
    /// <see cref="PenguinLocomotion"/> 의 크기 클램프를 서버가 강제할 수 있다 — 근거는
    /// <c>NetworkInputData.CameraYawDeg</c> 주석에 있다.</para>
    ///
    /// <para><b>점프는 눌림 상태다.</b> <c>PenguinInputReader.JumpPressedThisFrame</c> 은 읽는 순간
    /// 비워지는 래치인데, 그것을 그대로 네트워크에 실으면 재시뮬레이션에서 두 번 먹거나 씹힌다.
    /// 여기까지는 그 프레임의 눌림으로 오고, 에지 판정은 로코모션이 <c>grounded</c> 와 함께 한다.</para>
    /// </summary>
    public struct PenguinMoveInput
    {
        /// <summary>이동 축. x 는 좌우, y 는 전후. 각 성분은 -1..1.</summary>
        public Vector2 Move;

        /// <summary>이동 기준 카메라의 요(도).</summary>
        public float CameraYawDeg;

        /// <summary>달리기 홀드(좌시프트). 2026-08-23 조작 개편에서 Shift 는 달리기다.</summary>
        public bool SprintHeld;

        /// <summary>이번 스텝에 점프가 눌렸는가.</summary>
        public bool JumpPressed;

        /// <summary>좌클릭 홀드. 누르는 동안 눈덩이에 붙어 힘을 준다.</summary>
        public bool PackSnowHeld;

        /// <summary>이번 스텝에 눈덩이 만들기가 눌렸는가(E).</summary>
        public bool CreateSnowballPressed;

        /// <summary>이번 스텝에 터뜨리기가 눌렸는가.</summary>
        public bool BurstPressed;

        /// <summary>이번 스텝에 운반 의도(F)가 눌렸는가. 메기·내려놓기·접근 취소를 함께 뜻한다.</summary>
        public bool PickupPressed;

        /// <summary>이번 스텝에 협동 밀치기 타이밍을 제출했는가.</summary>
        public bool CoopShovePressed;

        /// <summary>
        /// 제출자가 <b>자기 화면의 마커</b>로 내린 판정. <see cref="CoopShovePressed"/> 가 참일 때만
        /// 의미가 있다.
        ///
        /// <para><b>왜 위상이 아니라 결과를 넘기는가.</b> 마커는 각자의 화면에서 돈다. 서버가
        /// 자기 위상으로 다시 판정하면, 지연 때문에 플레이어가 초록 구간에서 눌렀는데 실패로
        /// 처리되는 판이 생긴다 — <c>SnowBallCarrier.SubmitCoopTiming</c> 주석의 규약이 그것이다.
        /// 판정은 마커를 보는 쪽이 하고 서버는 모으기만 한다.</para>
        /// </summary>
        public bool CoopShoveSuccess;
    }
}
