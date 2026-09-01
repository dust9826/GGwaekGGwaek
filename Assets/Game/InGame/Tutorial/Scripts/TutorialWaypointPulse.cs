using UnityEngine;

namespace PPack
{
    public sealed class TutorialWaypointPulse : MonoBehaviour
    {
        [SerializeField] private float _bobHeightM = 0.18f;
        [SerializeField] private float _bobHz = 0.8f;
        [SerializeField] private float _spinDegPerSec = 38f;

        private Vector3 _baseLocalPosition;

        private void Awake() => _baseLocalPosition = transform.localPosition;

        private void Update()
        {
            float y = Mathf.Sin(Time.time * Mathf.PI * 2f * _bobHz) * _bobHeightM;
            transform.localPosition = _baseLocalPosition + Vector3.up * y;
            transform.Rotate(0f, _spinDegPerSec * Time.deltaTime, 0f, Space.Self);
        }
    }
}

