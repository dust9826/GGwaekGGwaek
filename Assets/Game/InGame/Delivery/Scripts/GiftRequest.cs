namespace PPack
{
    public enum ERequestState
    {
        Active,
        Completed,
        Expired,
    }

    /// <summary>한 의뢰. 저장(권위) 값은 Id·HouseIndex·WantedKind·RemainingSeconds(TTL)뿐이고,
    /// 나머지(거리·난이도·보상·추가시간)는 <see cref="RequestBalance"/>가 발행 시점에 유도한 값이다.
    /// 오브젝트 참조 대신 <see cref="HouseIndex"/>(int)만 들어 Fusion <c>[Networked]</c>로 옮기기 쉽다.
    ///
    /// <para>개별 제한시간(TTL)이 끝나면 그냥 사라진다 — 게임을 끝내지 않는다. 게임 종료는 오직
    /// 전역 시간(<see cref="GameManager"/>)이 0일 때다.</para></summary>
    public sealed class GiftRequest
    {
        public GiftRequest(int id, int houseIndex, EGiftBoxKind wantedKind, float distanceM,
                           in RequestBalanceResult balance)
        {
            Id = id;
            HouseIndex = houseIndex;
            WantedKind = wantedKind;
            DistanceM = distanceM;
            Difficulty = balance.Difficulty;
            Reward = balance.Reward;
            TimeBonusSeconds = balance.TimeBonusSeconds;
            TtlSeconds = balance.TtlSeconds;
            RemainingSeconds = balance.TtlSeconds;
            State = ERequestState.Active;
        }

        public int Id { get; }
        public int HouseIndex { get; }
        public EGiftBoxKind WantedKind { get; }
        public float DistanceM { get; }
        public float Difficulty { get; }
        public int Reward { get; }
        public float TimeBonusSeconds { get; }
        public float TtlSeconds { get; }
        public float RemainingSeconds { get; private set; }
        public ERequestState State { get; private set; }

        public bool IsExpired => State == ERequestState.Active && RemainingSeconds <= 0f;

        public void Tick(float deltaSeconds)
        {
            if (State != ERequestState.Active) return;
            RemainingSeconds -= deltaSeconds;
        }

        public void MarkCompleted()
        {
            if (State == ERequestState.Active) State = ERequestState.Completed;
        }

        public void MarkExpired()
        {
            if (State == ERequestState.Active) State = ERequestState.Expired;
        }
    }
}
