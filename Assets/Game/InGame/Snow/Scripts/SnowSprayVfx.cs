using UnityEngine;
using UnityEngine.VFX;

namespace PPack
{
    /// <summary>
    /// 눈보라를 <b>실제로 적용된 제거량</b>으로 구동한다. 로컬 입력이 아니라 필드에 적용된 양을
    /// 읽으므로, Fusion 이 오면 원격 플레이어의 스프레이도 그대로 보인다(루트 <c>AGENTS.md</c>).
    ///
    /// 방출 방향은 그래프가 <b>로컬 ±X</b> 로 고정한다(스펙 §9). 그래서 이 컴포넌트는 차량의
    /// 회전을 그대로 물려받아야 한다 — 로컬 X 가 차량의 오른쪽이 되게. 진행 방향으로 쏘지 않는
    /// 이유는 카메라가 차 뒤에 있어 그 자리가 곧 차체라서다. 먼지의 밀림 VFX 가 그것 때문에 죽었다.
    ///
    /// <b>깨끗한 길에서는 뜨지 않는다.</b> 제거량이 0 이면 방출을 멈춘다 — 먼지에서 내장 파티클을
    /// 기각한 요건이 이것이다.
    ///
    /// 신호원은 <see cref="SnowPlowBlade"/> 다. 예전에는 <c>SnowVehiclePad</c> 였는데, 날이
    /// <b>같은 격자를 영수증 없이 자르는 패드를 런타임에 끈다</b>(cs:261). 그래서 패드를 계속
    /// 구독하면 리그를 차량 프리팹에 올리는 순간 스프레이가 조용해진다 — 신호가 0 이 아니라
    /// <b>공급자가 사라지는</b> 실패라 화면에 아무 단서가 남지 않는다.
    /// </summary>
    [RequireComponent(typeof(VisualEffect))]
    public sealed class SnowSprayVfx : MonoBehaviour
    {
        [SerializeField] private SnowPlowBlade _blade;
        [Tooltip("이 제거량(cm·셀) 이상이면 방출한다. 0 이면 스탬프가 조금이라도 파면 방출.")]
        [SerializeField, Min(0)] private int _removedThreshold = 20;
        [Tooltip("제거가 멈춘 뒤 방출을 유지하는 시간(초). 자국이 끊겨 보이는 것을 막는다.")]
        [SerializeField, Min(0f)] private float _linger = 0.12f;

        private VisualEffect _effect;
        private float _remaining;

        /// <summary>지금 방출 중인가. 계측·판정이 구독한다.</summary>
        public bool Emitting { get; private set; }

        private void Awake()
        {
            _effect = GetComponent<VisualEffect>();
            if (_blade == null) _blade = GetComponentInParent<SnowPlowBlade>();
            SetEmitting(false);
        }

        private void FixedUpdate()
        {
            if (_blade == null) return;

            if (_blade.LastRemovedCm >= Mathf.Max(1, _removedThreshold)) _remaining = _linger;
            else _remaining -= Time.fixedDeltaTime;

            SetEmitting(_remaining > 0f);
        }

        private void SetEmitting(bool value)
        {
            Emitting = value;
            if (_effect == null) return;

            // pause 로 멈춘다 — enabled 를 끄면 살아 있는 파티클이 통째로 사라져 뚝 끊긴다.
            _effect.pause = !value;
        }
    }
}
