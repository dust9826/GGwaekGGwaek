namespace PPack
{
    /// <summary>눈덩이 투사체에 맞을 수 있는 대상의 계약. 효과는 구현체가 정한다.</summary>
    public interface ISnowballHittable
    {
        void OnSnowballHit(in SnowballHit hit);
    }
}
