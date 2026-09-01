using UnityEngine;

namespace PPack
{
    public enum ESnowBakeSurface
    {
        Ground,
        Road,
        Obstacle,
        Ignore
    }

    /// <summary>
    /// <see cref="SnowGroundBake"/>가 콜라이더를 해석하는 저작 표식. 자식 콜라이더는 가장 가까운
    /// 부모 표식을 물려받으므로 맵의 지오메트리 루트에 한 번만 붙이면 된다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowBakeSurface : MonoBehaviour
    {
        [SerializeField] private ESnowBakeSurface _surface = ESnowBakeSurface.Ground;

        [Tooltip("Road에서만 사용한다. 스테이지 Initial Depth에 곱할 시작 적설 배율이다.")]
        [SerializeField, Range(0f, 1f)] private float _initialDepthScale = 1f;

        public ESnowBakeSurface Surface => _surface;

        public byte InitialDepthScaleR8
        {
            get
            {
                if (_surface == ESnowBakeSurface.Obstacle || _surface == ESnowBakeSurface.Ignore) return 0;
                if (_surface == ESnowBakeSurface.Ground) return byte.MaxValue;
                return (byte)Mathf.RoundToInt(Mathf.Clamp01(_initialDepthScale) * byte.MaxValue);
            }
        }
    }
}
