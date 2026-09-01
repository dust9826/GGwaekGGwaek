#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    /// <summary>에디터 전용 수동 검증 씬에서 의뢰 실패와 선물 재배치를 흉내낸다.</summary>
    [DisallowMultipleComponent]
    public sealed class ThiefGiftRaidTestHarness : MonoBehaviour
    {
        [SerializeField] private ThiefDirector _director;
        [SerializeField] private ThiefRaidSite _raidSite;
        [SerializeField] private GameObject _giftPrefab;
        [SerializeField] private Transform[] _giftSpawnPoints = Array.Empty<Transform>();

        private int _nextRequestId = 1;
        private string _lastAction = "색상 버튼을 눌러 의뢰 실패를 흉내내세요.";

        public void Configure(ThiefDirector director, ThiefRaidSite raidSite,
            GameObject giftPrefab, Transform[] giftSpawnPoints)
        {
            _director = director;
            _raidSite = raidSite;
            _giftPrefab = giftPrefab;
            _giftSpawnPoints = giftSpawnPoints ?? Array.Empty<Transform>();
        }

        private void Awake()
        {
            RefillMissingGifts();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16f, 16f, 430f, 430f), GUI.skin.box);
            GUILayout.Label("도둑 선물 습격 테스트");
            GUILayout.Label("1~4 또는 버튼: 해당 색 의뢰 실패 예약 (2~5초 지연)");
            GUILayout.Label("WASD: 펭귄 이동 / 가까이 가서 도둑 반응 확인");
            GUILayout.Space(8f);

            DrawQueueButton("1  빨강 실패", EGiftBoxKind.Red);
            DrawQueueButton("2  노랑 실패", EGiftBoxKind.Yellow);
            DrawQueueButton("3  초록 실패", EGiftBoxKind.Green);
            DrawQueueButton("4  파랑 실패", EGiftBoxKind.Blue);

            if (GUILayout.Button("R  사라진 선물 다시 채우기")) RefillMissingGifts();
            if (GUILayout.Button(Time.timeScale < 0.9f ? "T  정상 속도(1x)" : "T  느리게 보기(0.25x)"))
                ToggleSlowMotion();
            GUILayout.Space(8f);
            ThiefActor[] thieves = FindObjectsByType<ThiefActor>(FindObjectsSortMode.None);
            GUILayout.Label($"예약된 습격: {(_director != null ? _director.PendingRaidCount : 0)}");
            GUILayout.Label($"활동 중 도둑: {thieves.Length}");
            GUILayout.Label($"보관소 안 선물: {CountAvailableGifts()}");
            GUILayout.Label($"재생 속도: {Time.timeScale:0.##}x");
            if (thieves.Length > 0)
            {
                ThiefActor thief = thieves[0];
                GUILayout.Label($"도둑: {thief.CurrentAction} / {thief.CurrentGait}");
                GUILayout.Label($"들기 단계: {thief.LiftPhase} {thief.LiftPhaseProgress01:P0}");
            }
            GUILayout.Label(_lastAction);
            GUILayout.EndArea();

            Event current = Event.current;
            if (current.type != EventType.KeyDown) return;
            switch (current.keyCode)
            {
                case KeyCode.Alpha1:
                    QueueRaid(EGiftBoxKind.Red);
                    break;
                case KeyCode.Alpha2:
                    QueueRaid(EGiftBoxKind.Yellow);
                    break;
                case KeyCode.Alpha3:
                    QueueRaid(EGiftBoxKind.Green);
                    break;
                case KeyCode.Alpha4:
                    QueueRaid(EGiftBoxKind.Blue);
                    break;
                case KeyCode.R:
                    RefillMissingGifts();
                    break;
                case KeyCode.T:
                    ToggleSlowMotion();
                    break;
                default:
                    return;
            }
            current.Use();
        }

        private void DrawQueueButton(string label, EGiftBoxKind kind)
        {
            if (GUILayout.Button(label)) QueueRaid(kind);
        }

        private void QueueRaid(EGiftBoxKind kind)
        {
            if (_director == null)
            {
                _lastAction = "ThiefDirector가 연결되지 않았습니다.";
                return;
            }

            _director.EnqueueRaid(_nextRequestId++, kind);
            _lastAction = $"{kind} 의뢰 실패를 예약했습니다.";
        }

        private void RefillMissingGifts()
        {
            if (_raidSite == null || _giftPrefab == null)
            {
                _lastAction = "습격 영역 또는 선물 프리팹이 연결되지 않았습니다.";
                return;
            }

            EGiftBoxKind[] kinds = Enum.GetValues(typeof(EGiftBoxKind)).Cast<EGiftBoxKind>().ToArray();
            for (int index = 0; index < kinds.Length && index < _giftSpawnPoints.Length; index++)
            {
                EGiftBoxKind kind = kinds[index];
                if (HasGift(kind)) continue;
                Transform spawnPoint = _giftSpawnPoints[index];
                if (spawnPoint == null) continue;

                GameObject giftObject = Instantiate(_giftPrefab,
                    spawnPoint.position, spawnPoint.rotation);
                giftObject.name = $"TestGift_{kind}";
                Gift gift = giftObject.GetComponent<Gift>();
                if (gift != null) gift.SetKind(kind);
            }

            _lastAction = "사라진 색상의 선물을 다시 채웠습니다.";
        }

        private void ToggleSlowMotion()
        {
            Time.timeScale = Time.timeScale < 0.9f ? 1f : 0.25f;
            _lastAction = $"재생 속도를 {Time.timeScale:0.##}x로 변경했습니다.";
        }

        private void OnDisable()
        {
            Time.timeScale = 1f;
        }

        private bool HasGift(EGiftBoxKind kind)
        {
            Scene scene = gameObject.scene;
            return FindObjectsByType<Gift>(FindObjectsSortMode.None).Any(gift =>
                gift != null && gift.gameObject.scene == scene && gift.Kind == kind &&
                !gift.IsCarried && _raidSite.Contains(gift.transform.position));
        }

        private int CountAvailableGifts()
        {
            if (_raidSite == null) return 0;
            Scene scene = gameObject.scene;
            return FindObjectsByType<Gift>(FindObjectsSortMode.None).Count(gift =>
                gift != null && gift.gameObject.scene == scene && !gift.IsCarried &&
                _raidSite.Contains(gift.transform.position));
        }
    }
}
#endif
