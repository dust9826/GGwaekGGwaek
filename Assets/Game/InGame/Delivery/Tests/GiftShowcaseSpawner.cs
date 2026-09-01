using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>무작위 선물 외형을 한 화면에서 반복 확인하기 위한 테스트 씬 전용 스포너.</summary>
    public sealed class GiftShowcaseSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject _giftPrefab;
        [SerializeField] private Vector2 _areaSize = new Vector2(10f, 5.8f);
        [SerializeField] private int _initialCount = 14;
        [SerializeField] private int _maximumVisible = 20;
        [SerializeField] private float _spawnInterval = 0.65f;
        [SerializeField] private float _minimumSpacing = 1.05f;
        [SerializeField] private Vector2Int _valueRange = new Vector2Int(1, 5);

        private readonly Queue<Gift> _spawned = new Queue<Gift>();
        private float _timer;

        public int VisibleCount => _spawned.Count;

        private void Start()
        {
            int count = Mathf.Clamp(_initialCount, 0, _maximumVisible);
            for (int index = 0; index < count; index++) SpawnOne();
            _timer = _spawnInterval;
        }

        private void Update()
        {
            if (_giftPrefab == null || _spawnInterval <= 0f) return;

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer += _spawnInterval;
            SpawnOne();
        }

        public void Configure(
            GameObject giftPrefab,
            Vector2 areaSize,
            int initialCount,
            int maximumVisible,
            float spawnInterval,
            float minimumSpacing)
        {
            _giftPrefab = giftPrefab;
            _areaSize = areaSize;
            _initialCount = initialCount;
            _maximumVisible = maximumVisible;
            _spawnInterval = spawnInterval;
            _minimumSpacing = minimumSpacing;
        }

        private void SpawnOne()
        {
            if (_giftPrefab == null || _maximumVisible <= 0) return;

            while (_spawned.Count >= _maximumVisible)
            {
                Gift oldest = _spawned.Dequeue();
                if (oldest != null) Destroy(oldest.gameObject);
            }

            Vector3 position = FindSpawnPosition();
            GameObject giftObject = Instantiate(
                _giftPrefab,
                position,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f),
                transform);
            Gift gift = giftObject.GetComponent<Gift>();
            if (gift == null)
            {
                Destroy(giftObject);
                return;
            }
            gift.name = $"Gift_Random_{gift.Id:000}";
            gift.gameObject.SetActive(true);

            GiftAppearance appearance = giftObject.GetComponent<GiftAppearance>();
            if (appearance != null) appearance.Randomize();
            gift.SetValue(Random.Range(_valueRange.x, _valueRange.y + 1));
            _spawned.Enqueue(gift);
        }

        private Vector3 FindSpawnPosition()
        {
            Vector3 fallback = transform.position;
            float minimumDistanceSquared = _minimumSpacing * _minimumSpacing;

            for (int attempt = 0; attempt < 20; attempt++)
            {
                Vector3 candidate = transform.position + new Vector3(
                    Random.Range(_areaSize.x * -0.5f, _areaSize.x * 0.5f),
                    0.02f,
                    Random.Range(_areaSize.y * -0.5f, _areaSize.y * 0.5f));
                fallback = candidate;

                bool overlaps = false;
                foreach (Gift existing in _spawned)
                {
                    if (existing == null) continue;
                    Vector2 delta = new Vector2(
                        existing.transform.position.x - candidate.x,
                        existing.transform.position.z - candidate.z);
                    if (delta.sqrMagnitude < minimumDistanceSquared)
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps) return candidate;
            }

            return fallback;
        }
    }
}
