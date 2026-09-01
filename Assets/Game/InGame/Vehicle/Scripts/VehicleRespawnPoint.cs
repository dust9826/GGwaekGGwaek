using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 차량이 처음 서 있고 추락 뒤 돌아올 공용 자세다. 에디트 모드에서는 마커를 옮기면
    /// 연결된 차량도 함께 옮겨, <see cref="VehicleController.Awake"/>가 올바른 yaw를 캡처하게 한다.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class VehicleRespawnPoint : MonoBehaviour
    {
        [Tooltip("이 자리에 세우고 되살릴 차량. 비어 있으면 아무것도 하지 않는다.")]
        [SerializeField] private Transform _player;

        [Tooltip("기즈모 크기(m). 판정에 쓰이지 않는 표시 전용 값이다.")]
        [SerializeField] private Vector3 _gizmoSize = new Vector3(1.8f, 0.9f, 4f);

        public Transform Player => _player;

        public void Configure(Transform player) => _player = player;

        public void Respawn()
        {
            if (_player == null) return;

            VehicleController controller = _player.GetComponent<VehicleController>();
            if (controller != null)
            {
                controller.RespawnAt(transform.position, transform.rotation);
                return;
            }

            _player.SetPositionAndRotation(transform.position, transform.rotation);
            Rigidbody rigidbody = _player.GetComponent<Rigidbody>();
            if (rigidbody == null) return;
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
        }

        protected virtual void Update()
        {
            if (Application.isPlaying) return;
            if (_player == null) return;
            if (_player.position == transform.position && _player.rotation == transform.rotation) return;
            _player.SetPositionAndRotation(transform.position, transform.rotation);
        }

        protected virtual void OnDrawGizmos()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.color = new Color(0.2f, 1f, 0.5f, 0.9f);
            Gizmos.DrawWireCube(new Vector3(0f, _gizmoSize.y * 0.5f, 0f), _gizmoSize);

            float tip = _gizmoSize.z * 0.5f + 1.2f;
            float head = _gizmoSize.z * 0.5f + 0.4f;
            float wing = _gizmoSize.x * 0.35f;
            var nose = new Vector3(0f, 0.1f, tip);
            Gizmos.DrawLine(new Vector3(0f, 0.1f, _gizmoSize.z * 0.5f), nose);
            Gizmos.DrawLine(nose, new Vector3(-wing, 0.1f, head));
            Gizmos.DrawLine(nose, new Vector3(wing, 0.1f, head));
        }
    }
}
