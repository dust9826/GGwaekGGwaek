using UnityEngine;

namespace PPack
{
    /// <summary>수동 조작 테스트에서 관성 상태와 실제 속도를 한 화면에 보여 준다.</summary>
    public sealed class PenguinMomentumHandlingProbe : MonoBehaviour
    {
        private PenguinLocomotion _locomotion;
        private PenguinSnowball _snowball;

        private void Start()
        {
            _locomotion = FindFirstObjectByType<PenguinLocomotion>();
            _snowball = FindFirstObjectByType<PenguinSnowball>();
        }

        private void OnGUI()
        {
            const float width = 430f;
            GUILayout.BeginArea(new Rect(16f, 16f, width, 245f), GUI.skin.box);
            GUILayout.Label("Momentum Handling Lab");
            GUILayout.Label("Slide/Carry: Shift propulsion · W tuck · S brake · A/D steer");
            GUILayout.Label("Snowball: E attach · W push · S brake · A/D orbit · F carry");

            if (_locomotion != null)
            {
                GUILayout.Space(6f);
                GUILayout.Label($"Speed {_locomotion.Speed:0.00} m/s · target " +
                                $"{_locomotion.MomentumTargetSpeedMps:0.00} m/s");
                GUILayout.Label($"Drive {_locomotion.MomentumBuildUp01:0.00} · steer " +
                                $"{_locomotion.MomentumSteerCommitment01:0.00} · " +
                                $"brake {_locomotion.IsMomentumBraking}");
                GUILayout.Label($"Slip {_locomotion.SlipAngleDeg:0.0}° · turn " +
                                $"{_locomotion.TurnRateDegPerSec:0.0}°/s");
            }

            if (_snowball != null && _snowball.HeldForPose != null)
            {
                SnowBallCarrier carrier = _snowball.HeldForPose;
                Rigidbody body = carrier.GetComponent<Rigidbody>();
                float speed = body != null
                    ? Vector3.ProjectOnPlane(body.linearVelocity,
                        carrier.SupportNormal).magnitude
                    : 0f;
                ESnowBallGrowthStage stage = SnowballStageModel.GetStage(carrier.RadiusM);
                float stageProgress01 = SnowballStageModel.GetStageProgress01(carrier.RadiusM);
                GUILayout.Space(6f);
                GUILayout.Label($"Ball Stage {(int)stage} " +
                                $"({stageProgress01:0.00}) · " +
                                $"{speed:0.00} m/s · target " +
                                $"{_snowball.MomentumTargetSpeedMps:0.00} m/s · " +
                                $"growth {carrier.GrowthProgress01:0.00}");
                SnowballMomentumMass momentumMass =
                    carrier.GetComponent<SnowballMomentumMass>();
                if (momentumMass != null)
                    GUILayout.Label($"Effective mass {momentumMass.EffectiveMassKg:0.0} kg · " +
                                    $"volume load {momentumMass.Load01:0.00}");
                GUILayout.Label($"Drive {_snowball.MomentumBuildUp01:0.00} · " +
                                $"brake {_snowball.IsMomentumBraking}");
            }

            GUILayout.EndArea();
        }
    }
}
