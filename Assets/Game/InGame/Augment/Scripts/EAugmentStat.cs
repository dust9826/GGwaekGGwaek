namespace PPack
{
    /// <summary>증강 효과가 꽂히는 축. <b>값은 언제나 가산 누적</b>이고, 배율로 쓰는 쪽이
    /// <c>1 + 값</c> 으로 읽는다. 확률 축은 값을 그대로 쓴다.</summary>
    public enum EAugmentStat
    {
        ClearTimeBonus = 0,
        RequestTtl = 1,
        Reward = 2,
        WalkSpeed = 3,
        ExtraGiftChance = 4,
    }
}
