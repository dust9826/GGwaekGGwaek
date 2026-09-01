using Fusion;

namespace PPack
{
    /// <summary>
    /// <b>한 번 일어나는</b> 스테이지 이벤트를 복제하는 공통 뼈대. 서버가 "일어났다" 를 알리고
    /// 다른 피어가 그것을 자기 화면에서 재생한다.
    ///
    /// <para><b>왜 뼈대가 필요한가.</b> 이 프로젝트는 같은 세 조각을 이미 세 번 손으로 썼다 —
    /// <c>NetJumpCount</c>, <c>NetAttackCount</c>, <c>NetClosedTicket</c>. 그리고 셋 다 주석으로
    /// 같은 경고를 달고 있다. 조각 하나만 빠져도 <b>증상이 네트워킹처럼 보이지 않는다:</b></para>
    ///
    /// <list type="bullet">
    /// <item><b>계수기가 아니라 펄스로 쓰면</b> 한 틱짜리 신호를 못 본 피어가 이벤트를 통째로 놓친다.</item>
    /// <item><b>스폰에서 프라이밍하지 않으면</b> 늦게 들어온 사람이 지나간 이벤트를 전부 재생한다.</item>
    /// </list>
    ///
    /// <para><b>여기서 다루지 않는 것 — 페이로드.</b> 무엇이 일어났는지의 내용은 파생 클래스가
    /// 자기 <c>[Networked]</c> 필드로 선언한다. 공용 페이로드(바이트 배열, 이벤트 id)를 두면
    /// <c>[Networked]</c> 의 타입 검사를 버리게 되고, 잘못 넣어도 컴파일이 통과한다.</para>
    ///
    /// <para><b>이어지는 상태에는 쓰지 않는다.</b> 시각이나 자세처럼 매 틱 흐르는 값은 "일어난 사건" 이
    /// 아니라 티켓이 맞지 않는다. 그런 것은 평범한 <see cref="NetworkBehaviour"/> 에
    /// <c>[Networked]</c> 필드를 두고 매 틱 복사한다(<c>ThiefNetworkHub</c> 가 그 예다).</para>
    ///
    /// <para>새 이벤트를 더하는 법: 이것을 상속하고 → 페이로드를 <c>[Networked]</c> 로 선언하고 →
    /// 서버 경로에서 페이로드를 쓴 뒤 <see cref="RaiseOnServer"/> 를 부르고 →
    /// <see cref="OnEventRaised"/> 에서 그 페이로드로 로컬 시스템을 돌린다. <b>이 파일은 고치지 않는다.</b></para>
    /// </summary>
    public abstract class StageEventNetBehaviour : NetworkBehaviour
    {
        /// <summary>일어난 횟수. <b>펄스가 아니라 계수기다</b> — 위 주석 참조.</summary>
        [Networked] private byte Ticket { get; set; }

        private byte _seen;

        /// <summary>이 피어가 서버가 아니다 — 복제값을 읽어 재생하는 쪽.</summary>
        protected bool IsFollower => Object != null && Object.IsValid && !Object.HasStateAuthority;

        /// <summary>
        /// <b>지금까지 일어난 것은 이미 지나간 일로 친다.</b> 이것이 없으면 늦게 들어온 사람이
        /// 접속하자마자 과거 이벤트를 재생한다.
        /// </summary>
        public override void Spawned() => _seen = Ticket;

        /// <summary>서버가 이벤트를 낸다. <b>페이로드를 먼저 쓰고</b> 이것을 부른다 —
        /// 순서가 반대면 다른 피어가 이전 페이로드로 재생할 수 있다.</summary>
        protected void RaiseOnServer()
        {
            if (Object == null || !Object.HasStateAuthority) return;
            Ticket = unchecked((byte)(Ticket + 1));
        }

        public override void Render()
        {
            if (!IsFollower || _seen == Ticket) return;
            _seen = Ticket;
            OnEventRaised();
        }

        /// <summary>복제된 페이로드를 읽어 이 피어에서 이벤트를 재생한다.</summary>
        protected abstract void OnEventRaised();
    }
}
