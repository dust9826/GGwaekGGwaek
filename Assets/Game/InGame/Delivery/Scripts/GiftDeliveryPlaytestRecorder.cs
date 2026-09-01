#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PPack
{
    /// <summary>개발 플레이 한 회차의 주문 결과를 0 키로 JSON에 저장한다.</summary>
    public sealed class GiftDeliveryPlaytestRecorder : MonoBehaviour
    {
        [Serializable]
        private sealed class OrderRecord
        {
            public int orderId;
            public int completedBeforeStart;
            public int houseIndex;
            public float startedAtSeconds;
            public float endedAtSeconds;
            public string state;
            public string failReason;
            public float routeLengthM;
            public float timeLimitSeconds;
            public float elapsedSeconds;
            public float remainingSeconds;
            public float timeUsedRatio;
            public float effectiveDeliverySpeedMps;
            public int requiredGiftCount;
            public int requiredTotalValue;

            [NonSerialized] public GiftDeliveryOrder Order;
        }

        [Serializable]
        private sealed class SessionReport
        {
            public string exportedAtUtc;
            public string scene;
            public float sessionSeconds;
            public string phase;
            public int completedCount;
            public int totalScore;
            public OrderRecord[] orders;
        }

        private readonly List<OrderRecord> _records = new List<OrderRecord>();
        private GiftDeliveryDirector _director;
        private float _startedAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoad()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Install();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Install();
        }

        private static void Install()
        {
            GiftDeliveryDirector[] directors = UnityEngine.Object.FindObjectsByType<GiftDeliveryDirector>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (GiftDeliveryDirector director in directors)
            {
                if (director.GetComponent<GiftDeliveryPlaytestRecorder>() == null)
                    director.gameObject.AddComponent<GiftDeliveryPlaytestRecorder>();
            }
        }

        private void Awake()
        {
            _director = GetComponent<GiftDeliveryDirector>();
            if (_director == null)
            {
                enabled = false;
                return;
            }

            _startedAt = Time.unscaledTime;
            _director.OrderStarted += OnOrderStarted;
            _director.OrderCompleted += OnOrderCompleted;
            _director.OrderFailed += OnOrderFailed;
            Debug.Log("[DeliveryPlaytest] 숫자 0을 누르면 현재 플레이 데이터를 JSON으로 저장합니다.");
        }

        private void OnDestroy()
        {
            if (_director == null) return;
            _director.OrderStarted -= OnOrderStarted;
            _director.OrderCompleted -= OnOrderCompleted;
            _director.OrderFailed -= OnOrderFailed;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (!keyboard.digit0Key.wasPressedThisFrame && !keyboard.numpad0Key.wasPressedThisFrame) return;

            Export();
        }

        private void OnOrderStarted(GiftDeliveryOrder order)
        {
            _records.Add(new OrderRecord
            {
                orderId = order.Id,
                completedBeforeStart = _director.CompletedCount,
                houseIndex = order.HouseIndex,
                startedAtSeconds = SessionSeconds,
                endedAtSeconds = -1f,
                state = order.State.ToString(),
                failReason = order.FailReason.ToString(),
                routeLengthM = order.RouteLength,
                timeLimitSeconds = order.TimeLimitSeconds,
                requiredGiftCount = order.RequiredGiftCount,
                requiredTotalValue = order.RequiredTotalValue,
                Order = order
            });
        }

        private void OnOrderCompleted(GiftDeliveryOrder order)
        {
            FinalizeRecord(order, EGiftDeliveryFailReason.None);
        }

        private void OnOrderFailed(GiftDeliveryOrder order, EGiftDeliveryFailReason reason)
        {
            OrderRecord record = FindRecord(order.Id);
            if (record == null)
            {
                record = new OrderRecord
                {
                    orderId = order.Id,
                    completedBeforeStart = _director.CompletedCount,
                    houseIndex = order.HouseIndex,
                    startedAtSeconds = SessionSeconds,
                    endedAtSeconds = -1f,
                    routeLengthM = order.RouteLength,
                    timeLimitSeconds = order.TimeLimitSeconds,
                    requiredGiftCount = order.RequiredGiftCount,
                    requiredTotalValue = order.RequiredTotalValue,
                    Order = order
                };
                _records.Add(record);
            }

            FinalizeRecord(record, reason);
        }

        private void FinalizeRecord(GiftDeliveryOrder order, EGiftDeliveryFailReason reason)
        {
            OrderRecord record = FindRecord(order.Id);
            if (record != null) FinalizeRecord(record, reason);
        }

        private void FinalizeRecord(OrderRecord record, EGiftDeliveryFailReason reason)
        {
            record.endedAtSeconds = SessionSeconds;
            record.state = record.Order.State.ToString();
            record.failReason = reason.ToString();
            CaptureTiming(record);
        }

        private void Export()
        {
            foreach (OrderRecord record in _records)
            {
                if (record.Order == null) continue;
                record.state = record.Order.State.ToString();
                record.failReason = record.Order.FailReason.ToString();
                CaptureTiming(record);
            }

            var report = new SessionReport
            {
                exportedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                scene = SceneManager.GetActiveScene().path,
                sessionSeconds = SessionSeconds,
                phase = _director.Phase.ToString(),
                completedCount = _director.CompletedCount,
                totalScore = _director.TotalScore,
                orders = _records.ToArray()
            };

            string directory = Path.Combine(Application.persistentDataPath, "PlaytestData", "Delivery");
            Directory.CreateDirectory(directory);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            string path = Path.Combine(directory, $"delivery_playtest_{timestamp}.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log($"[DeliveryPlaytest] 현재 플레이 데이터 저장 완료: {path}");
        }

        private OrderRecord FindRecord(int orderId)
        {
            for (int index = _records.Count - 1; index >= 0; index--)
                if (_records[index].orderId == orderId) return _records[index];
            return null;
        }

        private static void CaptureTiming(OrderRecord record)
        {
            record.remainingSeconds = Mathf.Max(record.Order.RemainingSeconds, 0f);
            record.elapsedSeconds = Mathf.Max(record.timeLimitSeconds - record.Order.RemainingSeconds, 0f);
            record.timeUsedRatio = record.timeLimitSeconds > 0f
                ? record.elapsedSeconds / record.timeLimitSeconds
                : 0f;
            record.effectiveDeliverySpeedMps = record.elapsedSeconds > 0f
                ? record.routeLengthM / record.elapsedSeconds
                : 0f;
        }

        private float SessionSeconds => Time.unscaledTime - _startedAt;
    }
}
#endif
