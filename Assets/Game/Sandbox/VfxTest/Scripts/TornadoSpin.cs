using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 토네이도 깔때기를 축 중심으로 돌린다.
    ///
    /// 깔때기는 VFX Graph 가 아니라 <b>일반 MeshRenderer</b> 다. 레퍼런스의 깔때기에는 명암이
    /// 있는데 VFX Graph 의 Unlit 출력으로는 평평한 색만 나오기 때문이다. VFX Graph 는 바닥의
    /// 잔해와 흙먼지를 맡는다.
    ///
    /// 위아래를 서로 다른 속도로 돌리고 싶으면 메시를 나누는 대신 셰이더에서 UV 를 흘리는 쪽이
    /// 싸다 — 여기서는 통짜 회전만 한다.
    /// </summary>
    public sealed class TornadoSpin : MonoBehaviour
    {
        [Tooltip("초당 회전 각도. 너무 빠르면 띠가 깜빡이고 너무 느리면 서 있는 조형물로 보인다.")]
        [SerializeField] private float _degreesPerSecond = 150f;

        [Tooltip("좌우로 천천히 흔들리는 폭(도). 0 이면 제자리에서 곧게 선다.")]
        [SerializeField] private float _swayDegrees = 2.5f;

        [SerializeField] private float _swaySpeed = 0.7f;

        private Vector3 _baseEuler;

        private void Awake() => _baseEuler = transform.localEulerAngles;

        private void Update()
        {
            float sway = Mathf.Sin(Time.time * _swaySpeed) * _swayDegrees;
            transform.localEulerAngles = new Vector3(
                _baseEuler.x + sway,
                _baseEuler.y + Time.time * _degreesPerSecond,
                _baseEuler.z);
        }
    }
}
