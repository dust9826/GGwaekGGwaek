using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 차를 코드로 몰아 <see cref="SnowCpuStage"/> 가 실제로 깎는지 확인하는 검증용 컴포넌트.
    ///
    /// <para>왜 필요한가: 자동화된 실행에는 키보드가 없고, 배치모드에서 프레임을 세는 방식은 실제 시간과
    /// 어긋난다(멀티 검증에서 240프레임이 3틱이었다). 여기서는 <see cref="Rigidbody.linearVelocity"/> 를
    /// 직접 밀어 정해진 시간만큼 전진시킨다 — 무엇을 보냈는지가 코드에 남고 재현된다.</para>
    ///
    /// <para>테스트 씬이 아닌 곳에 남겨 두면 사람이 몰 수 없게 되므로, 이름에 <c>__TEST__</c> 를 붙인
    /// 오브젝트에만 붙이고 확인이 끝나면 지운다.</para>
    /// </summary>
    public sealed class SnowCpuDriveProbe : MonoBehaviour
    {
        [SerializeField] private float _speedMps = 8f;
        [SerializeField] private float _durationSeconds = 6f;

        private Rigidbody _body;
        private float _elapsed;

        private void Start()
        {
            foreach (var body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
            {
                if (body.isKinematic) continue;

                _body = body;
                break;
            }
        }

        private void FixedUpdate()
        {
            if (_body == null) return;

            _elapsed += Time.fixedDeltaTime;
            if (_elapsed > _durationSeconds)
            {
                _body.linearVelocity = Vector3.zero;
                return;
            }

            Vector3 forward = _body.transform.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 velocity = forward * _speedMps;
            velocity.y = _body.linearVelocity.y;
            _body.linearVelocity = velocity;
        }
    }
}
