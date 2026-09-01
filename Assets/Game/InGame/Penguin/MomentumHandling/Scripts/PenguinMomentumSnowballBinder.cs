using UnityEngine;

namespace PPack
{
    /// <summary>관성 테스트 펭귄이 밀거나 운반하는 런타임 눈덩이에 조작용 질량을 붙인다.</summary>
    [DisallowMultipleComponent]
    public sealed class PenguinMomentumSnowballBinder : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float _maximumMassKg =
            SnowballStageModel.DefaultMaximumHandlingMassKg;
        [SerializeField, Min(0f)] private float _rollingResistanceCoefficient = 0.015f;
        [SerializeField, Min(0f)] private float _dragCoefficient = 0.47f;

        private PenguinSnowball _snowball;
        private PenguinCarry _carry;
        private PenguinMomentumHandling _handling;
        private SnowBallCarrier _boundCarrier;

        private void Awake()
        {
            _snowball = GetComponent<PenguinSnowball>();
            _carry = GetComponent<PenguinCarry>();
            _handling = GetComponent<PenguinMomentumHandling>();
        }

        private void Update()
        {
            SnowBallCarrier pushedCarrier = _snowball != null ? _snowball.HeldForPose : null;
            SnowBallCarrier carrier = pushedCarrier;
            if (carrier == null && _carry != null && _carry.IsCarrying)
                carrier = _carry.Cargo as SnowBallCarrier;
            if (carrier == null) return;

            SnowballMomentumMass momentumMass = carrier.GetComponent<SnowballMomentumMass>();
            if (momentumMass == null)
            {
                momentumMass = carrier.gameObject.AddComponent<SnowballMomentumMass>();
                momentumMass.Configure(_maximumMassKg, _rollingResistanceCoefficient,
                    _dragCoefficient);
            }
            else if (carrier != _boundCarrier)
            {
                momentumMass.Configure(_maximumMassKg, _rollingResistanceCoefficient,
                    _dragCoefficient);
            }
            momentumMass.Bind(pushedCarrier != null ? _snowball : null, _handling);
            _boundCarrier = carrier;
        }
    }
}
