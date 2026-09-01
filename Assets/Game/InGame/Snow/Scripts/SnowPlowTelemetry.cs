using System.Globalization;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 불변식 계기를 콘솔에 찍는다. <b>권위가 아니고 계측이다</b> — 읽기만 하므로 이 컴포넌트를
    /// 지워도 시뮬레이션이 그대로 돈다.
    ///
    /// 첫 줄이 <c>mass</c> 다. 판정 방법은 <b>값의 크기가 아니라 움직임의 성질</b>이다:
    /// <list type="bullet">
    /// <item><b>0 을 중심으로 떨린다</b> → 정상. 1바이트 격자에서는 애초에 0 이 아닐 이유가 없다.</item>
    /// <item><b>한 방향으로 단조 증가/감소한다</b> → 누출이다. 크기가 톨러런스 안이어도 누출이다.
    ///   v7 은 46 mL/s 로 3분 흐른 뒤에야 절대 문턱을 넘었다 — <b>기울기가 값보다 먼저 말해 준다.</b></item>
    /// <item><c>unplaced</c> 가 0 이 아니다 → 배치 커널이 쓸 수 없는 자리에 질량을 배정했다.
    ///   드리프트로 나타나기를 기다리지 않고 <b>즉시, 전체 크기로</b> 보고된다.</item>
    /// </list>
    ///
    /// <c>Debug.Log</c> 를 블레이드 안에 두지 않은 이유는 이 폴더의 다른 연출 컴포넌트와 같다 —
    /// 권위는 <c>UnityEngine</c> 의 로그·시간·입력을 읽지 않는 편이 헤드리스에서 안전하다.
    /// </summary>
    public sealed class SnowPlowTelemetry : MonoBehaviour
    {
        [SerializeField] private SnowPlowBlade _blade;

        [Tooltip("찍는 주기(초). 0 이면 안 찍는다.")]
        [SerializeField, Min(0f)] private float _intervalSeconds = 1f;

        [Tooltip("누출로 판정될 때만 찍는다. 평소를 조용하게 두고 싶을 때.")]
        [SerializeField] private bool _onlyWhenLeaking;

        private float _next;

        // 직전 표본. **기울기가 값보다 먼저 말해 준다** — 상수 오프셋(기준선 결함)과
        // 발산(누출)을 사람이 두 표본을 눈으로 빼지 않고 갈라내게 한다.
        private float _lastErrorL;
        private float _lastSampleTime;
        private bool _hasSample;

        private void Awake()
        {
            if (_blade == null) _blade = GetComponentInParent<SnowPlowBlade>();
            if (_blade == null) enabled = false;
        }

        private void Update()
        {
            if (_intervalSeconds <= 0f) return;
            if (Time.time < _next) return;
            _next = Time.time + _intervalSeconds;

            bool leaking = _blade.MassLeaking;
            if (_onlyWhenLeaking && !leaking) return;

            var c = CultureInfo.InvariantCulture;

            float errorL = _blade.InvariantErrorL;
            float driftLPerSec = _hasSample && Time.time > _lastSampleTime
                ? (errorL - _lastErrorL) / (Time.time - _lastSampleTime)
                : 0f;
            _lastErrorL = errorL;
            _lastSampleTime = Time.time;
            _hasSample = true;

            Debug.Log(string.Concat(
                "[Snow] mass=", errorL.ToString("F4", c), "L",
                " drift=", driftLPerSec.ToString("F3", c), "L/s",
                leaking ? " LEAK" : " ok",
                " tol=", _blade.MassToleranceL.ToString("F3", c), "L",
                " carried=", _blade.CarriedLitres.ToString("F3", c), "L",
                " deleted=", _blade.DeletedLitres.ToString("F1", c), "L",
                " unplaced=", _blade.UnplacedLitres.ToString("F4", c), "L",
                " unplacedPeak=", _blade.UnplacedPeakLitres.ToString("F4", c), "L"));

            Debug.Log(string.Concat(
                "[Snow] pile=", _blade.PileVolumeM3.ToString("F4", c), "m3",
                " h=", _blade.PileHeightM.ToString("F3", c), "m",
                " footprint=", _blade.PileFootprintWidthM.ToString("F3", c), "m",
                " support=", _blade.PileSupportWidthM.ToString("F2", c), "m",
                " cap=", _blade.PileCapacityM3.ToString("F3", c), "m3",
                " fill=", _blade.PileFill01.ToString("F3", c),
                " mass=", _blade.CarriedMassKg.ToString("F0", c), "kg",
                " heapFrac=", _blade.HeapFraction.ToString("F3", c),
                " releaseFrac=", _blade.ReleaseFraction.ToString("F3", c)));

            Debug.Log(string.Concat(
                "[Snow] blade=", _blade.BladeDown ? "DOWN" : "UP",
                " angle=", _blade.Angle.ToString(),
                " attached=", _blade.BladeAttached.ToString(),
                " fwd=", _blade.ForwardSpeedMps.ToString("F2", c), "m/s",
                " face=", _blade.FaceAngleDeg.ToString("F1", c), "deg",
                " castRate=", _blade.CastRateM3PerSec.ToString("F3", c), "m3/s",
                " castPush=", _blade.CastPushMps2.ToString("F3", c), "m/s2",
                " castYaw=", _blade.CastYawDegPerSec.ToString("F2", c), "deg/s",
                " deposits=", _blade.DepositCount.ToString(c),
                " depositVol=", _blade.DepositVolumeM3.ToString("F3", c), "m3"));

            Debug.Log(string.Concat(
                "[Snow] relaxCells=", _blade.RelaxWindowCells.ToString(c),
                " relaxFlows=", _blade.RelaxFlows.ToString(c),
                " relaxClipped=", _blade.RelaxWindowClipped.ToString(),
                " removedCm=", _blade.LastRemovedCm.ToString(c)));
        }
    }
}
