namespace PPack
{
    /// <summary>하루 안의 큰 구간. 연출·SFX·게임플레이가 "지금 밤인가"를 물을 때 쓰는 이름표이며,
    /// 실제 하늘 값은 <see cref="TimeOfDayConfig"/>의 그라디언트가 연속으로 결정한다.
    /// 즉 이 열거형은 <b>파생값</b>이지 하늘의 원본이 아니다.</summary>
    public enum EDayPhase
    {
        Night,
        Dawn,
        Day,
        Dusk,
    }
}
