using System;

namespace PPack
{
    /// <summary>스탯 하나에 더할 값. 배율 축은 0.4 가 +40%, 확률 축은 0.3 이 30%p 다.
    /// 패널티는 같은 형식에 음수를 넣는다.</summary>
    [Serializable]
    public struct AugmentEffect
    {
        public EAugmentStat Stat;
        public float Value;
    }
}
