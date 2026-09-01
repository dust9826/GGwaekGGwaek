using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 눈보라의 <b>시작과 경로</b>를 복제한다. 결과(깎인 눈)는 각 피어가 이미 시뮬하므로 보내지 않는다 —
    /// 루트 규약 "원인을 복제한다".
    ///
    /// <para><b>왜 필요한가.</b> <see cref="BlizzardEvent.Trigger()"/> 는 첫 줄에서
    /// <c>_snowStage.HasSimulationAuthority</c> 를 요구한다. 그것은 서버에서만 참이므로
    /// <b>클라이언트에서는 눈보라가 아예 시작되지 않았다</b>(2026-08-31 실측). 게다가 시작을 부르는
    /// 쪽도 각 피어의 로컬 날짜 카운터라 시점도 제각각이었다.</para>
    ///
    /// <para><b>경로를 각자 계산하게 하지 않는다.</b> <see cref="BlizzardRoutePlanner.TryPlan"/> 은
    /// <b>현재 눈 필드 상태</b>를 읽어 경로를 정하는데, 그 필드는 피어마다 자기가 시뮬한 결과라
    /// 완전히 같다는 보장이 없다. 같은 시드가 같은 결과를 준다는 가정이 조용히 깨지는 자리다.
    /// <b>정해진 경로를 보내는 편이 싸고 확실하다</b> — <see cref="BlizzardRoutePlan"/> 은
    /// <c>Vector2</c> 셋과 <c>float</c> 하나뿐이다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BlizzardNetHub : StageEventNetBehaviour
    {
        [Networked] private Vector2 NetStart { get; set; }
        [Networked] private Vector2 NetSecondRegion { get; set; }
        [Networked] private Vector2 NetDirection { get; set; }
        [Networked] private float NetTravel { get; set; }

        private BlizzardEvent _event;

        public override void Spawned()
        {
            base.Spawned();
            _event = FindAnyObjectByType<BlizzardEvent>(FindObjectsInactive.Include);
            if (_event == null)
                Debug.LogWarning($"[{nameof(BlizzardNetHub)}] 씬에 {nameof(BlizzardEvent)} 가 없다 - 눈보라가 동기화되지 않는다.");
        }

        /// <summary>서버가 눈보라를 시작한다. 경로를 먼저 쓰고 티켓을 올린다 — 순서가 반대면
        /// 다른 피어가 이전 경로로 재생한다.</summary>
        public void RaiseBlizzard(BlizzardRoutePlan route)
        {
            NetStart = route.Start;
            NetSecondRegion = route.SecondRegion;
            NetDirection = route.Direction;
            NetTravel = route.TravelDistance;
            RaiseOnServer();
        }

        /// <summary>권위를 요구하지 않는 오버로드를 쓴다. 인자 없는
        /// <see cref="BlizzardEvent.Trigger()"/> 는 클라이언트에서 항상 실패한다.</summary>
        protected override void OnEventRaised()
        {
            if (_event == null) return;
            _event.Trigger(new BlizzardRoutePlan(NetStart, NetSecondRegion, NetDirection, NetTravel));
        }
    }
}
