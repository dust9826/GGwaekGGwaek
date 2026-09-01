using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 플레이어 주변에 직접 밀어 배달할 수 있는 선물을 공급한다.
    /// </summary>
    public sealed class GiftSpawner : MonoBehaviour
    {
        [SerializeField] private Gift _giftPrefab;
        [SerializeField] private float _interval = 5f;
        [SerializeField] private float _radius = 5f;
        [SerializeField] private int _maxAlive = 10;
        [SerializeField] private Vector2Int _valueRange = new Vector2Int(1, 5);

        private float _timer;

        private void Update()
        {
            if (_giftPrefab == null) return;

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = _interval;

            if (Gift.All.Count >= _maxAlive) return;

            Vector2 offset = UnityEngine.Random.insideUnitCircle * _radius;
            Vector3 position = transform.position + new Vector3(offset.x, 0.4f, offset.y);
            Gift gift = Instantiate(_giftPrefab, position, Quaternion.identity);
            // Instantiate는 원본(템플릿)의 활성 상태를 그대로 복제한다. 템플릿은 씬에 미리보기가
            // 남지 않도록 비활성으로 만들어 두므로, 여기서 켜지 않으면 생성된 선물이 안 보이고
            // OnEnable이 안 불려 Gift.All에도 등록되지 않는다.
            gift.gameObject.SetActive(true);
            EnsureMovable(gift);
            gift.SetValue(UnityEngine.Random.Range(_valueRange.x, _valueRange.y + 1));
            gift.SetKind((EGiftBoxKind)UnityEngine.Random.Range(0, 7));
        }

        private static void EnsureMovable(Gift gift)
        {
            if (gift.TryGetComponent(out Rigidbody _)) return;

            Rigidbody body = gift.gameObject.AddComponent<Rigidbody>();
            body.mass = 2f;
            body.linearDamping = 0.8f;
            body.angularDamping = 0.8f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }
}
