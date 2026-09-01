using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 하루 진행도를 복제한다. <b>이어지는 상태이므로 <see cref="StageEventNetBehaviour"/> 를 쓰지
    /// 않는다</b> — 시각은 "일어난 사건" 이 아니라 매 틱 흐르는 값이라 티켓이 맞지 않는다.
    ///
    /// <para><b>왜 필요한가.</b> <see cref="TimeOfDayDirector"/> 는 네트워크를 전혀 모르고 자기
    /// <c>Update</c> 에서 로컬 시계로 시각을 굴린다. 모든 피어가 같은 값에서 출발하고 서버가 씬을
    /// 동시에 올리므로 <b>맞아 보이지만 동기화된 것이 아니다</b> — 각자 굴러서 어긋나고,
    /// <b>늦게 들어온 사람은 아침부터 시작한다</b>(2026-08-31 실측).</para>
    ///
    /// <para><b>날짜(<c>DayIndex</c>)는 복제하지 않는다.</b> 그것을 읽는 곳이
    /// <see cref="StageDateCoordinator"/> 하나뿐이고 그쪽이 서버 전용이 되기 때문이다. 쓰지 않는 값을
    /// 실어 보내지 않는다.</para>
    ///
    /// <para>보간이나 예측을 넣지 않는다. <c>SecondsPerDay</c> 가 분 단위라 틱당 변화가 미세해서
    /// 그냥 덮어써도 튀지 않는다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TimeOfDayNetHub : NetworkBehaviour
    {
        [Networked] private float NetNormalizedTime { get; set; }

        private TimeOfDayDirector _timeOfDay;

        /// <summary>스폰된 프리팹은 씬 오브젝트를 직렬화로 참조할 수 없다. <c>MissionNetHub</c> 와
        /// 같은 방식으로 씬에서 찾는다.</summary>
        public override void Spawned()
        {
            _timeOfDay = FindAnyObjectByType<TimeOfDayDirector>(FindObjectsInactive.Include);
            if (_timeOfDay == null)
                Debug.LogWarning($"[{nameof(TimeOfDayNetHub)}] 씬에 {nameof(TimeOfDayDirector)} 가 없다 - 시각이 동기화되지 않는다.");
        }

        public override void FixedUpdateNetwork()
        {
            if (_timeOfDay == null) return;
            if (Object.HasStateAuthority) NetNormalizedTime = _timeOfDay.NormalizedTime;
        }

        /// <summary>서버 값으로 끌어당긴다. 클라이언트의 로컬 시계는 그대로 두고 매 프레임 보정만
        /// 한다 — 멈춰 두면 스냅샷 사이에 시간이 정지해 보인다.</summary>
        public override void Render()
        {
            if (_timeOfDay == null || Object == null || !Object.IsValid || Object.HasStateAuthority) return;
            _timeOfDay.SetNormalizedTime(NetNormalizedTime);
        }
    }
}
